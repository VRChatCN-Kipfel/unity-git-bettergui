using System;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.IO;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git commit 扩展任务（自研，非子树内容）：上游 GitCommitTask 仅 --file 消息文件，
    /// 这里补 --amend / --signoff / --no-verify（JetBrains GitRepositoryCommitter 参数交集）。
    /// </summary>
    public sealed class GitCommitTaskEx : GitProcessTask<string>
    {
        private readonly string message;
        private readonly string body;
        private readonly string arguments;

        private SPath tempFile;

        public GitCommitTaskEx(IPlatform platform,
            string message, string body,
            bool amend = false, bool signoff = false, bool noVerify = false,
            CancellationToken token = default)
            : base(platform, null, outputProcessor: new StringOutputProcessor(), token)
        {
            Guard.ArgumentNotNullOrWhiteSpace(message, "message");
            Name = "git commit";
            this.message = message;
            this.body = body ?? string.Empty;

            tempFile = SPath.GetTempFilename("GitCommitTaskEx");
            var args = "-c i18n.commitencoding=utf8 commit --file \"{0}\"";
            if (amend) args += " --amend";
            if (signoff) args += " --signoff";
            if (noVerify) args += " --no-verify";
            arguments = string.Format(args, tempFile);
        }

        protected override void RaiseOnStart()
        {
            base.RaiseOnStart();
            tempFile.WriteAllLines(new[] { message, Environment.NewLine, body });
        }

        protected override void RaiseOnEnd()
        {
            tempFile.DeleteIfExists();
            base.RaiseOnEnd();
        }

        public override string ProcessArguments => arguments;
        public override TaskAffinity Affinity { get; set; } = TaskAffinity.Exclusive;
        public override string Message { get; set; } = "Committing...";
    }
}