```
我想创建一个动画播放器组件AnimationPlayer.cs
可以挂载到任意GameObject上播放旧版Clip资产
```

## AnimationPlayer类的数据组织结构如下
* AnimationPlayer       // 组件
    * Innitialized      // private bool,默认false,当前动画片段是否已经初始化
    * ActualLength      // private float,当前动画片段的总时长,指0到AnimClipList中最远的AnimClip的末尾时间,不考虑是否循环
    * CurrentTime       // private float,默认0.0f,当前播放时间
    * CurrentRitio      // private float,0到当前播放时间的长度占ActualLength的百分比[0-1]
    * IsPlaying         // private bool,默认false,当前是否正在播放
    * PlayerSpeed       // public float,默认1.0f,播放速度
    * AutoPlay          // public bool,默认false,是否自动播放
    * StartTime         // public float,默认0.0f,播放起始时间
      * UseRatio        // public bool,默认false,StartTime的值是否为比例,为真时StartTime取值限制[0-1]
    * DelayTime         // public float,默认0.0f,播放延迟时间
      * UseRatio        // public bool,默认false,DelayTime的值是否为比例,为真时DelayTime取值限制[0-1]
    
    * AnimClipList      // public List<AnimClip>,动画片段列表

    * Initialize        // private Function,初始化函数,逐个排查AnimClipList中启用的AnimClip项是否都为IsValid,并计算出ActualLength(忽略未启用的)
    * Play()            // public Function,从当前时间向后播放动画,CurrentTime不变,Speed = ABS(Speed)
    * Reverse()         // public Function,从当前时间向前播放动画,CurrentTime不变,Speed = -ABS(Speed)
    * Stop()            // public Function,停止动画,CurrentTime = 0,Speed不变,IsPlaying = false
    * Pause()           // public Function,暂停动画,CurrentTime不变,Speed不变,IsPlaying = false
    * Resume()          // public Function,恢复动画,CurrentTime不变,Speed不变,IsPlaying = true
    * PlayFromStart()   // public Function,从头开始，向后播放动画,CurrentTime = 0,Speed = ABS(Speed),IsPlaying = true
    * PlayFromEnd()     // public Function,从尾开始，向前播放动画,CurrentTime = ActualLength,Speed = -ABS(Speed),IsPlaying = true
    * SetCurrentTime()  // public Function,设置当前播放时间,可在播放的过程中实现跳转到具体时间点，并继续保持现有播放状态
    * JummpToByTime     // public Function,跳转到指定时间,实时操控播放的方法,自动停止现有播放状态,IsPlaying = false
    * JumpToByRatio     // public Function,跳转到指定比例,实时操控播放的方法,自动停止现有播放状态,IsPlaying = false

## AnimClip类的数据组织结构如下
* AnimClip              // class AnimClip,AnimationPlayer用的动画片段类
    * Clip              // public AnimationClip,引擎的Clip资产
    * GameObject        // public ObjectField,动画片段应用到的对象
    * Enabled           // public bool,默认true,当前片段是否启用,为真时才显示之后所有的参数
    * IsValid           // private bool,当前片段是否有效
    * AnimLength        // private float,Clip片段的长度
    * PlaceOffset       // public float,放置时间偏移
    * RangeTime         // private Vector2,当前片段的时间范围
    * Scale             // public float,默认1.0f,Clip片段长度的缩放，影响实际动画长度
    * Loop              // bool,默认false,是否循环
    * LoopType          // enum LoopType,默认“Repeat”,循环类型(Loop为真时才显示该参数)
           * Repeat     // 重复
           * Ping-pong  // 乒乓
    * BlendMode         // enum BlendMode,默认“Replace”,动画结果计算模式
           * Replace    // 替换
           * Additive   // 叠加
    
    * Initialize        // private Function,初始化函数,判断Clip资产是否可用,GameObject是否存在,如果都有效则IsValid=true,并计算出AnimLength、OccupyLength、RangeTime
    * PlayerTime2ClipTime   // private Function,将播放器时间转换为Clip时间,例如播放器时间为1,Clip
    * JumpToTime        // public Function,跳转到指定时间处,SampleAnimation()
