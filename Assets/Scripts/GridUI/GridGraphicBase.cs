using UnityEngine;
using UnityEngine.UI;

namespace Kernel.UI
{
    public abstract class GridGraphicBase : MaskableGraphic
    {
        protected abstract int LayoutGridWidth { get; }
        protected abstract int LayoutGridHeight { get; }
        protected abstract bool LayoutUsesAutoFitCellSize { get; }
        protected abstract float LayoutFitPaddingPx { get; }
        protected abstract float LayoutManualCellSize { get; }
        protected abstract float LayoutLineThickness { get; }

        public float ResolvedCellSize
        {
            get
            {
                ResolveLayout(out float resolvedCellSize, out _);
                return resolvedCellSize;
            }
        }

        /// <summary>
        /// summary: 将逻辑网格坐标转换为当前 Graphic 的本地坐标。
        /// </summary>
        /// <param name="mapPoint">逻辑网格平面坐标。</param>
        /// <returns>本地坐标。</returns>
        protected virtual Vector2 MapPointToLocal(Vector2 mapPoint)
        {
            return mapPoint;
        }

        /// <summary>
        /// summary: 将当前 Graphic 的本地坐标转换为逻辑网格坐标。
        /// </summary>
        /// <param name="localPoint">Graphic 本地坐标。</param>
        /// <returns>逻辑网格平面坐标。</returns>
        protected virtual Vector2 LocalPointToMap(Vector2 localPoint)
        {
            return localPoint;
        }

        /// <summary>
        /// summary: 计算网格布局后的单元格尺寸与原点。
        /// </summary>
        /// <param name="resolvedCellSize">输出的单元格尺寸。</param>
        /// <param name="gridOrigin">输出的网格原点。</param>
        /// <returns>无返回值。</returns>
        protected void ResolveLayout(out float resolvedCellSize, out Vector2 gridOrigin)
        {
            var rect = rectTransform.rect;
            int width = Mathf.Max(1, LayoutGridWidth);
            int height = Mathf.Max(1, LayoutGridHeight);
            float safePadding = Mathf.Max(0f, LayoutFitPaddingPx);
            float availableWidth = Mathf.Max(1f, rect.width - safePadding * 2f);
            float availableHeight = Mathf.Max(1f, rect.height - safePadding * 2f);

            if (LayoutUsesAutoFitCellSize)
            {
                float fromWidth = availableWidth / width;
                float fromHeight = availableHeight / height;
                resolvedCellSize = Mathf.Max(1f, Mathf.Min(fromWidth, fromHeight));
            }
            else
            {
                resolvedCellSize = Mathf.Max(1f, LayoutManualCellSize);
            }

            var gridPixelSize = new Vector2(width * resolvedCellSize, height * resolvedCellSize);
            gridOrigin = rect.center - gridPixelSize * 0.5f;
        }

        /// <summary>
        /// summary: 将本地坐标解析到网格 cell。
        /// </summary>
        /// <param name="localPoint">Graphic 本地坐标。</param>
        /// <param name="cell">解析出的 cell。</param>
        /// <param name="mapPoint">转换后的逻辑网格平面坐标。</param>
        /// <returns>命中网格范围时返回 true。</returns>
        protected bool TryLocalToCellCore(Vector2 localPoint, out Vector2Int cell, out Vector2 mapPoint)
        {
            cell = default;
            mapPoint = LocalPointToMap(localPoint);
            ResolveLayout(out float resolvedCellSize, out Vector2 origin);
            float relX = mapPoint.x - origin.x;
            float relY = mapPoint.y - origin.y;
            int x = Mathf.FloorToInt(relX / resolvedCellSize);
            int y = Mathf.FloorToInt(relY / resolvedCellSize);
            cell = new Vector2Int(x, y);
            return x >= 0 && x < Mathf.Max(1, LayoutGridWidth) && y >= 0 && y < Mathf.Max(1, LayoutGridHeight);
        }

        /// <summary>
        /// summary: 将屏幕坐标解析到网格 cell，并返回本地与逻辑网格平面坐标。
        /// </summary>
        /// <param name="screenPos">屏幕坐标。</param>
        /// <param name="cam">事件相机。</param>
        /// <param name="cell">解析出的 cell。</param>
        /// <param name="localPoint">Graphic 本地坐标。</param>
        /// <param name="mapPoint">逻辑网格平面坐标。</param>
        /// <returns>命中网格范围时返回 true。</returns>
        protected bool TryScreenToCellCore(Vector2 screenPos, Camera cam, out Vector2Int cell, out Vector2 localPoint, out Vector2 mapPoint)
        {
            cell = default;
            localPoint = default;
            mapPoint = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPos, cam, out localPoint))
            {
                return false;
            }

