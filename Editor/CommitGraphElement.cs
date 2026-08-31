using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>
    /// 提交图谱自绘元素：最小泳道布局 + UI Toolkit Painter2D 渲染。
    /// 布局算法（自研，借鉴 git 图谱普遍思想但不搬 JetBrains 工业管线）：
    ///   - commits 按时间倒序（head 在前），row = index，行高 RowHeight
    ///   - 每行左侧泳道带：活跃分支线占一个 lane，commit 节点画在该行所属 lane
    ///   - 边：从 commit 行连到每个 parent 行（parent 可能在本行之后/之前）
    /// 渲染：generateVisualContent 回调经 MeshGenerationContext.painter2D 画线（边）+ 实心圆（节点）。
    /// </summary>
    public sealed class CommitGraphElement : VisualElement
    {
        public const float RowHeight = 22f;
        public const float LaneWidth = 16f;
        public const float NodeRadius = 3.5f;

        private readonly List<GitLogEntry> commits = new List<GitLogEntry>();
        private readonly List<GraphRow> rows = new List<GraphRow>();

        private sealed class GraphRow
        {
            public int Lane;                       // 本 commit 节点所在泳道
            public List<GraphEdge> Edges = new List<GraphEdge>(); // (childLane->parentLane)
        }

        private sealed class GraphEdge
        {
            public int ChildLane;
            public int ParentLane;
            public int ParentRowOffset;            // 父行相对本行偏移（正=下方）
        }

        private sealed class LaneOccupant
        {
            public string CommitId;                // 当前占住泳道的提交（线的"尾巴"）
            public int CommitRow;
        }

        private string headId;
        private string summary;
        private int parentCount;
        private int edgeCount;

        public CommitGraphElement()
        {
            generateVisualContent += Paint;
            style.flexGrow = 1f;
        }

        public void SetData(IReadOnlyList<GitLogEntry> log)
        {
            commits.Clear();
            commits.AddRange(log);
            RebuildLayout();
        }

        /// <summary>纯布局计算（不依赖渲染）：供冒烟测试断言图谱结构。</summary>
        public (int rows, int edges, string head, int parents) LayoutInfo =>
            (rows.Count, edgeCount, headId, parentCount);

        private void RebuildLayout()
        {
            rows.Clear();
            var laneToTail = new List<LaneOccupant>();      // 泳道 -> 当前尾部提交
            var commitRow = new Dictionary<string, int>();   // 提交 -> 行号

            for (var r = 0; r < commits.Count; r++)
            {
                var c = commits[r];
                commitRow[c.CommitID] = r;
                rows.Add(new GraphRow());
            }

            for (var r = 0; r < commits.Count; r++)
            {
                var c = commits[r];
                var row = rows[r];

                // 分配/复用泳道：若本提交是某泳道的尾部（有分支线伸进来），占它；否则取最左空闲
                int lane = FindLaneFor(c.CommitID, laneToTail) ?? FirstFreeLane(laneToTail);
                row.Lane = lane;

                // 泳道线更新：本提交节点之后，线继续由第一父继承
                var parents = c.Parents;
                var firstParent = parents != null && parents.Count > 0 ? parents[0] : null;

                if (firstParent != null)
                {
                    SetLaneTail(laneToTail, lane, firstParent, r);
                }
                else
                {
                    FreeLane(laneToTail, lane);   // 根提交：无父，线到此结束
                }

                // 边：从本提交到每个 parent（含第二+父 -> 新泳道）
                if (parents == null) continue;
                for (var p = 0; p < parents.Count; p++)
                {
                    var pid = parents[p];
                    int parentRow = commitRow.TryGetValue(pid, out var pr) ? pr : -1;
                    int parentLane;

                    if (p == 0)
                    {
                        parentLane = lane;         // 第一父：同泳道垂直接线
                    }
                    else
                    {
                        // 第二+父：占一个别的泳道（尽量不撞现有泳道）
                        int free = FirstFreeLane(laneToTail);
                        parentLane = free;
                        // 第二父从此处向下延伸（若其尚未成行）
                        if (parentRow == -1)
                            SetLaneTail(laneToTail, free, pid, r);
                    }

                    if (parentRow != -1)
                    {
                        row.Edges.Add(new GraphEdge
                        {
                            ChildLane = lane,
                            ParentLane = parentLane,
                            ParentRowOffset = parentRow - r
                        });
                    }
                }

                if (r == 0) { headId = c.ShortID; summary = c.Summary; parentCount = c.Parents?.Count ?? 0; }
            }

            edgeCount = 0;
            foreach (var row in rows) edgeCount += row.Edges.Count;
        }

        private static int? FindLaneFor(string commitId, List<LaneOccupant> laneToTail)
        {
            for (var i = 0; i < laneToTail.Count; i++)
                if (laneToTail[i] != null && laneToTail[i].CommitId == commitId)
                    return i;
            return null;
        }

        private static int FirstFreeLane(List<LaneOccupant> laneToTail)
        {
            for (var i = 0; i < laneToTail.Count; i++)
                if (laneToTail[i] == null) return i;
            laneToTail.Add(null);
            return laneToTail.Count - 1;
        }

        private static void SetLaneTail(List<LaneOccupant> laneToTail, int lane, string commitId, int row)
        {
            if (laneToTail[lane] == null) laneToTail[lane] = new LaneOccupant();
            laneToTail[lane].CommitId = commitId;
            laneToTail[lane].CommitRow = row;
        }

        private static void FreeLane(List<LaneOccupant> laneToTail, int lane) => laneToTail[lane] = null;

        /// <summary>坐标换算：行 -> y（UI Toolkit 本地坐标，y 向下）。</summary>
        private static float RowY(int row) => row * RowHeight + RowHeight / 2f;
        private static float LaneX(int lane) => lane * LaneWidth + LaneWidth / 2f;

        private void Paint(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            if (painter == null) return;

            // 1) 边（先画线，节点压在上面）
            painter.lineWidth = 1.5f;
            painter.strokeColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
            for (var r = 0; r < rows.Count; r++)
            {
                foreach (var e in rows[r].Edges)
                {
                    // 折线：节点中心 -> 垂直段（父泳道）-> 父节点中心
                    float y0 = RowY(r), y1 = RowY(r + e.ParentRowOffset);
                    float x0 = LaneX(e.ChildLane), x1 = LaneX(e.ParentLane);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x0, y0));
                    if (x0 != x1)
                    {
                        painter.LineTo(new Vector2(x0, (y0 + y1) / 2f));
                        painter.LineTo(new Vector2(x1, (y0 + y1) / 2f));
                    }
                    painter.LineTo(new Vector2(x1, y1));
                    painter.Stroke();
                }
            }

            // 2) 节点（实心圆：Arc 整圆 + Fill）
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var isMerge = commits[r].Parents != null && commits[r].Parents.Count > 1;
                painter.fillColor = isMerge ? new Color(0.95f, 0.72f, 0.2f) : new Color(0.2f, 0.62f, 0.92f);
                painter.BeginPath();
                painter.Arc(new Vector2(LaneX(row.Lane), RowY(r)), NodeRadius,
                    Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
            }
        }
    }
}