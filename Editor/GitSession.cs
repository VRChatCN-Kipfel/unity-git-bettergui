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
        public string ProjectPath => projectPath;

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

        /// <summary>加载提交历史（含 parents 全量 + 文件变更列表），返回前强制拓扑序。</summary>
        public List<GitLogEntry> LoadHistory(int numberOfCommits = 0)
        {
            var task = new GitLogTask(platform, new GitObjectFactory(environment), null, numberOfCommits, CancellationToken.None)
                .Configure(platform.ProcessManager);
            var result = task.RunSynchronously();
            if (!task.Successful || result == null)
                throw new InvalidOperationException("git log failed: " + task.Errors);
            return EnsureTopologicalOrder(result);
        }

        /// <summary>
        /// 拓扑序兜底：git log 默认按日期排序，时间戳相同时可能是非拓扑序（子出现在父上方），
        /// 引擎（EdgesInRow/泳道 DFS/simple 链）假定"父必在行下方"。这里稳定地重排为 子先父后：
        /// 一个条目可出队当且仅当其"窗口内的所有子"都已出队（子=引用本提交为父的条目）；
        /// 窗口外的子忽略。输入已拓扑时输出与原序一致。
        /// </summary>
        private static List<GitLogEntry> EnsureTopologicalOrder(List<GitLogEntry> entries)
        {
            if (entries.Count <= 1) return entries;
            var index = new Dictionary<string, int>();
            for (var i = 0; i < entries.Count; i++) index[entries[i].CommitID] = i;

            // childSets[i]：窗口内以 i 为父的条目号
            var childSets = new List<List<int>>(entries.Count);
            for (var i = 0; i < entries.Count; i++) childSets.Add(new List<int>());
            for (var c = 0; c < entries.Count; c++)
            {
                var parents = entries[c].Parents;
                if (parents == null) continue;
                foreach (var p in parents)
                    if (index.TryGetValue(p, out var pi) && pi != c)
                        childSets[pi].Add(c);
            }

            var used = new bool[entries.Count];
            var result = new List<GitLogEntry>(entries.Count);
            var remaining = entries.Count;
            while (remaining > 0)
            {
                var picked = -1;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (used[i]) continue;
                    var ready = true;
                    foreach (var c in childSets[i])
                        if (!used[c]) { ready = false; break; }
                    if (ready) { picked = i; break; }
                }
                if (picked == -1) // 环（异常数据）：取第一个未放置，防死循环
                    for (var i = 0; i < entries.Count; i++)
                        if (!used[i]) { picked = i; break; }

                used[picked] = true;
                result.Add(entries[picked]);
                remaining--;
            }
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
                    // 串行执行（两个 git 进程并行曾在冒烟中出现偶发竞态；只读命令串行代价可忽略）
                    result.Combined = RunNameStatus($"diff-tree --no-commit-id -c -r --name-status -M {commitId}");
                    // 每父视图：git show -m（--diff-merges=separate 语义），每段头 "commit …(from parent)" 按父顺序
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

        // ---- 操作通道（左侧右键/Commit/分支弹窗共用；全部任务后台线程执行由调用方保证） ----

        /// <summary>执行 git 操作任务；失败抛 InvalidOperationException（附 stderr）。</summary>
        private static void RunOp(string name, ITask task)
        {
            task.RunSynchronously();
            if (task.Successful) return;
            var err = task.Errors;
            throw new InvalidOperationException(name + " failed"
                + (string.IsNullOrEmpty(err) ? " (exit code non-zero)" : ":\n" + err));
        }

        /// <summary>
        /// 工作区状态（status -b -u --porcelain）：localBranch/remoteBranch/ahead/behind + entries(X/Y)。
        /// 未使用缓存：窗口按 1.5s 指纹节流重建，Commit 页操作后主动重载。
        /// </summary>
        public GitStatus LoadStatus()
        {
            var task = new GitStatusTask(platform, new GitObjectFactory(environment))
                .Configure(platform.ProcessManager);
            var result = task.RunSynchronously();
            if (!task.Successful)
                throw new InvalidOperationException("git status failed" +
                    (string.IsNullOrEmpty(task.Errors) ? "" : ":\n" + task.Errors));
            return result;
        }

        /// <summary>暂存指定路径（git add -- paths；含未跟踪文件）。</summary>
        public void Stage(IEnumerable<string> paths)
        {
            RunOp("git add", new GitAddTask(platform, paths).Configure(platform.ProcessManager));
        }

        /// <summary>取消暂存（git reset HEAD -- paths；不丢工作区改动）。</summary>
        public void Unstage(IEnumerable<string> paths)
        {
            RunOp("git reset HEAD", new GitRemoveFromIndexTask(platform, paths).Configure(platform.ProcessManager));
        }

        /// <summary>撤销工作区改动到 HEAD（git checkout -- paths；危险操作由 UI 层确认）。</summary>
        public void Discard(IEnumerable<string> paths)
        {
            RunOp("git checkout --", new GitCheckoutPathsTask(platform, paths).Configure(platform.ProcessManager));
        }

        /// <summary>提交（git commit -F 临时消息文件 + flaga；失败抛 stderr（含 gpg 探测）。</summary>
        public void Commit(string message, string body, bool amend, bool signoff, bool noVerify)
        {
            RunOp("git commit",
                new GitCommitTaskEx(platform, message, body, amend, signoff, noVerify)
                    .Configure(platform.ProcessManager));
        }

        /// <summary>提交失败 stderr 探测：gpg 不可用/未解锁（gpgsign=true 环境常见）。</summary>
        public static bool DetectGpgError(string errors)
        {
            return errors != null
                && (errors.Contains("gpg failed to sign the data", StringComparison.Ordinal)
                    || errors.Contains("failed to sign the data", StringComparison.Ordinal));
        }

        /// <summary>重置当前分支到指定提交（--soft/--mixed/--hard，带确认由 UI 层负责）。</summary>
        public void ResetTo(string hash, GitResetMode mode)
        {
            RunOp("git reset", new GitResetTask(platform, hash, mode).Configure(platform.ProcessManager));
        }

        /// <summary>撤销指定提交（git revert --no-edit，生成新提交）。</summary>
        public void RevertCommit(string hash)
        {
            RunOp("git revert", new GitRevertTask(platform, hash).Configure(platform.ProcessManager));
        }

        /// <summary>基于指定 ref 新建分支（不切换；名称合法性交给 git 校验并回显错误）。</summary>
        public void NewBranch(string newName, string baseRef)
        {
            RunOp("git branch", new GitBranchCreateTask(platform, newName, baseRef).Configure(platform.ProcessManager));
        }

        /// <summary>检出分支/提交（提交会进入 detached HEAD，提示由 UI 层负责）。</summary>
        public void Checkout(string gitRef)
        {
            RunOp("git checkout", new GitSwitchBranchesTask(platform, gitRef).Configure(platform.ProcessManager));
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