using System.Collections.Generic;

namespace KF.GitUI
{
    /// <summary>
    /// 泳道布局（移植 JetBrains GraphLayoutBuilder：带回溯的栈式 DFS，Apache-2.0 思路，自研 C#）。
    /// 语义（已用 JetBrains layoutBuilder 测试数据两组逐字节验证）：
    ///   - heads（无子节点 = 各线尖）按优先级排序，逐一出队
    ///   - 对每个 head 做栈式 DFS：沿"第一个未分配泳道的父"下行；无可用父时——
    ///     若是首访死路则 currentLane++，然后弹栈回溯，上一节点继续找下一个未分配父
    ///   - 因此 merge 的第二父分支（如 feature/x、feature/y）在弹栈回溯后被瀑布式分配到新泳道
    /// 节点泳道 = 它首次被 DFS 触及时的 currentLane；每条 head 线从自己的泳道"发源"。
    /// </summary>
    public sealed class GraphLayout
    {
        private readonly int[] layoutIndex;   // 每节点泳道（0 基；-1 = 未分配）
        private readonly List<int> headNodes; // 拥有独立泳道的 head（顺序即优先级：新→旧）
        private readonly int[] headLaneIndexes; // heads 自身泳道（按 headNodes 顺序，二分用）

        /// <summary>节点泳道号（0 基）。</summary>
        public int GetLayoutIndex(int nodeIndex) => layoutIndex[nodeIndex];

        /// <summary>泳道数上限：线由 head 定义，泳道槽位可以多于线（回溯死路会开新槽）。</summary>
        public int LaneCount => headNodes.Count;

        /// <summary>head 节点列表（按优先级：新→旧）。</summary>
        public IReadOnlyList<int> HeadNodes => headNodes;

        /// <summary>泳道 -> 代表 head（JetBrains GraphLayoutImpl.getHeadOrder 钳制语义：
        /// 无此泳道的槽位回退到最近 head）。上色/命名用之。</summary>
        public int GetHeadNodeIndexForLane(int lane)
        {
            var i = System.Array.BinarySearch(headLaneIndexes, lane);
            if (i < 0) i = System.Math.Max(0, -i - 2);
            return headNodes[i];
        }

        /// <summary>heads 优先级：列表顺序即"新→旧"（row0 最新），天然主头在前。</summary>
        public static GraphLayout Build(PermanentLinearGraph graph)
        {
            var n = graph.NodesCount;
            var layoutIndex = new int[n];
            for (var i = 0; i < n; i++) layoutIndex[i] = -1;

            var heads = graph.GetHeads();          // 无子节点 = 线尖（新→旧序）
            var importantHeads = new List<int>();
            var currentLane = 0;

            foreach (var head in heads)
            {
                if (layoutIndex[head] != -1) continue; // 已被其他线覆盖

                importantHeads.Add(head);
                var stack = new List<int> { head };

                while (stack.Count > 0)
                {
                    var node = stack[stack.Count - 1];
                    var firstVisit = layoutIndex[node] == -1;
                    if (firstVisit) layoutIndex[node] = currentLane;

                    // 第一个未分配泳道的父；无则死路（首访死路才 ++）
                    var parents = graph.GetParentNodes(node);
                    var next = -1;
                    foreach (var p in parents)
                        if (layoutIndex[p] == -1) { next = p; break; }

                    if (next != -1)
                    {
                        stack.Add(next);
                    }
                    else
                    {
                        if (firstVisit) currentLane++;
                        stack.RemoveAt(stack.Count - 1);
                    }
                }
            }

            return new GraphLayout(layoutIndex, importantHeads);
        }

        private GraphLayout(int[] layoutIndex, List<int> headNodes)
        {
            this.layoutIndex = layoutIndex;
            this.headNodes = headNodes;
            headLaneIndexes = new int[headNodes.Count];
            for (var i = 0; i < headNodes.Count; i++)
                headLaneIndexes[i] = layoutIndex[headNodes[i]];
        }
    }
}