using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[System.Serializable]
public class Vector2Event : UnityEvent<Vector2> { }

[System.Serializable]
public class Vector2Vector2Event : UnityEvent<Vector2, Vector2> { }

[System.Serializable]
public class FloatEvent : UnityEvent<float> { }

public class InputHandler : MonoBehaviour
{
    [Header("输入动作资产")]
    [Tooltip("直接拖拽你的InputActionAsset到这里")]
    public InputActionAsset inputActionAsset;

    [Header("动作名称配置")]
    [Tooltip("如果你的动作名称与默认不同,请在此修改")]
    public string clickActionName = "Click";
    public string TurnActionName = "Turn";
    public string ScaleActionName = "Scale";
    public string MoveActionName = "Move";

    [Header("单击事件设置")]
    public float maxClickTime = 0.3f;
    public float maxClickDistance = 20;

    [Header("拖动事件设置")]
    public float dragMinDistance = 2;

    [Header("缩放事件设置")]
    [Tooltip("缩放动作输入轴值的乘数,可用于控制方向")]
    public float zoomMultiple = -1f;

    [Header("事件绑定")]
    public Vector2Event onSingleClick;             // 点击位置
    public Vector2Vector2Event onRotate;           // 当前位置, 上一位置
    public FloatEvent onScale;                     // 缩放因子
    public Vector2Vector2Event onPan;              // 当前位置, 上一位置

    [Header("调试")]
    public bool debugMode = false;

    // 输入动作缓存
    private InputAction clickAction;
    private InputAction TurnAction;
    private InputAction ScaleAction;
    private InputAction MoveAction;

    // 状态变量
    private bool OnLeftPress = false;
    private bool OnMiddlePress = false;
    private Vector2 lastPosition;
    private Vector2 Last_Mouse_L_Pos;
    private Vector2 Last_Mouse_M_Pos;
    private Vector2 TouchStartPosition;
    private float clickStartTime;
    private bool clickRegistered = false;

    // 触摸相关变量
    private int primaryTouchId = -1;
    private int secondaryTouchId = -1;
    private Vector2 primaryTouchStartPos;
    private float initialTouchDistance;
    private Vector2 initialTouchMidpoint;

    private void Awake()
    {
        // 从资产中查找动作
        if (inputActionAsset != null)
        {
            // 查找所有动作地图中的动作
            foreach (var map in inputActionAsset.actionMaps)
            {
                if (clickAction == null)
                    clickAction = map.FindAction(clickActionName, false);
                
                if (TurnAction == null)
                    TurnAction = map.FindAction(TurnActionName, false);
                
                if (ScaleAction == null)
                    ScaleAction = map.FindAction(ScaleActionName, false);
                
                if (MoveAction == null)
                    MoveAction = map.FindAction(MoveActionName, false);

                // 所有动作都找到后停止查找
                if (clickAction != null && TurnAction != null && 
                    ScaleAction != null && MoveAction != null)
                    break;
            }

            // 调试信息
            if (debugMode)
            {
                if (clickAction == null) Debug.LogWarning($"未找到名为 {clickActionName} 的动作");
                if (TurnAction == null) Debug.LogWarning($"未找到名为 {TurnActionName} 的动作");
                if (ScaleAction == null) Debug.LogWarning($"未找到名为 {ScaleActionName} 的动作");
                if (MoveAction == null) Debug.LogWarning($"未找到名为 {MoveActionName} 的动作");
            }
        }
        else if (debugMode)
        {
            Debug.LogWarning("未分配InputActionAsset,请拖拽你的输入动作资产到组件上");
        }
    }

    private void OnEnable()
    {
        // 启用输入动作并绑定回调
        if (clickAction != null)
        {
            clickAction.Enable();
            clickAction.performed += OnClickPerformed;
            clickAction.canceled += OnClickCanceled;
        }

        if (TurnAction != null)
        {
            TurnAction.Enable();
        }

        if (ScaleAction != null)
        {
            ScaleAction.Enable();
            ScaleAction.performed += OnScrollPerformed;
        }

        if (MoveAction != null)
        {
            MoveAction.Enable();
            MoveAction.performed += OnPanPerformed;
            MoveAction.canceled += OnPanCanceled;
        }
    }

