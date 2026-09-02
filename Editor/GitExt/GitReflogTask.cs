using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git reflog 任务（自研，非子树内容）：最近 HEAD 移动记录。
    /// 输出行：&lt;short&gt;|&lt;gd&gt;|&lt;gs&gt;（%h|%gd|%gs，'|' 分隔，M3-SOLUTION §1.1-2 实测格式）。
    /// 用途：最近签出分支（%gs 形如 "checkout: moving from A to B"；B 即最近分支）。
    /// </summary>
    public sealed class GitReflogTask : GitProcessTask<string>
    {
        public GitReflogTask(IPlatform platform, int count, CancellationToken token = default)
            : base(platform,
                "reflog --format=%h|%gd|%gs -" + count,
                new StringOutputProcessor(), token)
        {
            Name = "git reflog";
        }
    }
}