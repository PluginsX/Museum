using UnityEngine;

public class ImprovedCameraController : MonoBehaviour
{
    [Header("目标设置")]
    public Transform target; // 摄像机注视的目标对象
    public Vector3 targetOffset = Vector3.zero; // 目标物体的局部坐标偏移，便于微调注视中心

    [Header("距离控制")]
    public float distance = 10.0f;
    public float minDistance = 2.0f;
    public float maxDistance = 50.0f;
    private float targetDistance;

    [Header("旋转控制")]
    public float rotateSpeed = 1.0f; // 整体旋转速度系数
    public bool invertHorizontal = false;
    public bool invertVertical = false;
    private float currentX = 0.0f;
    private float currentY = 0.0f;
    private float targetX, targetY;

    [Header("平移控制")]
    public float panSpeed = 0.5f;
    private Vector3 currentPanOffset = Vector3.zero;
    private Vector3 targetPanOffset = Vector3.zero;

    [Header("缩放控制")] // 新增缩放控制参数组
    public float zoomSpeed = 0.5f; // 缩放速度系数[2](@ref)
    public float mouseWheelStep = 2.0f; // 鼠标滚轮步进值[3](@ref)

    [Header("延迟时间设置 (秒)")]
    public float rotationDelayTime = 0.1f; // 旋转延迟时间：从当前状态到目标状态所需的时间（秒）
    public float zoomDelayTime = 0.1f;     // 缩放延迟时间
    public float panDelayTime = 0.1f;      // 平移延迟时间

    [Header("双指操作")]
    public float pinchThreshold = 10.0f; // 改用更直观的像素阈值

    [Header("待机模式")]
    public bool enableIdleMode = true;
    public float idleWaitTime = 5.0f;
    public bool rotateInIdleMode = true;
    public float idleRotationSpeed = 10.0f;
    private float idleTimer = 0.0f;
    private bool isIdleMode = false;

