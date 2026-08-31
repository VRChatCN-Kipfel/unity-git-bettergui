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
        private readonly List<VisualElement> rowElements = new List<VisualElement>();
        private HashSet<int> runRows = new HashSet<int>(); // 折叠运行段行号（段内链条画点线样式）

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

        public void SetData(List<GitLogEntry> commits, RowPrinter rowPrinter,
            Dictionary<string, List<GitSession.GitRefInfo>> refsByCommit = null,
            LinearSegments segments = null)
        {
            log = commits;
            printer = rowPrinter;
            selectedRow = -1;
            runRows = new HashSet<int>();
            if (segments != null)
            {
                foreach (var (top, bottom) in segments.Runs)
                    for (var r = top; r <= bottom; r++) runRows.Add(r);
            }

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
            rowElements.Clear();

            for (var r = 0; r < log.Count; r++)
            {
                // 行容器：refs 标签 + 消息（JetBrains GraphCommitCellRenderer 同格编排）
                var rowEl = new VisualElement();
                rowEl.style.flexDirection = FlexDirection.Row;
                rowEl.style.height = RowHeight;
                rowEl.style.alignItems = Align.Center;

                if (refsByCommit != null && refsByCommit.TryGetValue(log[r].CommitID, out var refs))
                {
                    foreach (var rf in refs)
                        rowEl.Add(MakeRefChip(rf));
                }

                var label = new Label($"  {log[r].ShortID}  {log[r].Summary}");
                label.style.height = RowHeight;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.fontSize = 12;
                label.style.flexGrow = 1f;
                label.tooltip = "parents: " + string.Join(",", log[r].Parents) + "\nfiles: " + (log[r].Changes?.Count ?? 0);
                rowEl.Add(label);

                var row = r;
                rowEl.RegisterCallback<ClickEvent>(_ => Select(row));
                messageLabels.Add(label);
                rowElements.Add(rowEl);
                messageColumn.Add(rowEl);
            }

            MarkDirtyRepaint();
        }

        /// <summary>refs 标签 chip（JetBrains VcsLogLabelPainter 语义简化版）：类型着色、圆角、白字。</summary>
        private static Label MakeRefChip(GitSession.GitRefInfo rf)
        {
            var chip = new Label(rf.DisplayName);
            chip.style.fontSize = 10;
            chip.style.color = new Color(1f, 1f, 1f);
            chip.style.paddingLeft = 5;
            chip.style.paddingRight = 5;
            chip.style.paddingTop = 1;
            chip.style.paddingBottom = 1;
            chip.style.marginRight = 4;
            chip.style.borderTopLeftRadius = 3;
            chip.style.borderTopRightRadius = 3;
            chip.style.borderBottomLeftRadius = 3;
            chip.style.borderBottomRightRadius = 3;
            chip.style.unityFontStyleAndWeight = rf.Type == GitSession.RefType.Head ? FontStyle.Bold : FontStyle.Normal;
            switch (rf.Type)
            {
                case GitSession.RefType.Head: chip.style.backgroundColor = new Color(0.25f, 0.5f, 0.85f); break;
                case GitSession.RefType.Local: chip.style.backgroundColor = new Color(0.2f, 0.55f, 0.42f); break;
                case GitSession.RefType.Remote: chip.style.backgroundColor = new Color(0.42f, 0.42f, 0.45f); break;
                default: chip.style.backgroundColor = new Color(0.72f, 0.55f, 0.18f); break; // Tag
            }
            return chip;
        }

        public void Select(int row)
        {
            if (row < 0 || row >= (log?.Count ?? 0)) return;
            selectedRow = row;
            for (var i = 0; i < rowElements.Count; i++)
            {
                var bg = i == selectedRow ? new Color(0.25f, 0.45f, 0.75f, 0.55f) : Color.clear;
                rowElements[i].style.backgroundColor = bg;
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

        /// <summary>长边端点箭头（两笔短线指向，指示边的走向）。</summary>
        private static void PaintArrow(Painter2D painter, float x, float y, bool down)
        {
            painter.lineWidth = 1.0f;
            if (down)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x - 3.5f, y));
                painter.LineTo(new Vector2(x, y + 5f));
                painter.MoveTo(new Vector2(x + 3.5f, y));
                painter.LineTo(new Vector2(x, y + 5f));
            }
            else
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x - 3.5f, y));
                painter.LineTo(new Vector2(x, y - 5f));
                painter.MoveTo(new Vector2(x + 3.5f, y));
                painter.LineTo(new Vector2(x, y - 5f));
            }
            painter.Stroke();
        }

        /// <summary>点线（DOTTED）描边：沿线段 4px 点 / 3px 间隔，视觉连续柔和。</summary>
        private static void StrokeDotted(Painter2D painter, Vector2 a, Vector2 b)
        {
            var dir = b - a;
            var len = dir.magnitude;
            if (len < 1f) return;
            var unit = dir / len;
            for (var d = 2f; d < len - 2f; d += 7f)
            {
                var s = a + unit * d;
                var e = a + unit * Mathf.Min(d + 4f, len - 2f);
                painter.BeginPath();
                painter.MoveTo(s);
                painter.LineTo(e);
                painter.Stroke();
            }
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

            // 1) 边（先画，节点压上）。折叠段（runRows）内的链条边改画"点线"——连续不断、语义清晰
            for (var r = 0; r < printer.Rows; r++)
            {
                foreach (var e in printer.GetEdgesInRow(r))
                {
                    // 节点发出的边：起点 x = 节点泳道；在途边：起点 x = 行内位置槽位
                    var fromNode = e.IsDown ? e.UpNode : e.DownNode;
                    var x1 = LaneWidth * (fromNode == r ? printer.LayoutIndex(r) : e.FromPosition) + LaneWidth / 2f;
                    var y1 = r * RowHeight + RowHeight / 2f;
                    if (e.Kind == RowPrinter.RenderKind.ArrowDown || e.Kind == RowPrinter.RenderKind.ArrowUp)
                    {
                        // 长边端点箭头（JetBrains long-edge：中部不画，两端箭头指示方向）
                        painter.strokeColor = LaneColor(printer.LayoutIndex(e.UpNode));
                        PaintArrow(painter, x1, y1, e.Kind == RowPrinter.RenderKind.ArrowDown);
                        continue;
                    }
                    var selected = (r == selectedRow || (e.IsDown ? e.DownNode == selectedRow : e.UpNode == selectedRow));
                    painter.lineWidth = selected ? 1.0f : 0.4f;
                    painter.strokeColor = LaneColor(printer.LayoutIndex(e.UpNode >= 0 ? e.UpNode : r));
                    // 落点：命中端节点 -> 按该节点泳道 x（跨泳道即出现拐角）；否则按行内位置槽位
                    var x2 = LaneWidth * (e.NodeTargetRow >= 0 ? printer.LayoutIndex(e.NodeTargetRow) : e.ToPosition) + LaneWidth / 2f;
                    var y2 = (e.IsDown ? (r + 1) : (r - 1)) * RowHeight + RowHeight / 2f;
                    if (y2 < -RowHeight || y2 > printer.Rows * RowHeight + RowHeight) continue;

                    // 折叠段内：仅"本段链条边"（两端都在运行段内、同泳道垂直线）改点线——跨段的线保持实线
                    if (e.FromPosition == e.ToPosition &&
                        runRows.Contains(r) &&
                        runRows.Contains(e.IsDown ? e.DownNode : e.UpNode))
                    {
                        StrokeDotted(painter, new Vector2(x1, y1), new Vector2(x2, y2));
                        continue;
                    }
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x1, y1));
                    painter.LineTo(new Vector2(x2, y2));
                    painter.Stroke();
                }
            }

            // 2) 节点（全部保留，折叠段仅线条样式变化）
            for (var r = 0; r < printer.Rows; r++)
            {
                foreach (var n in printer.GetNodesInRow(r))
                {
                    var x0 = LaneWidth * printer.LayoutIndex(r) + LaneWidth / 2f; // 节点画在泳道 x（非行内位置）
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