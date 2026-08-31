using System.Collections.Generic;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.IO;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git checkout -- &lt;paths&gt;（撤销工作区改动到 HEAD；git 2.23 之前的等效 restore）。
    /// 路径引号包裹 + 正斜杠（与 GitAddTask 同法）。
    /// </summary>
    public sealed class GitCheckoutPathsTask : GitProcessTask<string>
    {
        public GitCheckoutPathsTask(IPlatform platform,
            IEnumerable<string> files,
            CancellationToken token = default)
            : base(platform, "checkout --" + JoinQuoted(files),
                new StringOutputProcessor(), token)
        {
            Name = "git checkout --";
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