using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.Events;

/// <summary>
/// WebGL平台音频管理器（最终版）
/// 核心参数：音频目录、音频资源名、循环、唤醒时播放
/// 完整保留所有音频控制API，支持资源名自动补全后缀
/// </summary>
[AddComponentMenu("Audio/WebGL Audio Manager")]
[DisallowMultipleComponent]
public class WebGLAudioManager : MonoBehaviour
{
    #region JS插件导入（与JSLib中的函数名完全匹配，带JS_前缀）
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JS_PlayAudio(string path, int loop, string audioKey);

    [DllImport("__Internal")]
    private static extern void JS_PauseAudio(string audioKey);

    [DllImport("__Internal")]
    private static extern void JS_StopAudio(string audioKey);

    [DllImport("__Internal")]
    private static extern void JS_DestroyAudio(string audioKey);

    [DllImport("__Internal")]
    private static extern float JS_GetAudioProgress(string audioKey);

    [DllImport("__Internal")]
    private static extern void JS_SetAudioVolume(string audioKey, float volume);

    [DllImport("__Internal")]
    private static extern float JS_GetAudioVolume(string audioKey);

    [DllImport("__Internal")]
    private static extern void JS_SeekAudio(string audioKey, float progress);
#endif
    #endregion

    #region 核心参数（仅保留四个关键参数）
    [Tooltip("StreamingAssets下的音频子目录（末尾需加/，默认：Audio/）")]
    public string audioDirectory = "Audio/";

    [Tooltip("音频资源名（含后缀，如Env_Desert.mp3；默认空，需手动设置或传入）")]
    public string audioFileName = "";

    [Tooltip("是否循环播放音频（默认：false）")]
    public bool isLoop = false;

    [Tooltip("唤醒时自动播放音频（默认：true；受浏览器自动播放策略限制）")]
    public bool playOnAwake = true;
    #endregion

    #region 事件与状态
    // 音频播放完成事件（参数：音频唯一标识）
    public UnityEvent<string> onAudioEnded;

    // 音频唯一标识（默认使用音频文件名，可自定义）
    private string defaultAudioKey => string.IsNullOrEmpty(audioFileName) ? "default_audio" : audioFileName;

    // 单例模式（全局唯一，方便外部调用）
    public static WebGLAudioManager Instance { get; private set; }
    #endregion

    #region 生命周期
    private void Awake()
    {
        // 单例初始化：防止重复实例化
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 跨场景保留
            // 注册JS播放完成回调
            RegisterAudioEndedCallback();
        }
        else
        {
            Destroy(gameObject); // 销毁重复实例
            return;
        }

        // 唤醒时播放（受浏览器策略限制，可能需要用户交互）
        if (playOnAwake && !string.IsNullOrEmpty(audioFileName))
        {
            PlayAudio(audioFileName, isLoop, defaultAudioKey);
        }
    }

    private void OnDestroy()
    {
        // 销毁音频实例，释放内存
        DestroyAudio();
    }
    #endregion

    #region 内部辅助方法
    /// <summary>
    /// 注册JS的音频播放完成回调到C#
    /// </summary>
    private void RegisterAudioEndedCallback()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.ExternalEval(@"
            // 定义全局回调函数，供JS触发
            window.onAudioEnded = function (audioKey) {
                // 调用Unity中的OnAudioEnded方法
                unityInstance.SendMessage('AudioManager', 'OnAudioEnded', audioKey);
            };
        ");
#endif
    }

    /// <summary>
    /// 拼接WebGL平台的音频路径（适配StreamingAssets）
    /// </summary>
    /// <param name="fileName">音频文件名（含后缀）</param>
    /// <returns>完整的浏览器可识别路径</returns>
    private string GetAudioPath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogError("[Audio] 音频文件名不能为空！");
            return "";
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // 拼接路径：StreamingAssets / audioDirectory / fileName
        string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, audioDirectory, fileName);
        // 替换路径分隔符为浏览器识别的/（Windows下是\）
        fullPath = fullPath.Replace("\\", "/");
        return fullPath;
#else
        return "";
