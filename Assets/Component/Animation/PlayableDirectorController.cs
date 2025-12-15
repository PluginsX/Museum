using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System.Collections.Generic;
using Museum.Component.Animation;

namespace Museum.Component.Animation
{
[System.Serializable]
public class TrackPlaySetting
{
    public TrackAsset track;
    public int playCount; // 循环次数，0表示无限播放，1=仅一次，2=两次等等
}

[RequireComponent(typeof(PlayableDirector))]
public class PlayableDirectorController : MonoBehaviour
{
    [Header("Timeline 配置")]
    public PlayableDirector playableDirector;
    [Header("播放器设置")]
    public bool playOnAwake = true;
    public float initialTime = 0f;
    public LoopType loopType = LoopType.None; // 循环播放方式
    public float Speed = 1f; // 播放速度（正数正向，负数反向）
    public bool ManualControl = false; // 手动控制模式
    public float CurrentRatio = 0f; // 当前时间比例 [0-1]
    [Header("轨道设置（自动从Timeline读取）")]
    public List<TrackPlaySetting> trackSettings;

    private int loopCount = 0; // 当前播放循环次数
    private bool isPlaying = false; // 自定义播放状态
    private float lastUpdateTime = 0f; // 上一帧的时间
    private float duration = 0f; // Timeline总时长（缓存，避免重复获取）

    // 公共属性，供编辑器访问
    public bool IsPlaying { get { return isPlaying; } }

    void OnValidate()
    {
        // 处理手动控制模式变化
        if (ManualControl)
        {
            isPlaying = false;
        }
        
        // 限制CurrentRatio的范围
        CurrentRatio = Mathf.Clamp(CurrentRatio, 0f, 1f);
        
        // 更新duration值
        if (playableDirector != null)
        {
            duration = (float)playableDirector.duration;
        }
        
        // 读取Timeline轨道设置
        if (playableDirector != null && playableDirector.playableAsset != null)
        {
            var timeline = playableDirector.playableAsset as TimelineAsset;
            if (timeline != null)
            {
                var outputs = timeline.GetOutputTracks();
                var outputList = new List<TrackAsset>();
                foreach (var track in outputs)
                {
                    if (!(track is UnityEngine.Timeline.MarkerTrack))
                    {
                        outputList.Add(track);
                    }
                }
                if (trackSettings == null || trackSettings.Count != outputList.Count)
                {
                    trackSettings = new List<TrackPlaySetting>();
                    foreach (var track in outputList)
                    {
                        trackSettings.Add(new TrackPlaySetting
                        {
                            track = track,
                            playCount = 0 // 0表示无限播放
                        });
                    }
                }
                else
                {
                    // 更新tracks，确保顺序一致
                    int i = 0;
                    foreach (var track in outputList)
                    {
                        if (i < trackSettings.Count)
                        {
                            trackSettings[i].track = track;
                        }
                        i++;
                    }
                }
            }
        }
    }

    void Awake()
    {
        loopCount = 0;
        foreach (var setting in trackSettings)
        {
            if (setting.track != null)
            {
                setting.track.muted = false; // 确保未静音
            }
        }

        // 自动获取PlayableDirector组件
        if (playableDirector == null)
        {
            playableDirector = GetComponent<PlayableDirector>();
        }

        // 配置播放器参数
        if (playableDirector != null)
        {
            // 更新duration值
            duration = (float)playableDirector.duration;
            
            // 取消PlayableDirector的唤醒时自动播放，避免播放冲突
            playableDirector.playOnAwake = false;
            
            // 禁用PlayableDirector的内置循环功能，完全由自定义逻辑控制
            playableDirector.extrapolationMode = DirectorWrapMode.Hold;
            
            playableDirector.Stop();
            playableDirector.time = initialTime;
            playableDirector.Evaluate();
        }
    }

    void Start()
    {
        // 再次确认PlayableDirector的初始化设置，确保万无一失
        if (playableDirector != null)
        {
            // 再次取消PlayableDirector的唤醒时自动播放，避免播放冲突
            playableDirector.playOnAwake = false;
            
            // 确保当前状态与设置一致
            playableDirector.Stop();
            playableDirector.time = initialTime;
            // 确保禁用PlayableDirector的内置循环功能
            playableDirector.extrapolationMode = DirectorWrapMode.Hold;
            playableDirector.Evaluate();
        }
        
        // 根据playOnAwake设置决定是否自动播放
        if (playOnAwake && playableDirector != null)
        {
            Play();
        }
    }

