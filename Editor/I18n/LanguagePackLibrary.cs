using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// 语言包库：发现、解析、校验、键级归并、覆盖率与骨架导出。
    ///
    /// 发现约定（贡献者只需关心第一条）：
    ///   1. 包内 <c>Editor/I18n/BuiltIn/&lt;lang&gt;.json</c>（+ 可选 <c>&lt;lang&gt;.meta.json</c>）
    ///   2. 项目内 <c>.gitui-i18n/</c>（可提交，团队统一术语）
    ///   3. 用户本地 <c>UserSettings/GitBetterGui/I18n/</c>（不入版本控制）
    ///
    /// 优先级：用户本地 ＞ 项目 ＞ 内置，且是**键级**合并（与 ignore 模板的整份覆盖不同，
    /// 因为语言包的天然单元是"一个键的文案"）。
    ///
    /// 注：目录探测与 <see cref="GitIgnoreTemplateLibrary"/> 有意保持平行实现、暂不抽公共基类——
    /// 那条链路已通过验证，本轮不做可能引入回归的重构；等出现第三个消费者再抽。
    /// </summary>
    public static class LanguagePackLibrary
    {
        public const string PackageName = "com.kf.gitui";
        public const string BuiltInRelativeDir = "Editor/I18n/BuiltIn";
        public const string ProjectSharedDirName = ".gitui-i18n";
        public const string UserLocalRelativeDir = "UserSettings/GitBetterGui/I18n";
        public const string MetaSuffix = ".meta.json";
        public const string PackSuffix = ".json";

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

        /// <summary>包内内置语言包目录（解析本程序集所属包的真实路径，失败退回 Packages/&lt;name&gt;）。</summary>
        public static string BuiltInDirectory()
        {
            try
            {
                // 完全限定：UnityEditor 里另有一个同名（已废弃）的 PackageInfo，using 会歧义 CS0104
                var pkg = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(typeof(LanguagePackLibrary).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                    return Path.Combine(pkg.resolvedPath, ToNative(BuiltInRelativeDir));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[gitui] package path probe failed: " + ex.Message);
            }
            return Path.GetFullPath(Path.Combine("Packages", PackageName, ToNative(BuiltInRelativeDir)));
        }

        public static string ProjectSharedDirectory()
        {
            return Path.Combine(ProjectRoot(), ProjectSharedDirName);
        }

        public static string UserLocalDirectory()
        {
            return Path.Combine(ProjectRoot(), ToNative(UserLocalRelativeDir));
        }

        /// <summary>
        /// 语言代码合法性：BCP-47 风格，2–3 位 ASCII 字母语言码，后可跟 1–2 段
        /// 2–8 位字母数字区域/变体码（<c>zh-CN</c>、<c>pt-BR</c>、<c>zh-Hans-CN</c>）。
        /// 它同时是文件名，所以禁止空格、下划线与路径分隔符。
        /// </summary>
        public static bool IsValidLangCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 16) return false;
            var parts = code.Split('-');
            if (parts.Length == 0 || parts.Length > 3) return false;

            var lang = parts[0];
            if (lang.Length < 2 || lang.Length > 3) return false;
            foreach (var c in lang)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;

            for (var i = 1; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length < 2 || part.Length > 8) return false;
                foreach (var c in part)
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                        return false;
            }
            return true;
        }

        /// <summary>发现全部语言（三来源归并后每门语言一份），按 Order → DisplayName 排序。</summary>
        public static List<LanguagePack> LoadAll()
        {
            var layers = new List<KeyValuePair<string, LanguagePack>>();
            foreach (var lang in DiscoverLanguages())
                layers.Add(new KeyValuePair<string, LanguagePack>(lang, null));

            var result = new List<LanguagePack>();
            foreach (var entry in layers)
            {
                var pack = Load(entry.Key);
                if (pack != null) result.Add(pack);
            }

            result.Sort((a, b) =>
            {
                var byOrder = a.Order.CompareTo(b.Order);
                if (byOrder != 0) return byOrder;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>三来源里出现过的语言代码（去重，保持内置 → 项目 → 个人的发现顺序）。</summary>
        public static List<string> DiscoverLanguages()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (var dir in new[] { BuiltInDirectory(), ProjectSharedDirectory(), UserLocalDirectory() })
            {
                foreach (var lang in LanguagesInDirectory(dir))
                    if (seen.Add(lang)) ordered.Add(lang);
            }
            return ordered;
        }

        /// <summary>单语言键级归并：内置 → 项目 → 个人（后者覆盖前者）。全无则返回 null。</summary>
        public static LanguagePack Load(string lang)
        {
            if (!IsValidLangCode(lang)) return null;

            var layers = new List<LanguagePack>();
            AddLayer(layers, Path.Combine(BuiltInDirectory(), lang + PackSuffix), LanguagePackSource.BuiltIn);
            AddLayer(layers, Path.Combine(ProjectSharedDirectory(), lang + PackSuffix), LanguagePackSource.ProjectShared);
            AddLayer(layers, Path.Combine(UserLocalDirectory(), lang + PackSuffix), LanguagePackSource.UserLocal);

            if (layers.Count == 0) return null;
            return MergeLayers(layers);
        }

        /// <summary>
        /// 键级归并（入参按优先级从低到高）。后层的键覆盖前层；Layers 的诊断信息全部累积。
        /// 元数据（NativeName/Maintainers/Order）取最高优先级层里"有给值"的那一层。
        /// </summary>
        public static LanguagePack MergeLayers(List<LanguagePack> layers)
        {
            if (layers == null || layers.Count == 0) return null;

            var merged = new LanguagePack
            {
                Lang = layers[0].Lang,
                Source = layers[0].Source,
                FilePath = layers[0].FilePath,
                Order = layers[0].Order
            };

            foreach (var layer in layers)
            {
                if (layer == null) continue;

                foreach (var kv in layer.Entries)
                    merged.Entries[kv.Key] = kv.Value;

                foreach (var key in layer.UnknownKeys)
                    if (!merged.UnknownKeys.Contains(key)) merged.UnknownKeys.Add(key);

                foreach (var key in layer.InvalidKeys)
                    if (!merged.InvalidKeys.Contains(key)) merged.InvalidKeys.Add(key);

                if (!string.IsNullOrEmpty(layer.Error))
                    merged.Error = layer.Error;

                merged.Source = layer.Source;
                merged.FilePath = layer.FilePath;
                if (!string.IsNullOrEmpty(layer.NativeName)) merged.NativeName = layer.NativeName;
                if (layer.Maintainers != null && layer.Maintainers.Count > 0)
                    merged.Maintainers = new List<string>(layer.Maintainers);
                if (layer.Order != 1000) merged.Order = layer.Order;
            }

            // 同一键既被高优先级层判为无效、又……不可能共存：无效键不会进入 Entries。
            // 但低层有效、高层无效时，高层的"无效"应胜出（否则用户改错的译文会静默回退到低层旧值，
            // 让人以为改动没生效）。这里把这类键从 Entries 中剔除。
            foreach (var invalid in merged.InvalidKeys)
                merged.Entries.Remove(invalid);

            return merged;
        }

        /// <summary>
        /// 解析单个语言包文件并做校验：未知键、下划线前缀注释键、占位符一致性。
        /// 文件级错误记入 <see cref="LanguagePack.Error"/> 而不是抛异常（一个坏文件不该让整库不可用）。
        /// </summary>
        public static LanguagePack ParseFile(string path, LanguagePackSource source)
        {
            var lang = Path.GetFileName(path);
            if (lang.EndsWith(MetaSuffix, StringComparison.OrdinalIgnoreCase))
                lang = lang.Substring(0, lang.Length - MetaSuffix.Length);
            else if (lang.EndsWith(PackSuffix, StringComparison.OrdinalIgnoreCase))
                lang = lang.Substring(0, lang.Length - PackSuffix.Length);

            var pack = new LanguagePack { Lang = lang, Source = source, FilePath = path };

            if (!IsValidLangCode(lang))
            {
                pack.Error = "invalid language code in file name: " + Path.GetFileName(path);
                return pack;
            }

            try
            {
                var map = FlatJson.Parse(File.ReadAllText(path));
                foreach (var kv in map)
                {
                    var key = kv.Key;
                    // `_` 前缀 = 贡献者写的说明/注释键，静默忽略（不是未知键）
                    if (key.StartsWith("_", StringComparison.Ordinal)) continue;

                    var english = I18n.EnglishText(key);
                    if (english == null)
                    {
                        pack.UnknownKeys.Add(key);
                        continue;
                    }

                    if (!LanguagePack.PlaceholdersMatch(english, kv.Value))
                    {
                        pack.InvalidKeys.Add(key);
                        continue;
                    }

                    pack.Entries[key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                pack.Error = ex.Message;
                pack.Entries.Clear();
            }

            LoadMeta(pack, path);
            return pack;
        }

        /// <summary>
        /// 导出语言骨架：把**全部键 + 英文原文**写成 &lt;lang&gt;.json，贡献者只需替换值。
        /// 已存在同名文件时拒绝（那是贡献者的成品，不能覆盖）。
        /// </summary>
        public static bool ExportSkeleton(string lang, string targetDirectory,
            out string packPath, out string metaPath, out string error)
        {
            packPath = null;
            metaPath = null;
            error = null;

            if (!IsValidLangCode(lang))
            {
                error = I18n.L(I18n.Keys.LanguageInvalidCode);
                return false;
            }

            var keys = I18n.AllKeys;
            if (keys.Count == 0)
            {
                error = I18n.L(I18n.Keys.LanguageSkeletonEmpty);
                return false;
            }

            try
            {
                Directory.CreateDirectory(targetDirectory);
                var packFile = Path.Combine(targetDirectory, lang + PackSuffix);
                if (File.Exists(packFile))
                {
                    error = I18n.L(I18n.Keys.LanguageSkeletonExists, packFile);
                    return false;
                }

                var pairs = new List<KeyValuePair<string, string>>(keys.Count);
                foreach (var key in keys)
                    pairs.Add(new KeyValuePair<string, string>(key, I18n.EnglishText(key) ?? string.Empty));

                File.WriteAllText(packFile, FlatJson.Serialize(pairs), new UTF8Encoding(false));

                var metaFile = Path.Combine(targetDirectory, lang + MetaSuffix);
                if (!File.Exists(metaFile))
                {
                    var meta = "{\n  \"nativeName\": \"" + lang + "\",\n  \"maintainers\": [],\n  \"order\": 1000\n}\n";
                    File.WriteAllText(metaFile, meta, new UTF8Encoding(false));
                }

                packPath = packFile;
                metaPath = metaFile;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ---- 内部 ----

        private static void AddLayer(List<LanguagePack> layers, string path, LanguagePackSource source)
        {
            if (!File.Exists(path)) return;
            layers.Add(ParseFile(path, source));
        }

        private static List<string> LanguagesInDirectory(string dir)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(dir)) return result;
            try
            {
                if (!Directory.Exists(dir)) return result;
                foreach (var path in Directory.GetFiles(dir, "*" + PackSuffix))
                {
                    var name = Path.GetFileName(path);
                    if (name.EndsWith(MetaSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                    var lang = name.Substring(0, name.Length - PackSuffix.Length);
                    if (IsValidLangCode(lang)) result.Add(lang);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[gitui] language pack scan failed ({dir}): {ex.Message}");
            }
            return result;
        }

        private static void LoadMeta(LanguagePack pack, string path)
        {
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var metaPath = Path.Combine(dir, pack.Lang + MetaSuffix);
            if (!File.Exists(metaPath)) return;

            try
            {
                var meta = JsonUtility.FromJson<LanguagePackMeta>(File.ReadAllText(metaPath));
                if (meta == null) return;
                if (!string.IsNullOrEmpty(meta.nativeName)) pack.NativeName = meta.nativeName.Trim();
                if (meta.maintainers != null) pack.Maintainers = new List<string>(meta.maintainers);
                if (meta.order != 0) pack.Order = meta.order;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[gitui] language meta unreadable ({metaPath}): {ex.Message}");
            }
        }

        private static string ToNative(string relative)
        {
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
