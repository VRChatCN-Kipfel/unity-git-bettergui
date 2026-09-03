using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git merge（自研，非子树内容）：上游 api 无 merge 任务。
    /// --no-edit 避免打开消息编辑器；冲突走 stderr（由 RunOp 包装为 InvalidOperationException）。
    /// Abort 模式：merge --abort（3-way 视图中止冲突合并）。
    /// </summary>
    public sealed class GitMergeTask : GitProcessTask<string>
    {
        public GitMergeTask(IPlatform platform, string gitRef, CancellationToken token = default)
            : base(platform, $"merge --no-edit {gitRef}", new StringOutputProcessor(), token)
        {
            Name = "git merge";
        }

        public GitMergeTask(IPlatform platform, bool abort, CancellationToken token = default)
            : base(platform, "merge --abort", new StringOutputProcessor(), token)
        {
            Name = "git merge --abort";
        }
    }
}