using System;
using System.Collections.Generic;
using System.Text;

namespace KF.GitUI
{
    /// <summary>diff 行类型（unified 语义）。</summary>
    public enum DiffLineKind
    {
        Context,   // 上下文行（" ")
        Old,       // 删除行（"-"）
        New,       // 新增行（"+"）
        HunkHeader // @@ 头（保留供调试/导航，不计入行对）
    }

    /// <summary>unified diff 中的一行（content 不含前缀字符）。</summary>
    public sealed class DiffLine
    {
        public DiffLineKind Kind;
        public string Content;
        /// <summary>该行在文件中的行号（Context/Old = 旧文件行号；New = 新文件行号；-1=无法确定）。</summary>
        public int LineNumber = -1;

        public bool IsChange => Kind == DiffLineKind.Old || Kind == DiffLineKind.New;

        public override string ToString()
        {
            char p = Kind == DiffLineKind.Old ? '-' : Kind == DiffLineKind.New ? '+' : ' ';
            return p + Content;
        }
    }

    /// <summary>单个 hunk：@@ -a,b +c,d @@ 及其中所有行。</summary>
    public sealed class DiffHunk
    {
        public int OldStart;
        public int OldCount;
        public int NewStart;
        public int NewCount;
        public List<DiffLine> Lines = new List<DiffLine>();
    }

    /// <summary>单个文件变更：diff --git 段。</summary>
    public sealed class DiffFile
    {
        public string OldPath = string.Empty; // a/ 侧（/dev/null 时为空）
        public string NewPath = string.Empty; // b/ 侧
        public bool IsNew;    // 新增文件（old=/dev/null）
        public bool IsDeleted; // 删除文件（new=/dev/null）
        public bool IsBinary; // Binary files ... differ
        public List<DiffHunk> Hunks = new List<DiffHunk>();
    }

    /// <summary>
    /// git unified diff 文本解析器（解析 git diff --no-ext-diff 输出）。
    /// 支持：diff --git 头 / index / --- / +++ / new|deleted file mode / rename from-to /
    /// /dev/null 标记 / @@ -l,c +l,c @@（c 省略=1）/ 行前缀 ' '-'+' / "\ No newline" 标记 / Binary 行。
    /// 不做词级处理；行对（Old↔New）消费由调用方负责（WordDiff 输入）。
    /// </summary>
    public static class UnifiedDiffParser
    {
        public static List<DiffFile> Parse(string output)
        {
            var files = new List<DiffFile>();
            if (string.IsNullOrEmpty(output)) return files;

            DiffFile cur = null;
            DiffHunk curHunk = null;
            int oldLineNo = -1, newLineNo = -1; // hunk 内游标（解析过的旧/新行计数）

            using (var reader = new System.IO.StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                    {
                        cur = new DiffFile();
                        files.Add(cur);
                        curHunk = null;
                        oldLineNo = newLineNo = -1;
                        continue;
                    }
                    if (cur == null) continue; // 头之前（索引/校验和等）忽略

                    // hunk 头（在任何文件头/行处理之前判定：curHunk 为 null 时也可能出现 @@）
                    if (line.StartsWith("@@ ", StringComparison.Ordinal))
                    {
                        curHunk = ParseHunkHeader(line);
                        if (curHunk != null)
                        {
                            cur.Hunks.Add(curHunk);
                            oldLineNo = curHunk.OldStart;
                            newLineNo = curHunk.NewStart;
                        }
                        continue;
                    }

                    if (curHunk == null)
                    {
                        if (line.StartsWith("--- ", StringComparison.Ordinal))
                        {
                            cur.OldPath = StripSidePrefix(line.Substring(4));
                            if (cur.OldPath == "/dev/null") { cur.IsNew = true; cur.OldPath = string.Empty; }
                        }
                        else if (line.StartsWith("+++ ", StringComparison.Ordinal))
                        {
                            cur.NewPath = StripSidePrefix(line.Substring(4));
                            if (cur.NewPath == "/dev/null") { cur.IsDeleted = true; cur.NewPath = string.Empty; }
                        }
                        else if (line.StartsWith("Binary files", StringComparison.Ordinal)
                                 || line.StartsWith("Binary file", StringComparison.Ordinal))
                        {
                            cur.IsBinary = true;
                        }
                        // index / new file mode / deleted file mode / rename from / rename to / similarity 直接忽略
                        continue;
                    }

                    // hunk 体内
                    if (curHunk == null) continue;

                    if (line.StartsWith("\\", StringComparison.Ordinal))
                    {
                        // "\ No newline at end of file"：把上一行的 LineNumber 标记为文件尾（无后继换行）。
                        if (curHunk.Lines.Count > 0)
                            curHunk.Lines[curHunk.Lines.Count - 1].LineNumber = -2; // -2 = 无尾换行
                        continue;
                    }

                    var kind = line.Length > 0 ? line[0] : ' ';
                    var content = line.Length > 1 ? line.Substring(1) : string.Empty;
                    DiffLine dl;
                    switch (kind)
                    {
                        case '-':
                            dl = new DiffLine { Kind = DiffLineKind.Old, Content = content, LineNumber = oldLineNo };
                            oldLineNo++;
                            break;
                        case '+':
                            dl = new DiffLine { Kind = DiffLineKind.New, Content = content, LineNumber = newLineNo };
                            newLineNo++;
                            break;
                        default:
                            dl = new DiffLine { Kind = DiffLineKind.Context, Content = content, LineNumber = oldLineNo };
                            oldLineNo++;
                            newLineNo++;
                            break;
                    }
                    curHunk.Lines.Add(dl);
                }
            }
            return files;
        }

