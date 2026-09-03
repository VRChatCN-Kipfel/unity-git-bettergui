using System.Collections.Generic;
using System.Text;

namespace KF.GitUI
{
    /// <summary>
    /// diff 词级高亮 → UITK rich text 字符串（2022.3 TextElement.enableRichText，TextCore 标签体系）。
    /// 约定（M3-SOLUTION §3.2 修订版，2026-10 用户反馈）：
    ///   · 删除段：&lt;mark=#FF6B6B55&gt;…&lt;/mark&gt;（仅红底，无删除线、不改字色——JetBrains 直觉）
    ///   · 新增段：&lt;mark=#6BCB7755&gt;…&lt;/mark&gt;（仅绿底，不改字色）
    ///   · 行首 `-`/`+` 符号由 DiffViewer 渲染层加（本类只管行内高亮）
    /// 文本必须先转义（&amp; &lt; &gt;），否则 rich text 解析器会把用户内容当标签/实体（安全 + 正确性）。
    /// </summary>
    public static class DiffRichText
    {
        // 语义色（仅背景，字色留给主题——用户反馈"红底红字+绿底绿字"观感差）
        public const string AddedBackground = "#6BCB7755";   // 半透明绿
        public const string DeletedBackground = "#FF6B6B55"; // 半透明红

        /// <summary>转义用户文本中的 & / &lt; / &gt;（rich text 上下文安全）。空串直返。</summary>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length + 8);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>整条旧行（删除侧）→ rich text：按片段拼装，Deleted 段仅套红底。</summary>
        public static string BuildDeletedLine(List<DiffFragment> oldFragments, string oldLine)
        {
            var sb = new StringBuilder(oldLine.Length + 64);
            foreach (var f in oldFragments)
            {
                var seg = Escape(oldLine.Substring(f.OldStart, f.OldLength));
                if (f.Kind == DiffFragmentKind.Deleted)
                    sb.Append("<mark=").Append(DeletedBackground).Append('>').Append(seg).Append("</mark>");
                else
                    sb.Append(seg);
            }
            return sb.ToString();
        }

        /// <summary>整条新行（新增侧）→ rich text：Added 段仅套绿底。</summary>
        public static string BuildAddedLine(List<DiffFragment> newFragments, string newLine)
        {
            var sb = new StringBuilder(newLine.Length + 64);
            foreach (var f in newFragments)
            {
                var seg = Escape(newLine.Substring(f.NewStart, f.NewLength));
                if (f.Kind == DiffFragmentKind.Added)
                    sb.Append("<mark=").Append(AddedBackground).Append('>').Append(seg).Append("</mark>");
                else
                    sb.Append(seg);
            }
            return sb.ToString();
        }

        /// <summary>上下文/纯行 → rich text（仅转义，不套标记）。</summary>
        public static string BuildPlainLine(string text)
        {
            return Escape(text);
        }

        /// <summary>整行删除染色（无配对行或词级退化时使用）：全段红底。</summary>
        public static string WrapDeleted(string text)
        {
            return "<mark=" + DeletedBackground + ">" + Escape(text) + "</mark>";
        }

        /// <summary>整行新增染色（无配对行或词级退化时使用）：全段绿底。</summary>
        public static string WrapAdded(string text)
        {
            return "<mark=" + AddedBackground + ">" + Escape(text) + "</mark>";
        }
    }
}