using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>
    /// 图谱表格（JetBrains GraphCommitCellRenderer 同格理念）：图谱与提交消息在"同一行"。
    /// 布局：单列 ScrollView 内容 = 横向 [图谱列(自绘全表) | 消息列(逐行 Label)]，共用滚动 -> 甬道对应。
    /// 绘制：整表一次绘制（painter2D 无裁剪断裂问题），几何按 SimpleGraphCellPainter：
    ///   边：行中心 → 邻行中心（同泳道垂直线 / 跨泳道斜线）；节点：车道中心实心圆。
    /// </summary>
    public sealed class GraphTable : VisualElement
    {
        public const float RowHeight = 22f;
        public const float LaneWidth = 16f;
        public const float NodeRadius = 4f;

        private RowPrinter printer;
        private List<GitLogEntry> log;
        private int selectedRow = -1;
        private int graphWidthPx;
        private readonly VisualElement graphColumn;
        private readonly VisualElement messageColumn;
        private readonly List<Label> messageLabels = new List<Label>();

        /// <summary>行选中回调（参数 = 行号）。</summary>
        public event System.Action<int> RowSelected;

        public int SelectedRow => selectedRow;

        public GraphTable()
        {
            style.flexDirection = FlexDirection.Row;
            graphColumn = new VisualElement();
            graphColumn.generateVisualContent += PaintGraph;
            graphColumn.style.alignSelf = Align.FlexStart;
            graphColumn.RegisterCallback<ClickEvent>(OnGraphClicked);
            messageColumn = new VisualElement();
            messageColumn.style.flexGrow = 1f;
            messageColumn.style.flexDirection = FlexDirection.Column;
            Add(graphColumn);
            Add(messageColumn);
        }

        /// <summary>点击图谱列：按行高换算成行号 -> 选中该行（节点同样选中，JetBrains 行级交互）。</summary>
        private void OnGraphClicked(ClickEvent ev)
        {
            var row = (int)(ev.localPosition.y / RowHeight);
            if (row >= 0 && row < (log?.Count ?? 0)) Select(row);
        }

        public void SetData(List<GitLogEntry> commits, RowPrinter rowPrinter)
        {
            log = commits;
            printer = rowPrinter;
            selectedRow = -1;

            // 图谱列宽度 = 全表最大元素数 * lane 宽 + 余量（保持一致对齐）
            var maxElements = 1;
            for (var r = 0; r < printer.Rows; r++)
                if (printer.RowElementCount(r) > maxElements) maxElements = printer.RowElementCount(r);
            graphWidthPx = (int)(LaneWidth * (maxElements + 1)) + 8;

            Clear();
            Add(graphColumn);
            Add(messageColumn);
            graphColumn.style.width = graphWidthPx;
            graphColumn.style.height = printer.Rows * RowHeight;
            messageColumn.Clear();
            messageLabels.Clear();

            for (var r = 0; r < log.Count; r++)
            {
                var label = new Label($"  {log[r].ShortID}  {log[r].Summary}");
                label.style.height = RowHeight;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.fontSize = 12;
                label.tooltip = "parents: " + string.Join(",", log[r].Parents) + "\nfiles: " + (log[r].Changes?.Count ?? 0);
                var row = r;
                label.RegisterCallback<ClickEvent>(_ => Select(row));
                messageLabels.Add(label);
                messageColumn.Add(label);
            }

            MarkDirtyRepaint();
        }

        public void Select(int row)
        {
            if (row < 0 || row >= (log?.Count ?? 0)) return;
            selectedRow = row;
            for (var i = 0; i < messageLabels.Count; i++)
            {
                var bg = i == selectedRow ? new Color(0.25f, 0.45f, 0.75f, 0.55f) : Color.clear;
                messageLabels[i].style.backgroundColor = bg;
            }
            graphColumn.MarkDirtyRepaint();
            RowSelected?.Invoke(row);
        }

        private Color LaneColor(int lane)
        {
            // 泳道 -> 色相（黄金角发散，任意 lane 数都区分）
            var hue = (lane * 0.61803398875f) % 1f;
            return UnityEngine.Color.HSVToRGB(hue, 0.72f, 0.88f);
        }

        private void PaintGraph(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            if (painter == null || printer == null) return;

            // 0) 选中行背景（贯通图谱列，与右侧消息蓝条连成一条）
            if (selectedRow >= 0)
            {
                var sy = selectedRow * RowHeight;
                painter.fillColor = new Color(0.23f, 0.42f, 0.72f, 0.35f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, sy));
                painter.LineTo(new Vector2(graphWidthPx, sy));
                painter.LineTo(new Vector2(graphWidthPx, sy + RowHeight));
                painter.LineTo(new Vector2(0f, sy + RowHeight));
                painter.ClosePath();
                painter.Fill();
            }

            // 1) 边（先画，节点压上）
            for (var r = 0; r < printer.Rows; r++)
            {
                foreach (var e in printer.GetEdgesInRow(r))
                {
                    var selected = (r == selectedRow || (e.IsDown ? e.DownNode == selectedRow : e.UpNode == selectedRow));
                    painter.lineWidth = selected ? 1.0f : 0.4f;
                    painter.strokeColor = LaneColor(printer.LayoutIndex(e.UpNode >= 0 ? e.UpNode : r));
                    var x1 = LaneWidth * e.FromPosition + LaneWidth / 2f;
                    var y1 = r * RowHeight + RowHeight / 2f;
                    var x2 = LaneWidth * e.ToPosition + LaneWidth / 2f;
                    var y2 = (e.IsDown ? (r + 1) : (r - 1)) * RowHeight + RowHeight / 2f;
                    if (y2 < -RowHeight || y2 > printer.Rows * RowHeight + RowHeight) continue;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x1, y1));
                    painter.LineTo(new Vector2(x2, y2));
                    painter.Stroke();
                }
            }

            // 2) 节点
            for (var r = 0; r < printer.Rows; r++)
            {
                foreach (var n in printer.GetNodesInRow(r))
                {
                    var x0 = LaneWidth * n.Position + LaneWidth / 2f;
                    var y0 = r * RowHeight + RowHeight / 2f;
                    var color = LaneColor(printer.LayoutIndex(r));
                    if (r == selectedRow)
                    {
                        // 选中：节点本体不动，用同色圆环套在点外（无黑边，JetBrains 观感）
                        painter.lineWidth = 1.4f;
                        painter.strokeColor = color;
                        painter.BeginPath();
                        painter.Arc(new Vector2(x0, y0), NodeRadius + 2.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                        painter.Stroke();
                    }
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.Arc(new Vector2(x0, y0), NodeRadius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Fill();

                    // head：加白色内点（OUTLINE_AND_FILL 语义)
                    if (n.IsHead)
                    {
                        painter.fillColor = Color.white;
                        painter.BeginPath();
                        painter.Arc(new Vector2(x0, y0), 1.8f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                        painter.Fill();
                    }
                }
            }
        }
    }
}