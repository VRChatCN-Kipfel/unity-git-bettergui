using System.Collections.Generic;
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
                [Keys.CommitGpgHint] = "GPG signing failed — check gpg-agent / private key, or disable signing.",
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
            };

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