    private void OnDisable()
    {
        // 禁用输入动作并解绑回调
        if (clickAction != null)
        {
            clickAction.performed -= OnClickPerformed;
            clickAction.canceled -= OnClickCanceled;
            clickAction.Disable();
        }

        if (TurnAction != null)
        {
            TurnAction.Disable();
        }

        if (ScaleAction != null)
        {
            ScaleAction.performed -= OnScrollPerformed;
            ScaleAction.Disable();
        }

        if (MoveAction != null)
        {
            MoveAction.performed -= OnPanPerformed;
            MoveAction.canceled -= OnPanCanceled;
            MoveAction.Disable();
        }
    }

    private void Update()
    {
        // 处理旋转
        if (OnLeftPress && TurnAction != null)
        {
            Vector2 currentPosition = Mouse.current.position.ReadValue();
            if (currentPosition != Last_Mouse_L_Pos)
            {
                onRotate.Invoke(currentPosition, Last_Mouse_L_Pos);
                if (debugMode) Debug.Log($"旋转: {currentPosition} -> {Last_Mouse_L_Pos}");
                Last_Mouse_L_Pos = currentPosition;
            }
        }

        // 处理平移
        if (OnMiddlePress && MoveAction != null)
        {
            Vector2 currentPosition = Mouse.current.position.ReadValue();
            if (currentPosition != Last_Mouse_M_Pos)
            {
                onPan.Invoke(currentPosition, Last_Mouse_M_Pos);
                if (debugMode) Debug.Log($"平移: {currentPosition} -> {Last_Mouse_M_Pos}");
                Last_Mouse_M_Pos = currentPosition;
            }
        }

        // 处理触摸输入
        HandleTouchInput();
    }

    private void OnClickPerformed(InputAction.CallbackContext context)
    {
        OnLeftPress = true;

        Vector2 position = Mouse.current.position.ReadValue();
        // 记录鼠标位置
        Last_Mouse_L_Pos = position;
        // 点击开始Time
        clickStartTime = Time.time;

        clickRegistered = false;

        if (debugMode) Debug.Log($"点击开始: {position}");
    }

    private void OnClickCanceled(InputAction.CallbackContext context)
    {
        OnLeftPress = false;
        
        Vector2 position = Mouse.current.position.ReadValue();
        float clickDuration = Time.time - clickStartTime;
        float clickDistance = Vector2.Distance(Last_Mouse_L_Pos, position);

        //Last_Mouse_L_Pos = new Vector2(0,0);

        // 检查是否满足单击条件
        if (clickDuration <= maxClickTime && clickDistance <= maxClickDistance && !clickRegistered)
        {
            onSingleClick.Invoke(position);
            clickRegistered = true;
            if (debugMode) Debug.Log($"触发单击: {position}");
        }

        if (debugMode) Debug.Log($"点击结束: {position}");
    }

    private void OnScrollPerformed(InputAction.CallbackContext context)
    {
        float scrollValue = context.ReadValue<float>();
        // 忽略Axis的大小只取正负
        float Direction = scrollValue / Mathf.Abs(scrollValue);
        // 乘以缩放因子
        float scaleFactor = Direction * zoomMultiple;
        onScale.Invoke(scaleFactor);
        
        if (debugMode) Debug.Log($"缩放: {scaleFactor}");
    }

    private void OnPanPerformed(InputAction.CallbackContext context)
    {
        OnMiddlePress = true;

        // 更新中键按下时的光标位置
        Last_Mouse_M_Pos = Mouse.current.position.ReadValue();

        if (debugMode) Debug.Log("平移开始");
    }

    private void OnPanCanceled(InputAction.CallbackContext context)
    {
        OnMiddlePress = false;

        // 更新中键松开时的光标位置
        //Last_Mouse_M_Pos = new Vector2(0,0);

        if (debugMode) Debug.Log("平移结束");
    }

