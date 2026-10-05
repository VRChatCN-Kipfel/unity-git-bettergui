using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KF.GitUI
{
    /// <summary>一次写入的结果（既是写盘返回值，也是 UI 预览的数据源）。</summary>
    public sealed class GitIgnoreWriteResult
    {
        /// <summary>目标 .gitignore 原先不存在（本次新建）。</summary>
        public bool Created;
        /// <summary>采用了覆盖模式且目标原先有内容（已备份）。</summary>
        public bool Overwritten;
        /// <summary>新增（或覆盖写入）的规则行数。</summary>
        public int AddedRules;
        /// <summary>合并模式下因已存在而跳过的规则行数。</summary>
        public int SkippedRules;
        /// <summary>覆盖模式下的备份路径；未备份为 null。</summary>
        public string BackupPath;
        /// <summary>写入后 .gitignore 的完整文本（LF 换行）。</summary>
        public string Result = string.Empty;

        /// <summary>合并模式下无任何新增。</summary>
        public bool NothingToAdd
        {
            get { return !Created && !Overwritten && AddedRules == 0; }
        }
    }

    /// <summary>
    /// .gitignore 写入器：模板正文 → 项目根的 .gitignore。
    /// 纯函数（Merge/Overwrite）与落盘（Write）分离，冒烟无需 IO 即可覆盖合并语义。
    ///
    /// 安全默认：已存在的 .gitignore 默认**合并**（只追加缺失规则，保留原有内容）；
    /// 覆盖模式必须先备份为 <c>.gitignore.bak</c>。写入一律 UTF-8 无 BOM + LF。
    /// </summary>
    public static class GitIgnoreWriter
    {
        public const string BackupSuffix = ".bak";

        /// <summary>归一化文本：CRLF/CR → LF，末尾恰好一个换行；空内容返回空串。</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.TrimEnd('\n') + "\n";
        }

        /// <summary>是否忽略规则行（非空、非注释）。</summary>
        public static bool IsRule(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            var t = line.Trim();
            return t.Length > 0 && t[0] != '#';
        }

        /// <summary>统计文本里的规则行数（注释/空行不计）。</summary>
        public static int CountRules(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var count = 0;
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                if (IsRule(line)) count++;
            return count;
        }

        /// <summary>
        /// 合并：把模板中**尚不存在**的规则追加到已有内容之后，保留已有内容原样。
        /// 以"注释行/空行"为块边界整块追加，因此新增部分自带分组注释，可读性接近手写。
        /// </summary>
        public static GitIgnoreWriteResult Merge(string existingText, string templateText)
        {
            var normalizedExisting = Normalize(existingText ?? string.Empty);
            var result = new GitIgnoreWriteResult
            {
                Created = normalizedExisting.Length == 0,
                Overwritten = false
            };

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in normalizedExisting.Split('\n'))
                if (IsRule(line)) known.Add(line.Trim());

            var blocks = SplitBlocks(templateText);
            var append = new List<string>();
            foreach (var block in blocks)
            {
                // 块内规则行；纯注释/空块不产出任何东西（避免追加孤零零的分节标题）
                var ruleLines = 0;
                var missing = 0;
                foreach (var line in block)
                {
                    if (!IsRule(line)) continue;
                    ruleLines++;
                    if (!known.Contains(line.Trim())) missing++;
                }
                if (ruleLines == 0) continue;
                result.SkippedRules += ruleLines - missing;
                if (missing == 0) continue;

                if (append.Count > 0) append.Add(string.Empty);
                // 块内逐行过滤：只跳过"已存在"的规则行，注释行照旧保留——
                // 整块照抄会把已存在的规则重复写入，那是错的。
                foreach (var line in block)
                {
                    if (IsRule(line) && known.Contains(line.Trim())) continue;
                    append.Add(line.TrimEnd());
                }
                result.AddedRules += missing;
            }

            var sb = new StringBuilder();
            if (normalizedExisting.Length > 0) sb.Append(normalizedExisting);
            foreach (var line in append) sb.Append(line).Append('\n');
            result.Result = sb.ToString();
            return result;
        }

        /// <summary>覆盖：整份替换为模板正文（预览/统计用；备份在 Write 里做）。</summary>
        public static GitIgnoreWriteResult Overwrite(string existingText, string templateText)
        {
            var normalizedExisting = Normalize(existingText ?? string.Empty);
            var body = Normalize(templateText ?? string.Empty);
            return new GitIgnoreWriteResult
            {
                Created = normalizedExisting.Length == 0,
                Overwritten = normalizedExisting.Length > 0,
                AddedRules = CountRules(body),
                SkippedRules = 0,
                Result = body
            };
        }

        /// <summary>
        /// 落盘。overwrite=false 走合并（默认），true 走覆盖并先备份。
        /// 目录不存在时创建；写入 UTF-8 无 BOM + LF。
        /// </summary>
        public static GitIgnoreWriteResult Write(string targetPath, string templateText, bool overwrite)
        {
            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("targetPath is empty", nameof(targetPath));

            var existing = File.Exists(targetPath) ? File.ReadAllText(targetPath) : null;
            var plan = overwrite ? Overwrite(existing, templateText) : Merge(existing, templateText);

            if (plan.Overwritten)
            {
                var backup = targetPath + BackupSuffix;
                File.Copy(targetPath, backup, true);
                plan.BackupPath = backup;
            }

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(targetPath, plan.Result, new UTF8Encoding(false));
            return plan;
        }

        /// <summary>
        /// 按注释行/空行切块：注释行开启新块，空行结束当前块。规则行归属最近的块。
        /// 这样"整块追加"能带上模板自己的分节注释。
        /// </summary>
        private static List<List<string>> SplitBlocks(string templateText)
        {
            var blocks = new List<List<string>>();
            var current = new List<string>();
            if (string.IsNullOrEmpty(templateText)) return blocks;

            var lines = templateText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    if (current.Count > 0) { blocks.Add(current); current = new List<string>(); }
                    continue;
                }

                if (trimmed[0] == '#' && current.Count > 0)
                {
                    // 注释行：若当前块已有规则，则它是新块的标题
                    var hasRule = false;
                    foreach (var l in current) if (IsRule(l)) { hasRule = true; break; }
                    if (hasRule) { blocks.Add(current); current = new List<string>(); }
                }

                current.Add(line);
            }
            if (current.Count > 0) blocks.Add(current);
            return blocks;
        }
    }
}
