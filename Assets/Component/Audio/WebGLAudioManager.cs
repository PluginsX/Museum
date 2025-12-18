using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Events;
using UnityEngine.Networking;
using Museum.Debug;

/// <summary>
/// WebGL平台音频管理器，支持运行时按平台切换播放方案
/// </summary>
[AddComponentMenu("Audio/WebGL Audio Manager")]
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class WebGLAudioManager : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void JS_PlayAudio(string path, int loop, string audioKey);
    [DllImport("__Internal")] private static extern void JS_PauseAudio(string audioKey);
    [DllImport("__Internal")] private static extern void JS_StopAudio(string audioKey);
    [DllImport("__Internal")] private static extern void JS_DestroyAudio(string audioKey);
    [DllImport("__Internal")] private static extern float JS_GetAudioProgress(string audioKey);
    [DllImport("__Internal")] private static extern void JS_SetAudioVolume(string audioKey, float volume);
    [DllImport("__Internal")] private static extern float JS_GetAudioVolume(string audioKey);
    [DllImport("__Internal")] private static extern void JS_SeekAudio(string audioKey, float progress);
#endif

    [Header("基础参数")]
    [Tooltip("StreamingAssets下的音频子目录（末尾需加/，默认：Audio/）")]
    public string audioDirectory = "Audio/";

    [Tooltip("音频资源名（含后缀，如Env_Desert.mp3；默认空，需手动设置或传入）")]
    public string audioFileName = string.Empty;

    [Tooltip("是否循环播放音频（默认：false）")]
    public bool isLoop = false;

    [Tooltip("唤醒时自动播放音频（默认：true；受浏览器自动播放策略限制）")]
    public bool playOnAwake = true;

    [Header("事件")]
    public UnityEvent<string> onAudioEnded;

    public static WebGLAudioManager Instance { get; private set; }

    private AudioSource _audioSource;
    private IAudioBackend _backend;
    private bool _useWebGlBackend;
    private static bool _hasShownUnmutePrompt;

    private string DefaultAudioKey => string.IsNullOrEmpty(audioFileName) ? "default_audio" : audioFileName;
    internal AudioSource UnityAudioSource => _audioSource;

    private interface IAudioBackend
    {
        void Initialize(WebGLAudioManager owner);
        void Play(string fileName, bool loop, string audioKey);
        void Pause(string audioKey);
        void Stop(string audioKey);
        void Destroy(string audioKey);
        float GetProgress(string audioKey);
        void SetVolume(string audioKey, float volume);
        float GetVolume(string audioKey);
        void Seek(string audioKey, float progress);
        void Tick();
        void Dispose();
    }

    #region Unity生命周期
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Log.Print("Audio", "warn", "WebGLAudioManager未挂在根节点，已跳过DontDestroyOnLoad。");
        }

        if (!TryGetComponent(out _audioSource))
        {
            Log.Print("Audio", "error", "缺少AudioSource组件，无法构建音频管理器。");
            enabled = false;
            return;
        }

        _audioSource.playOnAwake = false;

        _useWebGlBackend = ShouldUseWebGlBackend();
        _backend = _useWebGlBackend ? (IAudioBackend)new WebGlJsBackend() : new AudioSourceBackend();
        _backend.Initialize(this);

        if (playOnAwake)
        {
            PlayCurrentAudio();
        }
    }

    private void Update()
    {
        _backend?.Tick();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        _backend?.Destroy(DefaultAudioKey);
        _backend?.Dispose();
        _backend = null;
    }
    #endregion

    #region 外部API
    /// <summary>
    /// 播放音频。
    /// </summary>
    public void PlayAudio(string fileName, bool? loopOverride = null, string audioKey = null)
    {
        if (_backend == null)
        {
            Log.Print("Audio", "error", "音频播放后端尚未初始化。");
            return;
        }

        string resolvedFile = string.IsNullOrEmpty(fileName) ? audioFileName : fileName;
        string key = string.IsNullOrEmpty(audioKey) ? (string.IsNullOrEmpty(resolvedFile) ? DefaultAudioKey : audioKey ?? resolvedFile) : audioKey;
        bool loop = loopOverride ?? isLoop;

        if (_useWebGlBackend && string.IsNullOrEmpty(resolvedFile))
        {
            Log.Print("Audio", "error", "WebGL播放必须提供音频文件名。");
            return;
        }

        _backend.Play(resolvedFile, loop, key);
    }

    /// <summary>
    /// 使用当前Inspector配置的音频参数进行播放。
    /// </summary>
    public void PlayCurrentAudio()
    {
        if (_useWebGlBackend && string.IsNullOrEmpty(audioFileName))
        {
            Log.Print("Audio", "error", "当前音频文件名未设置，无法在WebGL上播放。");
            return;
        }

        _backend?.Play(audioFileName, isLoop, DefaultAudioKey);
    }

    public void PauseAudio()
    {
        _backend?.Pause(DefaultAudioKey);
    }

    public void StopAudio()
    {
        _backend?.Stop(DefaultAudioKey);
    }

    public void DestroyAudio()
    {
        _backend?.Destroy(DefaultAudioKey);
    }

    public float GetAudioProgress()
    {
        return _backend?.GetProgress(DefaultAudioKey) ?? 0f;
    }

    public void SetAudioVolume(float volume)
    {
        float clamped = Mathf.Clamp01(volume);
        _backend?.SetVolume(DefaultAudioKey, clamped);
    }

    public float GetAudioVolume()
    {
        return _backend?.GetVolume(DefaultAudioKey) ?? 1f;
    }

    public void SeekAudio(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        _backend?.Seek(DefaultAudioKey, clamped);
    }
    #endregion

    #region JS回调入口
    public void OnAudioEnded(string audioKey)
    {
        Log.Print("Audio", "debug", $"音频播放完成：{audioKey}");
        onAudioEnded?.Invoke(audioKey);
    }

    public void OnAudioUnlocked()
    {
        Log.Print("Audio", "debug", "收到WebGL音频解锁通知，尝试播放当前音频。");
        PlayCurrentAudio();
    }
    #endregion

    #region 内部工具方法
    private bool ShouldUseWebGlBackend()
    {
#if UNITY_WEBGL
        return Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor;
#else
        return false;
#endif
    }

    internal string BuildWebGlAudioPath(string fileName)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrEmpty(fileName))
        {
            Log.Print("Audio", "error", "音频文件名不能为空！");
            return string.Empty;
        }

        string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, audioDirectory, fileName);
        return fullPath.Replace("\\", "/");
