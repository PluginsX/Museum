using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Museum.Debug;

/// <summary>
/// WebGL视频播放器组件
/// 支持跨平台视频播放，自动配置VideoPlayer和RawImage
/// </summary>
[AddComponentMenu("UI/WebGL Video Player")]
[RequireComponent(typeof(VideoPlayer), typeof(RawImage))]
public class WebGLVideoPlayer : MonoBehaviour
{
    #region 组件引用和配置
    private VideoPlayer videoPlayer;
    private RawImage rawImage;

    // 在Inspector面板中填写视频文件名（如"test.mp4"、"demo.mp4"）
    public string videoFileName = "test.mp4";
    // 固定目录：StreamingAssets/Video/（无需修改，对应你的目录）
    public string videoFolder = "Video/";
    #endregion

    #region 事件委托
    /// <summary>
    /// 视频准备完成事件
    /// </summary>
    public System.Action onPrepared;

    /// <summary>
    /// 视频开始播放事件
    /// </summary>
    public System.Action onStarted;

    /// <summary>
    /// 视频播放完成事件
    /// </summary>
    public System.Action onCompleted;

    /// <summary>
    /// 视频播放进度更新事件（参数：当前时间，总时长）
    /// </summary>
    public System.Action<float, float> onProgressUpdated;
    #endregion

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

