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
        private RenderTexture _currentRT; // 当前已处理的RT，避免重复创建

        // 初始化目标RT
        public void Setup(RenderTexture targetRT)
        {
            // 如果RT没有改变，跳过重新创建
            if (_targetRT == targetRT && _targetRTHandle != null)
                return;

            _targetRT = targetRT;

            if (_targetRT == null)
            {
                Debug.LogWarning("[URPCameraToRTFeature] TargetRT is null");
                _currentRT = null;
                return;
            }

            // 释放旧的RTHandle
            if (_targetRTHandle != null)
            {
                _targetRTHandle.Release();
                _targetRTHandle = null;
            }

            // 创建RTHandle（依赖外部RT已正确配置）
            _targetRTHandle = RTHandles.Alloc(
                _targetRT,
                name: "TargetRT_Handle"
            );

            _currentRT = _targetRT;
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
            if (_targetRT == null || _targetRTHandle == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(_profilerTag);

            // 使用标准的Blit命令，确保URP兼容性
            cmd.Blit(_cameraColorTargetHandle.nameID, _targetRTHandle.nameID);

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
