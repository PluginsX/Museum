using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// WebGL视频播放器组件
/// 支持跨平台视频播放，自动配置VideoPlayer和RawImage
/// </summary>
[AddComponentMenu("UI/WebGL Video Player")]
[RequireComponent(typeof(VideoPlayer), typeof(RawImage))]
public class WebGLVideoPlayer : MonoBehaviour
{
    private VideoPlayer videoPlayer;
    private RawImage rawImage;
    // 在Inspector面板中填写视频文件名（如"test.mp4"、"demo.mp4"）
    public string videoFileName = "test.mp4";
    // 固定目录：StreamingAssets/Video/（无需修改，对应你的目录）
    public string videoFolder = "Video/";

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        rawImage = GetComponent<RawImage>();

        // 初始化VideoPlayer：渲染到RawImage的RenderTexture
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        // 创建RenderTexture（分辨率可根据视频调整，这里以1920*1080为例）
        videoPlayer.targetTexture = new RenderTexture(1920, 1080, 0);
        rawImage.texture = videoPlayer.targetTexture;

        // 获取跨平台的视频路径
        string videoPath = GetCrossPlatformVideoPath();
        if (!string.IsNullOrEmpty(videoPath))
        {
            videoPlayer.url = videoPath;
            // 预加载并播放视频
            videoPlayer.Prepare();
            videoPlayer.prepareCompleted += (source) =>
            {
                videoPlayer.Play();
                Debug.Log("视频开始播放：" + videoPath);
            };
        }
        else
        {
            Debug.LogError("视频路径为空或文件不存在！");
        }
    }

    /// <summary>
    /// 拼接跨平台的视频路径，适配你的StreamingAssets/Video/目录
    /// </summary>
    private string GetCrossPlatformVideoPath()
    {
        string fullPath = "";
        // 拼接StreamingAssets + Video文件夹 + 视频文件名
        string relativePath = System.IO.Path.Combine(videoFolder, videoFileName);

#if UNITY_WEBGL && !UNITY_EDITOR
        // === WebGL平台（关键！）===
        // Unity 2022.3中，WebGL的StreamingAssets路径是相对路径：
        // Build目录同级/StreamingAssets/Video/test.mp4
        // Application.streamingAssetsPath会自动返回正确的相对路径，直接拼接即可
        fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
        // 注意：WebGL中路径的分隔符会自动处理，无需手动替换（Unity会转成/）

#elif UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        // === 本地PC平台（Windows/Mac）===
        // 需要添加"file:///"前缀，否则无法加载
        fullPath = "file:///" + System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
        // 修复Windows的路径分隔符（可选，System.IO.Path会自动处理）
        fullPath = fullPath.Replace("\\", "/");

#elif UNITY_ANDROID
        // === 安卓平台 ===
        // 安卓的StreamingAssets路径是jar:file:///android_asset/，Application.streamingAssetsPath会自动包含
        fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);

#elif UNITY_IOS
        // === iOS平台 ===
        // iOS的StreamingAssets路径是应用内的路径，直接拼接即可
        fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);

#endif

        return fullPath;
    }

    // 可选：暂停/继续播放方法（如需UI控制）
    public void TogglePlayPause()
    {
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }
        else
        {
            videoPlayer.Play();
        }
    }
}
