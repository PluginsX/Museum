
using UnityEngine;
using Museum.Debug;

public class CameraController : MonoBehaviour
{
    [Header("目标设置")]
    public Camera mainCamera;
    public GameObject targetObject;

    [Header("旋转设置")]
    [Tooltip("增大默认值，让旋转更明显")]
    public float horizontalRotationSpeed = 0.2f;
    public float verticalRotationSpeed = 0.2f;
    public float rotationLerpSpeed = 5f;
    public float MaxPitch = 90f;
    public float MinPitch = -90f;

    [Header("缩放设置")]
    public float minCameraDistance = 0.2f;
    public float maxCameraDistance = 1f;
    public float zoomStepRatio = 0.3f;
    public float zoomLerpSpeed = 5f;

    [Header("平移设置")]
    [Tooltip("增大默认值，让平移更明显")]
    public float panSpeed = 0.005f;
    public float panLerpSpeed = 5f;
    public float screenEdgeMargin = 50f;

    [Header("调试")]
    public bool debugMode = false;

    private float camDistance;
    private Vector3 defaultCameraPosition;
    private Quaternion defaultCameraRotation;

    // 旋转
    private float currentYaw;
    private float currentPitch;
    private float targetYaw;
    private float targetPitch;

    // 缩放
    private float currentDistance;
    private float targetDistance;

    // 平移
    private Vector3 currentOffset;
    private Vector3 targetOffset;

    private void Awake()
    {
        // 如果过没有明确指定控制摄像机，默认控制当前主摄像
        if (mainCamera == null)
        {
            Log.Print("Camera", "Warning", "未指定控制摄像机，默认控制当前使用的摄像");
            mainCamera = Camera.main;
        }

        // 如果没有指定注视目标，默认注视摄像机前方2M
        if (targetObject == null)
        {
            Log.Print("Camera", "Warning", "未指定注视目标，创建默认目标");
            targetObject = new GameObject("LookTarget");
            targetObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 2f;
        }
    }

    private void Start()
    {
        defaultCameraPosition = mainCamera.transform.position;
        defaultCameraRotation = mainCamera.transform.rotation;

        /***********************************************************************************************
         * 初始化距离和角度
         */
        camDistance = Vector3.Distance(mainCamera.transform.position, targetObject.transform.position);
        // 默认距离
        currentDistance = camDistance;
        targetDistance = camDistance;
        // 获取摄像机启动旋转角
        Quaternion initialRotation = mainCamera.transform.rotation;
        // 当前角度
        currentYaw = initialRotation.eulerAngles.y;
        currentPitch = initialRotation.eulerAngles.x;
        // 目标角度
        targetYaw = currentYaw;
        targetPitch = currentPitch;
        // 位置偏移
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

        // 平滑位移
        currentOffset = Vector3.Lerp(currentOffset, targetOffset, panLerpSpeed * Time.deltaTime);

        // 位移后要更新目标点的位置，始终保持目标点在摄象机正前
        Vector3 targetPosition = targetObject.transform.position + currentOffset;

        // 将以上对摄象机的变换，转换为围绕目标点的摄象机变
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        mainCamera.transform.position = targetPosition - rotation * Vector3.forward * currentDistance;
        mainCamera.transform.rotation = rotation;
    }

    // 处理旋转事件（对应InputHandler的onRotate事件
    // 绑定到：InputHandler.onRotate
    public void OnRotate(Vector2 lastPos, Vector2 currentPos)
    {
        if (debugMode) Log.Print("Camera", "Debug", $"接收旋转输入: {currentPos} -> {lastPos}");
        
        Vector2 delta = currentPos - lastPos;
        
        // 计算旋转角度
        targetYaw += delta.x * horizontalRotationSpeed;
        targetPitch -= delta.y * verticalRotationSpeed;
        
        // 限制垂直旋转角度（避免翻转）
        targetPitch = Mathf.Clamp(targetPitch, MinPitch, MaxPitch);
    }

    // 处理平移事件（对应InputHandler的onPan事件
    // 绑定到：InputHandler.onPan
    public void OnMove(Vector2 lastPos, Vector2 currentPos)
    {
        if (debugMode) Log.Print("Camera", "Debug", $"接收平移输入: {currentPos} -> {lastPos}");

        // 计算屏幕空间的拖动差
        Vector2 delta = currentPos - lastPos;

        // 获取摄像机的右方向和上方向（视口平面的两个轴
        Vector3 cameraRight = mainCamera.transform.right;
        Vector3 cameraUp = mainCamera.transform.up;

        // 计算平移向量：基于摄像机视口平面
        // 注意：delta.x对应左右（摄像机右方为正），delta.y对应上下（摄像机上方为正
        // 负值是因为拖动方向与期望平移方向相反（向右拖动摄像机应向左移）（考虑当前距离，使平移速度更自然）
        Vector3 panDelta = -(delta.x * cameraRight + delta.y * cameraUp) * panSpeed * currentDistance;
        // 计算新的偏移
        Vector3 newOffset = targetOffset + panDelta;

        // 应用边界限制（确保目标不超出屏幕边缘
        // Vector3 newOffset = targetOffset + panDelta;
        // Vector3 targetScreenPos = mainCamera.WorldToScreenPoint(targetObject.transform.position + newOffset);

        // if (targetScreenPos.x >= screenEdgeMargin && targetScreenPos.x <= Screen.width - screenEdgeMargin)
        //     targetOffset.x = newOffset.x;

        // if (targetScreenPos.y >= screenEdgeMargin && targetScreenPos.y <= Screen.height - screenEdgeMargin)
        //     targetOffset.y = newOffset.y;
        // 上或
        // if (IsOnScreen(targetObject,panDelta))
        //     targetOffset = newOffset;

        // 更新目标偏移
        targetOffset = newOffset;

    }
    bool IsOnScreen(GameObject target,Vector3 NewOffSet) {
        Vector3 screenPos = mainCamera.WorldToViewportPoint(target.transform.position + NewOffSet);
        return (screenPos.x > 0 && screenPos.x < 1 && screenPos.y > 0 && screenPos.y < 1 && screenPos.z > 0);
    }
    
    // 处理缩放事件（对应InputHandler的onScale事件
    // 绑定到：InputHandler.onScale
    public void OnScale(float scaleFactor)
    {
        if (debugMode) Log.Print("Camera", "Debug", $"接收缩放输入: {scaleFactor}");
        if(scaleFactor==0)
            return;
        
        // 根据缩放因子调整距离（乘以步长比例控制缩放幅度）
        float newDistance = currentDistance *(1+(scaleFactor * zoomStepRatio));
        // 更新目标距离(缩放)，并加以安全范围限制
        targetDistance = Mathf.Clamp(newDistance, minCameraDistance, maxCameraDistance);
    }

    // 处理单击事件（可选：如需单击目标功能可启用）
    // 绑定到：InputHandler.onSingleClick（可选）
    public void OnClick(Vector2 clickPosition)
    {
        if (debugMode) Log.Print("Camera", "Debug", $"接收单击: {clickPosition}");
        
        // 可添加单击逻辑，例如射线检测选中目标
        // Ray ray = mainCamera.ScreenPointToRay(clickPosition);
        // if (Physics.Raycast(ray, out RaycastHit hit))
        // {
        //     targetObject = hit.collider.gameObject;
        // }
    }

    // 重置摄像机到初始
    public void ResetCamera()
    {
        targetYaw = defaultCameraRotation.eulerAngles.y;
        targetPitch = defaultCameraRotation.eulerAngles.x;
        targetDistance = camDistance;
        targetOffset = Vector3.zero;
    }
}