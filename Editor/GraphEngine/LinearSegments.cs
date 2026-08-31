using System.Collections.Generic;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>
    /// 线性段折叠（JetBrains CollapsedGraph 的展示级简化：不动行索引，只藏节点 + 虚线链）。
    /// 判据：行 r 是"纯线性中部" = 单父且父恰为 r+1（隐式链）且非 head 且无 ref 指向；
    /// 连续线性行数 ≥ minRun 的区间 [Top..Bottom] 折叠：中部节点 (Top+1..Bottom] 不画，
    /// 该段链条以虚线绘制（JetBrains DOTTED 边语义）。
    /// </summary>
    public sealed class LinearSegments
    {
        public readonly List<(int Top, int Bottom)> Runs = new List<(int, int)>();

        public static LinearSegments Build(IReadOnlyList<GitLogEntry> log, PermanentLinearGraph graph,
            HashSet<int> headNodes, ISet<string> refedCommits, int minRun = 5)
        {
            var result = new LinearSegments();
            var n = graph.NodesCount;
            var linear = new bool[n];
            for (var r = 0; r < n; r++)
            {
                var isChain = graph.IsSimpleNode(r) && r + 1 < n;
                // 排除：head、有 ref、分叉点（子节点数 !=1，如 merge/split 处）——只折叠"单链直下"段
                linear[r] = isChain && !headNodes.Contains(r) &&
                            graph.GetChildNodes(r).Count == 1 &&
                            !(refedCommits != null && refedCommits.Contains(log[r].CommitID));
            }

            var start = -1;
            for (var r = 0; r <= n; r++)
            {
                if (r < n && linear[r])
                {
                    if (start == -1) start = r;
                    continue;
                }
                if (start != -1)
                {
                    if (r - start >= minRun) result.Runs.Add((start, r - 1));
                    start = -1;
                }
            }
            return result;
        }

        /// <summary>中部行集合（隐藏节点的行号）。</summary>
        public HashSet<int> HiddenRows()
        {
            var set = new HashSet<int>();
            foreach (var (top, bottom) in Runs)
                for (var r = top + 1; r <= bottom; r++) set.Add(r);
            return set;
        }
    }
}