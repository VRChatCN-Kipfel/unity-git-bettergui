using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// 轻量 i18n：内置英文键表 + L(key) 取值；M4 中文 bundle 通过 SetTranslations 切换。
    /// 键 => 默认英文文案；缺失键返回键名并告警（冒烟可据此抓漏键）。
    /// </summary>
    /// <remarks>
    /// 术语定则（翻译唯一依据，M4 统一评审；代码内勿散落译法）：
    ///   fetch → 提取        revert → 撤销变动      commit → 提交
    ///   stage → 暂存        unstage → 取消暂存     checkout → 检出
    ///   branch → 分支       tag → 标签             merge → 合并
    ///   reset → 重置        stash → 贮藏           remote → 远程
    ///   2FA → 备选          index → 索引           worktree → 工作树
    /// 原则：动词一致、术语唯一；JetBrains 官方中文优先，冲突时以本表为准。
    /// MenuItem 目录路径（Window/Git/…）静态属性无法本地化，M2 保持英文。
    /// </remarks>
    public static partial class I18n
    {
        private static readonly Dictionary<string, string> Table =
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                // -- 窗口 --
                [Keys.WindowTitle] = "Git Better GUI",
                [Keys.GitUnavailable] = "Git unavailable: {0}",
                [Keys.SelectACommit] = "select a commit",
                // -- 图谱 --
                [Keys.GraphLoading] = "loading…",
                [Keys.GraphEmpty] = "(no commits for this filter)",
                [Keys.GraphStatusFormat] = "{0} commits · {1} line(s) · {2} refs · head {3} \"{4}\"",
                [Keys.GraphTooltipParents] = "parents: {0}",
                [Keys.GraphTooltipFiles] = "files: {0}",
                // -- 提交详情 --
                [Keys.LoadingChanges] = "loading changes…",
                [Keys.NoChangesParsed] = "(no file changes parsed)",
                [Keys.RootCommitNote] = "(root commit — full tree vs empty)",
                [Keys.NoMergeConflicts] = "✓ no merge conflicts",
                [Keys.ChangesToParent] = "Changes to parent {0}",
                [Keys.SectionMerged] = "Merged (all parents)",
                // -- 右键动作（图谱行提交语境） --
                [Keys.MenuCopyHash] = "Copy Hash",
                [Keys.MenuCopySummary] = "Copy Summary",
                [Keys.MenuNewBranch] = "New Branch…",
                [Keys.MenuNewBranchPrompt] = "New branch name (from {0}):",
                [Keys.MenuReset] = "Reset…",
                [Keys.MenuResetSoft] = "Soft",
                [Keys.MenuResetMixed] = "Mixed",
                [Keys.MenuResetHard] = "Hard",
                [Keys.MenuResetConfirm] = "Reset {0} to {1}?\n\nIndex/work tree will be modified.",
                [Keys.MenuResetHardWarn] = "DANGER: working tree changes will be discarded.",
                [Keys.MenuRevert] = "Revert Commit…",
                [Keys.MenuRevertConfirm] = "Revert commit {0}?\n\nThis creates a new commit that undoes it.",
                [Keys.MenuUncommit] = "Uncommit…",
                [Keys.MenuUncommitConfirm] = "Undo commit {0}?\n\nSoft-resets HEAD so the changes return to the staging area.",
                [Keys.MenuCherryPick] = "Cherry-Pick {0}…",
                [Keys.MenuCherryPickConfirm] = "Apply commit {0} to the current branch?",
                [Keys.MenuCheckout] = "Checkout…",
                [Keys.MenuCheckoutConfirm] = "Checkout {0}?\n\nThis detaches HEAD from the current branch.",
                [Keys.DialogOk] = "OK",
                [Keys.DialogCancel] = "Cancel",
                [Keys.MenuOpFailedTitle] = "Git operation failed",
                // -- 窗口 Tab / Commit 页 --
                [Keys.TabLog] = "Log",
                [Keys.TabCommit] = "Commit",
                [Keys.CommitSummaryLabel] = "Summary",
                [Keys.CommitBodyLabel] = "Description",
                [Keys.CommitAmend] = "Amend",
                [Keys.CommitSignoff] = "Sign off",
                [Keys.CommitNoVerify] = "No verify (skip hooks)",
                [Keys.CommitButton] = "Commit",
                [Keys.CommitRefresh] = "Refresh",
                [Keys.CommitClean] = "Working tree clean",
                [Keys.CommitNoChanges] = "(no unstaged changes)",
                [Keys.CommitNothingStaged] = "Nothing staged — stage files first (check the boxes below).",
                [Keys.CommitConflictInProgress] = "Merge/rebase in progress — resolve all conflicts and stage the resolved files first.",
                [Keys.CommitStagedGroup] = "Staged",
                [Keys.CommitChangesGroup] = "Changes",
                [Keys.CommitGpgHint] = "GPG signing failed — check gpg-agent / private key, or disable signing.",
                [Keys.CommitTemplates] = "Templates ▾",
                [Keys.CommitRecentMessages] = "Recent messages",
                [Keys.CommitUseTemplate] = "Use commit template",
                [Keys.CommitNoTemplate] = "no commit.template configured",
                // -- 文件/目录语境右键 --
                [Keys.MenuStage] = "Stage",
                [Keys.MenuUnstage] = "Unstage",
                [Keys.MenuStageAll] = "Stage All",
                [Keys.MenuUnstageAll] = "Unstage All",
                [Keys.MenuRevertFile] = "Revert (discard changes)",
                [Keys.MenuDiscardConfirm] = "Discard changes for {0}?\n\nThis cannot be undone.",
                [Keys.MenuDiscardCount] = "{0} files",
                [Keys.MenuOpen] = "Open",
                [Keys.MenuCopyPath] = "Copy Path",
                [Keys.BlameTitle] = "Blame — {0}",
                [Keys.MenuBlame] = "Blame",
                [Keys.DiffStageHunk] = "Stage hunk",
                [Keys.DiffRevertHunk] = "Revert hunk (discard changes)",
                [Keys.DiffBinary] = "Binary files differ",
                [Keys.DiffNoChanges] = "(no changes for this file at this scope)",
                [Keys.MenuCompareBranch] = "Compare with Branch…",
                [Keys.MenuCreateTag] = "Create Tag…",
                [Keys.CreateTagPrompt] = "Tag name:",
                // -- 分支弹窗 / Compare --
                [Keys.BranchTitle] = "Branches",
                [Keys.BranchFilter] = "Filter",
                [Keys.BranchCurrent] = "current",
                [Keys.BranchNewLabel] = "New branch:",
                [Keys.BranchNew] = "New",
                [Keys.BranchTagLabel] = "New tag:",
                [Keys.BranchTag] = "Tag",
                [Keys.BranchDelete] = "Delete",
                [Keys.BranchDeleteConfirm] = "Delete branch {0}?",
                [Keys.BranchDeleteTagConfirm] = "Delete tag {0}?",
                [Keys.BranchDeleteForce] = "Branch {0} is not fully merged. Force delete?",
                [Keys.BranchGroupLocal] = "Local",
                [Keys.BranchGroupRemote] = "Remotes",
                [Keys.BranchGroupTags] = "Tags",
                [Keys.BranchCheckoutTagConfirm] = "Checkout tag {0}? This detaches HEAD.",
                // -- 分支面板右键动作 --
                [Keys.BranchCtxCheckout] = "Checkout",
                [Keys.BranchCtxNewFrom] = "Branch from {0}…",
                [Keys.BranchCtxUpdate] = "Update",
                [Keys.BranchCtxPush] = "Push",
                [Keys.BranchCtxFetch] = "Fetch",
                [Keys.BranchCtxRename] = "Rename…",
                [Keys.BranchCtxRenamePrompt] = "New name for {0}:",
                [Keys.BranchCtxCompareWith] = "Compare with {0}…",
                [Keys.BranchCtxMergeInto] = "Merge {0} into {1}",
                [Keys.BranchCtxMergeConfirm] = "Merge {0} into {1}?",
                [Keys.BranchCtxUpstreamOps] = "Operations on {0}",
                [Keys.BranchCtxUpstreamCompare] = "Compare with upstream",
                [Keys.BranchCtxUpstreamMerge] = "Merge upstream into {0}",
                [Keys.BranchCtxPushCurrentTo] = "Push current branch to {0}",
                [Keys.BranchCtxNewBranchTitle] = "New Branch",
                [Keys.BranchCtxNoRemoteHint] = "no remote configured",
                [Keys.BranchFilterAll] = "All branches",
                [Keys.BranchFilterCurrent] = "Current branch",
                [Keys.BranchShowPanel] = "Show branches panel",
                [Keys.CompareResultTitle] = "Differences with {0}",
                [Keys.CompareNoChanges] = "(no differences)",
                [Keys.CompareBack] = "Back",
                [Keys.BranchCtxRebaseOnto] = "Rebase current branch onto {0}",
                [Keys.BranchCtxCheckoutAndRebase] = "Checkout {0} and rebase current branch onto it",
                [Keys.BranchCtxRebaseOntoUpstream] = "Rebase current branch onto {0}",
                [Keys.RebaseConflictHint] = "Rebase conflict: {0} files need resolution (see 3-way view)",
                [Keys.MergeConflictHint] = "Merge conflict: {0} files need resolution (see 3-way view)",
                [Keys.ConflictHint] = "Conflict: {0} files need resolution",
                [Keys.RebaseInProgress] = "Rebase in progress — resolve or abort/continue",
                [Keys.MergeInProgress] = "Merge in progress — resolve or abort",
                [Keys.RebaseContinue] = "Continue Rebase",
                [Keys.RebaseAbort] = "Abort Rebase",
                [Keys.Merge3Title] = "Resolve Conflicts",
                [Keys.Merge3NoConflicts] = "(no conflicts)",
                [Keys.Merge3File] = "Conflicts: {0}",
                [Keys.Merge3Yours] = "Yours",
                [Keys.Merge3Theirs] = "Theirs",
                [Keys.Merge3AcceptOurs] = "Accept Yours",
                [Keys.Merge3AcceptTheirs] = "Accept Theirs",
                [Keys.Merge3RebaseSwapNote] = "[rebase: Yours/Theirs swapped per git semantics]",
                [Keys.Merge3AllResolved] = "All conflicts resolved — commit (merge) or continue (rebase)",
                [Keys.Merge3AllResolvedMerge] = "All conflicts resolved — commit on the Commit page to finish the merge (Abort still available)",
                [Keys.Merge3AllResolvedRebase] = "All conflicts resolved — press Continue Rebase to finish, or Abort to cancel",
                [Keys.MergeAbort] = "Abort",
                [Keys.MergeAbortConfirm] = "Abort the in-progress merge/rebase and return to the pre-operation state?",
                [Keys.RemoteManageTitle] = "Manage Remotes",
                [Keys.RemoteName] = "Name",
                [Keys.RemoteUrl] = "URL",
                [Keys.RemoteAdd] = "Add / Update",
                [Keys.RemoteEditUrl] = "Edit URL",
                [Keys.RemoteRemove] = "Remove",
                [Keys.RemoteRemoveConfirm] = "Remove remote {0}?\n\nThis only removes the remote definition; local branches are unaffected.",
                [Keys.RemoteNameUrlRequired] = "Name and URL are required.",
                [Keys.RemoteNone] = "(no remotes configured — use the form below to add one)",
                [Keys.RemoteAdded] = "Remote {0} added.",
                [Keys.RemoteUpdated] = "Remote {0} URL updated.",
                [Keys.RemoteRemoved] = "Remote {0} removed.",
                [Keys.TagPush] = "Push tag to {0}",
                [Keys.TagPushConfirm] = "Push tag {0} to remote {1}?",
                [Keys.TagDeleteRemote] = "Delete tag on {0}",
                [Keys.TagDeleteRemoteConfirm] = "Delete tag {0} on remote {1}?",
                [Keys.TagNotOnRemote] = "Tag {0} does not exist on remote {1} (nothing to delete).",
                [Keys.TagPushed] = "Tag {0} pushed to remote {1}.",
                [Keys.TagDeletedRemote] = "Tag {0} deleted on remote {1}.",

                // -- M4 一键 ignore 模板 --
                [Keys.IgnoreTemplatesTitle] = "Ignore Templates",
                [Keys.IgnoreTemplatesButton] = "Ignore Template…",
                [Keys.IgnoreTemplatesHint] = "Built-in templates ship with the package. Drop your own under .gitui-ignore-templates/ (project, commit-friendly) or UserSettings/GitBetterGui/IgnoreTemplates/ (local only) — a template is one *.gitignore file plus an optional *.meta.json.",
                [Keys.IgnoreTemplatesDirectories] = "Built-in: {0}\nProject: {1}",
                [Keys.IgnoreTemplatesList] = "Templates",
                [Keys.IgnoreTemplatesPreview] = "Result preview",
                [Keys.IgnoreTemplatesSummary] = "{0} rule(s) will be added; {1} already present.",
                [Keys.IgnoreTemplatesWillCreate] = "This project has no .gitignore yet — it will be created with {0} rule(s).",
                [Keys.IgnoreTemplatesWillOverwrite] = "Overwrite mode — .gitignore will be replaced with {0} rule(s); the current file is backed up first.",
                [Keys.IgnoreTemplatesMode] = "Write mode",
                [Keys.IgnoreTemplatesModeMerge] = "Merge (append missing rules)",
                [Keys.IgnoreTemplatesModeOverwrite] = "Overwrite (back up first)",
                [Keys.IgnoreTemplatesWrite] = "Write .gitignore",
                [Keys.IgnoreTemplatesExport] = "Export current .gitignore as template…",
                [Keys.IgnoreTemplatesExportPrompt] = "Template id (lowercase kebab-case, e.g. my-studio-ignores):",
                [Keys.IgnoreTemplatesExportInvalid] = "Template id must be lowercase kebab-case: a–z, 0–9 and single dashes.",
                [Keys.IgnoreTemplatesExportEmpty] = "Nothing to export — this project has no .gitignore yet.",
                [Keys.IgnoreTemplatesExported] = "Exported template to {0}",
                [Keys.IgnoreTemplatesOpenFolder] = "Open project templates folder",
                [Keys.IgnoreTemplatesRescan] = "Rescan",
                [Keys.IgnoreTemplatesEmpty] = "(no templates found)",
                [Keys.IgnoreTemplatesError] = "Ignore template operation failed: {0}",
                [Keys.IgnoreTemplatesSourceBuiltIn] = "built-in",
                [Keys.IgnoreTemplatesSourceProject] = "project",
                [Keys.IgnoreTemplatesSourceUser] = "user",
                [Keys.IgnoreTemplatesCreated] = "Created .gitignore from “{0}” — {1} rule(s).",
                [Keys.IgnoreTemplatesMerged] = "Updated .gitignore from “{0}” — {1} added, {2} already present.",
                [Keys.IgnoreTemplatesOverwritten] = "Replaced .gitignore from “{0}” — {1} rule(s). Backup: {2}",
                [Keys.IgnoreTemplatesUpToDate] = "“{0}” would add nothing — .gitignore already covers it ({1} rule(s)).",

                // -- M4 界面语言（多语言贡献框架） --
                [Keys.LanguageTitle] = "Interface Language",
                [Keys.LanguageHint] = "A language pack is one <lang>.json file (flat key → text) plus an optional <lang>.meta.json. Drop it in .gitui-i18n/ (project, commit-friendly) or UserSettings/GitBetterGui/I18n/ (local only). Untranslated keys fall back to English, so a partial translation is a valid contribution.",
                [Keys.LanguageList] = "Available languages",
                [Keys.LanguageEnglish] = "English (built-in)",
                [Keys.LanguageCoverage] = "{0}/{1} keys translated",
                [Keys.LanguageMaintainers] = "maintainers: {0}",
                [Keys.LanguageFallbackNote] = "Keys you leave out fall back to English.",
                [Keys.LanguageUnknownKeys] = "{0} key(s) ignored — not present in this build",
                [Keys.LanguageInvalidKeys] = "{0} key(s) dropped — placeholder mismatch, they fall back to English",
                [Keys.LanguageApply] = "Use this language",
                [Keys.LanguageApplied] = "Interface language: {0}",
                [Keys.LanguageAppliedEnglish] = "Interface language: English (built-in)",
                [Keys.LanguageExport] = "Export skeleton for a new language…",
                [Keys.LanguageExportPrompt] = "Language code (BCP-47, e.g. zh-CN, ja-JP, pt-BR):",
                [Keys.LanguageExportDone] = "Skeleton written to {0} — replace the English values with your translation, then rescan.",
                [Keys.LanguageInvalidCode] = "Language code must look like zh-CN or ja-JP (2–3 letters, optional -Region).",
                [Keys.LanguageSkeletonEmpty] = "Cannot export a skeleton: the built-in English table is empty.",
                [Keys.LanguageSkeletonExists] = "A language pack already exists at {0} — not overwriting it.",
                [Keys.LanguageOpenFolder] = "Open project language folder",
                [Keys.LanguageRescan] = "Rescan",
                [Keys.LanguageNone] = "(no language pack found yet — export a skeleton to start one)",
                [Keys.LanguageError] = "Language pack failed: {0}",
            };

        /// <summary>英文基线快照（内置表原值）。切换语言时先整体恢复它、再叠加译文，
        /// 保证上一门语言的残留不会留在界面里。</summary>
        private static readonly Dictionary<string, string> EnglishBaseline;

        /// <summary>全部界面键，按 Keys 常量的声明顺序（只含表里确有英文文案的键）。</summary>
        private static readonly List<string> KeyList;

        /// <summary>语言偏好持久化键（EditorPrefs）。</summary>
        public const string PrefLanguage = "kf.gitui.language";

        static I18n()
        {
            EnglishBaseline = new Dictionary<string, string>(Table, System.StringComparer.Ordinal);

            // Keys 是一堆 const string，只能反射取；用 MetadataToken 排序以得到稳定的声明顺序
            // （Dictionary 的枚举顺序不可依赖，而覆盖率/骨架都要求稳定顺序）。
            var fields = typeof(Keys).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var ordered = new List<KeyValuePair<int, string>>();
            foreach (var field in fields)
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                var value = field.GetRawConstantValue() as string;
                if (string.IsNullOrEmpty(value)) continue;
                ordered.Add(new KeyValuePair<int, string>(field.MetadataToken, value));
            }
            ordered.Sort((a, b) => a.Key.CompareTo(b.Key));

            KeyList = new List<string>(ordered.Count);
            foreach (var entry in ordered)
            {
                if (!Table.ContainsKey(entry.Value))
                {
                    Debug.LogWarning("[i18n] key declared without a table entry: " + entry.Value);
                    continue;
                }
                if (!KeyList.Contains(entry.Value)) KeyList.Add(entry.Value);
            }
        }

        /// <summary>当前生效的语言代码；null/空串 = 英文基线（未启用任何语言包）。</summary>
        public static string CurrentLanguage { get; private set; }

        /// <summary>语言切换完成（界面需据此重建：已构建的 UI 不会自动换文案）。</summary>
        public static event System.Action LanguageChanged;

        /// <summary>全部界面键（Keys 声明顺序，且英文表里确有文案）。</summary>
        public static IReadOnlyList<string> AllKeys
        {
            get { return KeyList; }
        }

        /// <summary>界面键总数（语言包覆盖率的分母）。</summary>
        public static int KeyCount
        {
            get { return KeyList.Count; }
        }

        /// <summary>英文基线文案（语言包的比对基准与回退来源）；键不存在返回 null。</summary>
        public static string EnglishText(string key)
        {
            string value;
            return key != null && EnglishBaseline.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>
        /// 应用语言（不写 EditorPrefs）：先把表恢复到英文基线，再叠加语言包译文；
        /// 语言代码为空或找不到包时即回到英文。未提供的键天然沿用英文，因此**允许部分翻译**。
        /// </summary>
        public static void ApplyLanguage(string lang)
        {
            foreach (var kv in EnglishBaseline) Table[kv.Key] = kv.Value;

            LanguagePack pack = null;
            if (!string.IsNullOrEmpty(lang)) pack = LanguagePackLibrary.Load(lang);

            if (pack != null)
            {
                foreach (var kv in pack.Entries)
                    if (Table.ContainsKey(kv.Key)) Table[kv.Key] = kv.Value;
            }

            CurrentLanguage = pack != null ? pack.Lang : null;

            var handler = LanguageChanged;
            if (handler != null) handler();
        }

        /// <summary>显式选择语言：应用并写入 EditorPrefs（跨会话记住）。</summary>
        public static void SetLanguage(string lang)
        {
            ApplyLanguage(lang);
            EditorPrefs.SetString(PrefLanguage, CurrentLanguage ?? string.Empty);
        }

        /// <summary>
        /// 编辑器启动时恢复：优先用已保存的选择；没有则按系统语言猜一门（猜不到或没包 → 英文，且不写盘）。
        /// </summary>
        public static void InitFromPreferences()
        {
            var saved = EditorPrefs.GetString(PrefLanguage, string.Empty);
            if (!string.IsNullOrEmpty(saved))
            {
                ApplyLanguage(saved);
                return;
            }
            ApplyLanguage(GuessLanguageFromSystem());
        }

        /// <summary>系统语言 → 语言代码（没有对应语言包时 ApplyLanguage 会自动回退英文）。</summary>
        public static string GuessLanguageFromSystem()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.ChineseSimplified: return "zh-CN";
                case SystemLanguage.ChineseTraditional: return "zh-TW";
                case SystemLanguage.Chinese: return "zh-CN";
                case SystemLanguage.Japanese: return "ja-JP";
                case SystemLanguage.Korean: return "ko-KR";
                case SystemLanguage.German: return "de-DE";
                case SystemLanguage.French: return "fr-FR";
                case SystemLanguage.Spanish: return "es-ES";
                case SystemLanguage.Russian: return "ru-RU";
                case SystemLanguage.Portuguese: return "pt-BR";
                default: return null;
            }
        }

        /// <summary>当前生效键表（只读视图；冒烟/M4 bundle 校验用）。</summary>
        public static IReadOnlyDictionary<string, string> All => Table;

        /// <summary>
        /// M4：注入本地化 bundle。值可为空串表示"沿用英文兜底"；
        /// 未知键忽略（防止旧 bundle 键污染）。调用方负责按语言组织完整 bundle。
        /// </summary>
        public static void SetTranslations(Dictionary<string, string> translations)
        {
            if (translations == null) return;
            foreach (var kv in translations)
                if (Table.ContainsKey(kv.Key))
                    Table[kv.Key] = kv.Value ?? Table[kv.Key];
        }

        /// <summary>取键值；缺失键返回键名（+告警），冒烟可断言 All 覆盖全部使用键。</summary>
        public static string L(string key)
        {
            if (Table.TryGetValue(key, out var text)) return text;
            Debug.LogWarning("[i18n] missing key: " + key);
            return key;
        }

        /// <summary>取键值并格式化（键值为 {0} 占位格式串）。</summary>
        public static string L(string key, params object[] args)
        {
            return string.Format(L(key), args);
        }
    }
}