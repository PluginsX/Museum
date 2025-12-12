using UnityEngine;
using UnityEngine.Events;
using Museum.Debug;

namespace Museum.Component.UGUI
{
    /// <summary>
    /// UI可见性事件组
    /// 实时检测本UGUI对象与指定ViewPort的重叠比
    /// 并在可见性变化时触发事件
    /// </summary>
    public class UI_VisibleEvent : MonoBehaviour
    {
        [SerializeField]
        private RectTransform viewPort;
        [SerializeField]
        [Tooltip("true：只在VisibleProportion变化时调用OnMotion；false：每一帧都调用OnMotion")]
        private bool trigMotionOnChg = true;

        [SerializeField]
        [Tooltip("true：只触发第一次OnVisible事件；false：每次进入可见区域都触发")]
        private bool onlyFirstVisible = false;

        [SerializeField]
        [Tooltip("true：只触发第一次OnInvisible事件；false：每次离开可见区域都触发")]
        private bool onlyFirstInvisible = false;

        [SerializeField]
        public UnityEvent OnVisible = new UnityEvent();
        [SerializeField]
        public UnityEvent<float> OnMotion = new UnityEvent<float>();
        [SerializeField]
        public UnityEvent OnInvisible = new UnityEvent();

        private float visibleProportion = 0f;

        public float VisibleProportion => visibleProportion;

        private bool wasVisible = false;
        private float lastVisibleProportion = 0f; // 记录上一帧的可见比例
        private bool hasTriggeredFirstVisible = false; // 是否已触发过第一次可见事件
        private bool hasTriggeredFirstInvisible = false; // 是否已触发过第一次不可见事件
        private RectTransform rectTransform;
        private Canvas canvas;

        private void OnEnable()
        {
            rectTransform = GetComponent<RectTransform>();
            canvas = GetComponentInParent<Canvas>();
            wasVisible = false;
            lastVisibleProportion = 0f;
            hasTriggeredFirstVisible = false;
            hasTriggeredFirstInvisible = false;

            // 注册测试事件
            OnVisible.AddListener(IsVisible);
            OnMotion.AddListener(IsMotion);
            OnInvisible.AddListener(IsInVisible);
        }

        public void IsVisible(){
            Log.Print("UI", "Debug", "IsVisible() | Visible Proportion: " + visibleProportion);
        }
        public void IsMotion(float proportion){
            Log.Print("UI", "Debug", $"IsMotion() | Visible Proportion: {proportion:F2}");
        }
        public void IsInVisible(){
            Log.Print("UI", "Debug", "IsInVisible() | Visible Proportion: " + visibleProportion);
        }

        private void Update()
        {
            //Log.Print("UI", "Debug", "Proportion: " + visibleProportion);
            if (viewPort == null || rectTransform == null)
                return;

            CalculateVisibleProportion();
            HandleVisibilityChange();

            // 根据设置决定 OnMotion 触发方式
            if (visibleProportion > 0)
            {
                if (trigMotionOnChg)
                {
                    // 只在 visibleProportion 变化时调用
                    if (!Mathf.Approximately(visibleProportion, lastVisibleProportion))
                    {
                        OnMotion?.Invoke(visibleProportion);
                        lastVisibleProportion = visibleProportion;
                    }
                }
                else
                {
                    // 每一帧都调用
                    OnMotion?.Invoke(visibleProportion);
                }
            }
            else if (!trigMotionOnChg)
            {
                // 当不在可见区域且设置为逐帧调用时，也传递当前值（虽然为0）
                OnMotion?.Invoke(visibleProportion);
            }
        }

