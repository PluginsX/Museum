#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PBRMaterialPainterTool
{
    public enum ParameterSemantic
    {
        BaseColor,
        Roughness,
        Metallic,
        Occlusion,
        Emission,
        Opacity
    }

    [Serializable]
    public class ShaderTextureProperty
    {
        public string propertyName;
        public ParameterSemantic semantic;

        public ShaderTextureProperty(string propertyName, ParameterSemantic semantic)
        {
            this.propertyName = propertyName;
            this.semantic = semantic;
        }
    }

    public static class ShaderPropertyDiscovery
    {
        public static List<ShaderTextureProperty> DiscoverTextureProperties(Material referenceMaterial)
        {
            var result = new List<ShaderTextureProperty>();
            if (referenceMaterial == null || referenceMaterial.shader == null)
                return result;

            var shader = referenceMaterial.shader;
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;

                string prop = ShaderUtil.GetPropertyName(shader, i);
                if (TryMapPropertyToSemantic(prop, out var semantic))
                {
                    result.Add(new ShaderTextureProperty(prop, semantic));
                }
            }

            // Ensure no duplicates by semantic (prefer first discovered common mapping)
            var uniqueBySemantic = new Dictionary<ParameterSemantic, ShaderTextureProperty>();
            foreach (var p in result)
            {
                if (!uniqueBySemantic.ContainsKey(p.semantic))
                    uniqueBySemantic[p.semantic] = p;
            }

            return new List<ShaderTextureProperty>(uniqueBySemantic.Values);
        }

        private static bool TryMapPropertyToSemantic(string propertyName, out ParameterSemantic semantic)
        {
            // Normalize name for comparisons
            string n = propertyName.ToLowerInvariant();

            // BaseColor / Albedo
            if (n == "_basemap" || n == "_maintex" || n.Contains("albedo") || n.Contains("basecolor") || n.Contains("base_map"))
            {
                semantic = ParameterSemantic.BaseColor;
                return true;
            }

            // Metallic
            if (n.Contains("metallicglossmap") || n.Contains("metallic_map") || n.Contains("metallic"))
            {
                semantic = ParameterSemantic.Metallic;
                return true;
            }

            // Roughness / Smoothness (we prefer explicit roughness map names if present)
            if (n.Contains("roughness"))
            {
                semantic = ParameterSemantic.Roughness;
                return true;
            }
            if (n.Contains("smoothness"))
            {
                // Treat as roughness map conceptually; user paints roughness and the engine/shader may expect smoothness.
                semantic = ParameterSemantic.Roughness;
                return true;
            }

            // Occlusion
            if (n.Contains("occlusion"))
            {
                semantic = ParameterSemantic.Occlusion;
                return true;
            }

            // Emission
            if (n.Contains("emission"))
            {
                semantic = ParameterSemantic.Emission;
                return true;
            }

            // Opacity / Alpha
            if (n.Contains("opacity") || n.Contains("alphamap") || n.Contains("transparency"))
            {
                semantic = ParameterSemantic.Opacity;
                return true;
            }

            semantic = default;
            return false;
        }
    }

    [Serializable]
    public class PaintedParameterLayer : IDisposable
    {
        [NonSerialized] public RenderTexture workingTexture;
        public Texture2D baseTexture; // Optional import source
        public string propertyName; // Shader property to bind/export
        public ParameterSemantic semantic;
        public int textureSize = 2048;

        public void EnsureInitialized()
        {
            if (workingTexture != null && (workingTexture.width != textureSize || workingTexture.height != textureSize))
            {
                Release();
            }

            if (workingTexture == null)
            {
                var desc = new RenderTextureDescriptor(textureSize, textureSize, RenderTextureFormat.ARGB32, 0)
                {
                    sRGB = true,
                    useMipMap = false,
                    msaaSamples = 1
                };
                workingTexture = new RenderTexture(desc)
                {
                    name = $"PBRPainter_{semantic}_{propertyName}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
                workingTexture.Create();
                ClearToTransparent();
            }
        }

        public void ClearToTransparent()
        {
            EnsureInitialized();
            var prev = RenderTexture.active;
            RenderTexture.active = workingTexture;
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;
        }

        public void ImportBaseFromTexture(Texture2D src)
        {
            if (src == null) return;
            EnsureInitialized();
            Graphics.Blit(src, workingTexture);
            // Ensure alpha becomes 1 after import (treat base as fully opaque contribution)
            using (var temp = TempRT(textureSize))
            {
                var mat = PainterMaterials.CompositeMaskedMaterial;
                mat.SetTexture("_UnderTex", workingTexture);
                mat.SetTexture("_MaskTex", Texture2D.whiteTexture);
                mat.SetVector("_ChannelMask", new Vector4(1, 1, 1, 1));
                mat.SetColor("_Color", Color.clear); // color ignored when mask is white and we use underTex as both; force alpha to 1 below
                // Force alpha to 1 by blending with white mask and setting color.a=1 in a dedicated pass
                var prev = RenderTexture.active;
                Graphics.Blit(workingTexture, temp, PainterMaterials.ForceAlphaOneMaterial);
                Graphics.Blit(temp, workingTexture);
                RenderTexture.active = prev;
            }
            baseTexture = src;
        }

        public void StampBrushAtUV(Vector2 uv, Color brushColor, float uvRadius, float hardness, Texture2D alphaMask, Vector4 channelMask)
        {
            EnsureInitialized();
            using (var temp = TempRT(textureSize))
            {
                var mat = PainterMaterials.BrushStampMaterial;
                mat.SetTexture("_MainTex", workingTexture);
                mat.SetColor("_BrushColor", brushColor);
                mat.SetVector("_CenterUV", new Vector4(uv.x, uv.y, 0, 0));
                mat.SetFloat("_RadiusUV", Mathf.Max(uvRadius, 1e-5f));
                mat.SetFloat("_Hardness", Mathf.Clamp01(hardness));
                if (alphaMask != null)
                {
                    mat.SetTexture("_AlphaTex", alphaMask);
                    mat.SetFloat("_UseAlphaTex", 1f);
                }
                else
                {
                    mat.SetTexture("_AlphaTex", Texture2D.blackTexture);
                    mat.SetFloat("_UseAlphaTex", 0f);
                }
                mat.SetVector("_ChannelMask", channelMask);

                Graphics.Blit(workingTexture, temp, mat, 0);
                Graphics.Blit(temp, workingTexture);
            }
        }

        public void FillWhole(Color fillColor, Vector4 channelMask)
        {
            EnsureInitialized();
            using (var temp = TempRT(textureSize))
            {
                var mat = PainterMaterials.CompositeMaskedMaterial;
                mat.SetTexture("_UnderTex", workingTexture);
                mat.SetTexture("_MaskTex", Texture2D.whiteTexture);
                mat.SetColor("_Color", fillColor);
                mat.SetVector("_ChannelMask", channelMask);
                Graphics.Blit(null, temp, mat, 0);
                Graphics.Blit(temp, workingTexture);
            }
        }

        public Texture2D ToTexture2D(bool sRGB)
        {
            EnsureInitialized();
            var prev = RenderTexture.active;
            RenderTexture.active = workingTexture;
            var tex = new Texture2D(workingTexture.width, workingTexture.height, TextureFormat.RGBA32, false, !sRGB);
            tex.ReadPixels(new Rect(0, 0, workingTexture.width, workingTexture.height), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            return tex;
        }

        public void Release()
        {
            if (workingTexture != null)
            {
                workingTexture.Release();
                UnityEngine.Object.DestroyImmediate(workingTexture);
                workingTexture = null;
            }
        }

        public void Dispose()
        {
            Release();
        }

        private static RenderTexture TempRT(int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Repeat;
            return rt;
        }
    }

    [Serializable]
    public class MaterialLayer : IDisposable
    {
        public string layerName;
        public bool isVisible = true;
        public bool isFoldout = false;
        public Dictionary<ParameterSemantic, PaintedParameterLayer> parameters = new Dictionary<ParameterSemantic, PaintedParameterLayer>();

        public void EnsureParameters(List<ShaderTextureProperty> mappedProps, int textureSize)
        {
            var needed = new HashSet<ParameterSemantic>();
            foreach (var p in mappedProps) needed.Add(p.semantic);

            // Remove unused
            var toRemove = new List<ParameterSemantic>();
            foreach (var kv in parameters)
            {
                if (!needed.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            foreach (var k in toRemove)
            {
                parameters[k].Dispose();
                parameters.Remove(k);
            }

            // Add missing
            foreach (var prop in mappedProps)
            {
                if (!parameters.ContainsKey(prop.semantic))
                {
                    var layer = new PaintedParameterLayer
                    {
                        propertyName = prop.propertyName,
                        semantic = prop.semantic,
                        textureSize = textureSize
                    };
                    layer.EnsureInitialized();
                    parameters[prop.semantic] = layer;
                }
                else
                {
                    parameters[prop.semantic].textureSize = textureSize;
                    parameters[prop.semantic].EnsureInitialized();
                }
            }
        }

        public void Dispose()
        {
            foreach (var p in parameters.Values)
            {
                p.Dispose();
            }
            parameters.Clear();
        }
    }

    public static class PainterMaterials
    {
        private static Material _brushStamp;
        private static Material _layerBlend;
        private static Material _compositeMasked;
        private static Material _forceAlphaOne;

        public static Material BrushStampMaterial
        {
            get
            {
                if (_brushStamp == null)
                {
                    _brushStamp = CreateHiddenMaterial("Hidden/PBRPainter/BrushStamp");
                }
                return _brushStamp;
            }
        }

        public static Material LayerBlendMaterial
        {
            get
            {
                if (_layerBlend == null)
                {
                    _layerBlend = CreateHiddenMaterial("Hidden/PBRPainter/LayerBlend");
                }
                return _layerBlend;
            }
        }

        public static Material CompositeMaskedMaterial
        {
            get
            {
                if (_compositeMasked == null)
                {
                    _compositeMasked = CreateHiddenMaterial("Hidden/PBRPainter/CompositeMasked");
                }
                return _compositeMasked;
            }
        }

        public static Material ForceAlphaOneMaterial
        {
            get
            {
                if (_forceAlphaOne == null)
                {
                    _forceAlphaOne = CreateHiddenMaterial("Hidden/PBRPainter/ForceAlphaOne");
                }
                return _forceAlphaOne;
            }
        }

        private static Material CreateHiddenMaterial(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"PBR Painter: Shader not found: {shaderName}");
                return new Material(Shader.Find("Hidden/InternalErrorShader"));
            }
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return mat;
        }

        public static void Cleanup()
        {
            if (_brushStamp != null) UnityEngine.Object.DestroyImmediate(_brushStamp);
            if (_layerBlend != null) UnityEngine.Object.DestroyImmediate(_layerBlend);
            if (_compositeMasked != null) UnityEngine.Object.DestroyImmediate(_compositeMasked);
            if (_forceAlphaOne != null) UnityEngine.Object.DestroyImmediate(_forceAlphaOne);
            _brushStamp = null;
            _layerBlend = null;
            _compositeMasked = null;
            _forceAlphaOne = null;
        }
    }

    public static class LayerCompositor
    {
        public static RenderTexture Compose(List<MaterialLayer> layers, ParameterSemantic semantic, int size)
        {
            var result = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            result.filterMode = FilterMode.Bilinear;
            result.wrapMode = TextureWrapMode.Repeat;

            var prev = RenderTexture.active;
            RenderTexture.active = result;
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;

            var mat = PainterMaterials.LayerBlendMaterial;
            foreach (var layer in layers)
            {
                if (!layer.isVisible) continue;
                if (!layer.parameters.TryGetValue(semantic, out var param)) continue;
                param.EnsureInitialized();
                var temp = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                mat.SetTexture("_UnderTex", result);
                mat.SetTexture("_LayerTex", param.workingTexture);
                Graphics.Blit(null, temp, mat, 0);
                Graphics.Blit(temp, result);
                RenderTexture.ReleaseTemporary(temp);
            }

            return result;
        }
    }

    public static class ExportUtils
    {
        public static void ExportTextureToAsset(Texture2D tex, string assetPath, bool sRGB)
        {
            var png = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer != null)
            {
                importer.sRGBTexture = sRGB;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        public static bool IsColorSemantic(ParameterSemantic s)
        {
            return s == ParameterSemantic.BaseColor || s == ParameterSemantic.Emission;
        }

        public static Vector4 ChannelMaskFor(ParameterSemantic s)
        {
            switch (s)
            {
                case ParameterSemantic.BaseColor:
                case ParameterSemantic.Emission:
                    return new Vector4(1, 1, 1, 0); // RGB only
                case ParameterSemantic.Opacity:
                    return new Vector4(0, 0, 0, 1); // A only
                case ParameterSemantic.Roughness:
                case ParameterSemantic.Metallic:
                case ParameterSemantic.Occlusion:
                    return new Vector4(1, 0, 0, 0); // R only
                default:
                    return new Vector4(1, 1, 1, 0);
            }
        }

        public static Color NormalizeBrushColorForSemantic(Color input, ParameterSemantic s)
        {
            switch (s)
            {
                case ParameterSemantic.BaseColor:
                case ParameterSemantic.Emission:
                    return input; // use RGB, alpha as intensity
                case ParameterSemantic.Opacity:
                    return new Color(0, 0, 0, input.a);
                case ParameterSemantic.Roughness:
                case ParameterSemantic.Metallic:
                case ParameterSemantic.Occlusion:
                    float v = input.grayscale;
                    return new Color(v, v, v, input.a);
                default:
                    return input;
            }
        }
    }
}
#endif
