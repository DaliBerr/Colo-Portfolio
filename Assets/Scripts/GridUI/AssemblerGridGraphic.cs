using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kernel.UI
{
    // 作品集版本：去掉了蓝图缩略图接口（IBlueprintThumbnailGridView / ApplyThumbnailSnapshot），
    // 它依赖游戏里的建筑与蓝图类型。绘制逻辑与游戏一致。
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AssemblerGridGraphic : GridGraphicBase
    {
        [Header("Grid")]
        [SerializeField] private int displayWidth = 9;
        [SerializeField] private int displayHeight = 9;
        [SerializeField] private int activeWidth = 7;
        [SerializeField] private int activeHeight = 7;
        [SerializeField] private bool autoFitCellSize = true;
        [SerializeField] private float fitPaddingPx = 12f;
        [SerializeField] private float cellSize = 40f;
        [SerializeField] private float lineThickness = 1f;

        [Header("Colors")]
        [SerializeField] private Color activeCellColor = new(0.19f, 0.22f, 0.28f, 0.92f);
        [SerializeField] private Color inactiveCellColor = new(0.1f, 0.1f, 0.12f, 0.75f);
        [SerializeField] private Color lineColor = new(0.95f, 0.95f, 0.95f, 0.18f);
        [SerializeField] private Color hoverColor = new(0.3f, 0.65f, 1f, 0.28f);
        [SerializeField] private Color occupiedCellColor = new(0.95f, 0.76f, 0.18f, 0.34f);
        [SerializeField] private Color previewValidColor = new(0.18f, 0.85f, 0.35f, 0.46f);
        [SerializeField] private Color previewInvalidColor = new(0.95f, 0.25f, 0.25f, 0.52f);

        private bool _hasHoverCell;
        private Vector2Int _hoverCell;
        private readonly List<Vector2Int> _previewCells = new();
        private readonly List<Vector2Int> _occupiedCells = new();
        private readonly Dictionary<Vector2Int, Color> _occupiedCellColors = new();
        private bool _previewValid = true;

        protected override int LayoutGridWidth => DisplayWidth;
        protected override int LayoutGridHeight => DisplayHeight;
        protected override bool LayoutUsesAutoFitCellSize => autoFitCellSize;
        protected override float LayoutFitPaddingPx => fitPaddingPx;
        protected override float LayoutManualCellSize => cellSize;
        protected override float LayoutLineThickness => lineThickness;

        public int DisplayWidth => Mathf.Max(1, displayWidth);
        public int DisplayHeight => Mathf.Max(1, displayHeight);
        public int ActiveWidth => Mathf.Clamp(activeWidth, 1, DisplayWidth);
        public int ActiveHeight => Mathf.Clamp(activeHeight, 1, DisplayHeight);

        public RectInt ActiveRect => BuildActiveRect(DisplayWidth, DisplayHeight, ActiveWidth, ActiveHeight);

        public void SetDisplaySize(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (displayWidth == width && displayHeight == height)
            {
                return;
            }

            displayWidth = width;
            displayHeight = height;
            activeWidth = Mathf.Clamp(activeWidth, 1, displayWidth);
            activeHeight = Mathf.Clamp(activeHeight, 1, displayHeight);
            SetVerticesDirty();
        }

        public void SetActiveSize(int width, int height)
        {
            width = Mathf.Clamp(width, 1, DisplayWidth);
            height = Mathf.Clamp(height, 1, DisplayHeight);
            if (activeWidth == width && activeHeight == height)
            {
                return;
            }

            activeWidth = width;
            activeHeight = height;
            SetVerticesDirty();
        }

        public void SetHoverCell(Vector2Int? cell)
        {
            if (cell.HasValue)
            {
                _hasHoverCell = true;
                _hoverCell = cell.Value;
            }
            else
            {
                _hasHoverCell = false;
                _hoverCell = default;
            }

            SetVerticesDirty();
        }

        public void SetPreviewCell(Vector2Int? cell, bool isValid)
        {
            _previewCells.Clear();
            if (cell.HasValue)
            {
                _previewCells.Add(cell.Value);
                _previewValid = isValid;
            }
            else
            {
                _previewValid = true;
            }

            SetVerticesDirty();
        }

        public void SetPreviewCells(IReadOnlyList<Vector2Int> cells, bool isValid)
        {
            _previewCells.Clear();
            if (cells != null)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    _previewCells.Add(cells[i]);
                }
            }

            _previewValid = isValid;
            SetVerticesDirty();
        }

        /// <summary>设置已占格的颜色（按模块种类区分）；未设置颜色的占格用默认占格色。</summary>
        public void SetOccupiedCellColors(IReadOnlyDictionary<Vector2Int, Color> colors)
        {
            _occupiedCellColors.Clear();
            if (colors != null)
            {
                foreach (var pair in colors)
                {
                    _occupiedCellColors[pair.Key] = pair.Value;
                }
            }

            SetVerticesDirty();
        }

        public void SetOccupiedCells(IReadOnlyList<Vector2Int> cells)
        {
            _occupiedCells.Clear();
            if (cells != null)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    _occupiedCells.Add(cells[i]);
                }
            }

            SetVerticesDirty();
        }

        public bool TryScreenToCell(Vector2 screenPos, Camera cam, out Vector2Int cell, out Vector2 localPoint)
        {
            return TryScreenToCellCore(screenPos, cam, out cell, out localPoint);
        }

        public bool TryLocalToCell(Vector2 localPoint, out Vector2Int cell)
        {
            return TryLocalToCellCore(localPoint, out cell, out _);
        }

        public void SetReadOnlySnapshot(int width, int height, int activeWidthValue, int activeHeightValue, IReadOnlyList<Vector2Int> occupiedCells)
        {
            SetDisplaySize(width, height);
            SetActiveSize(activeWidthValue, activeHeightValue);
            SetHoverCell(null);
            SetPreviewCells((IReadOnlyList<Vector2Int>)null, true);
            SetOccupiedCells(occupiedCells);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (DisplayWidth <= 0 || DisplayHeight <= 0)
            {
                return;
            }

            ResolveLayout(out var resolvedCellSize, out var origin);
            DrawCells(vh, resolvedCellSize, origin);
            DrawGridLines(vh, resolvedCellSize, origin);
        }

        private void DrawCells(VertexHelper vh, float resolvedCellSize, Vector2 origin)
        {
            RectInt activeRect = ActiveRect;
            for (int x = 0; x < DisplayWidth; x++)
            {
                for (int y = 0; y < DisplayHeight; y++)
                {
                    var cell = new Vector2Int(x, y);
                    var p0 = new Vector2(origin.x + x * resolvedCellSize, origin.y + y * resolvedCellSize);
                    var p1 = p0 + new Vector2(resolvedCellSize, resolvedCellSize);

                    Color baseColor = activeRect.Contains(cell) ? activeCellColor : inactiveCellColor;
                    AddQuad(vh, p0, new Vector2(p1.x, p0.y), p1, new Vector2(p0.x, p1.y), baseColor);

                    if (_hasHoverCell && cell == _hoverCell)
                    {
                        AddQuad(vh, p0, new Vector2(p1.x, p0.y), p1, new Vector2(p0.x, p1.y), hoverColor);
                    }

                    if (_occupiedCells.Contains(cell))
                    {
                        var fill = _occupiedCellColors.TryGetValue(cell, out var moduleColor) ? moduleColor : occupiedCellColor;
                        AddQuad(vh, p0, new Vector2(p1.x, p0.y), p1, new Vector2(p0.x, p1.y), fill);
                    }

                    if (_previewCells.Contains(cell))
                    {
                        AddQuad(vh, p0, new Vector2(p1.x, p0.y), p1, new Vector2(p0.x, p1.y), _previewValid ? previewValidColor : previewInvalidColor);
                    }
                }
            }
        }

        private void DrawGridLines(VertexHelper vh, float resolvedCellSize, Vector2 origin)
        {
            DrawGridLinesCore(vh, resolvedCellSize, origin, lineColor);
        }

        private static RectInt BuildActiveRect(int displayWidth, int displayHeight, int activeWidth, int activeHeight)
        {
            displayWidth = Mathf.Max(1, displayWidth);
            displayHeight = Mathf.Max(1, displayHeight);
            activeWidth = Mathf.Clamp(activeWidth, 1, displayWidth);
            activeHeight = Mathf.Clamp(activeHeight, 1, displayHeight);
            int startX = Mathf.FloorToInt((displayWidth - activeWidth) * 0.5f);
            int startY = Mathf.FloorToInt((displayHeight - activeHeight) * 0.5f);
            return new RectInt(startX, startY, activeWidth, activeHeight);
        }
    }
}
