using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch; // 引入EnhancedTouch命名空间
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch; // 指定使用EnhancedTouch的Touch类

[RequireComponent(typeof(Camera))]
public class TouchCameraController : MonoBehaviour
{
    [Header("目标设置")]
    [Tooltip("摄像机注视的目标对象")]
    public GameObject targetObject;

    [Header("旋转设置")]
    [Tooltip("水平旋转速度乘数")]
    public float horizontalRotationSpeed = 1f;
    [Tooltip("垂直旋转速度乘数")]
    public float verticalRotationSpeed = 1f;
    [Tooltip("水平旋转插值速度")]
    public float horizontalRotationLerpSpeed = 0f;
    [Tooltip("垂直旋转插值速度")]
    public float verticalRotationLerpSpeed = 0f;

    [Header("缩放设置")]
    [Tooltip("最小摄像机距离")]
    public float minCameraDistance = 1f;
    [Tooltip("最大摄像机距离")]
    public float maxCameraDistance = 10f;
    [Tooltip("缩放比例乘数")]
    public float zoomScaleMultiplier = 1f;
    [Tooltip("缩放插值速度")]
    public float zoomLerpSpeed = 0f;

    [Header("平移设置")]
    [Tooltip("平移插值速度")]
    public float panLerpSpeed = 0f;
    [Tooltip("屏幕边界余量（像素）")]
    public float screenEdgeMargin = 50f;

    [Header("待机设置")]
    [Tooltip("是否启用待机检测")]
    public bool enableIdleDetection = true;
    [Tooltip("进入待机的等待时间（秒）")]
    public float idleWaitTime = 30f;
    [Tooltip("恢复默认状态的插值速度")]
    public float resetLerpSpeed = 2f;
    [Tooltip("待机时是否自动环绕目标")]
    public bool orbitOnIdle = true;
    [Tooltip("环绕速度（度/秒）")]
    public float orbitSpeed = 10f;

    private Camera mainCamera;
    private float camDistance;
    private Vector3 defaultCameraPosition;
    private Quaternion defaultCameraRotation;
    private Vector3 targetOffset; // 目标位置的偏移量，用于平移

    private float lastInputTime;
    private bool isIdle = false;

    // 旋转状态
    private float currentYaw;
    private float currentPitch;
    private float targetYaw;
    private float targetPitch;

    // 缩放状态
    private float currentDistance;
    private float targetDistance;

    // 平移状态
    private Vector3 currentOffset;
    //private Vector3 targetOffset;

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();
        
