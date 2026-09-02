using System;
using System.Collections.Generic;
using System.Text;

namespace KF.GitUI
{
    /// <summary>DiffViewer 显示行类型。</summary>
    public enum DiffRowKind
    {
        FileHeader, // diff --git a/.. b/..（含 new/deleted file 提示）
        HunkHeader, // @@ -l,c +l,c @@
        Context,    // 上下文行
        Old,        // 删除行（行级红底 + 词级删除高亮）
        New,        // 新增行（行级绿底 + 词级新增高亮）
        Binary,     // Binary files ... differ
        Fold        // 折叠占位（大文件：N 行被折叠，点击展开）
    }

    /// <summary>DiffViewer 单显示行（纯数据，UI 层按 Kind/RichText 绑定样式）。</summary>
    public sealed class DiffRow
    {
        public DiffRowKind Kind;
        public string RichText;   // 已转义 + 词级 <mark>/<s> 标签（Context/Header 仅转义）
        public int OldLineNo = -1; // gutter 旧行号（Context/Old 有效）
        public int NewLineNo = -1; // gutter 新行号（Context/New 有效）
        public int HunkIndex = -1; // 所属 hunk 序号（HunkHeader 行标记自身，正文行标记所属）
        public int FileIndex = -1; // 所属文件在 diff 输出中的序号（hunk 操作切片定位）
        public string FilePath = string.Empty; // 所属文件（FileHeader 行标记自身）

        public override string ToString() => $"[{Kind}] {RichText}";
    }

    /// <summary>折叠行标记（大文件：仅头 + hunk 摘要；渲染为可点击展开占位）。</summary>
    public static class DiffFold
    {
        /// <summary>单文件显示行数阈值（超过则折叠；0/负 = 不折叠）。</summary>
        public const int MaxFileRows = 400;

        /// <summary>折叠提示文本：N 行被折叠（点击展开）。</summary>
        public static string FoldLabel(int hidden) => $"… {hidden} lines folded (click to expand) …";
    }

    /// <summary>
    /// DiffFile 列表 → DiffViewer 显示行序列（纯函数，零 UI，冒烟可断言）。
    /// unified 语义：行级背景（Old 红/New 绿）+ 词级高亮（配对 -/+ 行对跑 WordDiff）；
    /// 无对侧行或退化（null）时整行染色（全段套 mark）。
    /// HunkHeader 行保留 @@ 文本供导航；行号 gutter 数据齐备。
    /// 大文件折叠：文件正文行数（不含头）超过 DiffFold.MaxFileRows 时，只产出
    /// 文件头 + 各 hunk 摘要（@@ + 变更行统计）+ 折叠标记行（FoldKind）。
    /// </summary>
    public static class DiffRows
    {
        /// <summary>构建全部显示行（多文件顺序：文件头 → 各 hunk → 下一文件头）。</summary>
        public static List<DiffRow> Build(List<DiffFile> files)
        {
            return Build(files, DiffFold.MaxFileRows);
        }

        /// <summary>构建全部显示行，可指定折叠阈值。</summary>
        public static List<DiffRow> Build(List<DiffFile> files, int foldThreshold)
        {
            var rows = new List<DiffRow>();
            if (files == null) return rows;
            for (var i = 0; i < files.Count; i++)
                BuildFile(files[i], rows, foldThreshold, i);
            return rows;
        }

        /// <summary>构建单个文件的行。</summary>
        public static void BuildFile(DiffFile f, List<DiffRow> rows)
        {
            BuildFile(f, rows, DiffFold.MaxFileRows, -1);
        }

        /// <summary>构建单个文件的行，可指定折叠阈值。</summary>
        public static void BuildFile(DiffFile f, List<DiffRow> rows, int foldThreshold)
        {
            BuildFile(f, rows, foldThreshold, -1);
        }

