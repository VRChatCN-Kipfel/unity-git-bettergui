using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git blame 任务（自研，非子树内容）：blame --porcelain（机器可解析，M3-SOLUTION §1.1-11 实测格式）。
    /// porcelain 行：&lt;sha&gt; &lt;orig-line&gt; &lt;final-line&gt; &lt;group-lines&gt;，后随 author/summary/filename 等
    /// key-value 头，正文行以 \t 开头。
    /// </summary>
    public sealed class GitBlameTask : GitProcessTask<string>
    {
        public GitBlameTask(IPlatform platform, string path, CancellationToken token = default)
            : base(platform, "blame --porcelain " + Quote(path), new StringOutputProcessor(), token)
        {
            Name = "git blame";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}