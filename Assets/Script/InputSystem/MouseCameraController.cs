using JetBrains.Annotations;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class MouseCameraController : MonoBehaviour
{
    [Header("目标设置")]
    public GameObject targetObject;

    [Header("旋转设置")]
    [Tooltip("增大默认值，让旋转更明显")]
    public float horizontalRotationSpeed = 0.2f;
    public float verticalRotationSpeed = 0.2f;
    public float rotationLerpSpeed = 10f;
    [Tooltip("最大Pitch角度")]
    public float maxPitch = 85f;
    [Tooltip("最小Pitch角度")]
    public float minPitch = 5f;

    [Header("缩放设置")]
    public float minCameraDistance = 1f;
    public float maxCameraDistance = 10f;
    public float zoomSensitivity = 1f;
    public float zoomLerpSpeed = 10f;

    [Header("平移设置")]
    [Tooltip("增大默认值，让平移更明显")]
    public float panSpeed = 0.01f;
    public float panLerpSpeed = 10f;
    public float screenEdgeMargin = 50f;

    [Header("调试")]
    public bool debugMode = false;

    private Camera mainCamera;
    private float camDistance;
    private Vector3 defaultCameraPosition;
    private Quaternion defaultCameraRotation;

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
    private Vector3 targetOffset;

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();

        if (targetObject == null)
        {
            Debug.LogWarning("未指定注视目标，创建默认目标");
            targetObject = new GameObject("Camera Target");
            targetObject.transform.position = transform.position + transform.forward * 5f;
        }
    }

    private void Start()
    {
        defaultCameraPosition = transform.position;
        defaultCameraRotation = transform.rotation;

        // 初始化距离和角度
        camDistance = Vector3.Distance(transform.position, targetObject.transform.position);
        currentDistance = camDistance;
        targetDistance = camDistance;

        Vector3 directionToTarget = targetObject.transform.position - transform.position;
        Quaternion initialRotation = Quaternion.LookRotation(directionToTarget);
        currentYaw = initialRotation.eulerAngles.y;
        currentPitch = initialRotation.eulerAngles.x;
        targetYaw = currentYaw;
        targetPitch = currentPitch;

        targetOffset = Vector3.zero;
        currentOffset = targetOffset;
    }

    private void Update()
    {
        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        // 平滑旋转
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, rotationLerpSpeed * Time.deltaTime);
        currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, rotationLerpSpeed * Time.deltaTime);

        // 平滑缩放
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, zoomLerpSpeed * Time.deltaTime);

        // 平滑平移
        currentOffset = Vector3.Lerp(currentOffset, targetOffset, panLerpSpeed * Time.deltaTime);

        // 应用变换
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        Vector3 targetPosition = targetObject.transform.position + currentOffset;
        transform.position = targetPosition - rotation * Vector3.forward * currentDistance;
        transform.rotation = rotation;
    }

    // 处理左键拖动旋转
    public void OnLeftDrag(Vector2 currentPos, Vector2 lastPos)
    {
        if (debugMode) Debug.Log($"接收左键拖动: {currentPos} -> {lastPos}");

        Vector2 delta = currentPos - lastPos;

        // 计算旋转角度
        targetYaw += delta.x * horizontalRotationSpeed;
        targetPitch -= delta.y * verticalRotationSpeed;

        // 限制垂直旋转角度
        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);


    }


    // 处理右键拖动平移（基于摄像机视口平面）
    public void OnRightDrag(Vector2 currentPos, Vector2 lastPos)
    {
        if (debugMode) Debug.Log($"接收右键拖动: {currentPos} -> {lastPos}");

        // 计算屏幕空间的拖动差值
        Vector2 delta = currentPos - lastPos;

        // 获取摄像机的右方向和上方向（视口平面的两个轴）
        Vector3 cameraRight = mainCamera.transform.right;
        Vector3 cameraUp = mainCamera.transform.up;

        // 计算平移向量：基于摄像机视口平面
        // 注意：delta.x对应左右（摄像机右方为正），delta.y对应上下（摄像机上方为正）
        // 负值是因为拖动方向与期望平移方向相反（向右拖动摄像机应向左移）
        Vector3 panDelta = -(delta.x * cameraRight + delta.y * cameraUp) * panSpeed * currentDistance;

        // 计算新的偏移量
        Vector3 newOffset = targetOffset + panDelta;
        // 先同步Z轴，因为Z轴是深度，不需要屏幕内检测
        //targetOffset.z = newOffset.z;

        // // 应用屏幕边界限制
        // Vector3 targetScreenPos = mainCamera.WorldToScreenPoint(targetObject.transform.position + newOffset);

        // Debug.Log("屏幕宽高为：[" + Screen.width + ',' + Screen.height + ']');
        // Debug.Log("屏幕空间坐标为：[" + targetScreenPos.x + ',' + targetScreenPos.y + ',' + targetScreenPos.z + ']');

        // // 检查水平边界
        // if (targetScreenPos.x > screenEdgeMargin && targetScreenPos.x < Screen.width - screenEdgeMargin)
        //     targetOffset.x = newOffset.x;

        // // 检查垂直边界
        // if (targetScreenPos.y > screenEdgeMargin && targetScreenPos.y < Screen.height - screenEdgeMargin)
        //     targetOffset.y = newOffset.y;

        if (IsOnScreen(targetObject,panDelta))
            targetOffset = newOffset;
    }
    bool IsOnScreen(GameObject target,Vector3 NewOffSet) {
        Vector3 screenPos = mainCamera.WorldToViewportPoint(target.transform.position + NewOffSet);
        return (screenPos.x > 0 && screenPos.x < 1 && screenPos.y > 0 && screenPos.y < 1 && screenPos.z > 0);
    }

    // 处理滚轮缩放
    public void OnScroll(float scale)
    {
        if (debugMode) Debug.Log($"接收缩放: {scale}");
        
        float newDistance = currentDistance * scale * zoomSensitivity;
        targetDistance = Mathf.Clamp(newDistance, minCameraDistance, maxCameraDistance);
    }

    // 重置摄像机
    public void ResetToDefault()
    {
        targetYaw = defaultCameraRotation.eulerAngles.y;
        targetPitch = defaultCameraRotation.eulerAngles.x;
        targetDistance = camDistance;
        targetOffset = Vector3.zero;
    }
}
