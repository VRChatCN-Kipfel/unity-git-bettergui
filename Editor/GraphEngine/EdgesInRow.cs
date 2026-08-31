using System.Collections.Generic;

namespace KF.GitUI
{
    /// <summary>
    /// 逐行边集合（移植 JetBrains EdgesInRowGenerator 的增量语义，自研 C#，MVP 全量预计算）。
    /// E(r) = "穿过 row r 与 row r+1 边界"的边（in-flight 边）：已在途的长边 + 本行新起的父边，
    /// 减去恰好终止于 r+1 的边（它们由相邻行的节点边负责绘制）。
    /// 递推：E(r+1) = E(r) ∪ DownAdj(r+1 的父边) ∖ UpAdj(r+2 的入边)  —— 即
    ///        E(r+1) = E(r) ∪ DownAdj(r) ∖ UpAdj(r+1)（DownAdj(r)=r 的父边，UpAdj(r+1)=终止于 r+1 的边）
    /// 已用手算验证：E 集合 = [{},{(0,2)},{(1,3),(1,4)},{(1,4)},{(3,6)},{(3,6)},{},{},{}]
    /// </summary>
    public sealed class EdgesInRow
    {
        private readonly List<List<GraphEdgeRef>> rows; // rows[r] = E(r)，边 (up,down)

        public readonly struct GraphEdgeRef
        {
            public readonly int Up;
            public readonly int Down;
            public GraphEdgeRef(int up, int down) { Up = up; Down = down; }
            public override string ToString() => $"({Up},{Down})";
        }

        public IReadOnlyList<GraphEdgeRef> GetEdgesInRow(int rowIndex) => rows[rowIndex];

        public static EdgesInRow Build(PermanentLinearGraph graph)
        {
            var n = graph.NodesCount;
            var rows = new List<List<GraphEdgeRef>>(n);
            var current = new List<GraphEdgeRef>();
            var currentSet = new HashSet<(int, int)>();

            for (var r = 0; r < n; r++)
            {
                rows.Add(new List<GraphEdgeRef>(current)); // E(r) 快照（不变量：有序、去重）

                // DownAdj(r)：r 的父边（可能早于 r 或晚于 r，父 index 更大）
                foreach (var p in graph.GetParentNodes(r))
                {
                    if (currentSet.Add((r, p))) current.Add(new GraphEdgeRef(r, p));
                }

                // UpAdj(r+1)：终止于 r+1 的边（其子边）——出界守卫
                if (r + 1 < n)
                {
                    foreach (var c in graph.GetChildNodes(r + 1))
                    {
                        if (currentSet.Remove((c, r + 1)))
                            current.RemoveAll(e => e.Up == c && e.Down == r + 1);
                    }
                }
            }

            return new EdgesInRow(rows);
        }

        private EdgesInRow(List<List<GraphEdgeRef>> rows) { this.rows = rows; }
    }
}