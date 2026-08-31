using System.Collections.Generic;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>
    /// 永久线性图（移植 JetBrains PermanentLinearGraphImpl，Apache-2.0 思路，自研 C# 实现）。
    /// 行 = 提交按新→旧（row 0 最新）。边方向：up=到更新提交(子)，down=到更旧提交(父)。
    /// 存储：simpleNodes 位图（单父且父恰为 i+1 的隐式边，零存储）+ CSR 邻接（长边：
    /// 跳父 / merge 第二父 / 父不在行内）。NOT_LOAD_COMMIT（负 id）MVP 不产出。
    /// </summary>
    public sealed class PermanentLinearGraph
    {
        private readonly bool[] simpleNodes;
        private readonly int[] nodeToEdgeIndex;   // 长边 CSR 偏移，长度 nodesCount+1
        private readonly int[] longEdges;         // 长边目标（父）节点
        private readonly int[] nodeToChildIndex;  // 子边 CSR 偏移（含隐式链子），长度 nodesCount+1
        private readonly int[] childEdges;        // 子节点（更新提交，index 更小）

        public int NodesCount => simpleNodes.Length;

        public bool IsSimpleNode(int nodeIndex) => simpleNodes[nodeIndex];

        /// <summary>该节点的长边目标（父节点 index；不含隐式 i+1 父）。</summary>
        public IEnumerable<int> GetLongEdges(int nodeIndex)
        {
            for (var i = nodeToEdgeIndex[nodeIndex]; i < nodeToEdgeIndex[nodeIndex + 1]; i++)
                yield return longEdges[i];
        }

        /// <summary>亮父点序列：先长边（保持提交中的父顺序），隐式父 i+1 放在最后（JetBrains 同）。
        /// 父不在图内（未加载）被构建期丢弃，故无 NOT_LOAD_COMMIT。</summary>
        public List<int> GetParentNodes(int nodeIndex)
        {
            var result = new List<int>();
            for (var i = nodeToEdgeIndex[nodeIndex]; i < nodeToEdgeIndex[nodeIndex + 1]; i++)
                result.Add(longEdges[i]);
            if (simpleNodes[nodeIndex]) result.Add(nodeIndex + 1);
            return result;
        }

        /// <summary>直接子节点（更新提交，index 更小；含隐式链子节点）。</summary>
        public List<int> GetChildNodes(int nodeIndex)
        {
            var result = new List<int>();
            for (var i = nodeToChildIndex[nodeIndex]; i < nodeToChildIndex[nodeIndex + 1]; i++)
                result.Add(childEdges[i]);
            return result;
        }

        /// <summary>无子节点的提交 = 各分支线尖（head）。</summary>
        public List<int> GetHeads()
        {
            var heads = new List<int>();
            for (var i = 0; i < NodesCount; i++)
                if (GetChildNodes(i).Count == 0) heads.Add(i);
            return heads;
        }

        public static PermanentLinearGraph Build(IReadOnlyList<GitLogEntry> commits,
            IReadOnlyDictionary<string, int> commitIndex)
        {
            var n = commits.Count;
            var simple = new bool[n];
            var longEdgeList = new List<int>[n];
            for (var i = 0; i < n; i++) longEdgeList[i] = new List<int>();

            for (var i = 0; i < n; i++)
            {
                var parents = commits[i].Parents;
                if (parents == null || parents.Count == 0) continue;

                var parentIndices = new List<int>();
                foreach (var p in parents)
                    if (commitIndex.TryGetValue(p, out var pi) && pi != i)
                        if (!parentIndices.Contains(pi)) parentIndices.Add(pi); // 去重（JetBrains DuplicateParentFixer）

                // JetBrains 规则：仅当"唯一父 == 下一行"才是隐式链（simple）；
                // 其余节点（含 merge 多父）所有父必须显式进长边——即使某个父恰好 == i+1。
                if (parentIndices.Count == 1 && parentIndices[0] == i + 1)
                {
                    simple[i] = true;
                    continue;
                }
                foreach (var pi in parentIndices)
                    longEdgeList[i].Add(pi);
            }

            var offsets = new int[n + 1];
            for (var i = 0; i < n; i++) offsets[i + 1] = offsets[i] + longEdgeList[i].Count;
            var edges = new int[offsets[n]];
            var fill = 0;
            for (var i = 0; i < n; i++)
                foreach (var e in longEdgeList[i]) edges[fill++] = e;

            // 子边 CSR：先数每个节点的子数（隐式链子的"子"来自 simple 上邻：c 的隐式父是 c+1 -> c 是 c+1 的子）
            var childCounts = new int[n];
            for (var c = 0; c < n; c++)
            {
                if (simple[c] && c + 1 < n) childCounts[c + 1]++;
                foreach (var pi in longEdgeList[c])
                    if (pi < n) childCounts[pi]++;
            }
            var childOffsets = new int[n + 1];
            for (var i = 0; i < n; i++) childOffsets[i + 1] = childOffsets[i] + childCounts[i];
            var childEdges = new int[childOffsets[n]];
            var childFillPos = (int[])childOffsets.Clone();
            for (var c = 0; c < n; c++)
            {
                if (simple[c] && c + 1 < n) childEdges[childFillPos[c + 1]++] = c;
                foreach (var pi in longEdgeList[c])
                    if (pi < n) childEdges[childFillPos[pi]++] = c;
            }

            return new PermanentLinearGraph(simple, offsets, edges, childOffsets, childEdges);
        }

        private PermanentLinearGraph(bool[] simple, int[] offsets, int[] longEdges,
            int[] childOffsets, int[] childEdges)
        {
            simpleNodes = simple;
            nodeToEdgeIndex = offsets;
            this.longEdges = longEdges;
            nodeToChildIndex = childOffsets;
            this.childEdges = childEdges;
        }
    }
}