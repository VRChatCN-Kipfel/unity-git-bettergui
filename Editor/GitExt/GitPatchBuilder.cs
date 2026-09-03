using System;
using System.IO;
using System.Text;

namespace KF.GitUI
{
    /// <summary>
    /// git apply patch 文件构建器（M3-SOLUTION §1.6 定版）：
    /// 从 git 自产 unified diff 文本**原样切片**目标 hunk（保留 diff --git 头 + index 行 + ---/+++ + 该 @@ 块），
    /// 写为 **LF 行尾 + 末尾必换行 + 无 BOM** 的临时文件（放仓库 .git/ 下避免 clean filter/status 污染）。
    /// 纪律：不要重拼 patch（git-gui/magit 均原样切片）；"\ No newline" 标记必须保留。
    /// </summary>
    public static class GitPatchBuilder
    {
        /// <summary>该 hunk 是否含 ' ' 上下文行（无上下文 → git apply 无法定位，hunk 级操作不可用）。</summary>
        public static bool HunkHasContext(string diffOutput, int fileIndex, int hunkIndex)
        {
            if (string.IsNullOrEmpty(diffOutput)) return false;
            var fileBlocks = SplitFileBlocks(diffOutput);
            if (fileIndex < 0 || fileIndex >= fileBlocks.Count) return false;
            var blockLines = fileBlocks[fileIndex].Split('\n');
            var hunkStart = FindHunkLineIndex(blockLines, hunkIndex);
            if (hunkStart < 0) return false;
            var hunkEnd = FindHunkEndLineIndex(blockLines, hunkStart);
            for (var li = hunkStart + 1; li < hunkEnd; li++)
            {
                var l = blockLines[li];
                if (l.Length > 0 && l[0] == ' ') return true;
            }
            return false;
        }

        /// <summary>
        /// 从完整 diff 输出切片第 fileIndex 个文件的第 hunkIndex 个 hunk，写临时 patch 文件。
        /// 返回 patch 文件绝对路径（LF 文本）；提取失败返回 null（调用方提示"重新生成 diff"）。
        /// 上下文补齐：git apply 对无上下文 hunk 无法定位（实测 U0 patch apply 必失败），
        /// 故切片时自动纳入 hunk 前后各 ContextExtend 条上下文行并重算 @@ 头计数
        /// （M3-SOLUTION §1.6 边界"短上下文拒绝"；与 git-gui/magit 上下文处理同向）。
        /// </summary>
        /// <param name="diffOutput">git diff 完整输出（LF 或 CRLF 文本均可）。</param>
        /// <param name="fileIndex">目标文件在输出中的序号（0 基）。</param>
        /// <param name="hunkIndex">目标 hunk 在文件内的序号（0 基）。</param>
        /// <param name="repoDir">仓库根（patch 临时目录 = .git/ 存在时用其下，否则系统临时目录）。</param>
        public static string WriteHunkPatch(string diffOutput, int fileIndex, int hunkIndex, string repoDir)
        {
            if (string.IsNullOrEmpty(diffOutput)) return null;

            var fileBlocks = SplitFileBlocks(diffOutput);
            if (fileIndex < 0 || fileIndex >= fileBlocks.Count) return null;
            var block = fileBlocks[fileIndex];

            // 行拆分（保留 \n；\r 已随末尾处理）
            var blockLines = block.Split('\n');
            var hunkLineIdx = FindHunkLineIndex(blockLines, hunkIndex);
            if (hunkLineIdx < 0) return null;
            // 目标 hunk 的起止行（行级；hunkEndLine = 下一个 @@ 或行尾）
            var hunkStartLine = hunkLineIdx;
            var hunkEndLine = FindHunkEndLineIndex(blockLines, hunkStartLine);

            // hunk 块内是否已有 ' ' 上下文行（U3 diff 自带 → 原样切片；U0 无 → 外部补齐）
            var hasInlineContext = false;
            var oldChanges = new System.Collections.Generic.List<string>();
            var newChanges = new System.Collections.Generic.List<string>();
            for (var li = hunkStartLine + 1; li < hunkEndLine; li++)
            {
                var l = blockLines[li];
                if (l.Length == 0) continue;
                if (l[0] == ' ') hasInlineContext = true;
                else if (l[0] == '-') oldChanges.Add(l);
                else if (l[0] == '+') newChanges.Add(l);
            }
            if (oldChanges.Count == 0 && newChanges.Count == 0) return null;

            var sb = new StringBuilder();

            if (hasInlineContext)
            {
                // U3（或自带上下文）：原样切片——文件头 + hunk 头 + 块内全部行（保上下文与 \ No-newline）
                for (var li = 0; li < hunkStartLine; li++)
                    sb.Append(blockLines[li]).Append('\n');
                sb.Append(blockLines[hunkStartLine]).Append('\n');
                for (var li = hunkStartLine + 1; li < hunkEndLine; li++)
                {
                    var l = blockLines[li];
                    if (l.Length > 0) sb.Append(l).Append('\n');
                }
            }
            else
            {
                // U0（无上下文）：外部补齐 ±2 上下文行 + 重算 @@ 头
                const int contextExtend = 2;
                var head = new System.Collections.Generic.List<string>();
                var tail = new System.Collections.Generic.List<string>();
                CollectContext(blockLines, hunkStartLine - 1, contextExtend, head, backward: true);
                CollectContext(blockLines, hunkEndLine, contextExtend, tail, backward: false);

                var (origOldStart, origNewStart) = ParseHunkStarts(blockLines[hunkStartLine]);
                if (origOldStart <= 0 || origNewStart <= 0) return null;
                var oldStart = origOldStart - head.Count;
                var newStart = origNewStart - head.Count;
                if (oldStart < 1) oldStart = 1;
                if (newStart < 1) newStart = 1;
                var oldCount = head.Count + oldChanges.Count;
                var newCount = head.Count + newChanges.Count;

                // 文件头（到 hunk 头行之前——U0 时此处无上下文行，干净）
                for (var li = 0; li < hunkStartLine; li++)
                    sb.Append(blockLines[li]).Append('\n');
                // 重算 @@ 头（保留函数名区段）
                var hunkHeaderTail = GetHunkHeaderTail(blockLines[hunkStartLine]);
                sb.Append("@@ -").Append(oldStart).Append(',').Append(oldCount)
                  .Append(" +").Append(newStart).Append(',').Append(newCount).Append(" @@").Append(hunkHeaderTail).Append('\n');
                foreach (var l in head) sb.Append(l).Append('\n');
                for (var li = hunkStartLine + 1; li < hunkEndLine; li++)
                {
                    var l = blockLines[li];
                    if (l.Length > 0 && (l[0] == '-' || l[0] == '+' || l[0] == '\\'))
                        sb.Append(l).Append('\n');
                }
                foreach (var l in tail) sb.Append(l).Append('\n');
            }

            var patchText = NormalizeLf(sb.ToString());
            var patchPath = GetPatchPath(repoDir);
            if (!patchText.EndsWith("\n", StringComparison.Ordinal)) patchText += "\n";
            File.WriteAllText(patchPath, patchText, new UTF8Encoding(false));
            return patchPath;
        }

