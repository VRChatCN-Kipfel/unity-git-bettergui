using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git rebase（非交互式，M2-SOLUTION §6 固化）：rebase &lt;branch&gt;。
    /// 编辑器阻塞防护：由 GitSession.Prepare 对全部 git 任务统一设置 GIT_EDITOR=true（Git for Windows 的
    /// sh 内建 true 立即成功，不弹编辑器）——2026-10 M3 人工测试实测：rebase --continue 默认会启动编辑器，
    /// 无抑制时 Unity 主线程同步等待 → Hold on busy 死锁 3+ 分钟。
    /// 注意：git 2.51 不支持 rebase --no-edit（"unknown option"），必须用环境变量方案。
    /// 冲突走 stderr（ProcessException 由 RunOp 包装）；进行中状态可由 status 首行
    /// "## HEAD (no branch)" + UU 条目判定（M3-SOLUTION §1.1-3 实测）。
    /// 交互式 rebase 序列编辑器 → M4（ROADMAP）。
    /// </summary>
    public sealed class GitRebaseTask : GitProcessTask<string>
    {
        public GitRebaseTask(IPlatform platform, string branch, CancellationToken token = default)
            : base(platform, "rebase " + Quote(branch), new StringOutputProcessor(), token)
        {
            Name = "git rebase";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    /// <summary>
    /// git pull --rebase（非交互式）：pull --rebase &lt;remote&gt; &lt;branch&gt;。
    /// api GitPullTask 无 --rebase 参数 → 自研（M2-SOLUTION §6 固化）。
    /// </summary>
    public sealed class GitPullRebaseTask : GitProcessTask<string>
    {
        public GitPullRebaseTask(IPlatform platform, string remote, string branch,
            CancellationToken token = default)
            : base(platform, "pull --rebase " + Quote(remote) + " " + Quote(branch),
                new StringOutputProcessor(), token)
        {
            Name = "git pull --rebase";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}