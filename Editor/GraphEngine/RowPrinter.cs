using System.Collections.Generic;

namespace KF.GitUI
{
    /// <summary>
    /// 行内打印元素（移植 JetBrains PrintElementGeneratorImpl + GraphElementComparatorByLayoutIndex 思路，自研）。
    /// 每行元素带 = [本行节点] + [E(r) 在途边]，按泳道比较器统一排序（跨泳道边可排到节点左侧）；
    /// 位置即槽位。打印元素：节点（head 标记）+ 边"本行槽位 → 邻行槽位"端点。
    /// 端点查找与 JetBrains 同：邻行元素按边匹配，找不到回退到端节点位置。
    /// </summary>
    public sealed class RowPrinter
    {
        public struct NodePrint { public int Position; public bool IsHead; }
        public struct EdgePrint { public int FromPosition; public int ToPosition; public bool IsDown; public int UpNode; public int DownNode; public RenderKind Kind; }

        /// <summary>边渲染种类（JetBrains long-edge 语义）。</summary>
        public enum RenderKind
        {
            Segment,   // 正常段（跨度 < LongEdgeSize）
            ArrowDown, // 长边上端下一行的向下箭头（只画箭头）
            ArrowUp,   // 长边下端上一行的向上箭头（只画箭头）
            Hidden     // 长边中部：不画（泳道位保留）
        }

        // JetBrains PrintElementGeneratorImpl：LONG_EDGE_SIZE=30 / LONG_EDGE_PART_SIZE=1
        public const int LongEdgeSize = 30;

        /// <summary>长边渲染判定（纯函数，冒烟可断言）。</summary>
        public static RenderKind DecideLongEdge(int span, int row, int up, int down)
        {
            if (span < LongEdgeSize) return RenderKind.Segment;
            if (row == up + 1) return RenderKind.ArrowDown;
            if (row == down - 1) return RenderKind.ArrowUp;
            return RenderKind.Hidden;
        }

        struct RowItem
        {
            public bool IsNode;
            public int Up;    // 边：上端；节点：-1
            public int Down;  // 边：下端
        }

        private readonly PermanentLinearGraph graph;
        private readonly GraphLayout layout;
        private readonly List<RowItem>[] sortedElements; // 每行排序后的元素带
        private readonly int[] nodePosition;             // 每行节点槽位
        private readonly List<NodePrint>[] nodes;
        private readonly List<EdgePrint>[] edges;

        public int Rows => sortedElements.Length;
        public IReadOnlyList<NodePrint> GetNodesInRow(int r) => nodes[r];
        public IReadOnlyList<EdgePrint> GetEdgesInRow(int r) => edges[r];
        public int RowElementCount(int r) => sortedElements[r].Count;
        public int LayoutIndex(int nodeIndex) => layout.GetLayoutIndex(nodeIndex);
        public int GetNodePosition(int r) => nodePosition[r];

        public static RowPrinter Build(PermanentLinearGraph graph, GraphLayout layout, EdgesInRow edgesInRow, HashSet<int> headNodes)
        {
            return new RowPrinter(graph, layout, edgesInRow, headNodes);
        }

        private RowPrinter(PermanentLinearGraph graph, GraphLayout layout, EdgesInRow edgesInRow, HashSet<int> headNodes)
        {
            this.graph = graph;
            this.layout = layout;
            var n = graph.NodesCount;
            sortedElements = new List<RowItem>[n];
            nodePosition = new int[n];
            nodes = new List<NodePrint>[n];
            edges = new List<EdgePrint>[n];
            var headSet = headNodes;

            for (var r = 0; r < n; r++)
            {
                // 1) 元素带（节点 + 在途边）统一排序
                var list = new List<RowItem>();
                list.Add(new RowItem { IsNode = true, Up = -1, Down = -1 });
                foreach (var e in edgesInRow.GetEdgesInRow(r))
                    list.Add(new RowItem { IsNode = false, Up = e.Up, Down = e.Down });
                list.Sort((a, b) => Compare(a, b, r));
                sortedElements[r] = list;

                var nodePos = 0;
                for (var i = 0; i < list.Count; i++)
                    if (list[i].IsNode) { nodePos = i; break; }
                nodePosition[r] = nodePos;
            }

            // 2) 打印元素（邻行元素带已全部就绪）
            for (var r = 0; r < n; r++)
            {
                var list = sortedElements[r];
                var nodePos = nodePosition[r];
                var rowNodes = new List<NodePrint> { new NodePrint { Position = nodePos, IsHead = headSet.Contains(r) } };
                var rowEdges = new List<EdgePrint>();

                // 节点邻接边 -> 邻行（下行=父，上行=子）
                foreach (var p in graph.GetParentNodes(r))
                    AddEdgeToNeighbor(r, nodePos, p, isDown: true, rowEdges);
                foreach (var c in graph.GetChildNodes(r))
                    AddEdgeToNeighbor(r, nodePos, c, isDown: false, rowEdges);

                // 在途边跨行延续（长边只在近端行画箭头，中部 Hidden 但泳道位保留）
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].IsNode) continue;
                    var ev = list[i];
                    var span = ev.Down - ev.Up;
                    if (span >= LongEdgeSize)
                    {
                        var kind = DecideLongEdge(span, r, ev.Up, ev.Down);
                        if (kind == RenderKind.ArrowDown || kind == RenderKind.ArrowUp)
                            rowEdges.Add(new EdgePrint { FromPosition = i, ToPosition = -1, IsDown = kind == RenderKind.ArrowDown, UpNode = ev.Up, DownNode = ev.Down, Kind = kind });
                        // Hidden：本行不产出绘制元素
                    }
                    else
                    {
                        if (r + 1 < n) AddEdgeBetween(r, i, ev, r + 1, isDown: true, rowEdges);
                        if (r - 1 >= 0) AddEdgeBetween(r, i, ev, r - 1, isDown: false, rowEdges);
                    }
                }