            return TryLocalToCellCore(localPoint, out cell, out mapPoint);
        }

        /// <summary>
        /// summary: 将屏幕坐标解析到网格 cell，并返回本地坐标。
        /// </summary>
        /// <param name="screenPos">屏幕坐标。</param>
        /// <param name="cam">事件相机。</param>
        /// <param name="cell">解析出的 cell。</param>
        /// <param name="localPoint">Graphic 本地坐标。</param>
        /// <returns>命中网格范围时返回 true。</returns>
        protected bool TryScreenToCellCore(Vector2 screenPos, Camera cam, out Vector2Int cell, out Vector2 localPoint)
        {
            return TryScreenToCellCore(screenPos, cam, out cell, out localPoint, out _);
        }

        /// <summary>
        /// summary: 返回指定 cell 的本地中心点。
        /// </summary>
        /// <param name="cell">目标 cell。</param>
        /// <returns>cell 对应的本地中心点。</returns>
        public Vector2 GetCellCenterLocal(Vector2Int cell)
        {
            ResolveLayout(out float resolvedCellSize, out Vector2 gridOrigin);
            return GetCellCenterLocal(cell, resolvedCellSize, gridOrigin);
        }

        /// <summary>
        /// summary: 返回指定 bounds 对应的本地矩形。
        /// </summary>
        /// <param name="bounds">逻辑网格矩形。</param>
        /// <returns>本地矩形。</returns>
        public Rect GetCellBoundsLocal(RectInt bounds)
        {
            ResolveLayout(out float resolvedCellSize, out Vector2 origin);
            Vector2 min = MapPointToLocal(new Vector2(
                origin.x + bounds.xMin * resolvedCellSize,
                origin.y + bounds.yMin * resolvedCellSize));
            Vector2 max = MapPointToLocal(new Vector2(
                origin.x + bounds.xMax * resolvedCellSize,
                origin.y + bounds.yMax * resolvedCellSize));
            return Rect.MinMaxRect(
                Mathf.Min(min.x, max.x),
                Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x),
                Mathf.Max(min.y, max.y));
        }

        /// <summary>
        /// summary: 返回指定 cell 的本地中心点。
        /// </summary>
        /// <param name="cell">目标 cell。</param>
        /// <param name="resolvedCellSize">单元格尺寸。</param>
        /// <param name="gridOrigin">网格原点。</param>
        /// <returns>cell 对应的本地中心点。</returns>
        protected Vector2 GetCellCenterLocal(Vector2Int cell, float resolvedCellSize, Vector2 gridOrigin)
        {
            var mapPoint = new Vector2(
                gridOrigin.x + (cell.x + 0.5f) * resolvedCellSize,
                gridOrigin.y + (cell.y + 0.5f) * resolvedCellSize);
            return MapPointToLocal(mapPoint);
        }

        /// <summary>
        /// summary: 绘制规则网格线。
        /// </summary>
        /// <param name="vh">网格顶点缓存。</param>
        /// <param name="resolvedCellSize">单元格尺寸。</param>
        /// <param name="gridOrigin">网格原点。</param>
        /// <param name="lineColor">线条颜色。</param>
        /// <returns>无返回值。</returns>
        protected void DrawGridLinesCore(VertexHelper vh, float resolvedCellSize, Vector2 gridOrigin, Color lineColor)
        {
            int width = Mathf.Max(1, LayoutGridWidth);
            int height = Mathf.Max(1, LayoutGridHeight);
            float thickness = Mathf.Max(0.25f, LayoutLineThickness);

            for (int x = 0; x <= width; x++)
            {
                float px = gridOrigin.x + x * resolvedCellSize;
                var a = MapPointToLocal(new Vector2(px - thickness * 0.5f, gridOrigin.y));
                var b = MapPointToLocal(new Vector2(px + thickness * 0.5f, gridOrigin.y));
                var c = MapPointToLocal(new Vector2(px + thickness * 0.5f, gridOrigin.y + height * resolvedCellSize));
                var d = MapPointToLocal(new Vector2(px - thickness * 0.5f, gridOrigin.y + height * resolvedCellSize));
                AddQuad(vh, a, b, c, d, lineColor);
            }

            for (int y = 0; y <= height; y++)
            {
                float py = gridOrigin.y + y * resolvedCellSize;
                var a = MapPointToLocal(new Vector2(gridOrigin.x, py - thickness * 0.5f));
                var b = MapPointToLocal(new Vector2(gridOrigin.x + width * resolvedCellSize, py - thickness * 0.5f));
                var c = MapPointToLocal(new Vector2(gridOrigin.x + width * resolvedCellSize, py + thickness * 0.5f));
                var d = MapPointToLocal(new Vector2(gridOrigin.x, py + thickness * 0.5f));
                AddQuad(vh, a, b, c, d, lineColor);
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        /// <summary>
        /// summary: 以单色填充四边形。
        /// </summary>
        /// <param name="vh">网格顶点缓存。</param>
        /// <param name="a">顶点 a。</param>
        /// <param name="b">顶点 b。</param>
        /// <param name="c">顶点 c。</param>
        /// <param name="d">顶点 d。</param>
        /// <param name="color">填充颜色。</param>
        /// <returns>无返回值。</returns>
        protected static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int start = vh.currentVertCount;
            var vert = UIVertex.simpleVert;
            vert.color = color;

            vert.position = a;
            vh.AddVert(vert);
            vert.position = b;
            vh.AddVert(vert);
            vert.position = c;
            vh.AddVert(vert);
            vert.position = d;
            vh.AddVert(vert);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        /// <summary>
        /// summary: 以四个顶点颜色填充四边形。
        /// </summary>
        /// <param name="vh">网格顶点缓存。</param>
        /// <param name="a">顶点 a。</param>
        /// <param name="b">顶点 b。</param>
        /// <param name="c">顶点 c。</param>
        /// <param name="d">顶点 d。</param>
        /// <param name="ca">顶点 a 颜色。</param>
        /// <param name="cb">顶点 b 颜色。</param>
        /// <param name="cc">顶点 c 颜色。</param>
        /// <param name="cd">顶点 d 颜色。</param>
        /// <returns>无返回值。</returns>
        protected static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int start = vh.currentVertCount;
            var vert = UIVertex.simpleVert;

            vert.position = a;
            vert.color = ca;
            vh.AddVert(vert);

            vert.position = b;
            vert.color = cb;
            vh.AddVert(vert);

            vert.position = c;
            vert.color = cc;
            vh.AddVert(vert);

            vert.position = d;
            vert.color = cd;
            vh.AddVert(vert);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
