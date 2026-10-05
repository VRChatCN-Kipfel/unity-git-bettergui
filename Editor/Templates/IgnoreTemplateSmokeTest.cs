using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// ignore 模板链路冒烟（batchmode，无 UI）：
    ///   Unity -batchmode -nographics -projectPath X -executeMethod KF.GitUI.IgnoreTemplateSmokeTest.Run
    /// 覆盖：内置模板发现与元数据、块级合并语义（不重复已有规则）、幂等、覆盖 + 备份、
    ///       目录自动创建、来源优先级归并、导出为模板、i18n 键完备性（缺失/死键双向）。
    /// 失败即 EditorApplication.Exit(1)，便于 CI/本地无人值守。
    /// </summary>
    public static class IgnoreTemplateSmokeTest
    {
        private static int failures;

        public static void Run()
        {
            failures = 0;
            var tempRoot = Path.Combine(Path.GetTempPath(), "gitui-templates-smoke-" + Guid.NewGuid().ToString("N"));
            Debug.Log($"[templates-smoke] start temp={tempRoot}");

            try
            {
                Directory.CreateDirectory(tempRoot);
                CheckBuiltInLibrary();
                CheckMergeSemantics();
                CheckWriteAndBackup(tempRoot);
                CheckPriorityMerge();
                CheckExport(tempRoot);
                CheckI18nKeys();
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { /* 清理失败不影响结论 */ }
            }

            if (failures == 0)
            {
                Debug.Log("[templates-smoke] ALL OK");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[templates-smoke] {failures} FAILURE(S)");
                EditorApplication.Exit(1);
            }
        }

        // ---- 1. 内置模板发现 + 元数据 ----

        private static void CheckBuiltInLibrary()
        {
            var all = GitIgnoreTemplateLibrary.LoadAll();
            var builtInDir = GitIgnoreTemplateLibrary.BuiltInDirectory();
            var builtIn = all.Where(t => t.Source == GitIgnoreTemplateSource.BuiltIn).ToList();

            Expect(builtIn.Count >= 4,
                $"built-in templates >= 4 (got {builtIn.Count}) from {builtInDir}");
            Expect(Directory.Exists(builtInDir), "built-in directory exists: " + builtInDir);

            var std = builtIn.FirstOrDefault(t => t.Id == "unity-standard");
            Expect(std != null, "built-in template unity-standard exists");
            if (std != null)
            {
                Expect(std.DisplayName == "Unity Standard",
                    $"unity-standard display name from meta (got '{std.DisplayName}')");
                Expect(std.Order == 100, $"unity-standard order from meta (got {std.Order})");
                Expect(std.RuleCount > 0, $"unity-standard has rules (got {std.RuleCount})");
                Expect(std.Content.EndsWith("\n", StringComparison.Ordinal),
                    "template content ends with exactly one newline");
                Expect(!std.Content.Contains("\r"), "template content normalized to LF");
                Expect(std.Tags.Count > 0, "unity-standard has tags");
            }

            // 排序：Order 升序（同 Order 再按显示名）
            var orders = all.Select(t => t.Order).ToList();
            var sorted = new List<int>(orders);
            sorted.Sort();
            Expect(orders.SequenceEqual(sorted), "templates sorted by Order ascending");

            // 每个模板都有可读正文、非空 id，且首行为用途注释（贡献格式的第一条硬要求）
            foreach (var t in all)
            {
                if (!GitIgnoreTemplate.IsValidId(t.Id)) Fail("invalid template id: " + t.Id);
                if (t.RuleCount == 0) Fail("template without rules: " + t.Id);
                var firstLine = t.Content.Split('\n').FirstOrDefault(l => l.Trim().Length > 0);
                if (firstLine == null || !firstLine.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    Fail("template does not start with a purpose comment: " + t.Id);
            }
            Expect(all.All(t => !string.IsNullOrEmpty(t.DisplayName)), "every template has a display name");
        }

        // ---- 2. 合并语义（纯函数） ----

        private static void CheckMergeSemantics()
        {
            const string template = "# Header\n[Ll]ibrary/\n[Tt]emp/\n\n# IDE\n.vs/\n*.user\n";

            var created = GitIgnoreWriter.Merge(null, template);
            Expect(created.Created, "merge into missing file reports Created");
            Expect(created.AddedRules == 4, $"merge into missing file adds 4 rules (got {created.AddedRules})");
            Expect(created.Result == GitIgnoreWriter.Normalize(template),
                "merge into missing file == normalized template");

            // 已存在其中一条 → 只追加缺失行，注释分节保留，且绝不重复已存在的规则
            var partial = GitIgnoreWriter.Merge("[Ll]ibrary/\n# mine\nMyStuff/\n", template);
            Expect(partial.AddedRules == 3, $"partial merge adds 3 rules (got {partial.AddedRules})");
            Expect(partial.SkippedRules == 1, $"partial merge skips 1 existing rule (got {partial.SkippedRules})");
            Expect(partial.Result.StartsWith("[Ll]ibrary/\n# mine\nMyStuff/\n", StringComparison.Ordinal),
                "existing content preserved verbatim and kept first");
            Expect(partial.Result.Contains("# Header"), "appended block keeps its section comment");
            Expect(partial.Result.Contains("# IDE"), "appended block keeps its second section comment");
            Expect(CountOccurrences(partial.Result, "[Ll]ibrary/") == 1,
                "existing rule is not duplicated by the append");

            // 幂等：把结果再合一次不动
            var again = GitIgnoreWriter.Merge(partial.Result, template);
            Expect(again.AddedRules == 0 && again.NothingToAdd,
                $"merge is idempotent (added {again.AddedRules})");
            Expect(again.Result == GitIgnoreWriter.Normalize(partial.Result),
                "idempotent merge leaves text unchanged");

            // CRLF 输入也要归一化（Windows 上用户手写的 .gitignore 常见）
            var crlf = GitIgnoreWriter.Merge("Library/\r\nTemp/\r\n", template);
            Expect(!crlf.Result.Contains("\r"), "CRLF existing content normalized to LF");

            Expect(GitIgnoreWriter.CountRules(template) == 4, "CountRules counts rules only");
            Expect(!GitIgnoreWriter.IsRule("# comment") && !GitIgnoreWriter.IsRule("   ")
                && GitIgnoreWriter.IsRule("!keep.txt"), "IsRule semantics (comments/blank out, negation in)");
        }

        // ---- 3. 落盘：覆盖 + 备份、合并幂等、目录自动创建 ----

        private static void CheckWriteAndBackup(string tempRoot)
        {
            var target = Path.Combine(tempRoot, "overwrite", ".gitignore");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "# old\nOldStuff/\n");

            var replaced = GitIgnoreWriter.Write(target, "# New\n[Ll]ibrary/\n", true);
            Expect(replaced.Overwritten, "overwrite reports Overwritten");
            Expect(replaced.AddedRules == 1, $"overwrite counts template rules (got {replaced.AddedRules})");
            Expect(File.Exists(target + GitIgnoreWriter.BackupSuffix), "overwrite creates .gitignore.bak");
            Expect(File.ReadAllText(target + GitIgnoreWriter.BackupSuffix).Contains("OldStuff/"),
                "backup holds the previous content");
            Expect(File.ReadAllText(target) == GitIgnoreWriter.Normalize("# New\n[Ll]ibrary/\n"),
                "target replaced by template body");
            Expect(!File.ReadAllText(target).Contains("OldStuff/"), "replaced content is gone from target");

            // merge 模式：目录不存在时自动创建；二次写入为 no-op；不产生备份
            var nested = Path.Combine(tempRoot, "nested", "deeper", ".gitignore");
            var first = GitIgnoreWriter.Write(nested, "# A\n[Aa]/\n", false);
            Expect(first.Created, "merge creates the file when absent");
            Expect(File.Exists(nested), "missing directories are created on write");
            var second = GitIgnoreWriter.Write(nested, "# A\n[Aa]/\n", false);
            Expect(second.NothingToAdd, "second merge reports nothing to add");
            Expect(!File.Exists(nested + GitIgnoreWriter.BackupSuffix), "merge mode never creates a backup");
        }

        // ---- 4. 来源优先级归并 ----

        private static void CheckPriorityMerge()
        {
            var builtIn = new List<GitIgnoreTemplate>
            {
                new GitIgnoreTemplate { Id = "shared-id", NameEn = "Built", Source = GitIgnoreTemplateSource.BuiltIn, Order = 100 },
                new GitIgnoreTemplate { Id = "only-built", NameEn = "OnlyBuilt", Source = GitIgnoreTemplateSource.BuiltIn, Order = 110 }
            };
            var user = new List<GitIgnoreTemplate>
            {
                new GitIgnoreTemplate { Id = "shared-id", NameEn = "User", Source = GitIgnoreTemplateSource.UserLocal, Order = 1000 }
            };

            var merged = GitIgnoreTemplateLibrary.MergeById(builtIn, user);
            Expect(merged.Count == 2, $"merge keeps both distinct ids (got {merged.Count})");
            var shared = merged.FirstOrDefault(t => t.Id == "shared-id");
            Expect(shared != null && shared.NameEn == "User",
                "higher-priority layer overrides the same id");
            Expect(shared != null && shared.Source == GitIgnoreTemplateSource.UserLocal,
                "override carries the winning source");

            Expect(GitIgnoreTemplate.IsValidId("unity-standard"), "valid id accepted");
            Expect(GitIgnoreTemplate.IsValidId("studio-2d"), "valid id with digit accepted");
            Expect(!GitIgnoreTemplate.IsValidId("Unity Standard"), "spaces rejected");
            Expect(!GitIgnoreTemplate.IsValidId("a--b"), "double dash rejected");
            Expect(!GitIgnoreTemplate.IsValidId("-lead"), "leading dash rejected");
            Expect(!GitIgnoreTemplate.IsValidId(""), "empty id rejected");
            Expect(GitIgnoreTemplate.DeriveName("unity-standard") == "Unity Standard",
                "id derives a readable display name");
        }

        // ---- 5. 导出为模板（贡献者的捷径） ----

        private static void CheckExport(string tempRoot)
        {
            var projectDir = Path.Combine(tempRoot, "proj");
            var sharedDir = Path.Combine(tempRoot, "shared");
            Directory.CreateDirectory(projectDir);
            var projectGitIgnore = Path.Combine(projectDir, ".gitignore");
            File.WriteAllText(projectGitIgnore, "Library/\nTemp/\n"); // 首行不是注释，导出时应自动补用途注释

            string exported;
            string error;

            Expect(!GitIgnoreTemplateLibrary.ExportAsTemplate(projectGitIgnore, sharedDir, "Bad Id", out exported, out error),
                "invalid template id rejected on export");
            Expect(!string.IsNullOrEmpty(error), "rejected export returns a reason");

            Expect(GitIgnoreTemplateLibrary.ExportAsTemplate(projectGitIgnore, sharedDir, "my-studio", out exported, out error),
                "valid export succeeds (" + error + ")");
            Expect(File.Exists(exported), "exported template file exists: " + exported);
            if (File.Exists(exported))
            {
                var body = File.ReadAllText(exported);
                Expect(body.StartsWith("# ", StringComparison.Ordinal),
                    "exported template starts with a purpose comment");
                Expect(body.Contains("Library/") && body.Contains("Temp/"),
                    "exported template keeps the project rules");
                Expect(GitIgnoreTemplate.FromFile(exported, GitIgnoreTemplateSource.ProjectShared) != null,
                    "exported file round-trips through the loader");
            }

            Expect(!GitIgnoreTemplateLibrary.ExportAsTemplate(
                    Path.Combine(tempRoot, "does-not-exist", ".gitignore"), sharedDir, "nope", out exported, out error),
                "export without source .gitignore is rejected");

            // 导出物放到"项目共享目录"后，应能被库扫描到并参与归并
            var loaded = GitIgnoreTemplateLibrary.LoadFromDirectory(sharedDir, GitIgnoreTemplateSource.ProjectShared);
            Expect(loaded.Any(t => t.Id == "my-studio"), "exported template is discoverable by the library");
        }

        // ---- 6. i18n 键完备性（只针对本次新增的 ui.templates.* 命名空间） ----

        private static void CheckI18nKeys()
        {
            const string prefix = "ui.templates.";

            var declared = typeof(I18n.Keys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => f.GetRawConstantValue() as string)
                .Where(v => v != null && v.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct()
                .ToList();

            var inTable = I18n.All.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            var missing = declared.Except(inTable).ToList();
            var dead = inTable.Except(declared).ToList();

            Expect(declared.Count >= 25, $"template key set is non-trivial (got {declared.Count})");
            Expect(missing.Count == 0, "no declared key missing from the table (" + string.Join(", ", missing) + ")");
            Expect(dead.Count == 0, "no dead table entry without a declared key (" + string.Join(", ", dead) + ")");

            foreach (var key in declared)
                if (string.IsNullOrEmpty(I18n.L(key))) Fail("key resolves to empty text: " + key);
        }

        // ---- helpers ----

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static void Expect(bool condition, string what)
        {
            if (condition) Debug.Log("[templates-smoke] ok: " + what);
            else Fail(what);
        }

        private static void Fail(string what)
        {
            failures++;
            Debug.LogError("[templates-smoke] FAIL: " + what);
        }
    }
}
