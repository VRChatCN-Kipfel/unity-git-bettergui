using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git cherry-pick（自研，非子树内容）：git cherry-pick &lt;hash&gt;（默认自动提交；-n 可选不提交）。
    /// 无冲突直接完成；冲突走 stderr（经 GitSession 容错判定，进 3-way 视图）。
    /// </summary>
    public sealed class GitCherryPickTask : GitProcessTask<string>
    {
        public GitCherryPickTask(IPlatform platform, string commitHash,
            bool noCommit = false, CancellationToken token = default)
            : base(platform, "cherry-pick" + (noCommit ? " -n" : "") + " " + Quote(commitHash),
                new StringOutputProcessor(), token)
        {
            Name = "git cherry-pick";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}