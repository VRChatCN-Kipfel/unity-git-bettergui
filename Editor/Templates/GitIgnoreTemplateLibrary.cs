using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// ignore 模板库：从三个目录发现模板并按优先级归并。
    ///
    /// 发现约定（贡献者只需关心第一条）：
    ///   1. 包内 <c>Editor/Templates/BuiltIn/&lt;id&gt;.gitignore</c>（+ 可选 <c>&lt;id&gt;.meta.json</c>）—— 内置，随包升级
    ///   2. 项目内 <c>.gitui-ignore-templates/</c> —— 可随仓库提交，团队共享
    ///   3. 用户本地 <c>UserSettings/GitBetterGui/IgnoreTemplates/</c> —— 不入版本控制，个人试验
    ///
    /// 优先级：用户本地 ＞ 项目共享 ＞ 内置（同名 id 高优先级整份覆盖低优先级）。
    /// 目录缺失是正常状态（不是错误）：扫不到就是空列表。
    /// </summary>
    public static class GitIgnoreTemplateLibrary
    {
        public const string PackageName = "com.kf.gitui";
        public const string BuiltInRelativeDir = "Editor/Templates/BuiltIn";
        public const string ProjectSharedDirName = ".gitui-ignore-templates";
        public const string UserLocalRelativeDir = "UserSettings/GitBetterGui/IgnoreTemplates";
        public const string GitIgnoreFileName = ".gitignore";

        /// <summary>Unity 工程根（Assets 的父目录）；取不到时退回当前工作目录。</summary>
        public static string ProjectRoot()
        {
            try
            {
                var data = Application.dataPath;
                if (!string.IsNullOrEmpty(data))
                {
                    var parent = Path.GetDirectoryName(data);
                    if (!string.IsNullOrEmpty(parent)) return parent;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[gitui] project root probe failed: " + ex.Message);
            }
            return Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// 包内内置模板目录。优先用 PackageManager 解析本程序集所属包的真实路径
        /// （file:/git/registry 安装形式都适用）；失败则退回 Unity 的 Packages/&lt;name&gt; 虚拟路径。
        /// </summary>
        public static string BuiltInDirectory()
        {
            try
            {
                // 完全限定：UnityEditor 里另有一个同名（已废弃）的 PackageInfo，using 会歧义 CS0104
                var pkg = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(typeof(GitIgnoreTemplateLibrary).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                    return Path.Combine(pkg.resolvedPath, ToNative(BuiltInRelativeDir));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[gitui] package path probe failed: " + ex.Message);
            }
            return Path.GetFullPath(Path.Combine("Packages", PackageName, ToNative(BuiltInRelativeDir)));
        }

        /// <summary>项目内共享模板目录（&lt;工程根&gt;/.gitui-ignore-templates）。</summary>
        public static string ProjectSharedDirectory()
        {
            return Path.Combine(ProjectRoot(), ProjectSharedDirName);
        }

        /// <summary>用户本地模板目录（&lt;工程根&gt;/UserSettings/GitBetterGui/IgnoreTemplates）。</summary>
        public static string UserLocalDirectory()
        {
            return Path.Combine(ProjectRoot(), ToNative(UserLocalRelativeDir));
        }

        /// <summary>项目根 .gitignore 的完整路径。</summary>
        public static string GitIgnorePath()
        {
            return Path.Combine(ProjectRoot(), GitIgnoreFileName);
        }

        /// <summary>全部可用模板：三来源归并 + 排序（Order 升序 → 显示名 → id）。</summary>
        public static List<GitIgnoreTemplate> LoadAll()
        {
            var merged = MergeById(
                LoadFromDirectory(BuiltInDirectory(), GitIgnoreTemplateSource.BuiltIn),
                LoadFromDirectory(ProjectSharedDirectory(), GitIgnoreTemplateSource.ProjectShared),
                LoadFromDirectory(UserLocalDirectory(), GitIgnoreTemplateSource.UserLocal));

            merged.Sort((a, b) =>
            {
                var byOrder = a.Order.CompareTo(b.Order);
                if (byOrder != 0) return byOrder;
                var byName = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                if (byName != 0) return byName;
                return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });
            return merged;
        }

        /// <summary>扫描单个目录；目录不存在或读失败时返回空列表（不抛）。</summary>
        public static List<GitIgnoreTemplate> LoadFromDirectory(string dir, GitIgnoreTemplateSource source)
        {
            var list = new List<GitIgnoreTemplate>();
            if (string.IsNullOrEmpty(dir)) return list;
            try
            {
                if (!Directory.Exists(dir)) return list;
                foreach (var path in Directory.GetFiles(dir, "*" + GitIgnoreFileName))
                {
                    var template = GitIgnoreTemplate.FromFile(path, source);
                    if (template != null) list.Add(template);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[gitui] ignore template scan failed ({dir}): {ex.Message}");
            }
            return list;
        }

        /// <summary>按 id 归并多层（后者覆盖前者），保持首次出现顺序。</summary>
        public static List<GitIgnoreTemplate> MergeById(params List<GitIgnoreTemplate>[] layers)
        {
            var order = new List<string>();
            var byId = new Dictionary<string, GitIgnoreTemplate>(StringComparer.Ordinal);
            if (layers != null)
            {
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    foreach (var t in layer)
                    {
                        if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                        if (!byId.ContainsKey(t.Id)) order.Add(t.Id);
                        byId[t.Id] = t; // 后层覆盖前层
                    }
                }
            }

            var result = new List<GitIgnoreTemplate>(order.Count);
            foreach (var id in order) result.Add(byId[id]);
            return result;
        }

        /// <summary>
        /// 把当前项目的 .gitignore 导出成"项目共享模板"，供提 PR 或团队内复用。
        /// 符合模板格式要求：首行必为用途注释（缺则自动补一行，不改动原有内容）。
        /// </summary>
        public static bool ExportProjectGitIgnore(string id, out string exportedPath, out string error)
        {
            return ExportAsTemplate(GitIgnorePath(), ProjectSharedDirectory(), id, out exportedPath, out error);
        }

        /// <summary>
        /// 导出为模板的纯路径版本（源 .gitignore + 目标目录均由调用方给出，便于冒烟与自定义落点）。
        /// </summary>
        public static bool ExportAsTemplate(string sourceGitIgnorePath, string targetDirectory,
            string id, out string exportedPath, out string error)
        {
            exportedPath = null;
            error = null;

            if (!GitIgnoreTemplate.IsValidId(id))
            {
                error = I18n.L(I18n.Keys.IgnoreTemplatesExportInvalid);
                return false;
            }

            if (string.IsNullOrEmpty(sourceGitIgnorePath) || !File.Exists(sourceGitIgnorePath))
            {
                error = I18n.L(I18n.Keys.IgnoreTemplatesExportEmpty);
                return false;
            }

            try
            {
                var body = GitIgnoreWriter.Normalize(File.ReadAllText(sourceGitIgnorePath));
                if (body.Length == 0)
                {
                    error = I18n.L(I18n.Keys.IgnoreTemplatesExportEmpty);
                    return false;
                }

                // 模板格式要求首行为用途注释；项目 .gitignore 通常没有，导出时补一行（不改动原有内容）。
                var firstLine = body.Split('\n')[0].TrimStart();
                if (firstLine.Length == 0 || firstLine[0] != '#')
                    body = "# " + GitIgnoreTemplate.DeriveName(id) + " — exported from a project .gitignore\n" + body;

                Directory.CreateDirectory(targetDirectory);
                var target = Path.Combine(targetDirectory, id + GitIgnoreFileName);
                File.WriteAllText(target, body, new UTF8Encoding(false));
                exportedPath = target;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ToNative(string relative)
        {
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
