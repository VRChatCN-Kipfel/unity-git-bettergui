using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git branch -m（自研，非子树内容）：本地分支重命名。
    /// </summary>
    public sealed class GitBranchRenameTask : GitProcessTask<string>
    {
        public GitBranchRenameTask(IPlatform platform, string oldName, string newName,
            CancellationToken token = default)
            : base(platform, $"branch -m {oldName} {newName}", new StringOutputProcessor(), token)
        {
            Name = "git branch -m";
        }
    }
}