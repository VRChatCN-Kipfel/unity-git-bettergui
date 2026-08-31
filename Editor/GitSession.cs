using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private readonly string projectPath;
        // 提交变更缓存：git 对象内容寻址、不可变 -> 按提交 hash 缓存永不过期，只需内存上限（有界 FIFO 淘汰）。
        // 会话级：窗口重开/刷新会重建 GitSession，缓存随之失效（历史/refs 的可变数据另靠指纹自动失效）。
        private const int ChangesCacheLimit = 512;
        private readonly Dictionary<string, CommitChanges> changesCache = new Dictionary<string, CommitChanges>();
        private readonly Queue<string> changesCacheOrder = new Queue<string>();
        private readonly object cacheLock = new object();

        public IGitEnvironment Environment => environment;
        public IPlatform Platform => platform;

        private GitSession(IGitEnvironment env, IPlatform p, string projectPath)
        {
            environment = env;
            platform = p;
            this.projectPath = projectPath;
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

            return new GitSession(env, platform, projectPath);
        }

        /// <summary>使可变数据缓存失效（refs 等）；提交变更缓存保留（不可变）。指纹变化时由窗口调用。</summary>
        public void InvalidateCaches()
        {
            lock (cacheLock) refsCache = null;
        }

        /// <summary>仓库状态指纹（HEAD 内容 + refs 树文件 mtime/size + packed-refs/FETCH_HEAD 状态）。</summary>
        public string GetFingerprint()
        {
            try
            {
                var sb = new StringBuilder();
                var gitDir = Path.Combine(projectPath, ".git");
                if (Directory.Exists(gitDir))
                {
                    sb.Append("HEAD=").Append(ReadFileOrEmpty(Path.Combine(gitDir, "HEAD")));
                    var refsDir = Path.Combine(gitDir, "refs");
                    if (Directory.Exists(refsDir))
                    {
                        var files = new List<string>(Directory.GetFiles(refsDir, "*", SearchOption.AllDirectories));
                        files.Sort(StringComparer.Ordinal);
                        foreach (var f in files)
                        {
                            var info = new FileInfo(f);
                            sb.Append('|').Append(info.FullName.Substring(gitDir.Length)).Append(':')
                                .Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length);
                        }
                    }
                    var packed = Path.Combine(gitDir, "packed-refs");
                    if (File.Exists(packed))
                    {
                        var info = new FileInfo(packed);
                        sb.Append("|packed-refs:").Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length);
                    }
                    var fetchHead = Path.Combine(gitDir, "FETCH_HEAD");
                    if (File.Exists(fetchHead))
                    {
                        var info = new FileInfo(fetchHead);
                        sb.Append("|FETCH_HEAD:").Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length);
                    }
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string ReadFileOrEmpty(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
            catch { return ""; }
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
                    // 两视图互不依赖 —— 并行启动两条 git 进程（延迟 = max 而非串行求和）
                    var combinedTask = System.Threading.Tasks.Task.Run(
                        () => RunNameStatus($"diff-tree --no-commit-id -c -r --name-status -M {commitId}"));
                    var sectionsTask = System.Threading.Tasks.Task.Run(
                        () => RunNameStatusSections($"show -m --name-status -M {commitId}"));

                    // 合并视图：相对全部父（git diff-tree -c 组合 diff 语义；无冲突时为空）
                    result.Combined = combinedTask.Result;
                    // 每父视图：git show -m（--diff-merges=separate 语义），每段头 "commit …(from parent)" 按父顺序
                    var sections = sectionsTask.Result;
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

                lock (cacheLock)
                {
                    changesCache[commitId] = result;
                    changesCacheOrder.Enqueue(commitId);
                    while (changesCacheOrder.Count > ChangesCacheLimit)
                    {
                        var evict = changesCacheOrder.Dequeue();
                        changesCache.Remove(evict);
                    }
                }
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>ref 类型（排序组）。</summary>
        public enum RefType { Head, Local, Remote, Tag }

        /// <summary>一条 ref 标签（显示名 + 目标提交）。</summary>
        public sealed class GitRefInfo
        {
            public RefType Type;
            public string DisplayName;
            public string CommitId;
            public bool IsCurrentHead;
        }

        private List<GitRefInfo> refsCache; // refs 可变：会话级缓存，后续由 RepositoryWatcher 失效

        /// <summary>
        /// 加载 ref → commit 映射（git for-each-ref，兼容 packed refs）。
        /// 排序对齐 JetBrains GitRefManager.groupForTable：HEAD/当前分支 -> 本地 -> remote -> tags（组内名序）。
        /// </summary>
        public List<GitRefInfo> LoadRefs()
        {
            if (refsCache != null) return refsCache;

            var task = new GitForEachRefTask(platform).Configure(platform.ProcessManager);
            var output = task.RunSynchronously();
            var result = new List<GitRefInfo>();
            if (task.Successful && output != null)
            {
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var refName = parts[0];
                    var commit = parts[1];
                    var isHead = parts.Length > 2 && parts[2].Trim() == "*";

                    GitRefInfo info;
                    if (refName.StartsWith("refs/remotes/", StringComparison.Ordinal))
                        info = new GitRefInfo { Type = RefType.Remote, DisplayName = refName.Substring("refs/remotes/".Length), CommitId = commit };
                    else if (refName.StartsWith("refs/tags/", StringComparison.Ordinal))
                        info = new GitRefInfo { Type = RefType.Tag, DisplayName = refName.Substring("refs/tags/".Length), CommitId = commit };
                    else if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
                        info = new GitRefInfo { Type = isHead ? RefType.Head : RefType.Local, DisplayName = refName.Substring("refs/heads/".Length), CommitId = commit, IsCurrentHead = isHead };
                    else
                        continue;
                    result.Add(info);
                }

                result.Sort((a, b) =>
                {
                    var g = GroupOrder(a.Type).CompareTo(GroupOrder(b.Type));
                    return g != 0 ? g : string.CompareOrdinal(a.DisplayName, b.DisplayName);
                });
            }

            lock (cacheLock) refsCache = result;
            return result;
        }

        private static int GroupOrder(RefType t)
        {
            switch (t)
            {
                case RefType.Head: return 0;
                case RefType.Local: return 1;
                case RefType.Remote: return 2;
                default: return 3;
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