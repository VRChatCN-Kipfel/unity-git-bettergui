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
        private readonly Dictionary<string, CommitChanges> changesCache = new Dictionary<string, CommitChanges>();
        private readonly object cacheLock = new object();

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

        /// <summary>提交变更集合（对齐 JetBrains DIFF_TO_PARENTS 数据模型 + VcsLogAsyncChangesTreeModel 展示）。</summary>
        public sealed class CommitChanges
        {
            /// <summary>合并视图：相对**全部**父都不同的文件（git diff-tree -c；为空 = 无 merge 冲突）。</summary>
            public List<(char Status, string Path)> Combined = new List<(char, string)>();

            /// <summary>每父视图：与 entry.Parents 一一对应（相对第 i 父的 diff）。</summary>
            public List<List<(char, string)>> PerParent = new List<List<(char, string)>>();
        }

        /// <summary>
        /// 按需加载提交变更（JetBrains DIFF_TO_PARENTS 语义）：
        ///   merge：合并视图（-c，相对全部父）+ 每父 diff（^1、^2 ...）
        ///   root：全树（diff-tree --root）
        /// 常规提交由 GitLogTask 已带，无需调用。失败返回 null。
        /// </summary>
        public CommitChanges LoadChangesFor(GitLogEntry entry)
        {
            var commitId = entry.CommitID;
            lock (cacheLock)
                if (changesCache.TryGetValue(commitId, out var cached)) return cached;

            try
            {
                var result = new CommitChanges();
                var parentCount = entry.Parents?.Count ?? 0;

                if (parentCount > 1)
                {
                    // 合并视图：相对全部父（git diff-tree -c 组合 diff 语义；无冲突时为空）
                    result.Combined = RunNameStatus($"diff-tree --no-commit-id -c -r --name-status -M {commitId}");

                    // 每父视图：一条 git show -m（--diff-merges=separate 语义），每段头 "commit …(from parent)" 按父顺序
                    var sections = RunNameStatusSections($"show -m --name-status -M {commitId}");
                    for (var i = 0; i < parentCount; i++)
                        result.PerParent.Add(i < sections.Count ? sections[i] : new List<(char, string)>());
                }
                else if (parentCount == 0)
                {
                    // root 提交：相对空树全量新增
                    result.Combined = RunNameStatus($"diff-tree --no-commit-id --root -r --name-status -M {commitId}");
                }
                else
                {
                    return null; // 常规提交已在 GitLogTask 中带
                }

                lock (cacheLock) changesCache[commitId] = result;
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private List<(char, string)> RunNameStatus(string arguments)
        {
            var task = new GitDiffNameStatusTask(platform, arguments)
                .Configure(platform.ProcessManager);
            var output = task.RunSynchronously();
            if (!task.Successful || output == null) return new List<(char, string)>();
            return ParseNameStatusLines(output);
        }

        /// <summary>git show -m：每父一段，段头为 "commit &lt;merge&gt; (from &lt;parent&gt;)"；取段内 name-status 行。</summary>
        private List<List<(char, string)>> RunNameStatusSections(string arguments)
        {
            var task = new GitDiffNameStatusTask(platform, arguments)
                .Configure(platform.ProcessManager);
            var output = task.RunSynchronously();
            var result = new List<List<(char, string)>>();
            if (!task.Successful || output == null) return result;

            List<(char, string)> current = null;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("commit "))
                {
                    current = new List<(char, string)>();
                    result.Add(current);
                    continue;
                }
                if (current == null) continue;
                var tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                current.Add((line[0], line.Substring(tab + 1)));
            }
            return result;
        }

        private static List<(char, string)> ParseNameStatusLines(string output)
        {
            var result = new List<(char, string)>();
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                var marker = parts[0].Length > 0 ? parts[0][0] : 'M';
                result.Add((marker, parts[parts.Length - 1]));
            }
            return result;
        }

        public void Dispose()
        {
            (platform as IDisposable)?.Dispose();
        }
    }
}