        // ---- 上下文补齐辅助 ----

        /// <summary>从 hunk 头行往前/后收集最多 n 条 ' ' 前缀上下文行（跳过其它类型）。</summary>
        private static void CollectContext(string[] blockLines, int from, int n,
            System.Collections.Generic.List<string> acc, bool backward)
        {
            var li = from;
            while (acc.Count < n)
            {
                if (li < 0 || li >= blockLines.Length) break;
                var l = blockLines[li];
                if (l.Length > 0 && l[0] == ' ' && !l.StartsWith("\\ ", StringComparison.Ordinal))
                {
                    acc.Insert(0, l);
                    if (acc.Count >= n) break;
                }
                li += backward ? -1 : 1;
            }
        }

        /// <summary>解析原 hunk 头 "@@ -A,B +C,D @@" 的起点 A/C（comma 或省略=1）。</summary>
        private static (int, int) ParseHunkStarts(string headerLine)
        {
            try
            {
                var parts = headerLine.Substring(3).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return (0, 0);
                var oldPart = parts[0].Substring(1); // 去 '-'
                var newPart = parts[1].Substring(1);  // 去 '+'
                var oldComma = oldPart.IndexOf(',');
                var newComma = newPart.IndexOf(',');
                var oldStart = int.Parse(oldComma >= 0 ? oldPart.Substring(0, oldComma) : oldPart,
                    System.Globalization.CultureInfo.InvariantCulture);
                var newStart = int.Parse(newComma >= 0 ? newPart.Substring(0, newComma) : newPart,
                    System.Globalization.CultureInfo.InvariantCulture);
                return (oldStart, newStart);
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>解析行数组里的目标 hunk 头行下标（第 hunkIndex 个 " @@ " 行首）。</summary>
        private static int FindHunkLineIndex(string[] lines, int hunkIndex)
        {
            for (var li = 0; li < lines.Length; li++)
            {
                if (lines[li].StartsWith("@@ ", StringComparison.Ordinal))
                {
                    if (hunkIndex == 0) return li;
                    hunkIndex--;
                }
            }
            return -1;
        }

        /// <summary>hunk 结束行下标（下一个 hunk 头，或行尾）。</summary>
        private static int FindHunkEndLineIndex(string[] lines, int startLine)
        {
            for (var li = startLine + 1; li < lines.Length; li++)
                if (lines[li].StartsWith("@@ ", StringComparison.Ordinal))
                    return li;
            return lines.Length;
        }

        /// <summary>取 "@@ -A,B +C,D @@" 里 @@ 之后的尾部（函数名区段，原样保留；无则空串）。</summary>
        private static string GetHunkHeaderTail(string headerLine)
        {
            var idx = headerLine.IndexOf(" @@", StringComparison.Ordinal);
            if (idx < 0) return string.Empty;
            var second = headerLine.IndexOf("@@", idx + 1, StringComparison.Ordinal);
            if (second < 0) return string.Empty;
            return headerLine.Substring(second + 2); // 含前导空格则保留
        }

        /// <summary>把 diff 输出按 "diff --git " 分割成文件块（保留每块含其行）。</summary>
        public static System.Collections.Generic.List<string> SplitFileBlocks(string diffOutput)
        {
            var blocks = new System.Collections.Generic.List<string>();
            var lines = diffOutput.Split(new[] { '\n' }, StringSplitOptions.None);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal) && sb.Length > 0)
                {
                    blocks.Add(sb.ToString());
                    sb.Clear();
                }
                sb.Append(line).Append('\n');
            }
            if (sb.Length > 0) blocks.Add(sb.ToString());
            return blocks;
        }

        /// <summary>统一行尾为 LF（git 自产 diff 在 Windows 上可能是 CRLF；apply 需 LF patch 文件）。</summary>
        private static string NormalizeLf(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string GetPatchPath(string repoDir)
        {
            var gitDir = repoDir != null ? Path.Combine(repoDir, ".git") : null;
            var dir = gitDir != null && Directory.Exists(gitDir) ? gitDir : Path.GetTempPath();
            return Path.Combine(dir, "kf-gitui-hunk.patch");
        }
    }
}