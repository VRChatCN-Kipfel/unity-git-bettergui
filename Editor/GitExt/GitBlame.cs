using System.Collections.Generic;

namespace KF.GitUI
{
    /// <summary>blame 单行（porcelain 解析产物）。</summary>
    public sealed class BlameLine
    {
        /// <summary>归属提交短号（8 位；uncommitted = 全 0）。</summary>
        public string CommitShort;
        /// <summary>归属提交完整 hash。</summary>
        public string CommitId;
        /// <summary>作者名（uncommitted = "Not Committed Yet"）。</summary>
        public string Author;
        /// <summary>提交摘要（消息首行）。</summary>
        public string Summary;
        /// <summary>文件中的行号（1 基）。</summary>
        public int LineNumber;
        /// <summary>行内容（去 \t 前缀）。</summary>
        public string Content;
    }

    /// <summary>
    /// git blame --porcelain 输出解析器（M3-SOLUTION §1.1-11 实测格式）：
    /// 起始行 `sha orig final group`，后续 key-value（author / author-mail / summary / filename / previous…），
    /// 以 \t 开头的行 = 内容行（归属当前组）。commit id 0000… = 未提交（工作区改动）。
    /// </summary>
    public static class BlameParser
    {
        public static List<BlameLine> Parse(string output)
        {
            var result = new List<BlameLine>();
            if (string.IsNullOrEmpty(output)) return result;

            string curSha = null;
            string curAuthor = null;
            string curSummary = null;
            var curLineNo = 0;

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, System.StringSplitOptions.None))
            {
                if (rawLine.Length == 0) continue;
                if (rawLine[0] == '\t')
                {
                    // 内容行：归属当前组（组头 final-line 是首行号；组内多行逐行 +1）
                    if (curSha != null)
                    {
                        result.Add(new BlameLine
                        {
                            CommitId = curSha,
                            CommitShort = curSha.Length >= 8 ? curSha.Substring(0, 8) : curSha,
                            Author = curAuthor ?? string.Empty,
                            Summary = curSummary ?? string.Empty,
                            LineNumber = curLineNo++,
                            Content = rawLine.Substring(1),
                        });
                    }
                    continue;
                }

                var space = rawLine.IndexOf(' ');
                if (space < 0) continue;
                var key = rawLine.Substring(0, space);
                var value = rawLine.Substring(space + 1).Trim();

                if (key.Length == 40) // 提交 hash 起始行（组头）
                {
                    curSha = key;
                    curAuthor = null;
                    curSummary = null;
                    var parts = value.Split(' ');
                    // porcelain 组头：<sha> <orig-line> <final-line> <group-size>
                    // final-line（当前文件行号）为第 2 列；group-size 是第 4 列（可能省略）
                    // 实测：`0000... 1 1 3`（3 行一组，final=1）→ parts[1]；组内多行由内容行 curLineNo++ 递增
                    if (parts.Length >= 2)
                        int.TryParse(parts[1], out curLineNo);
                }
                else if (key == "author")
                    curAuthor = value;
                else if (key == "summary")
                    curSummary = value;
                // author-mail/author-time/filename/previous 等忽略
            }
            return result;
        }
    }
}