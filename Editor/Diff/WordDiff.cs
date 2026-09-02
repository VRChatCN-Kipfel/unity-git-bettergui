using System;
using System.Collections.Generic;
using System.Text;

namespace KF.GitUI
{
    /// <summary>词级 diff 片段类型。</summary>
    public enum DiffFragmentKind
    {
        Unchanged, // 两侧共有（LCS 匹配）
        Added,     // 仅新行（插入）
        Deleted    // 仅旧行（删除）
    }

    /// <summary>词级对齐结果：old 行一侧的片段集合（渲染删除高亮用）。</summary>
    public sealed class DiffFragment
    {
        public DiffFragmentKind Kind;
        /// <summary>片段在 old 行的起始字符偏移（含）。</summary>
        public int OldStart;
        /// <summary>片段在 old 行的字符长度。</summary>
        public int OldLength;
        /// <summary>片段在 new 行的起始字符偏移（Unchanged 时有效，Added 时为 -1）。</summary>
        public int NewStart = -1;
        /// <summary>片段在 new 行的字符长度。</summary>
        public int NewLength;

        public override string ToString()
        {
            return $"[{Kind}] old({OldStart}+{OldLength}) new({NewStart}+{NewLength})";
        }
    }

    /// <summary>
    /// 词级 diff 引擎（自研，零依赖；M3-SOLUTION §1.5 定版路线 b）：
    /// 对行级 diff 的每个变更行对（old行 ↔ new行）做词级对齐。
    /// Tokenize 规则（仿 JetBrains ByWord）：
    ///   · ASCII 字母数字连写为词（会折叠大小写，保持原文）
    ///   · CJK 连续脚本逐字符（Unicode 表意文字/假名/谚文按单字符 token）
    ///   · 标点/空白各归一类（连续空白合并为一个 token；其他不可打印/标点单字符）
    /// LCS 用朴素 DP（行内 token 数通常 &lt;200，毫秒级；>256 截断为全行退化）。
    /// 输出：每行对两个 List&lt;DiffFragment&gt;（旧行片段 / 新行片段），调用方渲染高亮。
    /// </summary>
    public static class WordDiff
    {
        private const int MaxTokens = 256; // 超过则整行退化（不逐词）

        /// <summary>行级 diff 行对 → 词级片段对。返回 null 表示任一侧退化（整行染色由调用方决定）。</summary>
        public static PairResult Compare(string oldLine, string newLine)
        {
            var oldTokens = Tokenize(oldLine);
            var newTokens = Tokenize(newLine);
            if (oldTokens.Count > MaxTokens || newTokens.Count > MaxTokens)
                return null;

            var ops = LcsOps(oldTokens, newTokens);

            var oldFrags = new List<DiffFragment>();
            var newFrags = new List<DiffFragment>();
            int oldPos = 0, newPos = 0;

            foreach (var op in ops)
            {
                switch (op.Kind)
                {
                    case DiffFragmentKind.Unchanged:
                    {
                        var t = oldTokens[op.OldIndex];
                        oldFrags.Add(new DiffFragment { Kind = DiffFragmentKind.Unchanged, OldStart = oldPos, OldLength = t.Length, NewStart = newPos, NewLength = t.Length });
                        newFrags.Add(new DiffFragment { Kind = DiffFragmentKind.Unchanged, OldStart = oldPos, OldLength = t.Length, NewStart = newPos, NewLength = t.Length });
                        oldPos += t.Length;
                        newPos += t.Length;
                        break;
                    }
                    case DiffFragmentKind.Deleted:
                    {
                        var t = oldTokens[op.OldIndex];
                        oldFrags.Add(new DiffFragment { Kind = DiffFragmentKind.Deleted, OldStart = oldPos, OldLength = t.Length });
                        oldPos += t.Length;
                        break;
                    }
                    case DiffFragmentKind.Added:
                    {
                        var t = newTokens[op.NewIndex];
                        newFrags.Add(new DiffFragment { Kind = DiffFragmentKind.Added, OldStart = oldPos, OldLength = 0, NewStart = newPos, NewLength = t.Length });
                        newPos += t.Length;
                        break;
                    }
                }
            }
            return new PairResult(oldFrags, newFrags);
        }

        public sealed class PairResult
        {
            public readonly List<DiffFragment> OldFragments;
            public readonly List<DiffFragment> NewFragments;
            public PairResult(List<DiffFragment> oldFrags, List<DiffFragment> newFrags)
            {
                OldFragments = oldFrags;
                NewFragments = newFrags;
            }
        }

