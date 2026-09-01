namespace KF.GitUI
{
    /// <summary>i18n 键表（嵌套于 I18n，调用形如 I18n.Keys.Xxx）。</summary>
    public static partial class I18n
    {
        /// <summary>
        /// i18n 键表（常量）。键名对齐 JetBrains 风格：ui.&lt;区域&gt;.&lt;名称&gt;。
        /// UI 文案一律通过 I18n.L(I18n.Keys.Xxx) 取值，禁止散落硬编码（测试诊断消息除外）。
        /// </summary>
        public static class Keys
        {
        // -- 窗口 --
        public const string WindowTitle = "ui.window.title";
        public const string GitUnavailable = "ui.window.gitUnavailable"; // {0}=错误消息
        public const string SelectACommit = "ui.detail.selectACommit";

        // -- 图谱 --
        public const string GraphLoading = "ui.graph.loading";
        public const string GraphStatusFormat = "ui.graph.statusFormat"; // {0}=提交数 {1}=泳道数 {2}=refs数 {3}=head短号 {4}=head摘要
        public const string GraphTooltipParents = "ui.graph.tooltipParents"; // {0}=父提交列表
        public const string GraphTooltipFiles = "ui.graph.tooltipFiles"; // {0}=文件数

        // -- 提交详情 --
        public const string LoadingChanges = "ui.detail.loadingChanges";
        public const string NoChangesParsed = "ui.detail.noChangesParsed";
        public const string RootCommitNote = "ui.detail.rootCommitNote";
        public const string NoMergeConflicts = "ui.detail.noMergeConflicts";
        public const string ChangesToParent = "ui.detail.changesToParent"; // {0}=父提交短号
        public const string SectionMerged = "ui.detail.sectionMerged";

        // -- 右键动作（图谱行提交语境） --
        public const string MenuCopyHash = "menu.copyHash";
        public const string MenuCopySummary = "menu.copySummary";
        public const string MenuNewBranch = "menu.newBranch";
        public const string MenuNewBranchPrompt = "menu.newBranchPrompt"; // {0}=提交短号
        public const string MenuReset = "menu.reset";
        public const string MenuResetSoft = "menu.reset.soft";
        public const string MenuResetMixed = "menu.reset.mixed";
        public const string MenuResetHard = "menu.reset.hard";
        public const string MenuResetConfirm = "menu.reset.confirm"; // {0}=短号 {1}=模式
        public const string MenuResetHardWarn = "menu.reset.hardWarn";
        public const string MenuRevert = "menu.revert";
        public const string MenuRevertConfirm = "menu.revert.confirm"; // {0}=短号
        public const string MenuCheckout = "menu.checkout";
        public const string MenuCheckoutConfirm = "menu.checkout.confirm"; // {0}=短号
        public const string DialogOk = "dialog.ok";
        public const string DialogCancel = "dialog.cancel";
        public const string MenuOpFailedTitle = "dialog.opFailedTitle";

        // -- 窗口 Tab / Commit 页 --
        public const string TabLog = "tab.log";
        public const string TabCommit = "tab.commit";
        public const string CommitSummaryLabel = "commit.summaryLabel";
        public const string CommitBodyLabel = "commit.bodyLabel";
        public const string CommitAmend = "commit.amend";
        public const string CommitSignoff = "commit.signoff";
        public const string CommitNoVerify = "commit.noVerify";
        public const string CommitButton = "commit.commit";
        public const string CommitRefresh = "commit.refresh";
        public const string CommitClean = "commit.clean";
        public const string CommitGpgHint = "commit.gpgHint";

        // -- 文件/目录语境右键 --
        public const string MenuStage = "menu.stage";
        public const string MenuUnstage = "menu.unstage";
        public const string MenuStageAll = "menu.stageAll";
        public const string MenuUnstageAll = "menu.unstageAll";
        public const string MenuRevertFile = "menu.revertFile";
        public const string MenuDiscardConfirm = "menu.discardConfirm"; // {0}=路径或文件数
        public const string MenuDiscardCount = "menu.discardCount"; // {0}=文件数
        public const string MenuOpen = "menu.open";
        public const string MenuCopyPath = "menu.copyPath";
        public const string MenuCompareBranch = "menu.compareWithBranch";
        public const string MenuCreateTag = "menu.createTag";
        public const string CreateTagPrompt = "tag.prompt";

        // -- 分支弹窗 / Compare --
        public const string BranchTitle = "branch.title";
        public const string BranchFilter = "branch.filter";
        public const string BranchCurrent = "branch.current";
        public const string BranchNewLabel = "branch.newLabel";
        public const string BranchNew = "branch.new";
        public const string BranchTagLabel = "branch.tagLabel";
        public const string BranchTag = "branch.tag";
        public const string BranchDelete = "branch.delete";
        public const string BranchDeleteConfirm = "branch.deleteConfirm"; // {0}=分支名
        public const string BranchDeleteTagConfirm = "branch.deleteTagConfirm"; // {0}=标签名
        public const string BranchDeleteForce = "branch.deleteForce"; // {0}=分支名
        public const string BranchGroupLocal = "branch.groupLocal";
        public const string BranchGroupRemote = "branch.groupRemote";
        public const string BranchGroupTags = "branch.groupTags";
        public const string BranchCheckoutTagConfirm = "branch.checkoutTagConfirm"; // {0}=标签名

        // -- 分支面板右键动作 --
        public const string BranchCtxCheckout = "branch.ctx.checkout";
        public const string BranchCtxNewFrom = "branch.ctx.newFrom"; // {0}=基准分支/标签
        public const string BranchCtxUpdate = "branch.ctx.update";
        public const string BranchCtxPush = "branch.ctx.push";
        public const string BranchCtxFetch = "branch.ctx.fetch";
        public const string BranchCtxRename = "branch.ctx.rename";
        public const string BranchCtxRenamePrompt = "branch.ctx.renamePrompt"; // {0}=分支名
        public const string BranchCtxCompareWith = "branch.ctx.compareWith"; // {0}=对照 ref
        public const string BranchCtxMergeInto = "branch.ctx.mergeInto"; // {0}=来源 {1}=目标
        public const string BranchCtxMergeConfirm = "branch.ctx.mergeConfirm"; // {0}=来源 {1}=目标
        public const string BranchCtxUpstreamOps = "branch.ctx.upstreamOps"; // {0}=上游 ref
        public const string BranchCtxUpstreamCompare = "branch.ctx.upstreamCompare";
        public const string BranchCtxUpstreamMerge = "branch.ctx.upstreamMerge"; // {0}=当前分支
        public const string BranchCtxPushCurrentTo = "branch.ctx.pushCurrentTo"; // {0}=远程
        public const string BranchCtxNewBranchTitle = "branch.ctx.newBranchTitle";
        public const string BranchCtxNoRemoteHint = "branch.ctx.noRemoteHint";
        public const string CompareResultTitle = "compare.resultTitle"; // {0}=提交短号
        public const string CompareNoChanges = "compare.noChanges";
        public const string CompareBack = "compare.back";
        }
    }
}