        /// <summary>解析 @@ -l,c +l,c @@；返回 null 表示格式异常（容错跳过）。</summary>
        public static DiffHunk ParseHunkHeader(string line)
        {
            try
            {
                // @@ -12,3 +5,1 @@ optional section heading
                var parts = line.Substring(3).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return null;
                var oldPart = parts[0].Substring(1); // 去 '-'
                var newPart = parts[1].Substring(1); // 去 '+'
                int oldStart = 1, oldCount = 1;
                int newStart = 1, newCount = 1;
                var comma = oldPart.IndexOf(',');
                if (comma >= 0)
                {
                    oldStart = ParseRangeStart(oldPart, comma);
                    oldCount = ParseRangeCount(oldPart, comma);
                }
                else { oldStart = ParseRangeStart(oldPart, -1); }
                comma = newPart.IndexOf(',');
                if (comma >= 0)
                {
                    newStart = ParseRangeStart(newPart, comma);
                    newCount = ParseRangeCount(newPart, comma);
                }
                else { newStart = ParseRangeStart(newPart, -1); }

                return new DiffHunk
                {
                    OldStart = oldStart, OldCount = oldCount,
                    NewStart = newStart, NewCount = newCount
                };
            }
            catch
            {
                return null;
            }
        }

        private static int ParseRangeStart(string s, int commaIndex)
        {
            if (commaIndex >= 0) s = s.Substring(0, commaIndex);
            return int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int ParseRangeCount(string s, int commaIndex)
        {
            // 注意 0 计数：new 侧 "+,0" 表示删除整个文件内容
            var tail = s.Substring(commaIndex + 1);
            return int.Parse(tail, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>剥 git diff 的 a//b/ 侧前缀（---/+++ 行）。/dev/null 保持原样。</summary>
        private static string StripSidePrefix(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            p = UnquotePath(p);
            if (p.Length > 2 && (p[0] == 'a' || p[0] == 'b') && p[1] == '/')
                return p.Substring(2);
            return p;
        }

        /// <summary>去统一 diff 路径的引号（git 对含特殊字符路径加双引号 + C 风格转义）。</summary>
        private static string UnquotePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.Length >= 2 && p[0] == '"' && p[p.Length - 1] == '"')
            {
                var sb = new StringBuilder();
                var inner = p.Substring(1, p.Length - 2);
                for (var i = 0; i < inner.Length; i++)
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length)
                    {
                        i++;
                        char c = inner[i];
                        switch (c)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            default: sb.Append(c); break;
                        }
                    }
                    else sb.Append(inner[i]);
                }
                return sb.ToString();
            }
            return p;
        }
    }

    /// <summary>
    /// hunk 内变更行配对（词级高亮输入）：把 Old 行与 New 行按出现顺序一一配对。
    /// 语义（unified 默认）：同一 hunk 内 "-" 与 "+" 行的【第 i 个 "--" 与第 i 个 "+"】配对。
    /// 数量不等时剩余行单独输出（无配对；渲染时整行染色）。
    /// 注意：git 对"替换"输出为若干 - 行后跟若干 + 行，配对按序即可；对纯增/纯删（无对侧行）正确降级。
    /// </summary>
    public static class HunkLinePairing
    {
        public sealed class LinePair
        {
            public DiffLine Old; // 可为 null（纯新增）
            public DiffLine New; // 可为 null（纯删除）
            public bool HasPair => Old != null && New != null;
        }

        public static List<LinePair> Pair(DiffHunk hunk)
        {
            var result = new List<LinePair>();
            if (hunk == null) return result;

            // 提取 -/+ 行序列（保持顺序）
            var olds = new List<DiffLine>();
            var news = new List<DiffLine>();
            foreach (var l in hunk.Lines)
            {
                if (l.Kind == DiffLineKind.Old) olds.Add(l);
                else if (l.Kind == DiffLineKind.New) news.Add(l);
            }

            int n = Math.Max(olds.Count, news.Count);
            for (var i = 0; i < n; i++)
                result.Add(new LinePair
                {
                    Old = i < olds.Count ? olds[i] : null,
                    New = i < news.Count ? news[i] : null
                });
            return result;
        }

        /// <summary>纯配对（去掉无对侧的单边行）：供词级高亮（只处理 HasPair）。</summary>
        public static List<LinePair> PairAligned(DiffHunk hunk)
        {
            var all = Pair(hunk);
            var aligned = new List<LinePair>();
            foreach (var p in all)
                if (p.HasPair)
                    aligned.Add(p);
            return aligned;
        }
    }
}