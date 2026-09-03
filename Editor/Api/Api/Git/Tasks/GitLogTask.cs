using System.Threading;
using Unity.Editor.Tasks;

namespace Unity.VersionControl.Git.Tasks
{
    public class GitLogTask : GitProcessListTask<GitLogEntry>
    {
        private const string TaskName = "git log";
        // --branches --tags --remotes：多分支图谱必须包含所有本地分支+标签+远程跟踪分支的提交
        //（M3 人工测试实证：缺它则 feature/alpha 等独立分支 + origin/* 不在全量窗口，图谱画不全、筛选空）。
        // 不能用 --all：--all 含 refs/stash，会把 stash 的 WIP 提交混入图谱（2026-10 实测污染渲染）。
        // 三组组合与 --all 的提交集一致（rev-list --count 实测相等），但不含 stash。
        private const string baseArguments = @"-c i18n.logoutputencoding=utf8 -c core.quotepath=false log --branches --tags --remotes --pretty=format:""%H%n%P%n%aN%n%aE%n%aI%n%cN%n%cE%n%cI%n%B---GHUBODYEND---"" --name-status";
        private readonly string arguments;

        public GitLogTask(IPlatform platform,
            IGitObjectFactory gitObjectFactory,
            int numberOfCommits,
            CancellationToken token = default)
            : this(platform, gitObjectFactory, null, numberOfCommits, token)
        {}

        public GitLogTask(IPlatform platform,
            IGitObjectFactory gitObjectFactory,
            string file,
            CancellationToken token = default)
            : this(platform, gitObjectFactory, file, 0, token)
        {}

        public GitLogTask(IPlatform platform,
            IGitObjectFactory gitObjectFactory,
            string file = null, int numberOfCommits = 0,
            CancellationToken token = default)
            : base(platform, null, outputProcessor: new LogEntryOutputProcessor(gitObjectFactory), token: token)
        {
            Name = TaskName;
            arguments = baseArguments;
            if (numberOfCommits > 0)
                arguments += " -n " + numberOfCommits;

            if (file != null)
            {
                arguments += " -- ";
                arguments += " \"" + file + "\"";
            }
        }
        public override string ProcessArguments => arguments;
        public override TaskAffinity Affinity { get; set; } = TaskAffinity.Concurrent;
        public override string Message { get; set; } = "Loading the history...";
    }
}