#else
        return string.Empty;
#endif
    }

    internal string BuildLocalStreamingAudioUrl(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            Log.Print("Audio", "error", "音频文件名不能为空！");
            return string.Empty;
        }

        string directory = string.IsNullOrEmpty(audioDirectory) ? string.Empty : audioDirectory;
        string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, directory, fileName);
        fullPath = fullPath.Replace("\\", "/");

        if (!fullPath.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) && !fullPath.StartsWith("file://", System.StringComparison.OrdinalIgnoreCase))
        {
            fullPath = "file://" + fullPath;
        }

        return fullPath;
    }

    internal void RegisterAudioEndedCallback()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string target = gameObject.name.Replace("'", "\\'");
        Application.ExternalEval($@"
            if (typeof window !== 'undefined') {{
                window.onAudioEnded = function(audioKey) {{
                    if (typeof unityInstance !== 'undefined') {{
                        unityInstance.SendMessage('{target}', 'OnAudioEnded', audioKey);
                    }}
                }};
            }}
        ");
#endif
    }

    internal void NotifyAudioEnded(string audioKey)
    {
        OnAudioEnded(audioKey);
    }

    internal void ShowUnmutePromptIfNeeded()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_hasShownUnmutePrompt)
        {
            return;
        }

        _hasShownUnmutePrompt = true;
        Application.ExternalEval("if (window.ShowAudioUnmutePrompt) { window.ShowAudioUnmutePrompt(); }");
        Log.Print("Audio", "debug", "提示用户解除浏览器静音限制。");