                nodes[r] = rowNodes;
                edges[r] = rowEdges;
            }
        }

        private void AddEdgeToNeighbor(int sourceRow, int fromPosition, int neighborNode, bool isDown, List<EdgePrint> into)
        {
            var targetRow = isDown ? sourceRow + 1 : sourceRow - 1;
            if (targetRow < 0 || targetRow >= sortedElements.Length) return;
            // 邻行按边查找；找不到回退到端节点槽位
            //   DOWN 边 (sourceRow -> neighborNode)；UP 边 (neighborNode -> sourceRow)
            var ev = isDown ? new RowItem { Up = sourceRow, Down = neighborNode }
                            : new RowItem { Up = neighborNode, Down = sourceRow };
            var to = FindEdgePosition(targetRow, ev);
            if (to == -1) to = NodePositionInRow(targetRow, neighborNode);
            if (to != -1)
                into.Add(new EdgePrint { FromPosition = fromPosition, ToPosition = to, IsDown = isDown, UpNode = ev.Up, DownNode = ev.Down });
        }

        private void AddEdgeBetween(int sourceRow, int fromPosition, RowItem ev, int targetRow, bool isDown, List<EdgePrint> into)
        {
            if (targetRow < 0 || targetRow >= sortedElements.Length) return;
            var to = FindEdgePosition(targetRow, ev);
            if (to == -1) to = isDown ? NodePositionInRow(targetRow, ev.Down) : NodePositionInRow(targetRow, ev.Up);
            if (to != -1)
                into.Add(new EdgePrint { FromPosition = fromPosition, ToPosition = to, IsDown = isDown, UpNode = ev.Up, DownNode = ev.Down, Kind = RenderKind.Segment });
        }

        private int FindEdgePosition(int row, RowItem ev)
        {
            var list = sortedElements[row];
            for (var i = 0; i < list.Count; i++)
                if (!list[i].IsNode && list[i].Up == ev.Up && list[i].Down == ev.Down) return i;
            return -1;
        }

        private int NodePositionInRow(int row, int nodeIndex)
        {
            // 仅当目标行就是该节点所在行：节点槽位
            return nodeIndex == row ? nodePosition[row] : -1;
        }

        // ---- GraphElementComparatorByLayoutIndex 移植 ----
        private int LI(int nodeIndex) => layout.GetLayoutIndex(nodeIndex);

        private int CompareEdgeVsNode(RowItem edge, int nodeIndex)
        {
            var upLI = LI(edge.Up);
            var downLI = LI(edge.Down);
            var nodeLI = LI(nodeIndex);
            var maxEdgeLI = upLI > downLI ? upLI : downLI;
            if (maxEdgeLI != nodeLI) return maxEdgeLI - nodeLI;
            return edge.Up - nodeIndex;
        }

        private int CompareEdgeVsEdge(RowItem a, RowItem b)
        {
            if (a.Up == b.Up)
                return a.Down < b.Down ? -CompareEdgeVsNode(b, a.Down) : CompareEdgeVsNode(a, b.Down);
            if (a.Up < b.Up) return CompareEdgeVsNode(a, b.Up);
            return -CompareEdgeVsNode(b, a.Up);
        }

        private int Compare(RowItem a, RowItem b, int row)
        {
            if (a.IsNode && b.IsNode) return 0;
            if (a.IsNode) return -CompareEdgeVsNode(b, row); // 节点 = 本行节点
            if (b.IsNode) return CompareEdgeVsNode(a, row);
            return CompareEdgeVsEdge(a, b);
        }
    }
}