using System.Text;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>git apply 操作模式（M3-SOLUTION §3.3/1.6 定版三态）。</summary>
    public enum GitApplyMode
    {
        /// <summary>暂存到 index：apply --cached（patch 取工作区 diff）。</summary>
        Stage,
        /// <summary>取消暂存：apply --cached -R（patch 取 --cached diff，反向应用回 index）。</summary>
        Unstage,
        /// <summary>撤销工作区：apply -R（patch 取工作区 diff 反向应用）。</summary>
        Revert
    }

    /// <summary>
    /// git apply 任务（自研，非子树内容）：hunk 级 stage/unstage/revert 的执行通道。
    /// 危险点（M3-SOLUTION §1.6 实测）：patch 文件必须 LF 行尾 + 末尾换行 + 保留 "\ No newline" 标记，
    /// 否则 git apply exit=0 却静默不生效。patch 内容由调用方通过 GitPatchBuilder 生成（原样切片）。
    /// 失败（非零退出码）经 RunSynchronously 抛 ProcessException，由 GitSession.RunOp 统一包装。
    /// </summary>
    public sealed class GitApplyTask : GitProcessTask<string>
    {
        public GitApplyTask(IPlatform platform, string patchFilePath, GitApplyMode mode,
            CancellationToken token = default)
            : base(platform, BuildArgs(patchFilePath, mode), new StringOutputProcessor(), token)
        {
            Name = "git apply";
        }

        private static string BuildArgs(string patchFilePath, GitApplyMode mode)
        {
            var sb = new StringBuilder("apply --whitespace=nowarn");
            switch (mode)
            {
                case GitApplyMode.Stage:
                    sb.Append(" --cached");
                    break;
                case GitApplyMode.Unstage:
                    sb.Append(" --cached -R");
                    break;
                case GitApplyMode.Revert:
                    sb.Append(" -R");
                    break;
            }
            // 路径引号包裹（安全；patch 文件由我们写到 .git/ 临时目录，无空格但保持一致性）
            sb.Append(" \"").Append(patchFilePath.Replace('\\', '/')).Append('"');
            return sb.ToString();
        }
    }
}