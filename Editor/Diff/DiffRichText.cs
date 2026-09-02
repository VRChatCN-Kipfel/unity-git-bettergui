using System.Collections.Generic;
using System.Text;

namespace KF.GitUI
{
    /// <summary>
    /// diff 词级高亮 → UITK rich text 字符串（2022.3 TextElement.enableRichText，TextCore 标签体系）。
    /// 约定（M3-SOLUTION §3.2）：
    ///   · 删除段：&lt;mark=#FF6B6B55&gt;&lt;s&gt;…&lt;/s&gt;&lt;/mark&gt;（红底 + 删除线）
    ///   · 新增段：&lt;mark=#6BCB7755&gt;…&lt;/mark&gt;（绿底；可加 &lt;color=#1F6E43&gt; 深绿前景）
    ///   · 等宽字体由调用方在行元素 style 上设置（-unity-font），本类不涉及。
    /// 文本必须先转义（&amp; &lt; &gt;），否则 rich text 解析器会把用户内容当标签/实体（安全 + 正确性）。
    /// 注意：UITK rich text 转义用 HTML 实体（&amp;lt; 不用——直接 &amp; &lt; &gt; 三件套即可，与 TMP 语法一致）。
    /// </summary>
    public static class DiffRichText
    {
        // 语义色（编辑器暗/亮主题下视觉可辨；与 GraphTable 色板风格一致）
        public const string AddedBackground = "#6BCB7755"; // 半透明绿
        public const string AddedForeground = "#1F6E43";   // 深绿
        public const string DeletedBackground = "#FF6B6B55"; // 半透明红
        public const string DeletedForeground = "#B95050";   // 深红

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

        /// <summary>整条旧行（删除侧）→ rich text：按片段拼装，Deleted 段套 红底+删除线。</summary>
        public static string BuildDeletedLine(List<DiffFragment> oldFragments, string oldLine)
        {
            var sb = new StringBuilder(oldLine.Length + 64);
            foreach (var f in oldFragments)
            {
                var seg = Escape(oldLine.Substring(f.OldStart, f.OldLength));
                if (f.Kind == DiffFragmentKind.Deleted)
                    sb.Append("<mark=").Append(DeletedBackground).Append("><s><color=")
                      .Append(DeletedForeground).Append('>').Append(seg)
                      .Append("</color></s></mark>");
                else
                    sb.Append(seg);
            }
            return sb.ToString();
        }

        /// <summary>整条新行（新增侧）→ rich text：Added 段套 绿底+深绿前景。</summary>
        public static string BuildAddedLine(List<DiffFragment> newFragments, string newLine)
        {
            var sb = new StringBuilder(newLine.Length + 64);
            foreach (var f in newFragments)
            {
                var seg = Escape(newLine.Substring(f.NewStart, f.NewLength));
                if (f.Kind == DiffFragmentKind.Added)
                    sb.Append("<mark=").Append(AddedBackground).Append("><color=")
                      .Append(AddedForeground).Append('>').Append(seg)
                      .Append("</color></mark>");
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
    }
}