#endif
    }
    #endregion

    #region 播放后端实现
    private sealed class AudioSourceBackend : IAudioBackend
    {
        private WebGLAudioManager _owner;
        private AudioSource _audioSource;
        private bool _wasPlaying;
        private bool _monitorEnd;
        private string _currentKey;
        private string _currentClipName;
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();
        private Coroutine _loadingCoroutine;
        private string _pendingFile;
        private bool _pendingLoop;
        private string _pendingAudioKey;

        public void Initialize(WebGLAudioManager owner)
        {
            _owner = owner;
            _audioSource = owner.UnityAudioSource;
        }

        public void Play(string fileName, bool loop, string audioKey)
        {
            if (_audioSource == null)
            {
                Log.Print("Audio", "error", "AudioSource不存在，无法播放音频。");
                return;
            }

            _currentKey = string.IsNullOrEmpty(audioKey) ? _owner.DefaultAudioKey : audioKey;

            if (string.IsNullOrEmpty(fileName))
            {
                if (_audioSource.clip == null)
                {
                    Log.Print("Audio", "warn", "未配置本地AudioClip或音频文件名，无法在编辑器预览。");
                    return;
                }

                StartClipPlayback(loop);
                return;
            }

            if (_clipCache.TryGetValue(fileName, out var cachedClip) && cachedClip != null)
            {
                AssignClipAndPlay(cachedClip, fileName, loop);
                return;
            }

            if (_loadingCoroutine != null && _pendingFile == fileName)
            {
                _pendingLoop = loop;
                _pendingAudioKey = _currentKey;
                return;
            }

            if (_loadingCoroutine != null)
            {
                _owner.StopCoroutine(_loadingCoroutine);
                _loadingCoroutine = null;
            }

            _loadingCoroutine = _owner.StartCoroutine(LoadClipCoroutine(fileName, loop, _currentKey));
        }

        public void Pause(string audioKey)
        {
            if (_audioSource == null)
            {
                return;
            }

            _audioSource.Pause();
            _monitorEnd = false;
            _wasPlaying = _audioSource.isPlaying;
        }

        public void Stop(string audioKey)
        {
            if (_audioSource == null)
            {
                return;
            }

            _audioSource.Stop();
            _audioSource.time = 0f;
            _monitorEnd = false;
            _wasPlaying = false;
        }

        public void Destroy(string audioKey)
        {
            Stop(audioKey);
        }

        public float GetProgress(string audioKey)
        {
            if (_audioSource == null || _audioSource.clip == null)
            {
                return 0f;
            }

            if (Mathf.Approximately(_audioSource.clip.length, 0f))
            {
                return 0f;
            }

            return _audioSource.time / _audioSource.clip.length;
        }

        public void SetVolume(string audioKey, float volume)
        {
            if (_audioSource != null)
            {
                _audioSource.volume = volume;
            }
        }

        public float GetVolume(string audioKey)
        {
            return _audioSource != null ? _audioSource.volume : 1f;
        }

        public void Seek(string audioKey, float progress)
        {
            if (_audioSource == null || _audioSource.clip == null)
            {
                return;
            }

            float length = _audioSource.clip.length;
            _audioSource.time = length * Mathf.Clamp01(progress);
            _monitorEnd = !_audioSource.loop;
        }

        public void Tick()
        {
            if (_audioSource == null || !_monitorEnd)
            {
                _wasPlaying = _audioSource != null && _audioSource.isPlaying;
                return;
            }

            bool isPlaying = _audioSource.isPlaying;
            if (_wasPlaying && !isPlaying)
            {
                _monitorEnd = false;
                _owner?.NotifyAudioEnded(_currentKey);
            }

            _wasPlaying = isPlaying;
        }

        public void Dispose()
        {
            if (_loadingCoroutine != null)
            {
                _owner.StopCoroutine(_loadingCoroutine);
                _loadingCoroutine = null;
            }

            foreach (var clip in _clipCache.Values)
            {
                if (clip != null)
                {
                    Object.DestroyImmediate(clip);
                }
            }
            _clipCache.Clear();

            Stop(_currentKey);
        }

        private void AssignClipAndPlay(AudioClip clip, string fileName, bool loop)
        {
            _audioSource.clip = clip;
            _currentClipName = fileName;
            StartClipPlayback(loop);
        }

        private void StartClipPlayback(bool loop)
        {
            if (_audioSource.clip == null)
            {
                Log.Print("Audio", "warn", "AudioSource未绑定AudioClip，跳过播放。");
                return;
            }

            _audioSource.loop = loop;
            _audioSource.Play();
            _wasPlaying = _audioSource.isPlaying;
            _monitorEnd = !_audioSource.loop;
        }

        private IEnumerator LoadClipCoroutine(string fileName, bool loop, string audioKey)
        {
            _pendingFile = fileName;
            _pendingLoop = loop;
            _pendingAudioKey = audioKey;

#if UNITY_WEBGL && !UNITY_EDITOR
            yield break;
#else
            string url = _owner.BuildLocalStreamingAudioUrl(fileName);
            if (string.IsNullOrEmpty(url))
            {
                _loadingCoroutine = null;
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, ResolveAudioType(fileName)))
            {
                yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                if (request.result != UnityWebRequest.Result.Success)
#else
                if (request.isHttpError || request.isNetworkError)
#endif
                {
                    Log.Print("Audio", "error", $"编辑器音频加载失败：{request.error} ({fileName})");
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        clip.name = fileName;
                        _clipCache[fileName] = clip;
                        _currentKey = _pendingAudioKey;
                        AssignClipAndPlay(clip, fileName, _pendingLoop);
                    }
                }
            }