        if (targetObject == null)
        {
            Debug.LogWarning("未指定注视目标对象，将使用自身位置作为目标");
            targetObject = new GameObject("Camera Target");
            targetObject.transform.position = transform.position + transform.forward * 5f;
        }
    }

    private void Start()
    {
        // 初始化默认状态
        defaultCameraPosition = transform.position;
        defaultCameraRotation = transform.rotation;
        
        // 计算初始距离
        camDistance = Vector3.Distance(transform.position, targetObject.transform.position);
        currentDistance = camDistance;
        targetDistance = camDistance;

        // 计算初始旋转角度
        Vector3 directionToTarget = targetObject.transform.position - transform.position;
        Quaternion initialRotation = Quaternion.LookRotation(directionToTarget);
        currentYaw = initialRotation.eulerAngles.y;
        currentPitch = initialRotation.eulerAngles.x;
        targetYaw = currentYaw;
        targetPitch = currentPitch;

        // 初始化偏移量
        targetOffset = Vector3.zero;
        currentOffset = targetOffset;

        lastInputTime = Time.time;
    }

    private void Update()
    {
        HandleInputIdleDetection();
        UpdateCameraTransform();
    }

    // 处理输入空闲检测
    private void HandleInputIdleDetection()
    {
        if (!enableIdleDetection) return;

        // 检测输入活动
        if (Input.anyKeyDown || Touch.activeTouches.Count > 0)
        {
            lastInputTime = Time.time;
            isIdle = false;
        }

        // 检查是否进入待机状态
        if (Time.time - lastInputTime > idleWaitTime && !isIdle)
        {
            isIdle = true;
        }

        // 待机状态处理
        if (isIdle)
        {
            if (orbitOnIdle)
            {
                targetYaw += orbitSpeed * Time.deltaTime;
            }
            else
            {
                // 恢复到默认位置
                Quaternion defaultRot = Quaternion.LookRotation(
                    (targetObject.transform.position + targetOffset - defaultCameraPosition).normalized
                );
                targetYaw = defaultRot.eulerAngles.y;
                targetPitch = defaultRot.eulerAngles.x;
                targetDistance = Vector3.Distance(defaultCameraPosition, targetObject.transform.position + targetOffset);
            }
        }
    }

    // 更新摄像机变换
    private void UpdateCameraTransform()
    {
        // 插值计算旋转
        if (horizontalRotationLerpSpeed > 0)
        {
            currentYaw = Mathf.Lerp(currentYaw, targetYaw, horizontalRotationLerpSpeed * Time.deltaTime);
        }
        else
        {
            currentYaw = targetYaw;
        }

        if (verticalRotationLerpSpeed > 0)
        {
            currentPitch = Mathf.Lerp(currentPitch, targetPitch, verticalRotationLerpSpeed * Time.deltaTime);
        }
        else
        {
            currentPitch = targetPitch;
        }

        // 插值计算距离
        if (zoomLerpSpeed > 0)
        {
            currentDistance = Mathf.Lerp(currentDistance, targetDistance, zoomLerpSpeed * Time.deltaTime);
        }
        else
        {
            currentDistance = targetDistance;
        }

        // 插值计算偏移
        if (panLerpSpeed > 0)
        {
            currentOffset = Vector3.Lerp(currentOffset, targetOffset, panLerpSpeed * Time.deltaTime);
        }
        else
        {
            currentOffset = targetOffset;
        }

        // 应用旋转和位置
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        Vector3 targetPosition = targetObject.transform.position + currentOffset;
        transform.position = targetPosition - rotation * Vector3.forward * currentDistance;
        transform.rotation = rotation;
    }

    // 处理拖动旋转（绑定到触控拖动事件）
    public void OnDrag(Vector3 worldPosition)
    {
        if (isIdle)
        {
            isIdle = false;
            lastInputTime = Time.time;
        }

        // 将世界位置转换为屏幕坐标
        Vector2 screenPos = mainCamera.WorldToScreenPoint(worldPosition);
        
        // 获取上一帧的位置
        Vector2 prevScreenPos = mainCamera.WorldToScreenPoint(
            mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCamera.nearClipPlane))
        );

        // 计算屏幕空间的移动量
        Vector2 delta = screenPos - prevScreenPos;

        // 计算旋转角度（基于屏幕移动和当前距离）
        float horizontalDelta = -delta.x * horizontalRotationSpeed;
        float verticalDelta = -delta.y * verticalRotationSpeed;

        // 应用旋转限制（防止过度翻转）
        targetYaw += horizontalDelta;
        targetPitch = Mathf.Clamp(targetPitch + verticalDelta, 5f, 85f);
    }

    // 处理缩放（绑定到双指缩放事件）
    public void OnPinch(float scale)
    {
        if (isIdle)
        {
            isIdle = false;
            lastInputTime = Time.time;
        }

        // 计算新的距离
        float newDistance = currentDistance * scale * zoomScaleMultiplier;
        
        // 限制距离范围
        targetDistance = Mathf.Clamp(newDistance, minCameraDistance, maxCameraDistance);
    }

    // 处理平移（绑定到双指平移事件）
    public void OnTwoFingerPan(Vector3 moveDelta)
    {
        if (isIdle)
        {
            isIdle = false;
            lastInputTime = Time.time;
        }

        // 计算新的偏移量
        Vector3 newOffset = targetOffset - moveDelta;

        // 检查目标是否在屏幕内
        Vector3 targetScreenPos = mainCamera.WorldToScreenPoint(targetObject.transform.position + newOffset);
        
        bool isInScreen = true;
        
        // 检查左右边界
        if (targetScreenPos.x < screenEdgeMargin)
        {
            newOffset.x = targetOffset.x;
            isInScreen = false;
        }
        else if (targetScreenPos.x > Screen.width - screenEdgeMargin)
        {
            newOffset.x = targetOffset.x;
            isInScreen = false;
        }
        
        // 检查上下边界
        if (targetScreenPos.y < screenEdgeMargin)
        {
            newOffset.y = targetOffset.y;
            isInScreen = false;
        }
        else if (targetScreenPos.y > Screen.height - screenEdgeMargin)
        {
            newOffset.y = targetOffset.y;
            isInScreen = false;
        }

        // 如果在屏幕内，应用新的偏移量
        if (isInScreen)
        {
            targetOffset = newOffset;
        }
    }

    // 重置摄像机到初始状态
    public void ResetToDefault()
    {
        targetYaw = defaultCameraRotation.eulerAngles.y;
        targetPitch = defaultCameraRotation.eulerAngles.x;
        targetDistance = camDistance;
        targetOffset = Vector3.zero;
        
        lastInputTime = Time.time;
        isIdle = false;
    }
}
