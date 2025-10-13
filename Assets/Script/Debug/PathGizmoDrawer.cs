using System.Collections.Generic;
using UnityEngine;

public class PathGizmoDrawer : MonoBehaviour
{
    [Header("球体设置")]
    [SerializeField] private bool enableSphereDrawing = true;
    [SerializeField] private float sphereRadius = 0.5f;
    [SerializeField] private Color sphereColor = Color.yellow;
    [SerializeField] [Range(3, 50)] private int sphereSegments = 20;
    [SerializeField] private float sphereLifetime = 3.0f;
    [SerializeField] private float sphereDrawInterval = 1.0f;

    [Header("线段设置")]
    [SerializeField] private bool enableLineDrawing = true;
    [SerializeField] private float lineThickness = 0.05f;
    [SerializeField] private Color lineColor = Color.cyan;
    [SerializeField] private float lineLifetime = 3.0f;
    [SerializeField] private float nodeDrawInterval = 1.0f;

    [Header("高级设置")]
    [SerializeField] private bool drawOnlyWhenSelected = false;
    [SerializeField] private bool useWorldSpace = true;
    [SerializeField] private bool autoConnectNodes = true;

    private float lastSphereDrawTime;
    private float lastNodeDrawTime;
    private List<Vector3> pathNodes = new List<Vector3>();
    private List<GizmoData> activeGizmos = new List<GizmoData>();

    // Gizmo数据存储类
    [System.Serializable]
    private class GizmoData
    {
        public GizmoType type;
        public Vector3[] positions;
        public float radius;
        public Color color;
        public int segments;
        public float spawnTime;
        public float lifetime;
        public Transform relativeTransform;

        public GizmoData(GizmoType gizmoType, Vector3[] pos, float rad, Color col, 
                         int seg, float life, Transform relative = null)
        {
            type = gizmoType;
            positions = pos;
            radius = rad;
            color = col;
            segments = seg;
            spawnTime = Time.time;
            lifetime = life;
            relativeTransform = relative;
        }

        public bool IsExpired()
        {
            return Time.time > spawnTime + lifetime;
        }

        public Vector3 GetWorldPosition(int index = 0)
        {
            if (relativeTransform != null && positions != null && index < positions.Length)
            {
                return relativeTransform.TransformPoint(positions[index]);
            }
            return positions != null && positions.Length > index ? positions[index] : Vector3.zero;
        }
    }

    private enum GizmoType
    {
        Sphere,
        Line
    }

    void Update()
    {
        if (!enableSphereDrawing && !enableLineDrawing) return;

        // 根据时间间隔决定是否添加新节点
        if (Time.time - lastNodeDrawTime >= nodeDrawInterval)
        {
            AddNewPathNode();
            lastNodeDrawTime = Time.time;
        }

        // 根据时间间隔决定是否绘制新球体
        if (enableSphereDrawing && Time.time - lastSphereDrawTime >= sphereDrawInterval)
        {
            AddNewSphere();
            lastSphereDrawTime = Time.time;
        }

        // 清理过期的Gizmos
        CleanExpiredGizmos();
    }

    void OnDrawGizmos()
    {
        if (!drawOnlyWhenSelected)
        {
            DrawAllGizmos();
        }
    }

    void OnDrawGizmosSelected()
    {
        if (drawOnlyWhenSelected)
        {
            DrawAllGizmos();
        }
    }

    /// <summary>
    /// 添加新的路径节点
    /// </summary>
    private void AddNewPathNode()
    {
        if (!enableLineDrawing) return;

        Transform relativeTransform = useWorldSpace ? null : transform;
        Vector3 spawnPosition = useWorldSpace ? transform.position : Vector3.zero;
        
        pathNodes.Add(spawnPosition);

        // 如果节点数足够绘制线段，则创建线段Gizmo
        if (pathNodes.Count >= 2 && autoConnectNodes)
        {
            Vector3[] linePoints = new Vector3[] { pathNodes[pathNodes.Count - 2], pathNodes[pathNodes.Count - 1] };
            GizmoData newLine = new GizmoData(
                GizmoType.Line,
                linePoints,
                lineThickness,
                lineColor,
                0, // 线段不需要segments参数
                lineLifetime,
                relativeTransform
            );
            activeGizmos.Add(newLine);
        }
    }