    // 核心方法：跳转到指定时间并强制采样
    public void JumpToTime(float time)
    {
        if (playableDirector == null) return;

        // 限制时间范围
        // 当时间接近或等于duration时，将其设置为略小于duration的值
        // 因为Unity Timeline的有效范围是[0, duration)，不包括duration本身
        if (time >= duration && duration > 0)
        {
            time = duration - 0.001f;
        }
        else
        {
            time = Mathf.Clamp(time, 0, duration);
        }

        // 设置时间并强制采样
        playableDirector.time = time;
        playableDirector.Evaluate();
    }

    // 核心方法：跳转到指定比例对应时间并强制采样
    public void JumpToTimeByRatio(float Ratio)
    {
        if (playableDirector == null) return;

        // 停止播放
        isPlaying = false;
        // 限制比例范围
        Ratio = Mathf.Clamp(Ratio, 0f, 1f);
        // 计算对应的时间点
        float time = Ratio * duration;
        
        // 处理边界情况：当Ratio为1时，将时间设置为略小于duration的值
        // 因为Unity Timeline的有效范围是[0, duration)，不包括duration本身
        if (Ratio >= 1f && duration > 0)
        {
            time = duration - 0.001f;
        }
        else
        {
            // 限制时间范围
            time = Mathf.Clamp(time, 0, duration);
        }

        // 设置时间并强制采样
        playableDirector.time = time;
        playableDirector.Evaluate();
    }

    // 播放控制API
    public void PlayFromStart()
    {
        if (playableDirector == null) return;
        if (ManualControl) return; // 手动控制模式下不执行自动播放
        
        loopCount = 0;
        foreach (var setting in trackSettings)
        {
            if (setting.track != null)
            {
                setting.track.muted = false;
            }
        }
        
        // 跳转到初始时间
        JumpToTime(0);
        
        // 设置正向播放
        Speed = Mathf.Abs(Speed);
        
        isPlaying = true;
        lastUpdateTime = Time.time;
    }

    public void Pause()
    {
        isPlaying = false;
    }

    public void Resume()
    {
        if (playableDirector == null) return;
        if (ManualControl) return; // 手动控制模式下不执行自动播放
        isPlaying = true;
        lastUpdateTime = Time.time;
    }

    public void Play()
    {
        if (playableDirector == null) return;
        if (ManualControl) return; // 手动控制模式下不执行自动播放

        if(CurrentRatio >= 1)
        {
            PlayFromStart();
            return;
        }
        // 确保正向播放
        Speed = Mathf.Abs(Speed);
        isPlaying = true;
        lastUpdateTime = Time.time;
    }

    public void Stop()
    {
        if (playableDirector == null) return;

        // 停止播放
        isPlaying = false;
        
        // 重置时间到初始位置
        JumpToTime(initialTime);
        CurrentRatio = 0;
        
        // 重置播放速度为正值（正向播放）
        Speed = Mathf.Abs(Speed);
        
        // 重置计数系统
        loopCount = 0;
        
        // 取消所有轨道mute状态
        foreach (var setting in trackSettings)
        {
            if (setting.track != null)
            {
                setting.track.muted = false;
            }
        }
    }

    public void Reverse()
    {
        if (playableDirector == null) return;
        if (ManualControl) return; // 手动控制模式下不执行自动播放
        
        if(CurrentRatio <= 0)
        {
            PlayFromEnd();
            return;
        }
        // 设置反向播放（保持速度大小，只改变方向为负）
        Speed = -Mathf.Abs(Speed);
        
        // 确保处于播放状态
        isPlaying = true;
        
        // 更新最后更新时间，确保速度变化立即生效
        lastUpdateTime = Time.time;
    }

    public void PlayFromEnd()
    {
        if (playableDirector == null) return;
        if (ManualControl) return; // 手动控制模式下不执行自动播放
        
        loopCount = 0;
        foreach (var setting in trackSettings)
        {
            if (setting.track != null)
            {
                setting.track.muted = false;
            }
        }
        
        // 跳转到末尾
        JumpToTime(duration);
        
        // 设置反向播放
        Speed = -Mathf.Abs(Speed);
        
        isPlaying = true;
        lastUpdateTime = Time.time;
    }

