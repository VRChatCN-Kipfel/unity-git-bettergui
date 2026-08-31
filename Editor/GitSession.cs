using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.IO;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// Git 会话服务：封装 unity-git-bettergui 对 com.spoiledcat.git.api 的最小装配。
    /// 装配要点（基于 fork 源码实证）：
    ///   - ApplicationEnvironment 实现 IGitEnvironment（UnityEnvironment 子类）
    ///   - Platform 提供 TaskManager / ProcessManager / GitClient
    ///   - GitInstaller.FindSystemGit 只做系统 git 探活 + 版本校验，不触发 Dugite 下载
    ///   - 每个 Task 必须 .Configure(platform.ProcessManager) 才能跑（否则 Wrapper 为 null）
    /// </summary>
    public sealed class GitSession : IDisposable
    {
        private readonly IGitEnvironment environment;
        private readonly IPlatform platform;

        public IGitEnvironment Environment => environment;
        public IPlatform Platform => platform;

        private GitSession(IGitEnvironment env, IPlatform p)
        {
            environment = env;
            platform = p;
        }

        /// <summary>在给定项目目录建立 git 会话（探活系统 git + 绑定仓库）。git 缺失/<2.0 时抛异常。</summary>
        public static GitSession Open(string projectPath)
        {
            var env = new ApplicationEnvironment("unity-git-bettergui");
            env.Initialize(projectPath.ToSPath(), projectPath);

            var platform = new Platform(env);
            platform.Initialize(SynchronizationContext.Current);

            var installer = new GitInstaller(platform);
            var state = installer.FindSystemGit(new GitInstaller.GitInstallationState());
            env.GitInstallationState = state;

            if (!state.GitIsValid)
                throw new InvalidOperationException(
                    $"git not found or below v{Constants.MinimumGitVersion} ({state.GitExecutablePath})");

            env.InitializeRepository();
            if (!env.RepositoryPath.IsInitialized || !env.RepositoryPath.Exists(".git"))
                throw new InvalidOperationException("project is not a git repository: " + projectPath);

            return new GitSession(env, platform);
        }

        /// <summary>加载提交历史（含 parents 全量 + 文件变更列表）。</summary>
        public List<GitLogEntry> LoadHistory(int numberOfCommits = 0)
        {
            var task = new GitLogTask(platform, new GitObjectFactory(environment), null, numberOfCommits, CancellationToken.None)
                .Configure(platform.ProcessManager);
            var result = task.RunSynchronously();
            if (!task.Successful || result == null)
                throw new InvalidOperationException("git log failed: " + task.Errors);
            return result;
        }

        public void Dispose()
        {
            (platform as IDisposable)?.Dispose();
        }
    }
}