    /// <summary>
    /// 在当前位置添加新的球体
    /// </summary>
    private void AddNewSphere()
    {
        if (!enableSphereDrawing || pathNodes.Count == 0) return;

        Transform relativeTransform = useWorldSpace ? null : transform;
        Vector3 spawnPosition = pathNodes[pathNodes.Count - 1]; // 使用最新节点位置
        
        Vector3[] spherePosition = new Vector3[] { spawnPosition };
        GizmoData newSphere = new GizmoData(
            GizmoType.Sphere,
            spherePosition,
            sphereRadius,
            sphereColor,
            sphereSegments,
            sphereLifetime,
            relativeTransform
        );
        
        activeGizmos.Add(newSphere);
    }

    /// <summary>
    /// 绘制所有Gizmos
    /// </summary>
    private void DrawAllGizmos()
    {
        if (activeGizmos.Count == 0) return;

        foreach (GizmoData gizmo in activeGizmos)
        {
            DrawGizmo(gizmo);
        }
    }

    /// <summary>
    /// 绘制单个Gizmo
    /// </summary>
    private void DrawGizmo(GizmoData gizmo)
    {
        if (gizmo == null || gizmo.positions == null) return;

        // 计算随时间变化的透明度
        float lifeProgress = (Time.time - gizmo.spawnTime) / gizmo.lifetime;
        Color fadedColor = gizmo.color;
        fadedColor.a = gizmo.color.a * (1f - lifeProgress);
        Gizmos.color = fadedColor;

        switch (gizmo.type)
        {
            case GizmoType.Sphere:
                DrawWireSphere(gizmo);
                break;
            case GizmoType.Line:
                DrawLine(gizmo);
                break;
        }
    }

    /// <summary>
    /// 绘制线框球体
    /// </summary>
    private void DrawWireSphere(GizmoData sphere)
    {
        if (sphere.positions.Length == 0) return;

        Vector3 worldPosition = sphere.GetWorldPosition(0);
        Gizmos.DrawWireSphere(worldPosition, sphere.radius);

        // 绘制更精细的球体（多圈线框）
        DrawDetailedWireSphere(worldPosition, sphere.radius, sphere.segments);
    }

    /// <summary>
    /// 绘制线段
    /// </summary>
    private void DrawLine(GizmoData line)
    {
        if (line.positions.Length < 2) return;

        // 在Unity 2017.4.30f1中，我们需要手动绘制粗线段
        DrawThickLine(line.GetWorldPosition(0), line.GetWorldPosition(1), line.radius);
    }

    /// <summary>
    /// 绘制有厚度的线段（通过绘制多个细线段实现）
    /// </summary>
    private void DrawThickLine(Vector3 start, Vector3 end, float thickness)
    {
        // 计算线段的垂直方向
        Vector3 direction = (end - start).normalized;
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized * thickness * 0.5f;

        // 绘制多条线段来模拟有厚度的线
        Gizmos.DrawLine(start - perpendicular, end - perpendicular);
        Gizmos.DrawLine(start + perpendicular, end + perpendicular);
        Gizmos.DrawLine(start - perpendicular, start + perpendicular);
        Gizmos.DrawLine(end - perpendicular, end + perpendicular);
    }

