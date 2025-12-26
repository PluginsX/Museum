using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// URP 2022.3 适配：摄像机画面同步输出到屏幕 + RenderTexture (WebGL兼容版)
public class URPCameraToRTFeature : ScriptableRendererFeature
{
    private class URPCameraToRTPass : ScriptableRenderPass
    {
        private RTHandle _source;      // 摄像机源纹理
        private RTHandle _destination; // 目标RT句柄
        private RenderTexture _targetRT; // 外部引用的RT
        private readonly string _profilerTag = "CameraToRT_Blit";

        public void Setup(RenderTexture targetRT)
        {
            _targetRT = targetRT;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 获取摄像机源 (需配合 URP Asset -> Intermediate Texture: Always)
            _source = renderingData.cameraData.renderer.cameraColorTargetHandle;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 基础校验
            if (_targetRT == null || _source == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(_profilerTag);

            // ---------------------------------------------------------
            // 关键修复点 1：安全地获取 RTHandle
            // ---------------------------------------------------------
            // 检查句柄是否需要创建或更新
            if (_destination == null || _destination.rt != _targetRT)
            {
                // 使用 Alloc 包装外部 RT。
                // 注意：这种方式生成的 Handle，其生命周期由外部 RT 决定。
                _destination = RTHandles.Alloc(_targetRT);
            }

            // ---------------------------------------------------------
            // 关键修复点 2：使用 Blitter 处理翻转和拷贝
            // ---------------------------------------------------------
            // BlitCameraTexture 会自动处理 WebGL 的 Y-Flip 问题
            Blitter.BlitCameraTexture(cmd, _source, _destination);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // ---------------------------------------------------------
            // 关键修复点 3：不要 Release 外部资源！
            // ---------------------------------------------------------
            // _destination 包装的是一个 Project Asset (RenderTexture)。
            // 调用 Release() 会尝试销毁该 Asset，导致 "Destroying assets is not permitted" 错误。
            // 这里只需要将引用置空，让 GC 回收 Handle 对象本身即可。
            
            _destination = null; 
            
            // _source 是 URP 内部管理的，也不需要我们 Release
            _source = null;
        }
    }

    [Header("目标渲染纹理 (需设为 No Depth)")]
    public RenderTexture targetRT;
    
    private URPCameraToRTPass _cameraToRTPass;

    public override void Create()
    {
        _cameraToRTPass = new URPCameraToRTPass();
        // 建议在后处理之后执行，以捕获最终画面
        _cameraToRTPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 仅在 Game 视图或 Scene 视图，且是主摄像机时运行
        if (renderingData.cameraData.cameraType == CameraType.Game || renderingData.cameraData.cameraType == CameraType.SceneView)
        {
            if (renderingData.cameraData.camera.CompareTag("MainCamera") && targetRT != null)
            {
                _cameraToRTPass.Setup(targetRT);
                renderer.EnqueuePass(_cameraToRTPass);
            }
        }
    }
}