#endif

            _pendingFile = null;
            _loadingCoroutine = null;
        }

        private static AudioType ResolveAudioType(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return AudioType.UNKNOWN;
            }

            string extension = System.IO.Path.GetExtension(fileName)?.ToLowerInvariant();
            switch (extension)
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".aiff":
                case ".aif": return AudioType.AIFF;
                case ".mp3":
                default: return AudioType.MPEG;
            }
        }
    }

    private sealed class WebGlJsBackend : IAudioBackend
    {
        private WebGLAudioManager _owner;

        public void Initialize(WebGLAudioManager owner)
        {
            _owner = owner;
            _owner.RegisterAudioEndedCallback();
        }

        public void Play(string fileName, bool loop, string audioKey)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string path = _owner.BuildWebGlAudioPath(fileName);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _owner.ShowUnmutePromptIfNeeded();
            JS_PlayAudio(path, loop ? 1 : 0, audioKey);
#else
            Log.Print("Audio", "warn", "当前并非WebGL运行环境，无法使用JS音频后端。");
#endif
        }

        public void Pause(string audioKey)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JS_PauseAudio(audioKey);
#endif
        }

        public void Stop(string audioKey)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JS_StopAudio(audioKey);
#endif
        }

        public void Destroy(string audioKey)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JS_DestroyAudio(audioKey);
#endif
        }

        public float GetProgress(string audioKey)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return JS_GetAudioProgress(audioKey);
#else
            return 0f;
#endif
        }

        public void SetVolume(string audioKey, float volume)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JS_SetAudioVolume(audioKey, volume);
#endif
        }

        public float GetVolume(string audioKey)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return JS_GetAudioVolume(audioKey);
#else
            return 1f;
#endif
        }

        public void Seek(string audioKey, float progress)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JS_SeekAudio(audioKey, progress);
#endif
        }

        public void Tick()
        {
            // JS后端无需逐帧轮询
        }

        public void Dispose()
        {
            // 无需额外释放
        }
    }
    #endregion
}
