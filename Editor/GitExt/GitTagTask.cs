using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git tag -a（自研，非子树内容）：上游 api 无 tag 任务。
    /// 附注信息转义双引号（名称/提交合法性交给 git 校验并回显错误）。
    /// </summary>
    public sealed class GitTagTask : GitProcessTask<string>
    {
        public GitTagTask(IPlatform platform,
            string name, string commitHash, string message,
            CancellationToken token = default)
            : base(platform,
                $"tag -a {name} {commitHash} -m \"{Sanitize(message)}\"",
                new StringOutputProcessor(), token)
        {
            Name = "git tag -a";
        }

        private static string Sanitize(string message)
        {
            return (message ?? string.Empty).Replace("\"", "\\\"");
        }
    }
}