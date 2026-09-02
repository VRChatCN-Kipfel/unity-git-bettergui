using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git for-each-ref 任务（自研，非子树内容）：ref → commit 映射，兼容 packed refs。
    /// 输出行：&lt;refname&gt;\t&lt;objectname&gt;\t&lt;HEAD 标记&gt;\t&lt;upstream:short&gt;\t&lt;upstream:track&gt;
    /// （本地分支第 3 列 "*" 表示当前 HEAD；第 4 列为其跟踪的远程分支，如 "origin/main"，空=无跟踪；
    /// 第 5 列 = "[ahead N, behind M]" / "[gone]" / 空=同步，M3-SOLUTION §1.1-1 实测格式）。
    /// </summary>
    public class GitForEachRefTask : GitProcessTask<string>
    {
        public GitForEachRefTask(IPlatform platform, CancellationToken token = default)
            : base(platform,
                "for-each-ref --format=%(refname)%09%(objectname)%09%(HEAD)%09%(upstream:short)%09%(upstream:track)",
                new StringOutputProcessor(), token)
        {
            Name = "git for-each-ref";
        }
    }
}