    /// <summary>
    /// 绘制更详细的线框球体
    /// </summary>
    private void DrawDetailedWireSphere(Vector3 center, float radius, int segments)
    {
        if (segments <= 3) return;

        float angleStep = 360f / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float nextAngle = ((i + 1) % segments) * angleStep * Mathf.Deg2Rad;

            // 绘制经线环
            for (int j = 0; j < segments; j++)
            {
                float verticalAngle = j * angleStep * Mathf.Deg2Rad;
                float nextVerticalAngle = ((j + 1) % segments) * angleStep * Mathf.Deg2Rad;

                Vector3 point1 = GetSphericalPoint(center, radius, angle, verticalAngle);
                Vector3 point2 = GetSphericalPoint(center, radius, nextAngle, verticalAngle);
                Vector3 point3 = GetSphericalPoint(center, radius, angle, nextVerticalAngle);

                Gizmos.DrawLine(point1, point2);
                Gizmos.DrawLine(point1, point3);
            }
        }
    }

    /// <summary>
    /// 获取球体表面点坐标
    /// </summary>
    private Vector3 GetSphericalPoint(Vector3 center, float radius, float theta, float phi)
    {
        float x = center.x + radius * Mathf.Sin(phi) * Mathf.Cos(theta);
        float y = center.y + radius * Mathf.Cos(phi);
        float z = center.z + radius * Mathf.Sin(phi) * Mathf.Sin(theta);
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// 清理过期的Gizmos
    /// </summary>
    private void CleanExpiredGizmos()
    {
        for (int i = activeGizmos.Count - 1; i >= 0; i--)
        {
            if (activeGizmos[i].IsExpired())
            {
                activeGizmos.RemoveAt(i);
            }
        }

        // 同时清理过期的路径节点（避免内存泄漏）
        if (pathNodes.Count > 100) // 防止节点列表过大
        {
            pathNodes.RemoveRange(0, pathNodes.Count - 50);
        }
    }

    /// <summary>
    /// 手动添加一个路径节点
    /// </summary>
    public void AddManualNode(Vector3 position, bool drawSphere = true)
    {
        Transform relativeTransform = useWorldSpace ? null : transform;
        Vector3 actualPosition = useWorldSpace ? position : transform.InverseTransformPoint(position);

        pathNodes.Add(actualPosition);

        if (drawSphere && enableSphereDrawing)
        {
            Vector3[] spherePosition = new Vector3[] { actualPosition };
            GizmoData newSphere = new GizmoData(
                GizmoType.Sphere,
                spherePosition,
                sphereRadius,
                sphereColor,
                sphereSegments,
                sphereLifetime,
                relativeTransform
            );
            activeGizmos.Add(newSphere);
        }
    }

    /// <summary>
    /// 手动添加一条线段
    /// </summary>
    public void AddManualLine(Vector3 start, Vector3 end, float duration = -1f)
    {
        if (!enableLineDrawing) return;

        float actualLifetime = duration > 0 ? duration : lineLifetime;
        Transform relativeTransform = useWorldSpace ? null : transform;
        Vector3 actualStart = useWorldSpace ? start : transform.InverseTransformPoint(start);
        Vector3 actualEnd = useWorldSpace ? end : transform.InverseTransformPoint(end);

        Vector3[] linePoints = new Vector3[] { actualStart, actualEnd };
        GizmoData newLine = new GizmoData(
            GizmoType.Line,
            linePoints,
            lineThickness,
            lineColor,
            0,
            actualLifetime,
            relativeTransform
        );

        activeGizmos.Add(newLine);
    }

    /// <summary>
    /// 清除所有Gizmos和路径节点
    /// </summary>
    public void ClearAll()
    {
        activeGizmos.Clear();
        pathNodes.Clear();
    }

    /// <summary>
    /// 立即绘制临时线段和球体（不添加到管理列表）
    /// </summary>
    public void DrawInstantPath(Vector3[] nodes, float customRadius = -1f, Color? customColor = null)
    {
        if (nodes == null || nodes.Length < 2) return;

        float actualRadius = customRadius > 0 ? customRadius : sphereRadius;
        Color actualColor = customColor ?? sphereColor;

        Gizmos.color = actualColor;

        // 绘制线段
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            Gizmos.DrawLine(nodes[i], nodes[i + 1]);
        }

        // 绘制节点球体
        foreach (Vector3 node in nodes)
        {
            Gizmos.DrawWireSphere(node, actualRadius);
        }
    }
}