    private void HandleTouchInput()
    {
        // 检查是否有触摸屏
        if (Touchscreen.current == null)
            return;

        // 获取所有触摸控制点
        var touches = Touchscreen.current.touches;
        int activeTouches = 0;
        
        // 统计活跃触摸点数量
        foreach (var touch in touches)
        {
            if (touch.isInProgress)
                activeTouches++;
        }

        // 单指触摸 - 旋转
        if (activeTouches == 1)
        {
            foreach (var touch in touches)
            {
                if (touch.isInProgress)
                {
                    // 获取触摸ID
                    int currentTouchId = touch.touchId.ReadValue();
                    
                    if (primaryTouchId == -1)
                    {
                        // 新的触摸开始
                        primaryTouchId = currentTouchId;
                        primaryTouchStartPos = touch.position.ReadValue();
                        lastPosition = primaryTouchStartPos;
                        TouchStartPosition = primaryTouchStartPos;
                        clickStartTime = Time.time;
                        clickRegistered = false;
                        OnLeftPress = true;
                        
                        if (debugMode) Debug.Log($"触摸开始: {primaryTouchStartPos}, ID: {primaryTouchId}");
                    }
                    else if (currentTouchId == primaryTouchId)
                    {
                        // 正在进行的触摸
                        var phase = touch.phase.ReadValue();
                        var position = touch.position.ReadValue();
                        
                        if (phase == UnityEngine.InputSystem.TouchPhase.Moved && OnLeftPress)
                        {
                            // 检查是否超过最小拖动距离
                            if (Vector2.Distance(primaryTouchStartPos, position) > dragMinDistance)
                            {
                                onRotate.Invoke(position, lastPosition);
                                lastPosition = position;
                                
                                if (debugMode) Debug.Log($"触摸旋转: {position}");
                            }
                        }
                        else if (phase == UnityEngine.InputSystem.TouchPhase.Ended || 
                                 phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                        {
                            // 触摸结束
                            OnLeftPress = false;
                            primaryTouchId = -1;
                            
                            // 检查是否是单击
                            float clickDuration = Time.time - clickStartTime;
                            float clickDistance = Vector2.Distance(TouchStartPosition, position);
                            
                            if (clickDuration <= maxClickTime && clickDistance <= maxClickDistance && !clickRegistered)
                            {
                                onSingleClick.Invoke(position);
                                clickRegistered = true;
                                if (debugMode) Debug.Log($"触摸单击: {position}");
                            }
                            
                            if (debugMode) Debug.Log($"触摸结束: {position}");
                        }
                    }
                    break;
                }
            }
        }

        // 双指触摸 - 缩放和平移
        else if (activeTouches == 2)
        {
            TouchControl touch1 = null;
            TouchControl touch2 = null;

            // 获取两个活跃触摸点
            foreach (var touch in touches)
            {
                if (touch.isInProgress)
                {
                    if (touch1 == null)
                        touch1 = touch;
                    else
                    {
                        touch2 = touch;
                        break;
                    }
                }
            }

            if (touch1 != null && touch2 != null)
            {
                // 获取触摸ID
                int touch1Id = touch1.touchId.ReadValue();
                int touch2Id = touch2.touchId.ReadValue();

                // 确保我们追踪正确的触摸点
                if (primaryTouchId == -1 || secondaryTouchId == -1)
                {
                    primaryTouchId = touch1Id;
                    secondaryTouchId = touch2Id;
                    initialTouchDistance = Vector2.Distance(
                        touch1.position.ReadValue(),
                        touch2.position.ReadValue()
                    );
                    initialTouchMidpoint = (
                        touch1.position.ReadValue() +
                        touch2.position.ReadValue()
                    ) / 2f;
                    lastPosition = initialTouchMidpoint;

                    if (debugMode) Debug.Log($"双指触摸开始: ID1={primaryTouchId}, ID2={secondaryTouchId}");
                }
                else if ((touch1Id == primaryTouchId && touch2Id == secondaryTouchId) ||
                         (touch1Id == secondaryTouchId && touch2Id == primaryTouchId))
                {
                    // 计算当前距离和中点
                    Vector2 pos1 = touch1.position.ReadValue();
                    Vector2 pos2 = touch2.position.ReadValue();
                    float currentDistance = Vector2.Distance(pos1, pos2);
                    Vector2 currentMidpoint = (pos1 + pos2) / 2f;

                    // 处理缩放
                    float DeltaDistance = currentDistance - initialTouchDistance;
                    float scaleFactor = DeltaDistance > 0 ? 1 : (DeltaDistance < 0 ? -1 : 0);
                    // 触发缩放事件
                    onScale.Invoke(scaleFactor);
                    if (debugMode) Debug.Log($"触摸:双指缩放: {scaleFactor}");
                    // 更新双指间距
                    initialTouchDistance = currentDistance;

                    // 处理平移
                    if (currentMidpoint != lastPosition)
                    {
                        onPan.Invoke(currentMidpoint, lastPosition);
                        lastPosition = currentMidpoint;

                        if (debugMode) Debug.Log($"触摸:双指平移: {currentMidpoint}");
                    }
                }
            }
        }
        else
        {
            // 重置触摸状态
            primaryTouchId = -1;
            secondaryTouchId = -1;
            OnLeftPress = false;
        }
    }
}
