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

        /// <summary>
        /// 会话内 git 任务通用准备：Configure 之后把进程工作目录钉到仓库根。
        /// api 的 ProcessEnvironment.Configure 未设置 WorkingDirectory（源码注释掉），
        /// 默认会落到编辑器工程目录/Unity cwd —— 仓库 ≠ 工程目录时必须钉住（如端到端冒烟仓库）。
        /// </summary>
        private void Prepare(IProcessTask task)
        {
            if (task?.Wrapper?.StartInfo != null)
                task.Wrapper.StartInfo.WorkingDirectory = projectPath;
        }

        /// <summary>加载提交历史（含 parents 全量 + 文件变更列表），返回前强制拓扑序。</summary>
        public List<GitLogEntry> LoadHistory(int numberOfCommits = 0)
        {
            return LoadHistory(numberOfCommits, null);
        }

        /// <summary>
        /// 加载提交历史（可选 revision 分支过滤，JetBrains Log 分支筛选器语义）：
        /// revision 为 null = 全部分支；否则只保留该 ref（本地分支名 / refs/remotes/… / refs/tags/…）及其祖先。
        /// 实现为全量窗口 + 内存祖先过滤（LogEntryOutputProcessor 为 internal，无法在 GitExt 复刻任务；
        /// 窗口 ≤ 200 行时与 git log &lt;ref&gt; 语义等价）。
        /// </summary>
        public List<GitLogEntry> LoadHistory(int numberOfCommits, string revision)
        {
            var task = new GitLogTask(platform, new GitObjectFactory(environment), null, numberOfCommits, CancellationToken.None)
                .Configure(platform.ProcessManager);
            Prepare(task);
            var result = task.RunSynchronously();
            if (!task.Successful || result == null)
                throw new InvalidOperationException("git log failed: " + task.Errors);

            if (revision == null) return EnsureTopologicalOrder(result);

            // 内存祖先过滤：从 ref 的提交出发沿父收集窗口内可达集
            var commitId = FindRevisionCommitId(revision);
            if (commitId == null) return new List<GitLogEntry>();

            var index = new Dictionary<string, int>();
            for (var i = 0; i < result.Count; i++) index[result[i].CommitID] = i;
            if (!index.ContainsKey(commitId)) return new List<GitLogEntry>();

            var reachable = new HashSet<string> { commitId };
            var queue = new Queue<string>();
            queue.Enqueue(commitId);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!index.TryGetValue(cur, out var row)) continue;
                foreach (var p in result[row].Parents)
                    if (index.ContainsKey(p) && reachable.Add(p))
                        queue.Enqueue(p);
            }

            var kept = new List<GitLogEntry>();
            foreach (var e in result)
                if (reachable.Contains(e.CommitID)) kept.Add(e);
            return EnsureTopologicalOrder(kept);
        }

        /// <summary>GitRefInfo → 图谱筛选 refspec（本地=名；远程=refs/remotes/…；标签=refs/tags/…）。</summary>
        public static string ToRevision(GitSession.GitRefInfo r)
        {
            switch (r.Type)
            {
                case RefType.Remote: return "refs/remotes/" + r.DisplayName;
                case RefType.Tag: return "refs/tags/" + r.DisplayName;
                default: return r.DisplayName;
            }
        }

        private string FindRevisionCommitId(string revision)
        {
            List<GitRefInfo> refsNow;
            try { refsNow = LoadRefs(); } catch { return null; }
            if (refsNow == null) return null;
            foreach (var r in refsNow)
                if (ToRevision(r) == revision) return r.CommitId;
            return null;
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

        /// <summary>一条 ref 标签（显示名 + 目标提交 + 可选跟踪上游 + 分支级 ahead/behind）。</summary>
        public sealed class GitRefInfo
        {
            public RefType Type;
            public string DisplayName;
            public string CommitId;
            public bool IsCurrentHead;
            /// <summary>本地分支跟踪的远程分支（如 "origin/main"）；远程/标签/未跟踪为 null。</summary>
            public string Upstream;
            /// <summary>相对上游领先提交数（%(upstream:track) 解析；无上游/未跟踪为 0）。</summary>
            public int Ahead;
            /// <summary>相对上游落后提交数（同上）。</summary>
            public int Behind;
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
            Prepare(task);
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
                    var upstream = parts.Length > 3 ? parts[3].Trim() : null;
                    if (string.IsNullOrEmpty(upstream)) upstream = null;
                    // 第 5 列 = %(upstream:track)："[ahead N, behind M]" / "[gone]" / 空=同步
                    var (ahead, behind) = ParseUpstreamTrack(parts.Length > 4 ? parts[4] : null);

                    GitRefInfo info;
                    if (refName.StartsWith("refs/remotes/", StringComparison.Ordinal))
                        info = new GitRefInfo { Type = RefType.Remote, DisplayName = refName.Substring("refs/remotes/".Length), CommitId = commit };
                    else if (refName.StartsWith("refs/tags/", StringComparison.Ordinal))
                        info = new GitRefInfo { Type = RefType.Tag, DisplayName = refName.Substring("refs/tags/".Length), CommitId = commit };
                    else if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
                        info = new GitRefInfo { Type = isHead ? RefType.Head : RefType.Local, DisplayName = refName.Substring("refs/heads/".Length), CommitId = commit, IsCurrentHead = isHead, Upstream = upstream, Ahead = ahead, Behind = behind };
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

        /// <summary>解析 %(upstream:track) 列："[ahead N, behind M]" / "[ahead N]" / "[behind M]" / "[gone]" / 空。</summary>
        public static (int ahead, int behind) ParseUpstreamTrack(string track)
        {
            var ahead = 0;
            var behind = 0;
            if (string.IsNullOrEmpty(track)) return (0, 0);
            var inner = track.Trim().Trim('[', ']');
            if (inner.Length == 0 || inner == "gone") return (0, 0);
            foreach (var part in inner.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("ahead", StringComparison.Ordinal))
                    int.TryParse(p.Substring(5).Trim(), out ahead);
                else if (p.StartsWith("behind", StringComparison.Ordinal))
                    int.TryParse(p.Substring(6).Trim(), out behind);
            }
            return (ahead, behind);
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

        /// <summary>
        /// 执行 git 操作任务；失败抛 InvalidOperationException（附 stderr）。
        /// 注意：必须用 IProcessTask&lt;T&gt;（接口 ITask&lt;T&gt; 重声明的 RunSynchronously 才会内联执行
        /// 进程）；ITask/IProcessTask 接口上的 void RunSynchronously 是旧调度版本，会静默空转。
        /// </summary>
        private void RunOp<T>(string name, IProcessTask<T> task)
        {
            Prepare(task);
            try
            {
                task.RunSynchronously();
            }
            catch (ProcessException ex)
            {
                // git 失败（stderr 非零）时 api 在 RunSynchronously 内直接重抛 ProcessException；
                // 统一包装为 InvalidOperationException，调用方（菜单/弹窗/冒烟）只依赖这一种失败形态。
                throw new InvalidOperationException(name + " failed:\n" + ex.Message, ex);
            }
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
            Prepare(task);
            GitStatus result;
            try
            {
                result = task.RunSynchronously();
            }
            catch (ProcessException ex)
            {
                throw new InvalidOperationException("git status failed:\n" + ex.Message, ex);
            }
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

        /// <summary>删除本地分支（-d；未合并失败由 UI 层确认改用 force）。</summary>
        public void DeleteBranch(string name, bool force)
        {
            RunOp("git branch -d", new GitBranchDeleteTask(platform, name, force).Configure(platform.ProcessManager));
        }

        /// <summary>创建附注标签（git tag -a name commit -m msg）。</summary>
        public void CreateTag(string name, string commitHash, string message)
        {
            RunOp("git tag -a", new GitTagTask(platform, name, commitHash, message).Configure(platform.ProcessManager));
        }

        /// <summary>删除标签（git tag -d name）。</summary>
        public void DeleteTag(string name)
        {
            RunOp("git tag -d", new GitTagDeleteTask(platform, name).Configure(platform.ProcessManager));
        }

        /// <summary>更新当前分支（git pull 到其上游；无上游时 git 报错经 stderr 回显）。</summary>
        public void Pull()
        {
            RunOp("git pull", new GitPullTask(platform, null, null).Configure(platform.ProcessManager));
        }

        /// <summary>从指定 remote/branch 拉取合并（git pull remote branch）。</summary>
        public void Pull(string remote, string branch)
        {
            RunOp("git pull", new GitPullTask(platform, remote, branch).Configure(platform.ProcessManager));
        }

        /// <summary>推送当前分支到其上游（git push）。</summary>
        public void Push()
        {
            RunOp("git push", new GitPushTask(platform).Configure(platform.ProcessManager));
        }

        /// <summary>推送分支到指定远程（git push [-u] remote branch:branch）。</summary>
        public void Push(string remote, string branch, bool setUpstream)
        {
            RunOp("git push", new GitPushTask(platform, remote, branch, setUpstream).Configure(platform.ProcessManager));
        }

        /// <summary>合并 ref 到当前分支（git merge --no-edit）。</summary>
        /// <summary>合并（git merge --no-edit &lt;ref&gt;）。merge 冲突是预期结果不抛异常
        /// （冲突状态由 LoadConflictPaths/AnalyzeRebaseState 判定，3-way 视图处理）；真失败才抛。</summary>
        public void Merge(string gitRef)
        {
            var task = new GitMergeTask(platform, gitRef).Configure(platform.ProcessManager);
            Prepare(task);
            try
            {
                task.RunSynchronously();
            }
            catch (ProcessException ex)
            {
                var msg = (ex.Message ?? string.Empty) + "\n" + (task.Errors ?? string.Empty);
                if (msg.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                    || LoadConflictPathsQuietCount() > 0)
                    return;
                throw new InvalidOperationException("git merge failed:\n" + msg, ex);
            }
            if (!task.Successful)
            {
                var err = task.Errors;
                if ((err ?? string.Empty).Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                    || LoadConflictPathsQuietCount() > 0)
                    return;
                throw new InvalidOperationException("git merge failed"
                    + (string.IsNullOrEmpty(err) ? "" : ":\n" + err));
            }
        }

        private int LoadConflictPathsQuietCount()
        {
            try { return LoadConflictPaths().Count; }
            catch { return 0; }
        }

        /// <summary>本地分支重命名（git branch -m）。</summary>
        public void RenameBranch(string oldName, string newName)
        {
            RunOp("git branch -m", new GitBranchRenameTask(platform, oldName, newName).Configure(platform.ProcessManager));
        }

        /// <summary>单冲突文件的三 stage 内容（:1 base / :2 ours / :3 theirs；null=该侧缺失/删除）。</summary>
        public sealed class ConflictBlobs
        {
            public string Base;
            public string Ours;
            public string Theirs;
        }

        /// <summary>冲突文件路径列表（LoadStatus 的 Unmerged 条目；M3 3-way 视图入口）。</summary>
        public List<string> LoadConflictPaths()
        {
            var paths = new List<string>();
            var st = LoadStatus();
            if (st.Entries == null) return paths;
            foreach (var e in st.Entries)
                if (e.Unmerged)
                    paths.Add(e.path);
            return paths;
        }

        /// <summary>rebase 进行中快速查询（3-way 视图标签对调用；失败静默返回 false）。</summary>
        public bool IsRebaseInProgressQuiet()
        {
            try
            {
                var st = LoadStatus();
                return AnalyzeRebaseState(st, out var inR, out var _) && inR;
            }
            catch { return false; }
        }

        // ---- M3 P1 提交模板/最近消息 ----

        /// <summary>最近提交消息（git log -N --pretty=format:%B，`\n\n` 分隔 summary/body；最多 N 条）。
        /// 用 API 的 GitLogTask 拿 summary+description 拼接（%B 语义等价），避免新任务。</summary>
        public List<string> RecentMessages(int count)
        {
            var result = new List<string>();
            var log = LoadHistory(Math.Max(count, 1));
            foreach (var e in log)
            {
                if (result.Count >= count) break;
                var msg = e.Summary ?? string.Empty;
                if (!string.IsNullOrEmpty(e.Description))
                    msg += "\n\n" + e.Description;
                if (msg.Length > 0 && !result.Contains(msg))
                    result.Add(msg);
            }
            return result;
        }

        /// <summary>提交模板内容（git config --get commit.template → 读文件；未配置/读失败返回 null）。
        /// 用公开的 GitConfigGetAllTask（GitConfigGetTask 是 api internal 不可实例化）。</summary>
        public string LoadCommitTemplate()
        {
            try
            {
                var task = new GitConfigGetAllTask(platform, "commit.template", GitConfigSource.NonSpecified)
                    .Configure(platform.ProcessManager);
                Prepare(task);
                var all = task.RunSynchronously();
                if (!task.Successful || all == null || all.Count == 0)
                    return null;
                var path = all[0].Trim();
                if (!System.IO.File.Exists(path)) return null;
                return System.IO.File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        // ---- M3 P2 reflog 最近分支 ----

        /// <summary>
        /// 最近签出过的分支（reflog %gs 的 "checkout: moving from A to B" 取 B；去重、跳过 HEAD、截断到 N 条）。
        /// Branch tab 右击语义参照 JetBrains GitBranchesComboBoxAction 数据源（reflog 解析）。
        /// </summary>
        public List<string> RecentBranches(int count)
        {
            var result = new List<string>();
            var task = new GitReflogTask(platform, 30).Configure(platform.ProcessManager);
            Prepare(task);
            var output = task.RunSynchronously();
            if (!task.Successful || output == null) return result;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // 行格式 %h|%gd|%gs
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                var gs = parts[2];
                if (!gs.StartsWith("checkout: moving from ", StringComparison.Ordinal)) continue;
                var spec = gs.Substring("checkout: moving from ".Length).Trim();
                // spec 形如 "A to B"；B 是目标分支
                var arrow = spec.IndexOf(" to ", StringComparison.Ordinal);
                var to = arrow >= 0 ? spec.Substring(arrow + 4).Trim() : spec;
                if (to == "HEAD" || to.Length == 0) continue;
                if (!result.Contains(to))
                    result.Add(to);
                if (result.Count >= count) break;
            }
            return result;
        }

        /// <summary>提取（git fetch --prune --tags [remote]；remote 空 = 全部远程）。</summary>
        public void Fetch(string remote)
        {
            RunOp("git fetch", new GitFetchTask(platform, remote).Configure(platform.ProcessManager));
        }

        /// <summary>应用其它提交到当前分支（git cherry-pick &lt;hash&gt;；默认自动提交）。
        /// cherry-pick 冲突是预期结果不抛异常（冲突状态由 LoadConflictPaths 判定，3-way 视图处理）；真失败才抛。</summary>
        public void CherryPick(string commitHash)
        {
            var task = new GitCherryPickTask(platform, commitHash).Configure(platform.ProcessManager);
            Prepare(task);
            try
            {
                task.RunSynchronously();
            }
            catch (ProcessException ex)
            {
                var msg = (ex.Message ?? string.Empty) + "\n" + (task.Errors ?? string.Empty);
                if (msg.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                    || LoadConflictPathsQuietCount() > 0)
                    return;
                throw new InvalidOperationException("git cherry-pick failed:\n" + msg, ex);
            }
            if (!task.Successful)
            {
                var err = task.Errors;
                if ((err ?? string.Empty).Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                    || LoadConflictPathsQuietCount() > 0)
                    return;
                throw new InvalidOperationException("git cherry-pick failed"
                    + (string.IsNullOrEmpty(err) ? "" : ":\n" + err));
            }
        }

        // ---- M3 P1 remote 管理（api 四任务现成；入口 = BranchesPanel 空白右键「管理 Remotes…」） ----

        /// <summary>列出 remote 定义（git remote -v → GitRemote{name,url,function}）。</summary>
        public List<GitRemote> LoadRemotes()
        {
            var task = new GitRemoteListTask(platform).Configure(platform.ProcessManager);
            Prepare(task);
            var result = task.RunSynchronously();
            if (!task.Successful || result == null)
                throw new InvalidOperationException("git remote list failed: " + task.Errors);
            return result;
        }

        /// <summary>新建 remote（git remote add &lt;name&gt; &lt;url&gt;）。</summary>
        public void RemoteAdd(string name, string url)
        {
            RunOp("git remote add", new GitRemoteAddTask(platform, name, url).Configure(platform.ProcessManager));
        }

        /// <summary>修改 remote URL（git remote set-url &lt;name&gt; &lt;url&gt;）。</summary>
        public void RemoteSetUrl(string name, string url)
        {
            RunOp("git remote set-url",
                new GitRemoteChangeTask(platform, name, url).Configure(platform.ProcessManager));
        }

        /// <summary>删除 remote（git remote rm &lt;name&gt;）。</summary>
        public void RemoteRemove(string name)
        {
            RunOp("git remote rm",
                new GitRemoteRemoveTask(platform, name).Configure(platform.ProcessManager));
        }

        // ---- M3 P1 标签推送/远程标签 ----

        /// <summary>推送标签到远程（git push &lt;remote&gt; refs/tags/&lt;tag&gt;）。</summary>
        public void PushTag(string remote, string tag)
        {
            RunOp("git push tag",
                new GitTagPushTask(platform, remote, tag).Configure(platform.ProcessManager));
        }

        /// <summary>远程是否存在该标签（git ls-remote --tags &lt;remote&gt; refs/tags/&lt;tag&gt;）。</summary>
        public bool RemoteTagExists(string remote, string tag)
        {
            var task = new GitDiffNameStatusTask(platform,
                    "ls-remote --tags " + QuoteArg(remote) + " refs/tags/" + QuoteArg(tag))
                .Configure(platform.ProcessManager);
            Prepare(task);
            var output = task.RunSynchronously();
            if (!task.Successful || output == null) return false;
            return output.IndexOf("refs/tags/" + tag, StringComparison.Ordinal) >= 0;
        }

        /// <summary>删除远程标签（git push &lt;remote&gt; --delete refs/tags/&lt;tag&gt;）。</summary>
        public void DeleteRemoteTag(string remote, string tag)
        {
            RunOp("git push --delete tag",
                new GitRemoteTagDeleteTask(platform, remote, tag).Configure(platform.ProcessManager));
        }

        private static string QuoteArg(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// 撤销上一次提交（Uncommit，JetBrains GitUncommitAction 语义：仅 HEAD 提交）：
        /// reset --soft HEAD^ 保留工作区与 index；返回被撤销提交的完整消息（供 Commit 编辑器回填）。
        /// HEAD 无父（root 提交）时抛 InvalidOperationException。
        /// api GitResetTask(Soft) 现成（M3-SOLUTION §1.2）。
        /// </summary>
        public string Uncommit()
        {
            var log = LoadHistory(2);
            if (log.Count == 0)
                throw new InvalidOperationException("Uncommit failed: no commits");
            var head = log[0];
            var parent = head.Parents.Count > 0 ? head.Parents[0] : null;
            if (parent == null)
                throw new InvalidOperationException("Uncommit failed: root commit has no parent");
            RunOp("git reset --soft", new GitResetTask(platform, parent, GitResetMode.Soft)
                .Configure(platform.ProcessManager));
            // 完整消息（summary + body）回填 Commit 编辑器
            var sb = new System.Text.StringBuilder(head.Summary ?? string.Empty);
            if (!string.IsNullOrEmpty(head.Description))
                sb.Append("\n\n").Append(head.Description);
            return sb.ToString();
        }

        // ---- M3 3-way 冲突数据 ----

        /// <summary>单冲突文件的三 stage blob 内容（:1:=base :2:=ours :3:=theirs；M3-SOLUTION §1.1-4）。</summary>
        public ConflictBlobs LoadConflictBlobs(string path, out bool hasOurs, out bool hasTheirs)
        {
            hasOurs = hasTheirs = false;
            var result = new ConflictBlobs();
            // 用 git show :N:path 直接取（stage 存在才执行；缺失侧留 null）
            try { result.Base = ShowStage(path, 1); } catch { result.Base = null; }
            try { result.Ours = ShowStage(path, 2); hasOurs = true; } catch { result.Ours = null; }
            try { result.Theirs = ShowStage(path, 3); hasTheirs = true; } catch { result.Theirs = null; }
            return result;
        }

        private string ShowStage(string path, int stage)
        {
            var task = new GitDiffNameStatusTask(platform,
                    "show :" + stage + ":" + SanitizePath(path))
                .Configure(platform.ProcessManager);
            Prepare(task);
            var output = task.RunSynchronously();
            if (!task.Successful || output == null)
                throw new InvalidOperationException("git show stage " + stage + " failed for " + path);
            return output;
        }

        private static string SanitizePath(string path)
        {
            return path.Replace("\\", "/").Replace("\"", "\\\"");
        }

        /// <summary>冲突整侧接受（TWO-STEP 语义，M3-SOLUTION §1.1-5/§3.4）：
        /// checkout --ours/--theirs → git add（标记解决；DELETED 侧用 rm 由调用方依据 stage 存在性决定）。</summary>
        public void AcceptConflictSide(string path, GitCheckoutSide side)
        {
            if (side != GitCheckoutSide.Ours && side != GitCheckoutSide.Theirs) return;
            RunOp("git checkout --side",
                new GitCheckoutPathsTask(platform, new[] { path }, side).Configure(platform.ProcessManager));
            // checkout 只更新工作区，index 仍 UU → add 标记解决（探针实证必需）
            RunOp("git add", new GitAddTask(platform, new[] { path }).Configure(platform.ProcessManager));
        }

        /// <summary>删除侧冲突解决：对"对侧已删除"的路径用 git rm（而不是 add）。</summary>
        public void ResolveConflictDelete(string path, GitCheckoutSide keptSide)
        {
            if (keptSide != GitCheckoutSide.Ours && keptSide != GitCheckoutSide.Theirs) return;
            // 保留侧存在 → checkout 保留侧 → add；保留侧删除 → rm 亦可（此处简化为 add 路径，删除侧由 LoadStatus 覆盖）
            AcceptConflictSide(path, keptSide);
        }

        /// <summary>
        /// rebase 进行中判定（M3-SOLUTION §1.1-3 实证）：
        /// status 首行在 rebase 冲突时为 "## HEAD (no branch)"（GitStatus.LocalBranch 无真实分支名，
        /// api 解析 "HEAD (no branch)" 后 LocalBranch 为 "HEAD" 或空）+ Unmerged 条目。
        /// merge 冲突时首行为 "## main"（LocalBranch 有值）。据此区分并统计 UU 数。
        /// </summary>
        public static bool AnalyzeRebaseState(GitStatus status, out bool inRebase, out int unmergedCount)
        {
            inRebase = false;
            unmergedCount = 0;
            if (status == null || status.Entries == null) return false;
            foreach (var e in status.Entries)
                if (e.Unmerged) unmergedCount++;
            var branch = status.LocalBranch ?? string.Empty;
            // rebase 中：无当前分支（porcelain "## HEAD (no branch)"；api 可能解析为
            // "HEAD" 或 "HEAD (no branch)" 或 null/空）
            if (branch.Length == 0 || branch == "HEAD"
                || branch.StartsWith("HEAD", StringComparison.Ordinal)
                || branch.Contains("(no branch)", StringComparison.Ordinal))
                inRebase = unmergedCount > 0;
            return inRebase;
        }

        /// <summary>本分支变基到目标分支（git rebase &lt;branch&gt;；非交互）。
        /// rebase 冲突是**预期结果**不抛异常（由 AnalyzeRebaseState 判定进入冲突态）；
        /// 仅真实失败（非冲突错误）抛 InvalidOperationException。</summary>
        public void Rebase(string branch)
        {
            var task = new GitRebaseTask(platform, branch).Configure(platform.ProcessManager);
            Prepare(task);
            try
            {
                task.RunSynchronously();
            }
            catch (ProcessException ex)
            {
                // 冲突 → 正常进入 rebase 中状态；其它 stderr → 真失败
                var msg = (ex.Message ?? string.Empty) + "\n" + (task.Errors ?? string.Empty);
                if (msg.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                    || AnalyzeRebaseState(LoadStatusQuiet(), out var inR, out var _) && inR)
                    return;
                throw new InvalidOperationException("git rebase failed:\n" + msg, ex);
            }
            if (!task.Successful)
            {
                var err = task.Errors;
                if ((err ?? string.Empty).Contains("CONFLICT", StringComparison.OrdinalIgnoreCase))
                    return;
                throw new InvalidOperationException("git rebase failed"
                    + (string.IsNullOrEmpty(err) ? "" : ":\n" + err));
            }
        }

        private GitStatus LoadStatusQuiet()
        {
            try { return LoadStatus(); }
            catch { return default(GitStatus); }
        }

        /// <summary>拉取并变基（git pull --rebase &lt;remote&gt; &lt;branch&gt;）。</summary>
        public void PullRebase(string remote, string branch)
        {
            RunOp("git pull --rebase",
                new GitPullRebaseTask(platform, remote, branch).Configure(platform.ProcessManager));
        }

        /// <summary>rebase 冲突继续（git rebase --continue）。</summary>
        public void RebaseContinue()
        {
            RunOp("git rebase --continue",
                new GitRebaseTask(platform, "--continue").Configure(platform.ProcessManager));
        }

        /// <summary>rebase 中止（git rebase --abort）。</summary>
        public void RebaseAbort()
        {
            RunOp("git rebase --abort",
                new GitRebaseTask(platform, "--abort").Configure(platform.ProcessManager));
        }

        /// <summary>hunk 级操作（M3-SOLUTION §3.3）：对已捕获的 diff 输出切片目标 hunk → git apply 三态。</summary>
        /// <param name="diffOutput">git diff 完整输出（工作区 diff 或 --cached diff，取决于 mode 语义）。</param>
        /// <param name="fileIndex">目标文件序号（0 基）。</param>
        /// <param name="hunkIndex">目标 hunk 序号（0 基）。</param>
        /// <param name="mode">Stage=apply --cached；Unstage=apply --cached -R；Revert=apply -R。</param>
        /// <exception cref="InvalidOperationException">patch 提取失败或 git apply 失败（RunOp 统一包装）。</exception>
        public void ApplyHunk(string diffOutput, int fileIndex, int hunkIndex, GitApplyMode mode)
        {
            var patchPath = GitPatchBuilder.WriteHunkPatch(diffOutput, fileIndex, hunkIndex, projectPath);
            if (patchPath == null)
                throw new InvalidOperationException("git apply failed: hunk slice not found (diff changed?)");
            try
            {
                RunOp("git apply", new GitApplyTask(platform, patchPath, mode).Configure(platform.ProcessManager));
            }
            catch (Exception ex)
            {
                // 诊断友好：patch 内容附进异常（hunk 级 apply 失败多为上下文漂移/CRLF——直接可读）
                string patchInfo;
                try { patchInfo = System.IO.File.ReadAllText(patchPath); }
                catch { patchInfo = "<unreadable>"; }
                throw new InvalidOperationException(ex.Message + "\n[patch]\n" + patchInfo, ex);
            }
        }

        /// <summary>便捷：对工作区单个文件执行 hunk 级 Stage/Revert（内部先取 git diff）。</summary>
        public void ApplyWorktreeHunk(string path, int hunkIndex, GitApplyMode mode)
        {
            var diff = RunDiffRaw("-- " + GitDiffTask.JoinPaths(new[] { path }));
            ApplyHunk(diff, 0, hunkIndex, mode);
        }

        /// <summary>便捷：对已暂存文件执行 hunk 级 Unstage（内部取 git diff --cached）。</summary>
        public void UnstageHunk(string path, int hunkIndex)
        {
            var diff = RunDiffRaw("--cached -- " + GitDiffTask.JoinPaths(new[] { path }));
            ApplyHunk(diff, 0, hunkIndex, GitApplyMode.Unstage);
        }

        private string RunDiffRaw(string arguments)
        {
            var task = GitDiffTask.Raw(platform, arguments).Configure(platform.ProcessManager);
            Prepare(task);
            return task.RunSynchronously();
        }

        /// <summary>工作区完整 diff（冒烟/hunk 操作共用）。</summary>
        public string RunDiffPublic()
        {
            return RunDiffRaw("");
        }

        /// <summary>已暂存完整 diff（冒烟/UnstageHunk 共用）。</summary>
        public string RunCachedDiffPublic()
        {
            return RunDiffRaw("--cached");
        }

        /// <summary>工作区完整 diff（--unified=0：相邻改动拆成独立 hunk——hunk 级操作的前提）。</summary>
        public string RunDiffUnified0Public()
        {
            return RunDiffRaw("--unified=0");
        }

        /// <summary>使 refs 缓存失效（分支弹窗新建/删除/打标签后调用；历史/状态缓存不受影响）。</summary>
        public void InvalidateRefs()
        {
            lock (cacheLock) refsCache = null;
        }

        private List<(char, string)> RunNameStatus(string arguments)
        {
            var task = new GitDiffNameStatusTask(platform, arguments)
                .Configure(platform.ProcessManager);
            Prepare(task);
            var output = task.RunSynchronously();
            if (!task.Successful || output == null) return new List<(char, string)>();
            return ParseNameStatusLines(output);
        }

        /// <summary>git show -m：每父一段，段头为 "commit &lt;merge&gt; (from &lt;parent&gt;)"；取段内 name-status 行。</summary>
        private List<List<(char, string)>> RunNameStatusSections(string arguments)
        {
            var task = new GitDiffNameStatusTask(platform, arguments)
                .Configure(platform.ProcessManager);
            Prepare(task);
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