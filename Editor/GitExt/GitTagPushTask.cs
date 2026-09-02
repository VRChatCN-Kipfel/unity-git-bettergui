using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// 推送标签到远程（git push &lt;remote&gt; refs/tags/&lt;tag&gt;；自研——GitPushTask 不支持自定义 refspec）。
    /// 参照 GitRemoteBranchDeleteTask 的 push 语法；tag refspec 显式 refs/tags/ 前缀避免歧义（探针实测有效）。
    /// </summary>
    public sealed class GitTagPushTask : GitProcessTask<string>
    {
        public GitTagPushTask(IPlatform platform, string remote, string tag,
            CancellationToken token = default)
            : base(platform, "push " + Quote(remote) + " refs/tags/" + Quote(tag),
                new StringOutputProcessor(), token)
        {
            Name = "git push tag";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    /// <summary>
    /// 删除远程标签（git push &lt;remote&gt; --delete refs/tags/&lt;tag&gt;；自研）。
    /// 与 GitRemoteBranchDeleteTask（远程分支删除）同构；调用前应先 ls-remote --tags 确认存在
    /// （JetBrains GitDeleteRemoteTagOperation 语义）。
    /// </summary>
    public sealed class GitRemoteTagDeleteTask : GitProcessTask<string>
    {
        public GitRemoteTagDeleteTask(IPlatform platform, string remote, string tag,
            CancellationToken token = default)
            : base(platform, "push " + Quote(remote) + " --delete refs/tags/" + Quote(tag),
                new StringOutputProcessor(), token)
        {
            Name = "git push --delete tag";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}