using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// 模板来源；决定覆盖优先级（UserLocal ＞ ProjectShared ＞ BuiltIn，同名 id 高优先级覆盖低优先级）。
    /// </summary>
    public enum GitIgnoreTemplateSource
    {
        BuiltIn,        // 包内 Editor/Templates/BuiltIn（只读，随包升级）
        ProjectShared,  // 项目内 .gitui-ignore-templates（可提交，团队共享）
        UserLocal       // UserSettings/GitBetterGui/IgnoreTemplates（不入版本控制，个人）
    }

    /// <summary>
    /// &lt;id&gt;.meta.json 的映射结构（字段全部可选；缺失即用默认）。
    /// JsonUtility 只能反序列化 public 字段的 [Serializable] 类型，故不用 Dictionary。
    /// </summary>
    [Serializable]
    internal sealed class GitIgnoreTemplateMeta
    {
        public GitIgnoreLocalizedText name;
        public GitIgnoreLocalizedText description;
        public string[] tags;
        public int order;
    }

    /// <summary>元数据里的双语文本；任一侧可缺。</summary>
    [Serializable]
    internal sealed class GitIgnoreLocalizedText
    {
        public string zh;
        public string en;

        /// <summary>
        /// 取显示用文案。当前 UI 只有英文键表（zh-CN bundle 是 M4 本地化里程碑的交付项），
        /// 故此处固定优先 en；M4 接入语言切换后改为按当前 UI 语言优先。
        /// </summary>
        public string Pick()
        {
            if (!string.IsNullOrEmpty(en)) return en.Trim();
            if (!string.IsNullOrEmpty(zh)) return zh.Trim();
            return null;
        }
    }

    /// <summary>
    /// 一个可写入的 .gitignore 模板：正文 + 可选元数据 + 来源。
    /// 文件约定（贡献者只需遵守这一条）：<c>Editor/Templates/BuiltIn/&lt;id&gt;.gitignore</c>
    /// 为正文（必需），<c>&lt;id&gt;.meta.json</c> 为元数据（可选）。
    /// </summary>
    public sealed class GitIgnoreTemplate
    {
        /// <summary>模板 id：文件名去掉 <c>.gitignore</c> 后缀，小写 kebab-case。</summary>
        public string Id;

        public string NameZh;
        public string NameEn;
        public string DescriptionZh;
        public string DescriptionEn;
        public List<string> Tags = new List<string>();

        /// <summary>排序权重：越小越靠前（内置模板用 100/110/120…；自定义默认 1000）。</summary>
        public int Order;

        /// <summary>模板正文（已归一化为 LF 换行，末尾恰好一个换行）。</summary>
        public string Content;

        public GitIgnoreTemplateSource Source;

        /// <summary>正文文件的物理路径（供"打开所在目录"与诊断用）。</summary>
        public string FilePath;

        /// <summary>显示名：元数据 en ＞ zh ＞ 由 id 派生（unity-standard → Unity Standard）。</summary>
        public string DisplayName
        {
            get
            {
                var picked = (NameEn ?? string.Empty).Trim();
                if (picked.Length == 0) picked = (NameZh ?? string.Empty).Trim();
                return picked.Length > 0 ? picked : DeriveName(Id);
            }
        }

        /// <summary>描述（可能为空串）。</summary>
        public string Description
        {
            get
            {
                var picked = (DescriptionEn ?? string.Empty).Trim();
                if (picked.Length == 0) picked = (DescriptionZh ?? string.Empty).Trim();
                return picked;
            }
        }

        /// <summary>来源标签（UI 显示，走 I18n）。</summary>
        public string SourceLabel
        {
            get
            {
                switch (Source)
                {
                    case GitIgnoreTemplateSource.ProjectShared: return I18n.L(I18n.Keys.IgnoreTemplatesSourceProject);
                    case GitIgnoreTemplateSource.UserLocal: return I18n.L(I18n.Keys.IgnoreTemplatesSourceUser);
                    default: return I18n.L(I18n.Keys.IgnoreTemplatesSourceBuiltIn);
                }
            }
        }

        /// <summary>正文里的忽略规则行数（注释与空行不计）。</summary>
        public int RuleCount
        {
            get { return GitIgnoreWriter.CountRules(Content); }
        }

        /// <summary>由 id 派生显示名：<c>unity-standard</c> → <c>Unity Standard</c>。</summary>
        public static string DeriveName(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var parts = id.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                parts[i] = p.Length == 1
                    ? p.ToUpperInvariant()
                    : char.ToUpperInvariant(p[0]) + p.Substring(1);
            }
            return string.Join(" ", parts);
        }

        /// <summary>模板 id 是否合法（小写 kebab-case，供导出与 PR 校验复用）。</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id[0] == '-' || id[id.Length - 1] == '-') return false;
            var prevDash = false;
            foreach (var c in id)
            {
                if (c == '-')
                {
                    if (prevDash) return false;
                    prevDash = true;
                    continue;
                }
                prevDash = false;
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
            }
            return true;
        }

        /// <summary>
        /// 从 <c>&lt;id&gt;.gitignore</c> 读入模板；同目录的 <c>&lt;id&gt;.meta.json</c> 存在则解析元数据。
        /// 元数据损坏不致命：回退到 id 派生名，模板仍可用（一个坏文件不该让整库不可用）。
        /// </summary>
        public static GitIgnoreTemplate FromFile(string path, GitIgnoreTemplateSource source)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var id = Path.GetFileNameWithoutExtension(path);
            // 防御：`*.gitignore` 在 Windows 通配符语义下会匹配 `.gitignore` 本身（无基名），跳过之。
            if (string.IsNullOrEmpty(id) || id[0] == '.') return null;

            var t = new GitIgnoreTemplate
            {
                Id = id,
                Source = source,
                FilePath = path,
                Order = 1000,
                Content = GitIgnoreWriter.Normalize(File.ReadAllText(path))
            };

            var metaPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, id + ".meta.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var meta = JsonUtility.FromJson<GitIgnoreTemplateMeta>(File.ReadAllText(metaPath));
                    if (meta != null)
                    {
                        if (meta.name != null) { t.NameZh = meta.name.zh; t.NameEn = meta.name.en; }
                        if (meta.description != null) { t.DescriptionZh = meta.description.zh; t.DescriptionEn = meta.description.en; }
                        if (meta.tags != null) t.Tags = new List<string>(meta.tags);
                        if (meta.order != 0) t.Order = meta.order;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[gitui] ignore template meta unreadable ({metaPath}): {ex.Message}");
                }
            }

            return t;
        }
    }
}