    // 触摸旋转专用变量
    private bool isFirstTouch = true;
    private Vector3 initialWorldTouchDir; // 初始触摸方向（世界空间）
    private Quaternion initialCameraRotation; // 初始摄像机旋转

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target == null)
        {
            Debug.LogWarning("Target not assigned. Please assign a target in the inspector.");
            return;
        }

        Vector3 angles = transform.eulerAngles;
        currentX = targetX = angles.y;
        currentY = targetY = angles.x;
        targetDistance = distance;

        UpdateCameraPosition(true);
    }

    void Update()
    {
        if (target == null) return;

        bool hasInput = CheckInput();
        UpdateIdleTimer(hasInput);
        HandleIdleMode();
        UpdateCameraPosition(false);
    }

    bool CheckInput()
    {
        bool hasInput = false;

        if (Input.touchCount > 0)
        {
            hasInput = true;
            HandleTouchInput();
        }
        else
        {
            if (HandleMouseInput())
            {
                hasInput = true;
            }
            isFirstTouch = true; // 鼠标模式下，每次开始都视为首次触摸
        }

        return hasInput;
    }

    void HandleTouchInput()
    {
        // 单点触摸：旋转
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            Vector3 touchWorldPoint = GetWorldPointOnTargetPlane(touch.position);

            if (touch.phase == TouchPhase.Began)
            {
                isFirstTouch = true;
            }

            if (touch.phase == TouchPhase.Moved)
            {
                if (isFirstTouch)
                {
                    // 记录初始方向
                    initialWorldTouchDir = (touchWorldPoint - target.position).normalized;
                    initialCameraRotation = transform.rotation;
                    isFirstTouch = false;
                }
                else
                {
                    // 计算当前方向
                    Vector3 currentWorldTouchDir = (touchWorldPoint - target.position).normalized;

                    // 计算从初始方向到当前方向的旋转
                    Quaternion deltaRotation = Quaternion.FromToRotation(initialWorldTouchDir, currentWorldTouchDir);
                    // 将这个旋转应用到初始摄像机旋转上，得到目标旋转
                    Quaternion targetRot = deltaRotation * initialCameraRotation;

                    // 将目标旋转转换为欧拉角，用于后续的插值
                    Vector3 targetEuler = targetRot.eulerAngles;
                    targetX = targetEuler.y;
                    targetY = targetEuler.x;
                }
            }
        }
        // 双点触摸：缩放或平移
        else if (Input.touchCount == 2)
        {
            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

            float prevTouchDeltaMag = (touch0PrevPos - touch1PrevPos).magnitude;
            float touchDeltaMag = (touch0.position - touch1.position).magnitude;

            float deltaMagnitudeDiff = prevTouchDeltaMag - touchDeltaMag;

            Vector2 centerPoint = (touch0.position + touch1.position) / 2;
            Vector2 prevCenterPoint = (touch0PrevPos + touch1PrevPos) / 2;
            Vector2 centerDelta = centerPoint - prevCenterPoint;

            if (Mathf.Abs(deltaMagnitudeDiff) > pinchThreshold)
            {
                // 缩放
                targetDistance += deltaMagnitudeDiff * zoomSpeed;
                targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            }
            else if (centerDelta.magnitude > 0.1f)
            {
                // 平移
                Vector3 worldDelta = cam.transform.right * centerDelta.x * panSpeed + cam.transform.up * centerDelta.y * panSpeed;
                targetPanOffset -= worldDelta * Time.deltaTime;
            }
        }
    }

    bool HandleMouseInput()
    {
        bool hasInput = false;

        // 鼠标左键拖动：旋转
        if (Input.GetMouseButton(0))
        {
            hasInput = true;
            targetX += Input.GetAxis("Mouse X") * rotateSpeed * (invertHorizontal ? -1 : 1);
            targetY -= Input.GetAxis("Mouse Y") * rotateSpeed * (invertVertical ? -1 : 1);
            targetY = Mathf.Clamp(targetY, -80, 80);
        }

        // 鼠标右键拖动：平移
        if (Input.GetMouseButton(1))
        {
            hasInput = true;
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            Vector3 worldDelta = cam.transform.right * mouseX * panSpeed + cam.transform.up * mouseY * panSpeed;
            targetPanOffset -= worldDelta;
        }

        // 鼠标滚轮：缩放
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            hasInput = true;
            targetDistance -= scroll * mouseWheelStep;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }

        return hasInput;
    }

    void UpdateIdleTimer(bool hasInput)
    {
        if (!enableIdleMode) return;

        if (hasInput)
        {
            idleTimer = 0.0f;
            isIdleMode = false;
        }
        else
        {
            idleTimer += Time.deltaTime;
            if (idleTimer >= idleWaitTime)
            {
                isIdleMode = true;
            }
        }
    }

    void HandleIdleMode()
    {
        if (!enableIdleMode || !isIdleMode) return;

        if (rotateInIdleMode)
        {
            targetX += idleRotationSpeed * Time.deltaTime;
        }
        targetPanOffset = Vector3.Lerp(targetPanOffset, Vector3.zero, Time.deltaTime / panDelayTime);
        targetDistance = Mathf.Lerp(targetDistance, distance, Time.deltaTime / zoomDelayTime);
    }

    void UpdateCameraPosition(bool immediate)
    {
        // 计算平滑系数（基于延迟时间）。延迟时间越短，跟进越快。
        float rotationLerpFactor = immediate ? 1.0f : Mathf.Clamp01(Time.deltaTime / rotationDelayTime);
        float zoomLerpFactor = immediate ? 1.0f : Mathf.Clamp01(Time.deltaTime / zoomDelayTime);
        float panLerpFactor = immediate ? 1.0f : Mathf.Clamp01(Time.deltaTime / panDelayTime);

        // 应用插值
        currentX = Mathf.LerpAngle(currentX, targetX, rotationLerpFactor);
        currentY = Mathf.Lerp(currentY, targetY, rotationLerpFactor);
        distance = Mathf.Lerp(distance, targetDistance, zoomLerpFactor);
        currentPanOffset = Vector3.Lerp(currentPanOffset, targetPanOffset, panLerpFactor);

        // 计算最终位置和旋转
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        Vector3 targetFinalPosition = target.position + targetOffset + currentPanOffset;
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -distance);
        Vector3 position = rotation * negDistance + targetFinalPosition;

        transform.rotation = rotation;
        transform.position = position;
    }

    // 获取触摸点在与摄像机视角垂直且过目标点的平面上的世界坐标
    private Vector3 GetWorldPointOnTargetPlane(Vector2 screenPosition)
    {
        Plane targetPlane = new Plane(-cam.transform.forward, target.position + targetOffset);
        Ray ray = cam.ScreenPointToRay(screenPosition);
        float enter;
        if (targetPlane.Raycast(ray, out enter))
        {
            return ray.GetPoint(enter);
        }
        // 如果射线与平面未相交（理论上应始终相交），返回一个默认值
        return target.position + targetOffset + cam.transform.right * screenPosition.x + cam.transform.up * screenPosition.y;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void ResetCamera()
    {
        targetX = 0.0f;
        targetY = 0.0f;
        targetDistance = distance;
        targetPanOffset = Vector3.zero;
        idleTimer = 0.0f;
        isIdleMode = false;
        isFirstTouch = true;
        UpdateCameraPosition(true);
    }
}