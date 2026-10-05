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
    /// 多语言链路冒烟（batchmode，无 UI）：
    ///   Unity -batchmode -nographics -projectPath X -executeMethod KF.GitUI.I18nSmokeTest.Run
    /// 覆盖：键表一致性、扁平 JSON 解析（含拒绝非法输入）、占位符校验、语言包解析与诊断、
    ///       三来源键级归并、语言代码校验、ApplyLanguage（部分翻译回退 / 无残留 / 找不到包）、
    ///       骨架导出与"已存在不覆盖"、ui.language.* 键双向完备性。
    /// 失败即 EditorApplication.Exit(1)。
    /// </summary>
    public static class I18nSmokeTest
    {
        private static int failures;

        public static void Run()
        {
            failures = 0;
            var tempRoot = Path.Combine(Path.GetTempPath(), "gitui-i18n-smoke-" + Guid.NewGuid().ToString("N"));
            var projectPack = Path.Combine(LanguagePackLibrary.ProjectSharedDirectory(), "zz-smoke.json");

            Debug.Log($"[i18n-smoke] start temp={tempRoot} keys={I18n.KeyCount}");

            try
            {
                Directory.CreateDirectory(tempRoot);
                CheckKeyTables();
                CheckFlatJson();
                CheckPlaceholders();
                CheckLangCodes();
                CheckParseFile(tempRoot);
                CheckMergeLayers();
                CheckApplyLanguage(projectPack);
                CheckExportSkeleton(tempRoot);
                CheckBuiltInPacks();
                CheckLanguageKeys();
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex);
            }
            finally
            {
                I18n.ApplyLanguage(null);                       // 恢复英文基线
                try { if (File.Exists(projectPack)) File.Delete(projectPack); } catch { }
                try { Directory.Delete(tempRoot, true); } catch { }
            }

            if (failures == 0)
            {
                Debug.Log("[i18n-smoke] ALL OK");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[i18n-smoke] {failures} FAILURE(S)");
                EditorApplication.Exit(1);
            }
        }

        // ---- 1. 键表自身的一致性 ----

        private static void CheckKeyTables()
        {
            var constants = typeof(I18n.Keys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => f.GetRawConstantValue() as string)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();

            Expect(I18n.KeyCount == constants.Count,
                $"AllKeys matches key constants ({I18n.KeyCount} vs {constants.Count})");
            Expect(I18n.All.Count == I18n.KeyCount,
                $"english table size matches AllKeys ({I18n.All.Count} vs {I18n.KeyCount})");
            Expect(constants.All(k => I18n.AllKeys.Contains(k)),
                "every declared key constant is present in AllKeys");
            Expect(I18n.AllKeys.All(k => !string.IsNullOrEmpty(I18n.EnglishText(k))),
                "every key has an English baseline text");

            // 英文表自身的占位符索引必须连续（0..n-1）：跳号说明模板串写错了，
            // 而语言包的占位符校验是以英文为基准的，基准错了后面全错。
            foreach (var key in I18n.AllKeys)
            {
                var placeholders = LanguagePack.ExtractPlaceholders(I18n.EnglishText(key));
                var expected = 0;
                foreach (var index in placeholders.Keys)
                {
                    if (index != expected)
                    {
                        Fail($"key {key} has a non-contiguous placeholder index {{{index}}} (expected {{{expected}}})");
                        break;
                    }
                    expected++;
                }
            }
        }

        // ---- 2. 扁平 JSON ----

        private static void CheckFlatJson()
        {
            var simple = FlatJson.Parse("{ \"a\": \"b\", \"c\": \"d\" }");
            Expect(simple.Count == 2 && simple["a"] == "b" && simple["c"] == "d", "parses a flat map");

            Expect(FlatJson.Parse("{}").Count == 0, "parses an empty object");
            Expect(FlatJson.Parse("{\n  \"k\" : \"v\"\n}\n").Count == 1, "tolerates whitespace and newlines");

            var escaped = FlatJson.Parse("{\"a\":\"line\\nbreak \\\"q\\\" back\\\\slash \\u0041\"}");
            Expect(escaped["a"] == "line\nbreak \"q\" back\\slash A", "decodes escapes (got: " + escaped["a"] + ")");

            var multi = FlatJson.Parse("{\"a\":\"一\",\"b\":\"二\"}");
            Expect(multi["a"] == "一" && multi["b"] == "二", "keeps non-ASCII text");

            ExpectThrows(() => FlatJson.Parse("{\"a\":\"b\",}"), "rejects a trailing comma");
            ExpectThrows(() => FlatJson.Parse("{\"a\":\"b\",\"a\":\"c\"}"), "rejects a duplicate key");
            ExpectThrows(() => FlatJson.Parse("{\"a\":1}"), "rejects a non-string value");
            ExpectThrows(() => FlatJson.Parse("{\"a\":\"b\"} trailing"), "rejects trailing content");
            ExpectThrows(() => FlatJson.Parse("[1,2]"), "rejects a non-object root");
            ExpectThrows(() => FlatJson.Parse("{\"a\":\"b\""), "rejects an unterminated object");

            var roundTrip = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("ui.a", "quote \" and \\ and \n and 中文"),
                new KeyValuePair<string, string>("ui.b", "")
            };
            var parsed = FlatJson.Parse(FlatJson.Serialize(roundTrip));
            Expect(parsed.Count == 2 && parsed["ui.a"] == roundTrip[0].Value && parsed["ui.b"] == "",
                "serialize → parse round-trips exactly");
        }

        // ---- 3. 占位符 ----

        private static void CheckPlaceholders()
        {
            Expect(LanguagePack.PlaceholdersMatch("Save {0} of {1}", "保存 {0} / {1}"),
                "same indices pass");
            Expect(LanguagePack.PlaceholdersMatch("Save {0} of {1}", "{1} / {0} 保存"),
                "reordered placeholders pass (word order is the translator's call)");
            Expect(LanguagePack.PlaceholdersMatch("no args here", "这里没有参数"),
                "no placeholders on either side passes");
            Expect(!LanguagePack.PlaceholdersMatch("Save {0}", "保存"),
                "missing placeholder is rejected");
            Expect(!LanguagePack.PlaceholdersMatch("Save", "保存 {0}"),
                "extra placeholder is rejected");
            Expect(!LanguagePack.PlaceholdersMatch("Save {0:N0}", "保存 {0}"),
                "different format specifier is rejected");
            Expect(!LanguagePack.PlaceholdersMatch("Save {0} of {1}", "保存 {0}"),
                "partial index set is rejected");

            var extracted = LanguagePack.ExtractPlaceholders("{0} and {1:N0} and {0}");
            Expect(extracted.Count == 2 && extracted[0] == "" && extracted[1] == "N0",
                "extraction merges repeated indices and keeps specifiers");
        }

        // ---- 4. 语言代码 ----

        private static void CheckLangCodes()
        {
            Expect(LanguagePackLibrary.IsValidLangCode("zh-CN"), "zh-CN accepted");
            Expect(LanguagePackLibrary.IsValidLangCode("ja"), "single-segment ja accepted");
            Expect(LanguagePackLibrary.IsValidLangCode("pt-BR"), "pt-BR accepted");
            Expect(LanguagePackLibrary.IsValidLangCode("zh-Hans-CN"), "zh-Hans-CN accepted");
            Expect(!LanguagePackLibrary.IsValidLangCode("zh_CN"), "underscore rejected (it would break file names)");
            Expect(!LanguagePackLibrary.IsValidLangCode("z"), "one-letter language rejected");
            Expect(!LanguagePackLibrary.IsValidLangCode("bad code"), "space rejected");
            Expect(!LanguagePackLibrary.IsValidLangCode("../evil"), "path traversal rejected");
            Expect(!LanguagePackLibrary.IsValidLangCode(""), "empty rejected");
        }

        // ---- 5. 单文件解析与诊断 ----

        private static void CheckParseFile(string tempRoot)
        {
            var dir = Path.Combine(tempRoot, "parse");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "zh-CN.json");
            File.WriteAllText(path,
                "{\n" +
                "  \"_comment\": \"贡献者写的说明，应被静默忽略\",\n" +
                "  \"ui.window.title\": \"Git 增强界面\",\n" +
                "  \"ui.nope.missing\": \"本版本不存在的键\",\n" +
                "  \"ui.graph.statusFormat\": \"坏 {0}\"\n" +
                "}\n");

            File.WriteAllText(Path.Combine(dir, "zh-CN.meta.json"),
                "{ \"nativeName\": \"简体中文\", \"maintainers\": [\"tester\"], \"order\": 42 }");

            var pack = LanguagePackLibrary.ParseFile(path, LanguagePackSource.ProjectShared);
            Expect(pack.Lang == "zh-CN", "language code comes from the file name");
            Expect(string.IsNullOrEmpty(pack.Error), "valid file has no error: " + pack.Error);
            Expect(pack.Entries.Count == 1 && pack.Entries.ContainsKey("ui.window.title"),
                $"only valid entries are kept (got {pack.Entries.Count})");
            Expect(pack.Entries["ui.window.title"] == "Git 增强界面", "translation value is kept");
            Expect(pack.UnknownKeys.Count == 1 && pack.UnknownKeys[0] == "ui.nope.missing",
                "unknown key is reported, not applied");
            Expect(!pack.UnknownKeys.Contains("_comment"),
                "underscore-prefixed comment keys are ignored silently");
            Expect(pack.InvalidKeys.Count == 1 && pack.InvalidKeys[0] == "ui.graph.statusFormat",
                "placeholder mismatch is dropped and reported");
            Expect(pack.NativeName == "简体中文", "meta nativeName is read");
            Expect(pack.Maintainers.Count == 1 && pack.Maintainers[0] == "tester", "meta maintainers are read");
            Expect(pack.Order == 42, "meta order is read");
            Expect(pack.CoverageText.EndsWith("/" + I18n.KeyCount, StringComparison.Ordinal),
                "coverage text uses the key total");

            var badName = Path.Combine(dir, "bad name.json");
            File.WriteAllText(badName, "{ \"ui.window.title\": \"x\" }");
            var badPack = LanguagePackLibrary.ParseFile(badName, LanguagePackSource.ProjectShared);
            Expect(!string.IsNullOrEmpty(badPack.Error), "invalid language code in the file name is an error");
            Expect(badPack.Entries.Count == 0, "invalid language code yields no entries");

            var broken = Path.Combine(dir, "ja-JP.json");
            File.WriteAllText(broken, "{ \"ui.window.title\": }");
            var brokenPack = LanguagePackLibrary.ParseFile(broken, LanguagePackSource.ProjectShared);
            Expect(!string.IsNullOrEmpty(brokenPack.Error), "malformed json is reported as an error");
            Expect(brokenPack.Entries.Count == 0, "malformed json yields no entries");
        }

        // ---- 6. 键级归并 ----

        private static void CheckMergeLayers()
        {
            var low = new LanguagePack { Lang = "xx-YY", Source = LanguagePackSource.BuiltIn, Order = 100 };
            low.Entries["a"] = "low-a";
            low.Entries["b"] = "low-b";
            low.NativeName = "Low";

            var high = new LanguagePack { Lang = "xx-YY", Source = LanguagePackSource.UserLocal, Order = 1000 };
            high.Entries["b"] = "high-b";
            high.Entries["c"] = "high-c";
            high.NativeName = "High";

            var merged = LanguagePackLibrary.MergeLayers(new List<LanguagePack> { low, high });
            Expect(merged != null && merged.Lang == "xx-YY", "merged pack keeps the language code");
            Expect(merged.Entries["a"] == "low-a", "lower layer provides keys the higher one omits");
            Expect(merged.Entries["b"] == "high-b", "higher layer wins per key");
            Expect(merged.Entries["c"] == "high-c", "higher layer adds its own keys");
            Expect(merged.NativeName == "High", "metadata comes from the highest layer that provides it");
            Expect(merged.Source == LanguagePackSource.UserLocal, "source is the highest layer");

            // 高层把某键判为无效（占位符不一致）时，不能让低层的旧译文静默顶上来——
            // 否则用户改错了译文却以为"改动没生效"，比直接回退英文更难排查。
            var highInvalid = new LanguagePack { Lang = "xx-YY", Source = LanguagePackSource.UserLocal };
            highInvalid.InvalidKeys.Add("a");
            var merged2 = LanguagePackLibrary.MergeLayers(new List<LanguagePack> { low, highInvalid });
            Expect(!merged2.Entries.ContainsKey("a"),
                "a key invalidated by a higher layer is dropped, not silently re-filled from a lower layer");
            Expect(merged2.InvalidKeys.Contains("a"), "the dropped key is reported as invalid");

            Expect(LanguagePackLibrary.MergeLayers(new List<LanguagePack>()) == null,
                "merging no layers returns null");
        }

        // ---- 7. 应用语言（真实三来源目录） ----

        private static void CheckApplyLanguage(string projectPackPath)
        {
            var dir = Path.GetDirectoryName(projectPackPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(projectPackPath, "{ \"ui.window.title\": \"冒烟标题\" }");

            // 应用部分翻译：只覆盖一个键，其余沿用英文
            I18n.ApplyLanguage("zz-smoke");
            Expect(I18n.CurrentLanguage == "zz-smoke", "current language is recorded");
            Expect(I18n.L(I18n.Keys.WindowTitle) == "冒烟标题", "translated key is applied");
            Expect(I18n.L(I18n.Keys.GraphLoading) == I18n.EnglishText(I18n.Keys.GraphLoading),
                "keys the pack omits fall back to English (partial translation is valid)");
            Expect(I18n.L(I18n.Keys.GraphStatusFormat, 1, 2, 3, "abc", "def").Length > 0,
                "formatting a fallback key still works");

            // 找不到的语言包 → 回英文，不留在半吊子状态
            I18n.ApplyLanguage("qq-ZZ");
            Expect(I18n.CurrentLanguage == null, "missing pack falls back to the English baseline");
            Expect(I18n.L(I18n.Keys.WindowTitle) == I18n.EnglishText(I18n.Keys.WindowTitle),
                "baseline text is restored after a failed language switch");

            // 切换语言不得残留上一门语言
            I18n.ApplyLanguage("zz-smoke");
            I18n.ApplyLanguage(null);
            Expect(I18n.CurrentLanguage == null, "applying null restores English");
            Expect(I18n.L(I18n.Keys.WindowTitle) == I18n.EnglishText(I18n.Keys.WindowTitle),
                "no residue from the previous language");
        }

        // ---- 8. 骨架导出 ----

        private static void CheckExportSkeleton(string tempRoot)
        {
            var dir = Path.Combine(tempRoot, "skeleton");
            string packPath;
            string metaPath;
            string error;

            Expect(LanguagePackLibrary.ExportSkeleton("zh-CN", dir, out packPath, out metaPath, out error),
                "skeleton export succeeds (" + error + ")");
            Expect(File.Exists(packPath) && File.Exists(metaPath), "skeleton writes both files");

            var reloaded = LanguagePackLibrary.ParseFile(packPath, LanguagePackSource.ProjectShared);
            Expect(string.IsNullOrEmpty(reloaded.Error), "exported skeleton parses: " + reloaded.Error);
            Expect(reloaded.Entries.Count == I18n.KeyCount,
                $"skeleton contains every key ({reloaded.Entries.Count}/{I18n.KeyCount})");
            Expect(reloaded.UnknownKeys.Count == 0 && reloaded.InvalidKeys.Count == 0,
                "skeleton is diagnostic-clean");
            Expect(reloaded.Entries[I18n.Keys.WindowTitle] == I18n.EnglishText(I18n.Keys.WindowTitle),
                "skeleton fills each key with the English source text");

            Expect(!LanguagePackLibrary.ExportSkeleton("zh-CN", dir, out packPath, out metaPath, out error),
                "an existing language pack is never overwritten");
            Expect(!string.IsNullOrEmpty(error), "the refusal explains itself");

            Expect(!LanguagePackLibrary.ExportSkeleton("bad code", dir, out packPath, out metaPath, out error),
                "invalid language code is rejected on export");
        }

        // ---- 10. 内置语言包自检（"若存在"式断言，不硬编码具体语言） ----

        private static void CheckBuiltInPacks()
        {
            var dir = LanguagePackLibrary.BuiltInDirectory();
            if (!Directory.Exists(dir))
            {
                Debug.Log("[i18n-smoke] no built-in language pack directory yet: " + dir);
                return;
            }

            var count = 0;
            foreach (var path in Directory.GetFiles(dir, "*" + LanguagePackLibrary.PackSuffix))
            {
                if (path.EndsWith(LanguagePackLibrary.MetaSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                var pack = LanguagePackLibrary.ParseFile(path, LanguagePackSource.BuiltIn);
                count++;

                Expect(string.IsNullOrEmpty(pack.Error),
                    $"built-in pack {pack.Lang} parses cleanly ({pack.Error})");
                Expect(pack.UnknownKeys.Count == 0,
                    $"built-in pack {pack.Lang} has no unknown keys ({string.Join(", ", pack.UnknownKeys.ToArray())})");
                Expect(pack.InvalidKeys.Count == 0,
                    $"built-in pack {pack.Lang} has no placeholder mismatches ({string.Join(", ", pack.InvalidKeys.ToArray())})");

                Debug.Log($"[i18n-smoke] built-in pack {pack.Lang}: coverage {pack.CoverageText}, "
                    + $"maintainers {(pack.Maintainers.Count == 0 ? "-" : string.Join(",", pack.Maintainers.ToArray()))}");
            }

            if (count == 0) Debug.Log("[i18n-smoke] no built-in language pack yet");
        }

        // ---- 9. ui.language.* 键完备性 ----

        private static void CheckLanguageKeys()
        {
            const string prefix = "ui.language.";

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

            Expect(declared.Count >= 20, $"language key set is non-trivial (got {declared.Count})");
            Expect(missing.Count == 0, "no declared key missing from the table (" + string.Join(", ", missing) + ")");
            Expect(dead.Count == 0, "no dead table entry without a declared key (" + string.Join(", ", dead) + ")");

            foreach (var key in declared)
                if (string.IsNullOrEmpty(I18n.L(key))) Fail("key resolves to empty text: " + key);
        }

        // ---- helpers ----

        private static void ExpectThrows(Action action, string what)
        {
            try
            {
                action();
                Fail(what + " (no exception thrown)");
            }
            catch (Exception)
            {
                Debug.Log("[i18n-smoke] ok: " + what);
            }
        }

        private static void Expect(bool condition, string what)
        {
            if (condition) Debug.Log("[i18n-smoke] ok: " + what);
            else Fail(what);
        }

        private static void Fail(string what)
        {
            failures++;
            Debug.LogError("[i18n-smoke] FAIL: " + what);
        }
    }
}
