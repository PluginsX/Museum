using UnityEngine;

/// <summary>
/// 世界空间UI朝向控制器 (Billboard)
/// 挂在 World Space Canvas 下的任意 UI 对象上即可
/// </summary>
[ExecuteAlways] // 允许在编辑器模式下直接看到效果，无需运行
public class UI_WorldBillboard : MonoBehaviour
{
    public enum FaceMode
    {
        [Tooltip("平行模式：UI平面永远平行于屏幕平面（最适合UI，边缘无畸变）")]
        MatchCameraRotation,
        
        [Tooltip("聚焦模式：UI正心永远指向摄像机位置（适合3D场景中的物体，可能有透视畸变）")]
        FaceCameraPosition
    }

    [Header("核心设置")]
    [Tooltip("如果为空，自动使用主摄像机")]
    public Camera targetCamera;
    
    [Tooltip("朝向模式")]
    public FaceMode faceMode = FaceMode.MatchCameraRotation;

    [Header("轴向控制 (打钩代表该轴跟随摄像机，不打钩代表锁定)")]
    public bool rotateX = true; // 俯仰 (Pitch)
    public bool rotateY = true; // 偏航 (Yaw)
    public bool rotateZ = true; // 翻滚 (Roll) - UI通常建议关闭此项

    [Header("微调")]
    [Tooltip("额外的旋转偏移，用于修正素材本身的朝向问题")]
    public Vector3 rotationOffset = Vector3.zero;

    // 记录初始旋转，用于当某个轴被锁定时，保持该轴的原始角度
    private Quaternion originalRotation;
    
    private void Start()
    {
        originalRotation = transform.localRotation;
        if (Application.isPlaying && targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void LateUpdate()
    {
        // 编辑器模式下自动查找摄像机
        #if UNITY_EDITOR
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null && UnityEditor.SceneView.lastActiveSceneView != null)
            targetCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
        #endif

        if (targetCamera == null) return;

        UpdateLookAt();
    }

    private void UpdateLookAt()
    {
        Quaternion targetRotation;

        // 1. 计算目标全旋转
        if (faceMode == FaceMode.MatchCameraRotation)
        {
            // 模式A：完全复制摄像机的旋转
            // 这样能保证UI平面与屏幕平面平行，文字看起来最清晰，不会有透视变形
            targetRotation = targetCamera.transform.rotation;
        }
        else
        {
            // 模式B：LookRotation
            // 这样会让UI的中心点正对着摄像机镜头
            Vector3 direction = transform.position - targetCamera.transform.position;
            // 如果距离极近，保持当前旋转避免错误
            if (direction.sqrMagnitude < 0.001f) return; 
            targetRotation = Quaternion.LookRotation(direction);
        }

        // 2. 处理轴向锁定逻辑
        // 我们将旋转转换为欧拉角来分别处理 X, Y, Z
        Vector3 targetEuler = targetRotation.eulerAngles;
        Vector3 originalEuler = originalRotation.eulerAngles;
        
        // 如果是在运行中，我们希望锁定的轴保持由于父级变换带来的旋转，或者是Start时的旋转
        // 这里为了简单稳定，混合使用：
        // 如果轴开启：使用计算出的 targetEuler
        // 如果轴关闭：保持物体当前的 rotation (或者你可以改为保持 originalRotation)
        
        Vector3 currentEuler = transform.rotation.eulerAngles;

        float x = rotateX ? targetEuler.x : currentEuler.x;
        float y = rotateY ? targetEuler.y : currentEuler.y;
        float z = rotateZ ? targetEuler.z : currentEuler.z;

        // 3. 应用旋转 + 偏移
        Quaternion finalRotation = Quaternion.Euler(x, y, z);
        
        // 应用额外的偏移 (例如你需要让 UI 默认旋转 180 度)
        transform.rotation = finalRotation * Quaternion.Euler(rotationOffset);
    }
    
    // 当在编辑器中Reset组件时调用
    private void Reset()
    {
        rotateX = true;
        rotateY = true;
        rotateZ = false; // UI 通常不需要 Z 轴旋转 (Roll)
        rotationOffset = Vector3.zero;
        faceMode = FaceMode.MatchCameraRotation;
    }
}