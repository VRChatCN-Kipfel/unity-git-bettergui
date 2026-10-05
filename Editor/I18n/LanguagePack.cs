using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KF.GitUI
{
    /// <summary>语言包来源；决定键级合并的优先级（UserLocal ＞ ProjectShared ＞ BuiltIn）。</summary>
    public enum LanguagePackSource
    {
        BuiltIn,        // 包内 Editor/I18n/BuiltIn（随包升级，只读）
        ProjectShared,  // 项目内 .gitui-i18n（可提交，团队统一术语）
        UserLocal       // UserSettings/GitBetterGui/I18n（不入版本控制，个人试验）
    }

    /// <summary>&lt;lang&gt;.meta.json 的映射结构（字段全部可选）。</summary>
    [Serializable]
    internal sealed class LanguagePackMeta
    {
        public string nativeName;
        public string[] maintainers;
        public int order;
    }

    /// <summary>
    /// 一门语言的语言包：官方基线（英文表）+ 本包提供的译文。
    ///
    /// 与 ignore 模板的关键差异（刻意的）：
    ///   1. 语言包是**键级**单元，所以合并也是键级的（个人只想改几条术语时不必复制整份）；
    ///   2. 译文带格式占位符，占位符不一致的条目会被**丢弃并回退英文**（不信任输入，
    ///      否则 <see cref="I18n.L(string, object[])"/> 会抛 FormatException）；
    ///   3. 允许部分翻译，未提供的键自动回退英文，界面显示覆盖率。
    /// </summary>
    public sealed class LanguagePack
    {
        /// <summary>BCP-47 风格语言代码（= 文件名去掉 .json）。</summary>
        public string Lang;

        /// <summary>该语言的自称（来自 meta；缺省回退到语言代码）。</summary>
        public string NativeName;

        public List<string> Maintainers = new List<string>();

        /// <summary>语言下拉里的排序权重，越小越靠前（缺省 1000）。</summary>
        public int Order = 1000;

        public LanguagePackSource Source;

        /// <summary>最高优先级层的文件路径（诊断/定位用）。</summary>
        public string FilePath;

        /// <summary>校验通过、且键在英文表里存在的条目。</summary>
        public SortedDictionary<string, string> Entries =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>英文表里没有的键（忽略并告警；旧语言包不会污染新版本）。</summary>
        public List<string> UnknownKeys = new List<string>();

        /// <summary>占位符与英文不一致而被丢弃的键（这些键会回退英文）。</summary>
        public List<string> InvalidKeys = new List<string>();

        /// <summary>整个文件级错误（解析失败 / 语言代码非法）；非空即表示本层未被采用。</summary>
        public string Error;

        /// <summary>已翻译键数（= 覆盖率分子）。</summary>
        public int Coverage
        {
            get { return Entries.Count; }
        }

        /// <summary>语言选择界面显示的标签：<c>简体中文 (zh-CN)</c>。</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(NativeName) || NativeName == Lang) return Lang;
                return NativeName + " (" + Lang + ")";
            }
        }

        /// <summary>覆盖率文本：<c>181/181</c>。</summary>
        public string CoverageText
        {
            get { return Coverage + "/" + I18n.KeyCount; }
        }

        // ---- 占位符 ----

        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{(\d+)(?::([^}]*))?\}", RegexOptions.Compiled);

        /// <summary>
        /// 提取格式占位符：索引 → 格式说明符（无说明符为空串）。
        /// 例：<c>"{0} of {1:N0}"</c> → {0:"", 1:"N0"}。
        /// </summary>
        public static SortedDictionary<int, string> ExtractPlaceholders(string text)
        {
            var result = new SortedDictionary<int, string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (Match m in PlaceholderPattern.Matches(text))
            {
                var index = int.Parse(m.Groups[1].Value);
                var spec = m.Groups[2].Success ? m.Groups[2].Value : string.Empty;
                result[index] = spec;
            }
            return result;
        }

        /// <summary>
        /// 译文是否可以安全用于格式化：占位符**索引集合**与**每个索引的格式说明符**必须与英文一致。
        /// 顺序不作要求——目标语言可以自由调整语序。
        /// </summary>
        public static bool PlaceholdersMatch(string english, string translation)
        {
            var expected = ExtractPlaceholders(english);
            var actual = ExtractPlaceholders(translation);
            if (expected.Count != actual.Count) return false;
            foreach (var kv in expected)
            {
                string spec;
                if (!actual.TryGetValue(kv.Key, out spec)) return false;
                if (!string.Equals(spec ?? string.Empty, kv.Value ?? string.Empty, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}