    /// <summary>
    /// 重置所有轨道的播放状态，用于重新开始动画
    /// </summary>
    public void ResetTracks()
    {
        loopCount = 0;
        foreach (var setting in trackSettings)
        {
            if (setting.track != null)
            {
                setting.track.muted = false;
            }
        }
        JumpToTime(0);
    }

    /// <summary>
    /// 处理循环完成事件：增加循环计数并mute需要的轨道
    /// </summary>
    private void TriggerLoop()
    {
        // 检查是否还有轨道需要在这次循环后mute
        int futureCount = loopCount + 1;
        bool canCount = false;
        for (int i = 0; i < trackSettings.Count; i++)
        {
            var setting = trackSettings[i];
            if (setting.playCount == 0 || futureCount <= setting.playCount)
            {
                canCount = true;
                break;
            }
        }

        if (canCount)
        {
            loopCount++;

            // 检查每个轨道是否需要mute
            bool anyMuted = false;
            for (int i = 0; i < trackSettings.Count; i++)
            {
                var setting = trackSettings[i];
                if (setting.playCount > 0 && loopCount >= setting.playCount && setting.track != null)
                {
                    setting.track.muted = true;
                    anyMuted = true;
                }
            }
            if (anyMuted)
            {
                // 延迟执行RebuildGraph
                StartCoroutine(RebuildGraphNextFrame());
            }
        }
    }

    /// <summary>
    /// 在下一帧重建PlayableGraph，避免影响当前播放
    /// </summary>
    private System.Collections.IEnumerator RebuildGraphNextFrame()
    {
        yield return null;
        if (playableDirector != null)
        {
            playableDirector.RebuildGraph();
        }
    }

    void Update()
    {
        // 如果PlayableDirector被禁用，重置计数器并取消所有轨道mute
        if (playableDirector == null || !playableDirector.enabled || !playableDirector.gameObject.activeInHierarchy)
        {
            loopCount = 0;
            isPlaying = false;
            foreach (var setting in trackSettings)
            {
                if (setting.track != null)
                {
                    setting.track.muted = false;
                }
            }
            return;
        }

        // 更新duration值（确保播放过程中持续有效，可能动态变化）
        duration = (float)playableDirector.duration;

        // 手动控制模式下不执行自动播放
        if (ManualControl)
        {
            isPlaying = false;
            return;
        }

        if (isPlaying)
        {
            // 计算时间差
            float deltaTime = Time.time - lastUpdateTime;
            lastUpdateTime = Time.time;
            
            // 计算新的时间
            float currentTime = (float)playableDirector.time;
            float newTime = currentTime + Speed * deltaTime;
            
            bool needLoop = false;
            
            // 处理边界情况
            if (Speed > 0) // 正向播放
            {
                if (newTime >= duration)
                {
                    if (loopType != LoopType.None)
                    {
                        // 循环播放
                        switch (loopType)
                        {
                            case LoopType.Repeat:
                                newTime = 0;
                                break;
                            case LoopType.PingPong:
                                newTime = duration;
                                Speed *= -1;
                                break;
                        }
                        needLoop = true;
                    }
                    else
                    {
                        // 非循环模式，停在末尾
                        newTime = duration;
                        isPlaying = false;
                    }
                }
            }
            else if (Speed < 0) // 反向播放
            {
                if (newTime <= 0)
                {
                    if (loopType != LoopType.None)
                    {
                        // 循环播放
                        switch (loopType)
                        {
                            case LoopType.Repeat:
                                newTime = duration;
                                break;
                            case LoopType.PingPong:
                                newTime = 0;
                                Speed *= -1;
                                break;
                        }
                        needLoop = true;
                    }
                    else
                    {
                        // 非循环模式，停在开头
                        newTime = 0;
                        isPlaying = false;
                    }
                }
            }
            
            // 更新时间
            JumpToTime(newTime);
            
            // 自动播放时同步更新CurrentRatio
            CurrentRatio = newTime / duration;
            
            // 处理循环
            if (needLoop)
            {
                TriggerLoop();
            }
        }
    }
}
}