        private void CalculateVisibleProportion()
        {
            // 获取两个RectTransform在屏幕空间中的矩形
            Rect thisScreenRect = GetScreenRect(rectTransform, canvas);
            Rect viewPortScreenRect = GetScreenRect(viewPort, canvas);

            // 计算两个屏幕矩形的重叠面积
            float overlapArea = GetRectOverlapArea(thisScreenRect, viewPortScreenRect);

            // 计算ViewPort的屏幕面积
            float viewPortArea = viewPortScreenRect.width * viewPortScreenRect.height;

            if (viewPortArea > 0)
            {
                visibleProportion = overlapArea / viewPortArea;
            }
            else
            {
                visibleProportion = 0f;
            }

            visibleProportion = Mathf.Clamp01(visibleProportion);
        }

        /// <summary>
        /// 获取RectTransform在屏幕空间中的矩形
        /// </summary>
        private Rect GetScreenRect(RectTransform rectTransform, Canvas canvas)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            // 获取Canvas渲染相机
            Camera cam = null;
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera || canvas.renderMode == RenderMode.WorldSpace)
            {
                cam = canvas.worldCamera;
            }

            // 将世界坐标转换为屏幕坐标
            for (int i = 0; i < 4; i++)
            {
                if (cam != null)
                {
                    corners[i] = cam.WorldToScreenPoint(corners[i]);
                }
                else
                {
                    // ScreenSpaceOverlay模式，直接使用xy坐标作为屏幕坐标
                    corners[i] = new Vector3(corners[i].x, corners[i].y, 0);
                }
            }

            // 计算包围盒
            Bounds bounds = new Bounds(corners[0], Vector3.zero);
            for (int i = 1; i < 4; i++)
            {
                bounds.Encapsulate(corners[i]);
            }

