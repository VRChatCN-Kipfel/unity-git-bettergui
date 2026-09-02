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
                [Keys.MenuUncommit] = "Uncommit…",
                [Keys.MenuUncommitConfirm] = "Undo commit {0}?\n\nSoft-resets HEAD so the changes return to the staging area.",
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
                [Keys.RemoteManageTitle] = "Manage Remotes",
                [Keys.RemoteName] = "Name",
                [Keys.RemoteUrl] = "URL",
                [Keys.RemoteAdd] = "Add / Update",
                [Keys.RemoteEditUrl] = "Edit URL",
                [Keys.RemoteRemove] = "Remove",
                [Keys.RemoteRemoveConfirm] = "Remove remote {0}?\n\nThis only removes the remote definition; local branches are unaffected.",
                [Keys.RemoteNameUrlRequired] = "Name and URL are required.",
                [Keys.RemoteAdded] = "Remote {0} added.",
                [Keys.RemoteUpdated] = "Remote {0} URL updated.",
                [Keys.RemoteRemoved] = "Remote {0} removed.",
                [Keys.TagPush] = "Push tag to {0}",
                [Keys.TagPushConfirm] = "Push tag {0} to remote {1}?",
                [Keys.TagDeleteRemote] = "Delete tag on {0}",
                [Keys.TagDeleteRemoteConfirm] = "Delete tag {0} on remote {1}?",
                [Keys.TagNotOnRemote] = "Tag {0} does not exist on remote {1} (nothing to delete).",
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