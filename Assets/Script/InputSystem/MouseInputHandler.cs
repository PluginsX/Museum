using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class MouseInputHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("单击事件设置")]
    public float maxClickTime = 0.3f;
    public int maxClickDistance = 20;

    [Header("拖动事件设置")]
    [Tooltip("降低触发门槛，更容易触发拖动")]
    public float dragMinTime = 0.05f;
    public int dragMinDistance = 2;

    [Header("缩放事件设置")]
    public float scrollSensitivity = 1f;
    public float zoomPercentage = 0.1f;

    [Header("事件绑定")]
    public UnityEvent<Vector2> onSingleClick;
    public UnityEvent<Vector2, Vector2> onLeftDrag;  // 当前位置, 上一位置
    public UnityEvent<Vector2, Vector2> onRightDrag; // 当前位置, 上一位置
    public UnityEvent<float> onScroll;

    private RectTransform rectTransform;
    private bool isMouseDown = false;
    private Vector2 mouseDownPos;
    private Vector2 lastMousePos;
    private float mouseDownTime;
    private bool hasStartedDrag = false;
    private PointerEventData.InputButton pressedButton;

    // 调试用
    [SerializeField] private bool debugMode = false;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void Update()
    {
        // 处理鼠标滚轮（只有这个需要在Update中检测）
        float scrollDelta = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scrollDelta) > 0 && IsMouseInRect(Input.mousePosition))
        {
            float scale = 1 + (scrollDelta > 0 ? -zoomPercentage : zoomPercentage) * scrollSensitivity;
            onScroll.Invoke(scale);
            if (debugMode) Debug.Log($"滚轮缩放: {scale}");
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsMouseInRect(eventData.position)) return;

        isMouseDown = true;
        mouseDownPos = eventData.position;
        lastMousePos = eventData.position;
        mouseDownTime = Time.time;
        hasStartedDrag = false;
        pressedButton = eventData.button;

        if (debugMode) Debug.Log($"鼠标按下: {pressedButton} 在 {eventData.position}");
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isMouseDown || eventData.button != pressedButton) return;

        float clickDuration = Time.time - mouseDownTime;
        float clickDistance = Vector2.Distance(mouseDownPos, eventData.position);

        if (!hasStartedDrag && clickDuration <= maxClickTime && clickDistance <= maxClickDistance)
        {
            onSingleClick.Invoke(eventData.position);
            if (debugMode) Debug.Log("触发单击事件");
        }

        isMouseDown = false;
        hasStartedDrag = false;
        if (debugMode) Debug.Log("鼠标释放");
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isMouseDown || eventData.button != pressedButton) return;

        // 计算拖动时间和距离
        float dragDuration = Time.time - mouseDownTime;
        float dragDistance = Vector2.Distance(mouseDownPos, eventData.position);

        // 检查是否满足拖动条件
        if (!hasStartedDrag)
        {
            if (dragDuration > dragMinTime || dragDistance > dragMinDistance)
            {
                hasStartedDrag = true;
                if (debugMode) Debug.Log($"开始拖动: {pressedButton}");
            }
            else
            {
                lastMousePos = eventData.position;
                return;
            }
        }

        // 根据鼠标按键类型触发不同事件
        if (pressedButton == PointerEventData.InputButton.Left)
        {
            onLeftDrag.Invoke(eventData.position, lastMousePos);
            if (debugMode) Debug.Log($"左键拖动: {eventData.position}");
        }
        else if (pressedButton == PointerEventData.InputButton.Right)
        {
            onRightDrag.Invoke(eventData.position, lastMousePos);
            if (debugMode) Debug.Log($"右键拖动: {eventData.position}");
        }

        lastMousePos = eventData.position;
    }

    // 检查鼠标是否在当前UI范围内
    private bool IsMouseInRect(Vector2 screenPos)
    {
        bool result = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, 
            screenPos, 
            null, 
            out Vector2 localPos
        ) && rectTransform.rect.Contains(localPos);
        
        if (debugMode && !result)
            Debug.LogWarning("鼠标在UI范围外");
            
        return result;
    }
}
