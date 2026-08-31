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
        public const string AllParents = "ui.detail.allParents";
        public const string ChangesToParent = "ui.detail.changesToParent"; // {0}=父提交短号
        }
    }
}