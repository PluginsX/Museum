using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 通用URP卡通描边后处理（修复cameraColorTargetHandle时机问题）
public class ToonOutlineRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class OutlineSettings
    {
        [Tooltip("描边颜色")]
        public Color outlineColor = Color.black;

        [Tooltip("描边厚度（0.1-5）")]
        [Range(0.1f, 5f)]
        public float outlineThickness = 1.0f;

        [Tooltip("深度敏感度（0.1-10）")]
        [Range(0.1f, 10f)]
        public float depthSensitivity = 1.0f;

        [Tooltip("法线敏感度（0.1-10）")]
        [Range(0.1f, 10f)]
        public float normalSensitivity = 1.0f;

        [Tooltip("后处理执行时机（建议AfterRenderingPostProcessing）")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    [Header("卡通描边设置")]
    public OutlineSettings settings = new OutlineSettings();

    private ToonOutlineRenderPass _outlinePass;
    private Material _outlineMaterial;

    public override void Create()
    {
        // 加载Shader并创建材质
        Shader outlineShader = Shader.Find("Custom/URP/ToonOutlinePostProcess");
        if (outlineShader != null)
        {
            _outlineMaterial = CoreUtils.CreateEngineMaterial(outlineShader);
        }
        else
        {
            Debug.LogError("找不到描边Shader！请确认Shader路径为 Custom/URP/ToonOutlinePostProcess");
            return;
        }

        // 初始化渲染通道（仅传入材质和参数，不传递纹理句柄）
        _outlinePass = new ToonOutlineRenderPass(_outlineMaterial, settings);
        _outlinePass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_outlineMaterial == null || !renderingData.cameraData.postProcessEnabled)
            return;

        // 仅将通道加入队列，不访问cameraColorTargetHandle
        renderer.EnqueuePass(_outlinePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        CoreUtils.Destroy(_outlineMaterial);
        _outlinePass?.Dispose();
    }

    // 核心修复：在RenderPass内部获取cameraColorTargetHandle
    private class ToonOutlineRenderPass : ScriptableRenderPass
    {
        private readonly Material _outlineMat;
        private readonly OutlineSettings _settings;
        private RTHandle _cameraColorTargetHandle; // 延迟到Execute阶段获取
        private RTHandle _tempRT; // 临时RT
        private readonly string _profilerTag = "ToonOutlinePostProcess";

        public ToonOutlineRenderPass(Material mat, OutlineSettings settings)
        {
            _outlineMat = mat;
            _settings = settings;
        }

        // 关键：在OnCameraSetup阶段获取纹理句柄（此时纹理已创建）
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 正确时机获取cameraColorTargetHandle
            _cameraColorTargetHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            
            // 创建临时RT（匹配摄像机目标纹理规格）
            RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
            rtDesc.depthBufferBits = 0; // 后处理无需深度
            _tempRT = RTHandles.Alloc(rtDesc, name: "TempOutlineRT");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 校验必要条件
            if (_outlineMat == null || _cameraColorTargetHandle == null || _tempRT == null)
                return;

            // 1. 绑定Shader参数
            _outlineMat.SetColor("_OutlineColor", _settings.outlineColor);
            _outlineMat.SetFloat("_OutlineThickness", _settings.outlineThickness);
            _outlineMat.SetFloat("_DepthSensitivity", _settings.depthSensitivity);
            _outlineMat.SetFloat("_NormalSensitivity", _settings.normalSensitivity);

            // 2. 执行描边渲染
            CommandBuffer cmd = CommandBufferPool.Get(_profilerTag);
            cmd.BeginSample(_profilerTag);

            // 核心：原纹理 → 临时RT（执行描边）→ 原纹理
            Blitter.BlitCameraTexture(cmd, _cameraColorTargetHandle, _tempRT, _outlineMat, 0);
            Blitter.BlitCameraTexture(cmd, _tempRT, _cameraColorTargetHandle);

            cmd.EndSample(_profilerTag);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // 清理资源（此时纹理已可安全释放）
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (_tempRT != null)
            {
                _tempRT.Release();
                _tempRT = null;
            }
            _cameraColorTargetHandle = null;
        }

        public void Dispose()
        {
            _tempRT?.Release();
        }
    }
}