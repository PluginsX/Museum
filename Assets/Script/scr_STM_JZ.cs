using UnityEngine;

public class scr_STM_JZ : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("旋转速度（度/秒）")]
    public float rotationSpeed = 30f; // 默认30度/秒[1,4](@ref)
    [Tooltip("是否启用自动旋转")]
    public bool autoRotate = true; // 可通过代码或Inspector开关[4](@ref)

    // Update is called once per frame
    void Update()
    {
        if (autoRotate)
        {
            // 绕Z轴旋转（世界坐标系）[3,5](@ref)
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            /* 替代方案（四元数版）[3](@ref)
            transform.rotation *= Quaternion.Euler(0, 0, rotationSpeed * Time.deltaTime);
            */
        }
    }

    // 外部调用的旋转开关方法[4](@ref)
    public void ToggleRotation(bool enable)
    {
        autoRotate = enable;
    }
}