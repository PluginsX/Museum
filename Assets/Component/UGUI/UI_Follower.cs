using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class UIFollower : MonoBehaviour
{
    [Tooltip("需要跟随的目标对象")]
    public GameObject targetObject;
    
    [Tooltip("UGUI面向摄像机的方式")]
    public FaceCameraMode faceCameraMode = FaceCameraMode.DoNotFace;
    
    private RectTransform rectTransform;
    private Camera uiCamera;
    private Vector3 initialOffset;
    private Vector3 lastTargetPosition;

    // 面向摄像机的选项枚举
    public enum FaceCameraMode
    {
        DoNotFace,       // 不面向摄像机
        FaceCamera,      // 完全面向摄像机
        FaceCameraUpAxis // 仅围绕Up轴面向摄像机
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        
        // 获取UI摄像机（通常是Canvas的摄像机，如果没有则使用主摄像机）
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.worldCamera != null)
        {
            uiCamera = canvas.worldCamera;
        }
        else
        {
            uiCamera = Camera.main;
            Debug.LogWarning("没有找到UI摄像机，将使用主摄像机");
        }
    }

    private void Start()
    {
        // 初始化偏移量
        if (targetObject != null)
        {
            UpdateInitialOffset();
            lastTargetPosition = targetObject.transform.position;
        }
        else
        {
            Debug.LogError("请指定跟随的目标对象！");
        }
    }

    private void LateUpdate()
    {
        if (targetObject == null || uiCamera == null) return;

        // 如果目标位置发生变化，则更新UI位置
        if (targetObject.transform.position != lastTargetPosition)
        {
            UpdateUIPosition();
            lastTargetPosition = targetObject.transform.position;
        }

        // 根据设置处理面向摄像机
        HandleFaceCamera();
    }

    /// <summary>
    /// 更新初始偏移量
    /// </summary>
    private void UpdateInitialOffset()
    {
        // 将UI位置从屏幕空间转换到世界空间，计算与目标的偏移
        Vector3 uiWorldPosition;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            rectTransform, 
            rectTransform.anchoredPosition, 
            uiCamera, 
            out uiWorldPosition
        );
        
        initialOffset = uiWorldPosition - targetObject.transform.position;
    }

    /// <summary>
    /// 更新UI位置以跟随目标
    /// </summary>
    private void UpdateUIPosition()
    {
        // 计算UI应该在世界空间中的位置
        Vector3 targetWorldPosition = targetObject.transform.position + initialOffset;
        
        // 将世界空间位置转换为屏幕空间位置
        // 根据提供的方法定义修改：使用返回值而不是out参数，仅传递两个参数
        Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(uiCamera, targetWorldPosition);
        
        // 更新UI的位置
        rectTransform.position = screenPosition;
    }

    /// <summary>
    /// 处理面向摄像机的逻辑
    /// </summary>
    private void HandleFaceCamera()
    {
        switch (faceCameraMode)
        {
            case FaceCameraMode.FaceCamera:
                // 完全面向摄像机
                Quaternion lookRotation = Quaternion.LookRotation(uiCamera.transform.forward);
                rectTransform.rotation = lookRotation;
                break;
                
            case FaceCameraMode.FaceCameraUpAxis:
                // 仅围绕Up轴面向摄像机
                Vector3 targetDirection = uiCamera.transform.position - transform.position;
                targetDirection.y = 0; // 忽略Y轴，只在水平面上旋转
                if (targetDirection.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
                    rectTransform.rotation = targetRotation;
                }
                break;
                
            case FaceCameraMode.DoNotFace:
                // 不做任何旋转处理
                break;
        }
    }

    // 在编辑器中当目标对象变化时更新偏移量
    private void OnValidate()
    {
        if (Application.isPlaying && targetObject != null && uiCamera != null)
        {
            UpdateInitialOffset();
        }
    }
}
