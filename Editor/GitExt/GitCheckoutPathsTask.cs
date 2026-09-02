using System.Collections.Generic;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.IO;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>checkout 冲突侧（3-way 整侧接受；M3-SOLUTION §3.4）。</summary>
    public enum GitCheckoutSide
    {
        /// <summary>checkout -- paths（撤销到 HEAD，原语义）。</summary>
        None,
        /// <summary>checkout --ours -- paths（取当前分支侧）。</summary>
        Ours,
        /// <summary>checkout --theirs -- paths（取合并进来的一侧）。</summary>
        Theirs
    }

    /// <summary>
    /// git checkout [--ours|--theirs] -- &lt;paths&gt;：
    /// 撤销工作区改动（None）或冲突整侧接受（Ours/Theirs，M3 3-way 视图）。
    /// 注意（M3-SOLUTION §1.1-5 实测）：checkout --ours/--theirs 不全清 UU——之后必须 git add
    /// （或 DELETED 侧 git rm）标记解决；路径引号包裹 + 正斜杠（与 GitAddTask 同法）。
    /// </summary>
    public sealed class GitCheckoutPathsTask : GitProcessTask<string>
    {
        public GitCheckoutPathsTask(IPlatform platform,
            IEnumerable<string> files,
            CancellationToken token = default)
            : this(platform, files, GitCheckoutSide.None, token)
        {
        }

        public GitCheckoutPathsTask(IPlatform platform,
            IEnumerable<string> files, GitCheckoutSide side,
            CancellationToken token = default)
            : base(platform, BuildArgs(files, side),
                new StringOutputProcessor(), token)
        {
            Name = "git checkout";
        }

        private static string BuildArgs(IEnumerable<string> files, GitCheckoutSide side)
        {
            var sideArg = side == GitCheckoutSide.Ours ? " --ours"
                : side == GitCheckoutSide.Theirs ? " --theirs" : "";
            return "checkout" + sideArg + " --" + JoinQuoted(files);
        }

        private static string JoinQuoted(IEnumerable<string> files)
        {
            Guard.ArgumentNotNull(files, "files");
            var sb = new System.Text.StringBuilder();
            foreach (var file in files)
                sb.Append(" \"").Append(file.ToSPath().ToString(SlashMode.Forward)).Append('"');
            return sb.ToString();
        }
    }
}