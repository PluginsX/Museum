using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

[RequireComponent(typeof(RectTransform))]
public class TouchInputHandler : MonoBehaviour
{
    [Header("单击事件设置")]
    [Tooltip("最长按下时间（秒）")]
    public float maxTapTime = 0.3f;
    [Tooltip("最长滑动距离（像素）")]
    public int maxTapDistance = 20;

    [Header("拖动事件设置")]
    [Tooltip("触发拖动的最小按下时间（秒）")]
    public float dragMinTime = 0.3f;
    [Tooltip("触发拖动的最小滑动距离（像素）")]
    public int dragMinDistance = 20;

    [Header("缩放事件设置")]
    [Tooltip("触发缩放的最小距离变化（像素）")]
    public float pinchMinDelta = 10f;

    [Header("平移事件设置")]
    [Tooltip("触发平移的最小位置变化（像素）")]
    public int panMinDelta = 10;

    [Header("事件绑定")]
    public UnityEvent<Vector3> onSingleTap;
    public UnityEvent<Vector3> onDrag;
    public UnityEvent<float> onPinch;
    public UnityEvent<Vector3> onTwoFingerPan;

    private RectTransform rectTransform;
    private Camera mainCamera;
    
    // 单点触控状态
    private bool isSingleTouching = false;
    private int singleTouchId = -1;
    private Vector2 singleTouchStartPos;
    private float singleTouchStartTime;
    private bool hasStartedDrag = false;

    // 双点触控状态
    private bool isTwoFingerTouching = false;
    private float initialPinchDistance;
    private Vector2 initialTwoFingerCenter;
    private bool hasStartedPinch = false;
    private bool hasStartedTwoFingerPan = false;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        mainCamera = Camera.main;
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerUp += OnFingerUp;
        Touch.onFingerMove += OnFingerMove;
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerUp -= OnFingerUp;
        Touch.onFingerMove -= OnFingerMove;
        EnhancedTouchSupport.Disable();
    }

    private void OnFingerDown(Finger finger)
    {
        // 检查触摸是否在当前UI范围内
        if (!IsTouchInRect(finger.currentTouch.screenPosition))
            return;

        if (Touch.activeTouches.Count == 1)
        {
            // 单点触控开始
            isSingleTouching = true;
            singleTouchId = finger.index;
            singleTouchStartPos = finger.currentTouch.screenPosition;
            singleTouchStartTime = Time.time;
            hasStartedDrag = false;
        }
        else if (Touch.activeTouches.Count == 2)
        {
            // 双点触控开始
            isTwoFingerTouching = true;
            
            // 记录初始距离和中心位置
            var touches = Touch.activeTouches;
            initialPinchDistance = Vector2.Distance(
                touches[0].screenPosition, 
                touches[1].screenPosition
            );
            initialTwoFingerCenter = (touches[0].screenPosition + touches[1].screenPosition) / 2f;
            
            hasStartedPinch = false;
            hasStartedTwoFingerPan = false;
        }
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger.index == singleTouchId && isSingleTouching)
        {
            // 处理单点触控结束
            float touchDuration = Time.time - singleTouchStartTime;
            float touchDistance = Vector2.Distance(
                singleTouchStartPos, 
                finger.currentTouch.screenPosition
            );

            // 如果没有开始拖动，检查是否是单击
            if (!hasStartedDrag && 
                touchDuration <= maxTapTime && 
                touchDistance <= maxTapDistance)
            {
                Vector3 worldPos = ScreenToWorldPoint(finger.currentTouch.screenPosition);
                onSingleTap.Invoke(worldPos);
            }

            isSingleTouching = false;
            singleTouchId = -1;
        }

        // 双点触控结束
        if (Touch.activeTouches.Count < 2 && isTwoFingerTouching)
        {
            isTwoFingerTouching = false;
        }
    }

    private void OnFingerMove(Finger finger)
    {
        // 检查触摸是否在当前UI范围内
        if (!IsTouchInRect(finger.currentTouch.screenPosition))
            return;

        // 处理单点拖动
        if (isSingleTouching && !isTwoFingerTouching && finger.index == singleTouchId)
        {
            float touchDuration = Time.time - singleTouchStartTime;
            float touchDistance = Vector2.Distance(
                singleTouchStartPos, 
                finger.currentTouch.screenPosition
            );

            // 满足拖动条件
            if (!hasStartedDrag && 
                (touchDuration > dragMinTime || touchDistance > dragMinDistance))
            {
                hasStartedDrag = true;
            }

            // 如果已经开始拖动，触发拖动事件
            if (hasStartedDrag)
            {
                Vector3 worldPos = ScreenToWorldPoint(finger.currentTouch.screenPosition);
                onDrag.Invoke(worldPos);
            }
        }
        // 处理双点操作
        else if (isTwoFingerTouching && Touch.activeTouches.Count == 2)
        {
            var touches = Touch.activeTouches;
            float currentDistance = Vector2.Distance(
                touches[0].screenPosition, 
                touches[1].screenPosition
            );
            
            Vector2 currentCenter = (touches[0].screenPosition + touches[1].screenPosition) / 2f;
            Vector2 centerDelta = currentCenter - initialTwoFingerCenter;

            // 处理缩放
            float distanceDelta = Mathf.Abs(currentDistance - initialPinchDistance);
            if (!hasStartedPinch && !hasStartedTwoFingerPan)
            {
                // 判断是缩放还是平移
                if (distanceDelta >= pinchMinDelta)
                {
                    hasStartedPinch = true;
                }
                else if (centerDelta.magnitude >= panMinDelta)
                {
                    hasStartedTwoFingerPan = true;
                }
            }

            if (hasStartedPinch)
            {
                float scale = currentDistance / initialPinchDistance;
                onPinch.Invoke(scale);
            }
            else if (hasStartedTwoFingerPan)
            {
                Vector3 worldDelta = ScreenDeltaToWorldDelta(centerDelta);
                onTwoFingerPan.Invoke(worldDelta);
            }
        }
    }

    // 检查触摸位置是否在当前UI矩形内
    private bool IsTouchInRect(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, 
            screenPos, 
            null, 
            out Vector2 localPos
        );
        
        return rectTransform.rect.Contains(localPos);
    }

    // 将屏幕坐标转换为世界坐标
    private Vector3 ScreenToWorldPoint(Vector2 screenPos)
    {
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
            rectTransform, 
            screenPos, 
            mainCamera, 
            out Vector3 worldPos
        ))
        {
            return worldPos;
        }
        return Vector3.zero;
    }

    // 将屏幕空间的位移转换为世界空间的位移
    private Vector3 ScreenDeltaToWorldDelta(Vector2 screenDelta)
    {
        // 取矩形中心作为参考点
        Vector2 centerScreenPos = RectTransformUtility.WorldToScreenPoint(mainCamera, rectTransform.position);
        
        // 计算两个点的世界坐标
        Vector3 startWorldPos = ScreenToWorldPoint(centerScreenPos);
        Vector3 endWorldPos = ScreenToWorldPoint(centerScreenPos + screenDelta);
        
        return endWorldPos - startWorldPos;
    }
}