        /// <summary>分词：ASCII 字母数字下划线连为词；CJK 连续脚本逐字符；连续空白一个 token；其余标点单字符。</summary>
        public static List<string> Tokenize(string line)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(line)) return list;

            var sb = new StringBuilder();
            int i = 0;
            int n = line.Length;
            while (i < n)
            {
                char c = line[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    // ASCII word run
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_') && line[i] < 0x80)
                        i++;
                    if (i > start)
                    {
                        list.Add(line.Substring(start, i - start));
                        continue;
                    }
                    // 非 ASCII 字母（CJK 等）：逐字符
                    list.Add(c.ToString());
                    i++;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    int start = i;
                    while (i < n && char.IsWhiteSpace(line[i])) i++;
                    list.Add(line.Substring(start, i - start));
                    continue;
                }
                list.Add(c.ToString());
                i++;
            }
            return list;
        }

        /// <summary>两 token 序列 LCS（朴素 DP + 回溯）。返回操作序列（old/new 下标索引）。</summary>
        public struct Op
        {
            public DiffFragmentKind Kind;
            /// <summary>Unchanged/Deleted 时 = oldTokens 下标；Added 时 = -1。</summary>
            public int OldIndex;
            /// <summary>Unchanged/Added 时 = newTokens 下标；Deleted 时 = -1。</summary>
            public int NewIndex;
        }

        public static List<Op> LcsOps(List<string> oldTokens, List<string> newTokens)
        {
            int n = oldTokens.Count, m = newTokens.Count;
            // dp[i][j] = LCS(旧[0..i), 新[0..j))
            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    dp[i, j] = string.Equals(oldTokens[i], newTokens[j], StringComparison.Ordinal)
                        ? dp[i + 1, j + 1] + 1
                        : Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }

            var ops = new List<Op>(n + m);
            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (string.Equals(oldTokens[a], newTokens[b], StringComparison.Ordinal))
                {
                    ops.Add(new Op { Kind = DiffFragmentKind.Unchanged, OldIndex = a, NewIndex = b });
                    a++; b++;
                }
                else if (dp[a + 1, b] >= dp[a, b + 1])
                {
                    ops.Add(new Op { Kind = DiffFragmentKind.Deleted, OldIndex = a, NewIndex = -1 });
                    a++;
                }
                else
                {
                    ops.Add(new Op { Kind = DiffFragmentKind.Added, OldIndex = -1, NewIndex = b });
                    b++;
                }
            }
            while (a < n) { ops.Add(new Op { Kind = DiffFragmentKind.Deleted, OldIndex = a, NewIndex = -1 }); a++; }
            while (b < m) { ops.Add(new Op { Kind = DiffFragmentKind.Added, OldIndex = -1, NewIndex = b }); b++; }
            return ops;
        }

        /// <summary>
        /// 按片段顺序把某侧行文本拼成富文本（UI 渲染层核心算法）。
        /// old 侧：frags = result.OldFragments，line = oldLine（使用 OldStart/Deleted 标记）。
        /// new 侧：frags = result.NewFragments，line = newLine（使用 NewStart/Added 标记）。
        /// 标记串由调用方提供（如 "&lt;mark=#..."；经 Unity rich text 渲染）。
        /// </summary>
        public static string BuildRichText(List<DiffFragment> frags, string line,
            bool sideIsOld,
            string deletedMarkOpen, string deletedMarkClose,
            string addedMarkOpen, string addedMarkClose)
        {
            var sb = new StringBuilder();
            foreach (var f in frags)
            {
                if (f.Kind == DiffFragmentKind.Deleted && sideIsOld)
                    sb.Append(deletedMarkOpen).Append(line, f.OldStart, f.OldLength).Append(deletedMarkClose);
                else if (f.Kind == DiffFragmentKind.Added && !sideIsOld)
                    sb.Append(addedMarkOpen).Append(line, f.NewStart, f.NewLength).Append(addedMarkClose);
                else if (f.Kind == DiffFragmentKind.Unchanged)
                    sb.Append(line, sideIsOld ? f.OldStart : f.NewStart, sideIsOld ? f.OldLength : f.NewLength);
                // 对侧片段（old 侧的 Added / new 侧的 Deleted）跳过
            }
            return sb.ToString();
        }
    }
}