        // 注册视频播放事件
        RegisterVideoEvents();

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
                Log.Print("Video", "debug", $"视频开始播放：{videoPath}");
                onPrepared?.Invoke(); // 触发准备完成事件
            };
        }
        else
        {
            Log.Print("Video", "error", "视频路径为空或文件不存在！");
        }
    }

    /// <summary>
    /// 注册视频播放相关事件
    /// </summary>
    private void RegisterVideoEvents()
    {
        if (videoPlayer == null) return;

        // 视频开始播放事件
        videoPlayer.started += (source) =>
        {
            Log.Print("Video", "debug", "视频开始播放");
            onStarted?.Invoke();
        };

        // 视频播放完成事件
        videoPlayer.loopPointReached += (source) =>
        {
            Log.Print("Video", "debug", "视频播放完成");
            onCompleted?.Invoke();
        };
    }

    void Update()
    {
        // 实时更新播放进度（可选，用于进度条等UI更新）
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            float currentTime = GetCurrentTime();
            float duration = GetDuration();
            if (duration > 0)
            {
                onProgressUpdated?.Invoke(currentTime, duration);
            }
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

    #region 基本播放控制API
    /// <summary>
    /// 播放视频
    /// </summary>
    public void Play()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Play();
            Log.Print("Video", "debug", "开始播放");
        }
    }

    /// <summary>
    /// 暂停视频
    /// </summary>
    public void Pause()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Pause();
            Log.Print("Video", "debug", "暂停播放");
        }
    }

    /// <summary>
    /// 停止视频（暂停并重置到开头）
    /// </summary>
    public void Stop()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            Log.Print("Video", "debug", "停止播放");
        }
    }

    /// <summary>
    /// 切换播放/暂停状态
    /// </summary>
    public void TogglePlayPause()
    {
        if (videoPlayer.isPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }
    #endregion

    #region 时间控制API
    /// <summary>
    /// 跳转到指定时间（秒）
    /// </summary>
    /// <param name="time">目标时间（秒）</param>
    public void SeekToTime(float time)
    {
        if (videoPlayer != null && videoPlayer.canSetTime)
        {
            videoPlayer.time = Mathf.Clamp(time, 0, GetDuration());
            Log.Print("Video", "debug", $"跳转到时间: {time}s");
        }
    }

    /// <summary>
    /// 跳转到指定进度（0-1）
    /// </summary>
    /// <param name="progress">目标进度（0=开头，1=结尾）</param>
    public void SeekToProgress(float progress)
    {
        float duration = GetDuration();
        if (duration > 0)
        {
            SeekToTime(progress * duration);
        }
    }

    /// <summary>
    /// 获取当前播放时间（秒）
    /// </summary>
    /// <returns>当前时间（秒）</returns>
    public float GetCurrentTime()
    {
        return videoPlayer != null ? (float)videoPlayer.time : 0f;
    }

    /// <summary>
    /// 获取视频总时长（秒）
    /// </summary>
    /// <returns>总时长（秒）</returns>
    public float GetDuration()
    {
        return videoPlayer != null ? (float)videoPlayer.length : 0f;
    }

    /// <summary>
    /// 获取播放进度（0-1）
    /// </summary>
    /// <returns>播放进度</returns>
    public float GetProgress()
    {
        float duration = GetDuration();
        return duration > 0 ? GetCurrentTime() / duration : 0f;
    }
    #endregion

    #region 播放状态查询API
    /// <summary>
    /// 获取是否正在播放
    /// </summary>
    /// <returns>true=正在播放，false=暂停/停止</returns>
    public bool IsPlaying()
    {
        return videoPlayer != null && videoPlayer.isPlaying;
    }

    /// <summary>
    /// 获取是否已准备好播放
    /// </summary>
    /// <returns>true=已准备好，false=未准备好</returns>
    public bool IsPrepared()
    {
        return videoPlayer != null && videoPlayer.isPrepared;
    }

    /// <summary>
    /// 获取是否循环播放
    /// </summary>
    /// <returns>true=循环播放，false=不循环</returns>
    public bool IsLooping()
    {
        return videoPlayer != null && videoPlayer.isLooping;
    }
    #endregion

    #region 音量和速度控制API
    /// <summary>
    /// 设置音量（0-1）
    /// </summary>
    /// <param name="volume">音量值（0=静音，1=最大）</param>
    public void SetVolume(float volume)
    {
        if (videoPlayer != null)
        {
            videoPlayer.SetDirectAudioVolume(0, Mathf.Clamp01(volume));
            Log.Print("Video", "debug", $"设置音量: {volume}");
        }
    }

    /// <summary>
    /// 获取当前音量（0-1）
    /// 注意：VideoPlayer不支持直接获取音量，此方法返回设置的音量值
    /// </summary>
    /// <returns>当前音量值（由于API限制，总是返回1.0f）</returns>
    public float GetVolume()
    {
        // VideoPlayer没有GetDirectAudioVolume方法
        // 音量只能设置，不能获取
        Log.Print("Video", "warning", "VideoPlayer不支持获取音量，返回默认值1.0f");
        return 1f;
    }

    /// <summary>
    /// 设置播放速度（0.5-2.0倍速）
    /// </summary>
    /// <param name="speed">播放速度倍数</param>
    public void SetPlaybackSpeed(float speed)
    {
        if (videoPlayer != null && videoPlayer.canSetPlaybackSpeed)
        {
            videoPlayer.playbackSpeed = Mathf.Clamp(speed, 0.5f, 2.0f);
            Log.Print("Video", "debug", $"设置播放速度: {speed}x");
        }
    }

    /// <summary>
    /// 获取播放速度
    /// </summary>
    /// <returns>播放速度倍数</returns>
    public float GetPlaybackSpeed()
    {
        return videoPlayer != null ? videoPlayer.playbackSpeed : 1f;
    }

    /// <summary>
    /// 设置是否循环播放
    /// </summary>
    /// <param name="loop">true=循环播放，false=不循环</param>
    public void SetLoop(bool loop)
    {
        if (videoPlayer != null)
        {
            videoPlayer.isLooping = loop;
            Log.Print("Video", "debug", $"设置循环播放: {loop}");
        }
    }
    #endregion

    #region 高级控制API
    /// <summary>
    /// 重新加载视频（切换视频源时使用）
    /// </summary>
    /// <param name="newVideoFileName">新的视频文件名</param>
    public void LoadVideo(string newVideoFileName)
    {
        if (string.IsNullOrEmpty(newVideoFileName))
        {
            Log.Print("Video", "error", "视频文件名不能为空");
            return;
        }

        videoFileName = newVideoFileName;
        string videoPath = GetCrossPlatformVideoPath();

        if (!string.IsNullOrEmpty(videoPath))
        {
            videoPlayer.Stop();
            videoPlayer.url = videoPath;
            videoPlayer.Prepare();
            Log.Print("Video", "debug", $"加载新视频: {newVideoFileName}");
        }
        else
        {
            Log.Print("Video", "error", "视频路径无效");
        }
    }

    /// <summary>
    /// 获取视频分辨率
    /// </summary>
    /// <returns>视频分辨率（Vector2Int）</returns>
    public Vector2Int GetVideoResolution()
    {
        if (videoPlayer != null && videoPlayer.texture != null)
        {
            return new Vector2Int(videoPlayer.texture.width, videoPlayer.texture.height);
        }
        return Vector2Int.zero;
    }
    #endregion
}
