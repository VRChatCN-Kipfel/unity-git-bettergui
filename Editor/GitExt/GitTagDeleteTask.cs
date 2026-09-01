using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git tag -d（删除标签，自研，非子树内容）。名称引号包裹防空格/特殊字符。
    /// </summary>
    public sealed class GitTagDeleteTask : GitProcessTask<string>
    {
        public GitTagDeleteTask(IPlatform platform, string name, CancellationToken token = default)
            : base(platform, $"tag -d \"{name}\"", new StringOutputProcessor(), token)
        {
            Name = "git tag -d";
        }
    }
}