using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git for-each-ref 任务（自研，非子树内容）：ref → commit 映射，兼容 packed refs。
    /// 输出行：&lt;refname&gt;\t&lt;objectname&gt;\t&lt;HEAD 标记&gt;（HEAD 指向的分支第 3 列为 "*"）。
    /// </summary>
    public class GitForEachRefTask : GitProcessTask<string>
    {
        public GitForEachRefTask(IPlatform platform, CancellationToken token = default)
            : base(platform,
                "for-each-ref --format=%(refname)%09%(objectname)%09%(HEAD)",
                new StringOutputProcessor(), token)
        {
            Name = "git for-each-ref";
        }
    }
}