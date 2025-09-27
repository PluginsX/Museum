using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using TMPro;

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
    public Vector2Event OnClick;                     // 点击位置
    public Vector2Vector2Event OnRotate;             // 当前位置, 上一位置
    public FloatEvent OnScale;                       // 缩放因子
    public Vector2Vector2Event OnMove;               // 当前位置, 上一位置

    [Header("调试")]
    public bool debugMode = false;
    public TextMeshProUGUI DebugOutputUI;

    // 输入动作缓存
    private InputAction ClickAction;
    private InputAction TurnAction;
    private InputAction ScaleAction;
    private InputAction MoveAction;

    // 状态变量
    private DeviceType Device_Type;
    private bool OnClickPress = false;
    private bool OnTurnPress = false;
    private bool OnMiddlePress = false;
    private bool OnScaleActive = false;
    private Vector2 Last_Click_Pos;
    private Vector2 last_Touch_Pos;
    private Vector2 Last_Turn_Pos;
    private Vector2 Last_Move_Pos;
    private Vector2 TouchStartPosition;
    private float clickStartTime;
    private bool clickRegistered = false;
    private bool UserInputMode = false; // True 为触摸，Flase 为鼠标

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
                if (ClickAction == null)
                    ClickAction = map.FindAction(clickActionName, false);

                if (TurnAction == null)
                    TurnAction = map.FindAction(TurnActionName, false);

                if (ScaleAction == null)
                    ScaleAction = map.FindAction(ScaleActionName, false);

                if (MoveAction == null)
                    MoveAction = map.FindAction(MoveActionName, false);

                // 所有动作都找到后停止查找
                if (ClickAction != null && TurnAction != null &&
                    ScaleAction != null && MoveAction != null)
                    break;
            }

            // 调试信息
            if (debugMode)
            {
                if (ClickAction == null) Debug.LogWarning($"未找到名为 {clickActionName} 的动作");
                if (TurnAction == null) Debug.LogWarning($"未找到名为 {TurnActionName} 的动作");
                if (ScaleAction == null) Debug.LogWarning($"未找到名为 {ScaleActionName} 的动作");
                if (MoveAction == null) Debug.LogWarning($"未找到名为 {MoveActionName} 的动作");
            }
        }
        else if (debugMode)
        {
            Debug.LogWarning("未分配InputActionAsset,请拖拽你的输入动作资产到组件上");
        }


        // 获取设备类型
        Device_Type = SystemInfo.deviceType;
    }

    private void OnEnable()
    {
        // 启用输入动作并绑定回调
        if (ClickAction != null)
        {
            ClickAction.Enable();
            ClickAction.performed += OnClickPerformed;
            ClickAction.canceled += OnClickCanceled;
        }

        if (TurnAction != null)
        {
            TurnAction.Enable();
            TurnAction.performed += OnTurnPerformed;
            TurnAction.canceled += OnTurnCanceled;
        }

        if (ScaleAction != null)
        {
            ScaleAction.Enable();
            ScaleAction.performed += OnScrollPerformed;
            ScaleAction.canceled += OnScrollCanceled;
        }

        if (MoveAction != null)
        {
            MoveAction.Enable();
            MoveAction.performed += OnMovePerformed;
            MoveAction.canceled += OnMoveCanceled;
        }

    }

    private void OnDisable()
    {
        // 禁用输入动作并解绑回调
        if (ClickAction != null)
        {
            ClickAction.performed -= OnClickPerformed;
            ClickAction.canceled -= OnClickCanceled;
            ClickAction.Disable();
        }

        if (TurnAction != null)
        {
            TurnAction.performed -= OnTurnPerformed;
            TurnAction.canceled -= OnTurnCanceled;
            TurnAction.Disable();
        }

        if (ScaleAction != null)
        {
            ScaleAction.performed -= OnScrollPerformed;
            ScaleAction.canceled -= OnScrollCanceled;
            ScaleAction.Disable();
        }

        if (MoveAction != null)
        {
            MoveAction.performed -= OnMovePerformed;
            MoveAction.canceled -= OnMoveCanceled;
            MoveAction.Disable();
        }
    }

    private void OnClickPerformed(InputAction.CallbackContext context)
    {
        OnClickPress = true;

        Vector2 position = Mouse.current.position.ReadValue();
        // 记录鼠标位置
        Last_Click_Pos = position;
        // 点击开始Time
        clickStartTime = Time.time;

        clickRegistered = false;

        if (debugMode) Debug.Log($"点击开始: {position}");
    }

    private void OnClickCanceled(InputAction.CallbackContext context)
    {
        OnClickPress = false;

        Vector2 position = Mouse.current.position.ReadValue();
        float clickDuration = Time.time - clickStartTime;
        float clickDistance = Vector2.Distance(Last_Click_Pos, position);

        //Last_Click_Pos = new Vector2(0,0);

        // 检查是否满足单击条件
        if (clickDuration <= maxClickTime && clickDistance <= maxClickDistance && !clickRegistered)
        {
            OnClick.Invoke(position);
            clickRegistered = true;
            if (debugMode) Debug.Log($"触发单击: {position}");
        }

        if (debugMode) Debug.Log($"点击结束: {position}");
    }

    private void OnScrollPerformed(InputAction.CallbackContext context)
    {
        OnScaleActive = true;

        float scrollValue = context.ReadValue<float>();
        // 忽略Axis的大小只取正负
        float Direction = scrollValue / Mathf.Abs(scrollValue);
        // 乘以缩放因子
        float scaleFactor = Direction * zoomMultiple;
        OnScale.Invoke(scaleFactor);

        if (debugMode) Debug.Log($"缩放开始: {scaleFactor}");
    }
    private void OnScrollCanceled(InputAction.CallbackContext context)
    {
        OnScaleActive = false;

        float scrollValue = context.ReadValue<float>();
        // 忽略Axis的大小只取正负
        float Direction = scrollValue / Mathf.Abs(scrollValue);
        // 乘以缩放因子
        float scaleFactor = Direction * zoomMultiple;

        if (debugMode) Debug.Log($"缩放结束: {scaleFactor}");
    }
    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        OnMiddlePress = true;

        // 更新中键按下时的光标位置
        Last_Move_Pos = Mouse.current.position.ReadValue();

        if (debugMode) Debug.Log("平移开始");
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        OnMiddlePress = false;

        // 更新中键松开时的光标位置
        //Last_Move_Pos = new Vector2(0,0);

        if (debugMode) Debug.Log("平移结束");
    }

    private void OnTurnPerformed(InputAction.CallbackContext context)
    {
        OnTurnPress = true;

        Vector2 position = Mouse.current.position.ReadValue();
        // 记录鼠标位置
        Last_Turn_Pos = position;
        // 点击开始Time
        clickStartTime = Time.time;

        clickRegistered = false;

        if (debugMode) Debug.Log($"旋转开始: {position}");
    }

    private void OnTurnCanceled(InputAction.CallbackContext context)
    {
        OnTurnPress = false;

        Vector2 position = Mouse.current.position.ReadValue();
        float clickDuration = Time.time - clickStartTime;
        float clickDistance = Vector2.Distance(Last_Turn_Pos, position);

        //Last_Turn_Pos = new Vector2(0,0);

        // 检查是否满足单击条件
        if (clickDuration <= maxClickTime && clickDistance <= maxClickDistance && !clickRegistered)
        {
            OnClick.Invoke(position);
            clickRegistered = true;
            if (debugMode) Debug.Log($"触发单击: {position}");
        }

        if (debugMode) Debug.Log($"旋转结束: {position}");
    }

    // 处理触控输入
    private void HandleTouchInput()
    {
        if (debugMode) Debug.Log("正在处理触摸输入");
        // 检查是否有触摸屏
        // if (Touchscreen.current == null)
        //     Debug.Log("没有检测到触摸屏");
        //     return;

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
                        last_Touch_Pos = primaryTouchStartPos;
                        TouchStartPosition = primaryTouchStartPos;
                        clickStartTime = Time.time;
                        clickRegistered = false;
                        OnTurnPress = true;

                        if (debugMode) Debug.Log($"触摸开始: {primaryTouchStartPos}, ID: {primaryTouchId}");
                    }
                    else if (currentTouchId == primaryTouchId)
                    {
                        // 正在进行的触摸
                        var phase = touch.phase.ReadValue();
                        var position = touch.position.ReadValue();

                        if (phase == UnityEngine.InputSystem.TouchPhase.Moved && OnTurnPress)
                        {
                            // 检查是否超过最小拖动距离
                            if (Vector2.Distance(primaryTouchStartPos, position) > dragMinDistance)
                            {
                                OnRotate.Invoke(last_Touch_Pos, position);
                                last_Touch_Pos = position;

                                if (debugMode) Debug.Log($"触摸旋转: {position}");
                            }
                        }
                        else if (phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                                 phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                        {
                            // 触摸结束
                            OnTurnPress = false;
                            primaryTouchId = -1;

                            // 检查是否是单击
                            float clickDuration = Time.time - clickStartTime;
                            float clickDistance = Vector2.Distance(TouchStartPosition, position);

                            if (clickDuration <= maxClickTime && clickDistance <= maxClickDistance && !clickRegistered)
                            {
                                OnClick.Invoke(position);
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
                    last_Touch_Pos = initialTouchMidpoint;

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
                    float scaleFactor = DeltaDistance > 0 ? -1 : (DeltaDistance < 0 ? 1 : 0);
                    // 触发缩放事件
                    OnScale.Invoke(scaleFactor);
                    if (debugMode) Debug.Log($"触摸:双指缩放: {scaleFactor}");

                    // 更新双指间距
                    initialTouchDistance = currentDistance;

                    // 处理平移
                    if (currentMidpoint != last_Touch_Pos)
                    {
                        OnMove.Invoke(last_Touch_Pos, currentMidpoint);
                        last_Touch_Pos = currentMidpoint;

                        if (debugMode) Debug.Log($"触摸:双指平移: {last_Touch_Pos}->{currentMidpoint}");
                    }
                }
            }
        }
        else
        {
            // 重置触摸状态
            primaryTouchId = -1;
            secondaryTouchId = -1;
            OnTurnPress = false;
        }
    }
    // 处理鼠标输入
    private void HandleMouseInput()
    {
        if (debugMode) Debug.Log("正在处理鼠标输入");
        // 处理旋转
        if (OnTurnPress)
        {
            Vector2 currentPosition = Mouse.current.position.ReadValue();
            if (currentPosition != Last_Turn_Pos)
            {
                OnRotate.Invoke(Last_Turn_Pos, currentPosition);
                if (debugMode) Debug.Log($"持续旋转: {Last_Turn_Pos} -> {currentPosition}");
                // 更新上一次位置
                Last_Turn_Pos = currentPosition;
            }
        }

        // 处理平移
        if (OnMiddlePress)
        {
            Vector2 currentPosition = Mouse.current.position.ReadValue();
            if (currentPosition != Last_Move_Pos)
            {
                OnMove.Invoke(Last_Move_Pos, currentPosition);
                if (debugMode) Debug.Log($"持续平移: {Last_Move_Pos} -> {currentPosition}");
                // 更新上一次位置
                Last_Move_Pos = currentPosition;
            }
        }
    }

    // 检查触摸是否开始（用于快速切换输入模式）
    private bool CheckInputMode()
    {
        // 1. 检查当前是否有可用的触摸屏设备
        if (Touchscreen.current == null)
            return false;

        // 2. 遍历所有可能的触摸点
        for (int i = 0; i < Touchscreen.current.touches.Count; i++)
        {
            TouchControl touch = Touchscreen.current.touches[i];
            // 3. 检查该触摸点是否被按下（活跃）
            if (touch.press.isPressed)
            {
                return true;
            }
        }

        return false;
    }



    private void Update()
    {

        // 调试信息输出到UI
        DebugOutputUI.text = "设备类型: "+Device_Type.ToString()+"\t|\t输入模式: " + (UserInputMode ? "触控" : "鼠标");

        // 输入模式检查与切换
        bool CurrentInputMode = CheckInputMode();
        if (debugMode && UserInputMode != CurrentInputMode){
            if(CurrentInputMode){
                // 切换到鼠标模式时，重置触控默认值
                primaryTouchId = -1;
                secondaryTouchId = -1;
                OnTurnPress = false;
            }
            Debug.Log("输入模式切换为: " + (CurrentInputMode ? "触控" : "鼠标"));
        }
        // 更新输入模式
        UserInputMode = CurrentInputMode;
        
        // 根据当前输入类型处理（触摸优先）
        if (UserInputMode)
        {
            // 触摸活跃时只处理触摸输入
            HandleTouchInput();
        }
        else
        {
            // 触摸不活跃时处理鼠标输入
            HandleMouseInput();
        }
    }
    
    
}