            return new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
        }

        /// <summary>
        /// 计算两个Rect的重叠面积
        /// </summary>
        private float GetRectOverlapArea(Rect rect1, Rect rect2)
        {
            float left = Mathf.Max(rect1.xMin, rect2.xMin);
            float right = Mathf.Min(rect1.xMax, rect2.xMax);
            float bottom = Mathf.Max(rect1.yMin, rect2.yMin);
            float top = Mathf.Min(rect1.yMax, rect2.yMax);

            float overlapWidth = Mathf.Max(0, right - left);
            float overlapHeight = Mathf.Max(0, top - bottom);

            return overlapWidth * overlapHeight;
        }

        /// <summary>
        /// 使用Sutherland-Hodgman算法计算两个凸多边形的交集面
        /// 支持任意方向（包括旋转）的矩
        /// </summary>
        private float CalculateConvexPolygonOverlapArea(Vector3[] polygon1, Vector3[] polygon2)
        {
            // D坐标投影D
            Vector2[] poly1_2D = Project3DTo2D(polygon1);
            Vector2[] poly2_2D = Project3DTo2D(polygon2);

            // 使用Sutherland-Hodgman算法计算交集
            Vector2[] intersection = SutherlandHodgmanClip(poly1_2D, poly2_2D);

            if (intersection == null || intersection.Length < 3)
                return 0f;

            return CalculatePolygonArea(intersection);
        }

        /// <summary>
        /// D点投影到2D平面（XY平面
        /// </summary>
        private Vector2[] Project3DTo2D(Vector3[] points)
        {
            Vector2[] result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new Vector2(points[i].x, points[i].y);
            }
            return result;
        }

        /// <summary>
        /// Sutherland-Hodgman多边形裁剪算
        /// 计算两个凸多边形的交
        /// </summary>
        private Vector2[] SutherlandHodgmanClip(Vector2[] subjectPolygon, Vector2[] clipPolygon)
        {
            Vector2[] outputList = new Vector2[subjectPolygon.Length];
            System.Array.Copy(subjectPolygon, outputList, subjectPolygon.Length);
            int outputCount = subjectPolygon.Length;

            for (int i = 0; i < clipPolygon.Length; i++)
            {
                if (outputCount == 0)
                    return null;

                Vector2 clipEdgeStart = clipPolygon[i];
                Vector2 clipEdgeEnd = clipPolygon[(i + 1) % clipPolygon.Length];

                Vector2[] inputList = new Vector2[outputCount];
                System.Array.Copy(outputList, inputList, outputCount);

                outputList = new Vector2[outputCount * 2];
                outputCount = 0;

                if (inputList.Length == 0)
                    continue;

                Vector2 S = inputList[inputList.Length - 1];

                for (int j = 0; j < inputList.Length; j++)
                {
                    Vector2 E = inputList[j];

                    if (IsInside(E, clipEdgeStart, clipEdgeEnd))
                    {
                        if (!IsInside(S, clipEdgeStart, clipEdgeEnd))
                        {
                            Vector2 intersection = GetLineIntersection(S, E, clipEdgeStart, clipEdgeEnd);
                            if (outputCount < outputList.Length)
                            {
                                outputList[outputCount++] = intersection;
                            }
                        }
                        if (outputCount < outputList.Length)
                        {
                            outputList[outputCount++] = E;
                        }
                    }
                    else if (IsInside(S, clipEdgeStart, clipEdgeEnd))
                    {
                        Vector2 intersection = GetLineIntersection(S, E, clipEdgeStart, clipEdgeEnd);
                        if (outputCount < outputList.Length)
                        {
                            outputList[outputCount++] = intersection;
                        }
                    }

                    S = E;
                }
            }

            if (outputCount == 0)
                return null;

            Vector2[] result = new Vector2[outputCount];
            System.Array.Copy(outputList, result, outputCount);
            return result;
        }

        /// <summary>
        /// 判断点是否在边的内侧（左侧）
        /// </summary>
        private bool IsInside(Vector2 point, Vector2 edgeStart, Vector2 edgeEnd)
        {
            return ((edgeEnd.x - edgeStart.x) * (point.y - edgeStart.y) -
                    (edgeEnd.y - edgeStart.y) * (point.x - edgeStart.x)) >= -0.0001f;
        }

        /// <summary>
        /// 获取两条线段的交
        /// </summary>
        private Vector2 GetLineIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float x1 = p1.x, y1 = p1.y;
            float x2 = p2.x, y2 = p2.y;
            float x3 = p3.x, y3 = p3.y;
            float x4 = p4.x, y4 = p4.y;

            float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);

            if (Mathf.Abs(denom) < 0.0001f)
                return (p1 + p2) * 0.5f;

            float t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;

            return new Vector2(x1 + t * (x2 - x1), y1 + t * (y2 - y1));
        }

        /// <summary>
        /// 计算多边形面积（使用鞋带公式
        /// </summary>
        private float CalculatePolygonArea(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
                return 0f;

            float area = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 p1 = polygon[i];
                Vector2 p2 = polygon[(i + 1) % polygon.Length];
                area += p1.x * p2.y - p2.x * p1.y;
            }

            return Mathf.Abs(area) * 0.5f;
        }

        /// <summary>
        /// 计算3D多边形面
        /// </summary>
        private float CalculatePolygonArea(Vector3[] polygon)
        {
            Vector2[] polygon2D = Project3DTo2D(polygon);
            return CalculatePolygonArea(polygon2D);
        }

        /// <summary>
        /// 处理可见性变化事件
        /// </summary>
        private void HandleVisibilityChange()
        {
            bool isNowVisible = visibleProportion > 0;

            if (isNowVisible && !wasVisible)
            {
                // 可见事件
                if (!onlyFirstVisible || !hasTriggeredFirstVisible)
                {
                    OnVisible?.Invoke();
                    hasTriggeredFirstVisible = true;
                }
                wasVisible = true;
            }
            else if (!isNowVisible && wasVisible)
            {
                // 不可见事件
                if (!onlyFirstInvisible || !hasTriggeredFirstInvisible)
                {
                    OnInvisible?.Invoke();
                    hasTriggeredFirstInvisible = true;
                }
                wasVisible = false;
            }
        }
    }

}
