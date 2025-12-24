using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// URP 2022.3 适配：摄像机画面同步输出到屏幕+RenderTexture
public class URPCameraToRTFeature : ScriptableRendererFeature
{
    // 自定义渲染通道（核心逻辑）
    private class URPCameraToRTPass : ScriptableRenderPass
    {
        private RTHandle _cameraColorTargetHandle; // 替换旧的RenderTargetIdentifier
        private RTHandle _targetRTHandle; // 目标RT的句柄
        private readonly string _profilerTag = "CameraToRT";
        private RenderTexture _targetRT; // 外部传入的RenderTexture

        // 初始化目标RT
        public void Setup(RenderTexture targetRT)
        {
            _targetRT = targetRT;
            // 创建RTHandle（适配URP新接口）
            if (_targetRT != null && _targetRT.IsCreated())
            {
                _targetRTHandle = RTHandles.Alloc(
                    _targetRT,
                    name: "TargetRT_Handle"
                );
            }
        }

        // 摄像机初始化时获取颜色目标句柄
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 替换过时的 cameraColorTarget → cameraColorTargetHandle
            _cameraColorTargetHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
        }

        // 执行渲染：复制画面到RT
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 校验RT有效性
            if (_targetRT == null || !_targetRT.IsCreated() || _targetRTHandle == null) 
                return;

            CommandBuffer cmd = CommandBufferPool.Get(_profilerTag);
            
            // 替换过时的 Blit → 使用URP兼容的Blit（支持RTHandle）
            Blitter.BlitCameraTexture(cmd, _cameraColorTargetHandle, _targetRTHandle, Vector2.one, 0);

            // 执行并释放命令缓冲区
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // 清理资源（关键：释放RTHandle避免内存泄漏）
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            _cameraColorTargetHandle = null;
            if (_targetRTHandle != null)
            {
                _targetRTHandle.Release();
                _targetRTHandle = null;
            }
        }
    }

    [Header("目标渲染纹理")]
    public RenderTexture targetRT; // 拖入创建好的RenderTexture
    private URPCameraToRTPass _cameraToRTPass;

    // 初始化渲染特性
    public override void Create()
    {
        _cameraToRTPass = new URPCameraToRTPass();
        // 渲染时机：后期处理完成后（包含所有画面）
        _cameraToRTPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    // 将渲染通道注入URP管线
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 仅对主摄像机生效（可按需修改过滤逻辑）
        if (renderingData.cameraData.camera.CompareTag("MainCamera"))
        {
            _cameraToRTPass.Setup(targetRT);
            renderer.EnqueuePass(_cameraToRTPass);
        }
    }

    // 销毁时清理资源
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _cameraToRTPass?.OnCameraCleanup(null);
    }
}