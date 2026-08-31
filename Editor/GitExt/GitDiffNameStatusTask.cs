using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git diff/diff-tree --name-status 任务（在 api 任务框架上自研，非子树内容）。
    /// 用途：merge 提交的"相对第一父的变更"（JetBrains DIFF_TO_PARENTS 语义）、root 提交全树。
    /// git log --name-status 默认不对 merge 出 diff，故按需单独跑。
    /// </summary>
    public class GitDiffNameStatusTask : GitProcessTask<string>
    {
        public GitDiffNameStatusTask(IPlatform platform, string arguments, CancellationToken token = default)
            : base(platform, arguments, new StringOutputProcessor(), token)
        {
            Name = "git diff --name-status";
        }
    }
}