#endif
    }

    /// <summary>
    /// 音频播放完成的回调方法（由JS触发）
    /// </summary>
    /// <param name="audioKey">音频唯一标识</param>
    public void OnAudioEnded(string audioKey)
    {
        Debug.Log($"[Audio] 音频播放完成：{audioKey}");
        onAudioEnded?.Invoke(audioKey);
    }

    /// <summary>
    /// 音频解锁完成的回调方法（由WebGL模板JS触发）
    /// </summary>
    public void OnAudioUnlocked()
    {
        Debug.Log("[Audio] 音频上下文已解锁，可以正常播放音频");
        // 这里可以添加音频解锁后的逻辑，比如自动播放背景音乐
        PlayCurrentAudio();
    }
    #endregion

    #region 对外公开的音频控制API（完整保留，支持动态参数）
    /// <summary>
    /// 播放音频（核心API，支持动态传入参数）
    /// </summary>
    /// <param name="fileName">音频文件名（含后缀，如Env_Desert.mp3）</param>
    /// <param name="isLoop">是否循环播放（默认使用Inspector配置的isLoop参数）</param>
    /// <param name="audioKey">音频唯一标识（默认使用文件名，不可重复）</param>
    public void PlayAudio(string fileName, bool? isLoop = null, string audioKey = null)
    {
        string path = GetAudioPath(fileName);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // 若未传入isLoop，使用Inspector面板配置的参数；否则使用传入的参数
        bool loop = isLoop ?? this.isLoop;
        // 若未指定audioKey，使用默认标识
        string key = string.IsNullOrEmpty(audioKey) ? (string.IsNullOrEmpty(fileName) ? "default_audio" : fileName) : audioKey;

#if UNITY_WEBGL && !UNITY_EDITOR
        JS_PlayAudio(path, loop ? 1 : 0, key);
#else
        // 非WebGL平台：仅打印日志，方便调试
        Debug.Log($"[Audio] 非WebGL平台，准备播放音频：{path}");
#endif
    }

    /// <summary>
    /// 暂停当前配置的音频
    /// </summary>
    public void PauseAudio()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JS_PauseAudio(defaultAudioKey);
#endif
    }

    /// <summary>
    /// 停止当前配置的音频（暂停并重置播放位置）
    /// </summary>
    public void StopAudio()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JS_StopAudio(defaultAudioKey);
#endif
    }

    /// <summary>
    /// 销毁当前配置的音频实例（释放内存，不再使用时调用）
    /// </summary>
    public void DestroyAudio()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JS_DestroyAudio(defaultAudioKey);
#endif
    }

    /// <summary>
    /// 获取当前配置音频的播放进度（0~1）
    /// </summary>
    /// <returns>播放进度（0=未开始，1=完成）</returns>
    public float GetAudioProgress()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return JS_GetAudioProgress(defaultAudioKey);
#else
        return 0f;
#endif
    }

    /// <summary>
    /// 设置当前配置音频的音量（0~1）
    /// </summary>
    /// <param name="volume">音量值（0=静音，1=最大）</param>
    public void SetAudioVolume(float volume)
    {
        // 限制音量在0~1之间，防止超出范围
        float clampedVolume = Mathf.Clamp01(volume);

#if UNITY_WEBGL && !UNITY_EDITOR
        JS_SetAudioVolume(defaultAudioKey, clampedVolume);
#endif
    }

    /// <summary>
    /// 获取当前配置音频的音量（0~1）
    /// </summary>
    /// <returns>当前音量值</returns>
    public float GetAudioVolume()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return JS_GetAudioVolume(defaultAudioKey);
#else
        return 1f;
#endif
    }

    /// <summary>
    /// 跳转到当前配置音频的指定进度（0~1）
    /// </summary>
    /// <param name="progress">目标进度（0=开头，1=结尾）</param>
    public void SeekAudio(float progress)
    {
        // 限制进度在0~1之间，防止超出范围
        float clampedProgress = Mathf.Clamp01(progress);

#if UNITY_WEBGL && !UNITY_EDITOR
        JS_SeekAudio(defaultAudioKey, clampedProgress);
#endif
    }
    #endregion

    #region 快捷API（基于当前配置的音频参数）
    /// <summary>
    /// 播放当前配置的音频（使用Inspector中的参数）
    /// </summary>
    /// <param name="isLoop">是否循环播放（默认使用Inspector配置的isLoop参数）</param>
    public void PlayCurrentAudio()
    {
        if (string.IsNullOrEmpty(audioFileName))
        {
            Debug.LogError("[Audio] 当前音频文件名未设置！");
            return;
        }
        PlayAudio(audioFileName, isLoop, defaultAudioKey);
    }
    #endregion
}