        /// <summary>构建单个文件的行，可指定折叠阈值与文件序号（hunk 操作定位）。</summary>
        public static void BuildFile(DiffFile f, List<DiffRow> rows, int foldThreshold, int fileIndex)
        {
            if (f == null) return;

            // 文件头行
            var header = "diff --git "
                + (f.IsNew ? "/dev/null" : "a/" + f.OldPath) + " "
                + (f.IsDeleted ? "/dev/null" : "b/" + f.NewPath);
            if (f.IsNew) header += "  (new file)";
            else if (f.IsDeleted) header += "  (deleted file)";
            rows.Add(new DiffRow
            {
                Kind = DiffRowKind.FileHeader,
                RichText = DiffRichText.BuildPlainLine(header),
                FilePath = f.IsNew ? f.NewPath : f.OldPath,
                FileIndex = fileIndex
            });

            if (f.IsBinary)
            {
                rows.Add(new DiffRow { Kind = DiffRowKind.Binary, RichText = "Binary files differ" });
                return;
            }

            // 大文件折叠：只统计各 hunk 变更规模，产出摘要行
            var totalBody = 0;
            foreach (var h in f.Hunks) totalBody += h.Lines.Count;
            var fold = foldThreshold > 0 && totalBody > foldThreshold;

            var hunkIndex = 0;
            foreach (var h in f.Hunks)
            {
                rows.Add(new DiffRow
                {
                    Kind = DiffRowKind.HunkHeader,
                    RichText = DiffRichText.BuildPlainLine(BuildHunkHeader(h)),
                    OldLineNo = h.OldStart,
                    NewLineNo = h.NewStart,
                    HunkIndex = hunkIndex,
                    FileIndex = fileIndex
                });

                if (fold)
                {
                    var changes = 0;
                    foreach (var l in h.Lines) if (l.IsChange) changes++;
                    rows.Add(new DiffRow
                    {
                        Kind = DiffRowKind.Fold,
                        RichText = DiffRichText.BuildPlainLine(
                            $"[{h.Lines.Count - changes} ctx / {changes} changes — " + DiffFold.FoldLabel(h.Lines.Count) + "]"),
                        HunkIndex = hunkIndex,
                        FileIndex = fileIndex
                    });
                    hunkIndex++;
                    continue;
                }

                // -/+ 行配对（顺序对齐；纯删/纯增无对侧→整行染色）
                // 注意：Old/New 行各自独立计数（HunkLinePairing.Pair 保证 old/new 列表顺序一致，
                // 且两侧同序对应对侧行；共享计数会越界/错位）
                var pairs = HunkLinePairing.Pair(h);
                var oldPairIdx = 0;
                var newPairIdx = 0;
                foreach (var line in h.Lines)
                {
                    switch (line.Kind)
                    {
                        case DiffLineKind.Context:
                            rows.Add(new DiffRow
                            {
                                Kind = DiffRowKind.Context,
                                RichText = DiffRichText.BuildPlainLine(line.Content),
                                OldLineNo = line.LineNumber,
                                NewLineNo = line.LineNumber,
                                HunkIndex = hunkIndex,
                                FileIndex = fileIndex
                            });
                            break;
                        case DiffLineKind.Old:
                        {
                            var pair = (oldPairIdx < pairs.Count) ? pairs[oldPairIdx] : null;
                            oldPairIdx++;
                            string rich;
                            if (pair != null && pair.New != null)
                            {
                                var wr = WordDiff.Compare(pair.Old.Content, pair.New.Content);
                                rich = wr != null
                                    ? DiffRichText.BuildDeletedLine(wr.OldFragments, pair.Old.Content)
                                    : DiffRichText.WrapDeleted(line.Content);
                            }
                            else
                                rich = DiffRichText.WrapDeleted(line.Content);
                            rows.Add(new DiffRow
                            {
                                Kind = DiffRowKind.Old, RichText = rich,
                                OldLineNo = line.LineNumber, HunkIndex = hunkIndex, FileIndex = fileIndex
                            });
                            break;
                        }
                        case DiffLineKind.New:
                        {
                            var pair = (newPairIdx < pairs.Count) ? pairs[newPairIdx] : null;
                            newPairIdx++;
                            string rich;
                            if (pair != null && pair.Old != null)
                            {
                                var wr = WordDiff.Compare(pair.Old.Content, pair.New.Content);
                                rich = wr != null
                                    ? DiffRichText.BuildAddedLine(wr.NewFragments, pair.New.Content)
                                    : DiffRichText.WrapAdded(line.Content);
                            }
                            else
                                rich = DiffRichText.WrapAdded(line.Content);
                            rows.Add(new DiffRow
                            {
                                Kind = DiffRowKind.New, RichText = rich,
                                NewLineNo = line.LineNumber, HunkIndex = hunkIndex, FileIndex = fileIndex
                            });
                            break;
                        }
                    }
                }
                hunkIndex++;
            }
        }

        private static string BuildHunkHeader(DiffHunk h)
        {
            return $"@@ -{h.OldStart},{h.OldCount} +{h.NewStart},{h.NewCount} @@";
        }
    }
}