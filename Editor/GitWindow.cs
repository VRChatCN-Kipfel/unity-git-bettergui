using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>
    /// unity-git-bettergui 主窗口：三栏布局 + JetBrains 管线图谱。
    ///   左（图谱表格：图谱+提交消息同行同格，甬道对应）| 右：上（改动文件列表）+ 下（提交详情）
    /// 图谱管线：GitLogTask -> PermanentLinearGraph(CSR+隐式链) -> GraphLayout(head 栈式 DFS 泳道)
    ///   -> EdgesInRow(逐行在途边) -> RowPrinter(行内元素排序+打印元素) -> GraphTable 自绘。
    /// </summary>
    public class GitWindow : EditorWindow
    {
        private GitSession session;
        private List<GitLogEntry> logEntries = new List<GitLogEntry>();
        private GraphTable graphTable;
        private Label graphStatus;
        private ChangesTree changesTree;
        private Label detailText;
        private string lastFingerprint;
        private double lastFingerprintCheck;

        [MenuItem("Window/Git/Git Better GUI (wip)")]
        public static void Open()
        {
            var w = GetWindow<GitWindow>();
            w.titleContent = new GUIContent(I18n.L(I18n.Keys.WindowTitle));
            w.minSize = new Vector2(900, 500);
            w.Show();
        }

        /// <summary>
        /// 批处理冒烟测试：-executeMethod KF.GitUI.GitWindow.SmokeTest
        /// 验证：三栏布局 + 引擎（泳道/逐行边/行内元素）+ 真实提交加载。
        /// 测试仓库（9 提交，git log 按日期排序）：r0 810e7c4(merge y) r1 58c902b(merge x)
        ///   r2 8f7a1f9 r3 ca9b7f8 r4 cbeacbf r5 c3ce8fb r6 3964f75 r7 f908745 r8 e91cc21
        /// </summary>
        public static void SmokeTest()
        {
            var root = new GitWindow().BuildLayout();
            var outer = root.Q<TwoPaneSplitView>("outer-split");
            var inner = root.Q<TwoPaneSplitView>("inner-split");
            UnityEngine.Debug.Log($"[gitui] SMOKE diag: outer={outer?.childCount ?? -1} inner={inner?.childCount ?? -1} toolbar={(root.Q<VisualElement>("toolbar") != null)}");
            if (outer == null || inner == null || outer.childCount != 2 || inner.childCount != 2)
                throw new System.Exception("SMOKE FAIL: layout tree incomplete");

            using (var s = GitSession.Open(Environment.CurrentDirectory))
            {
                var log = s.LoadHistory(200);
                if (log.Count == 0) throw new System.Exception("SMOKE FAIL: no commits parsed");
                if (log[0].ShortID != "810e7c4") throw new System.Exception("SMOKE FAIL: head != 810e7c4");

                var commitIndex = new Dictionary<string, int>();
                for (var i = 0; i < log.Count; i++) commitIndex[log[i].CommitID] = i;

                // 1) 永久图 + 泳道（JetBrains 语义，见引擎注释）
                var pGraph = PermanentLinearGraph.Build(log, commitIndex);
                var layout = GraphLayout.Build(pGraph);
                var expectedLanes = new[] { 0, 0, 2, 0, 1, 1, 0, 0, 0 };
                var lanes = new int[pGraph.NodesCount];
                for (var i = 0; i < pGraph.NodesCount; i++) lanes[i] = layout.GetLayoutIndex(i);
                if (string.Join(",", lanes) != string.Join(",", expectedLanes))
                    throw new System.Exception("SMOKE FAIL: engine lanes=[" + string.Join(",", lanes) + "] expect [0,0,2,0,1,1,0,0,0]");
                if (layout.LaneCount != 1) throw new System.Exception($"SMOKE FAIL: engine LaneCount={layout.LaneCount} expect 1");
                var maxLane = 0;
                for (var i = 0; i < lanes.Length; i++) if (lanes[i] > maxLane) maxLane = lanes[i];
                if (maxLane != 2) throw new System.Exception($"SMOKE FAIL: engine maxLane={maxLane} expect 2");

                // 2) 逐行在途边（手算期望：E=[{},{0,2},{1,3 1,4},{1,4},{3,6},{3,6},{},{},{}]）
                var eir = EdgesInRow.Build(pGraph);
                var expectedCounts = new[] { 0, 1, 2, 1, 1, 1, 0, 0, 0 };
                for (var r = 0; r < 9; r++)
                {
                    var c = eir.GetEdgesInRow(r).Count;
                    if (c != expectedCounts[r]) throw new System.Exception($"SMOKE FAIL: EdgesInRow[{r}].Count={c} expect {expectedCounts[r]}");
                }
                var e1 = eir.GetEdgesInRow(1);
                if (e1.Count != 1 || e1[0].Up != 0 || e1[0].Down != 2)
                    throw new System.Exception("SMOKE FAIL: EdgesInRow[1] != {(0,2)}");

                // 3) 行内打印元素（节点槽位断言：r0=0, r1=0, r2=2——feature/y 节点在 Feature/x 边右侧）
                var headSet = new HashSet<int>(layout.HeadNodes);
                RowPrinter printer;
                try
                {
                    printer = RowPrinter.Build(pGraph, layout, eir, headSet);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception("SMOKE FAIL: RowPrinter.Build -> " + ex.GetType().Name + "\n" + ex.StackTrace, ex);
                }
                if (printer.GetNodePosition(0) != 0 || printer.GetNodePosition(1) != 0 || printer.GetNodePosition(2) != 2)
                    throw new System.Exception($"SMOKE FAIL: nodePositions 0/1/2 = {printer.GetNodePosition(0)}/{printer.GetNodePosition(1)}/{printer.GetNodePosition(2)} expect 0/0/2");
                // row0 应有 2 条下行边（父 58c902b 与 8f7a1f9）
                if (printer.GetEdgesInRow(0).Count != 2)
                    throw new System.Exception($"SMOKE FAIL: row0 edges={printer.GetEdgesInRow(0).Count} expect 2");

                // 5) merge/root 变更按需加载（JetBrains DIFF_TO_PARENTS：合并视图 + 每父分组）
                if (log[0].Parents.Count != 2)
                    throw new System.Exception($"SMOKE FAIL: merge parents={log[0].Parents.Count} expect 2");
                var mergeChanges = s.LoadChangesFor(log[0]);
                if (mergeChanges == null || mergeChanges.PerParent.Count != 2)
                    throw new System.Exception("SMOKE FAIL: merge PerParent != 2 groups");
                // 810e7c4 是干净合并（无冲突）-> 合并视图为空；相对第一父出现 featY.txt
                if (mergeChanges.Combined.Count != 0)
                    throw new System.Exception("SMOKE FAIL: clean merge combined should be empty");
                var hasFeatY = false;
                foreach (var (st, path) in mergeChanges.PerParent[0])
                    if (path.Contains("featY")) hasFeatY = true;
                if (!hasFeatY)
                    throw new System.Exception("SMOKE FAIL: merge parent0 changes missing featY.txt");
                var rootChanges = s.LoadChangesFor(log[8]);
                if (rootChanges == null || rootChanges.Combined.Count == 0)
                    throw new System.Exception("SMOKE FAIL: root Combined empty");

                // 6) refs 映射（JetBrains groupForTable：HEAD 当前分支 -> 本地 -> remote -> tags）
                var refs = s.LoadRefs();
                if (refs.Count < 3) throw new System.Exception($"SMOKE FAIL: refs count={refs.Count} expect >=3");
                if (refs[0].Type != GitSession.RefType.Head || refs[0].DisplayName != "main")
                    throw new System.Exception($"SMOKE FAIL: first ref != HEAD main ({refs[0].Type}/{refs[0].DisplayName})");
                if (refs[0].CommitId != log[0].CommitID)
                    throw new System.Exception("SMOKE FAIL: main ref not pointing at head commit");
                var byName = new Dictionary<string, GitSession.GitRefInfo>();
                foreach (var rf in refs) byName[rf.DisplayName] = rf;
                if (!byName.ContainsKey("feature/x") || !byName.ContainsKey("feature/y"))
                    throw new System.Exception("SMOKE FAIL: feature branches missing from refs");

                // 7) 长边箭头判定（JetBrains long-edge：span>=30 的近端 ±1 行画箭头，中部 Hidden；<30 全部 Segment）
                if (RowPrinter.DecideLongEdge(32, 1, 0, 40) != RowPrinter.RenderKind.ArrowDown)
                    throw new System.Exception("SMOKE FAIL: DecideLongEdge ArrowDown");
                if (RowPrinter.DecideLongEdge(32, 39, 0, 40) != RowPrinter.RenderKind.ArrowUp)
                    throw new System.Exception("SMOKE FAIL: DecideLongEdge ArrowUp");
                if (RowPrinter.DecideLongEdge(32, 20, 0, 40) != RowPrinter.RenderKind.Hidden)
                    throw new System.Exception("SMOKE FAIL: DecideLongEdge Hidden");
                if (RowPrinter.DecideLongEdge(29, 20, 0, 29) != RowPrinter.RenderKind.Segment)
                    throw new System.Exception("SMOKE FAIL: DecideLongEdge Segment");

                // 8) i18n 键表（缺失键返回键名可抓漏；使用中的键全部有值；格式键可格式化）
                if (I18n.L(I18n.Keys.WindowTitle) != "Git Better GUI")
                    throw new System.Exception("SMOKE FAIL: i18n WindowTitle");
                if (I18n.L("definitely.missing.key") != "definitely.missing.key")
                    throw new System.Exception("SMOKE FAIL: i18n missing-key fallback");
                if (!I18n.L(I18n.Keys.GraphStatusFormat).Contains("{0}"))
                    throw new System.Exception("SMOKE FAIL: i18n status format key");
                if (I18n.L(I18n.Keys.ChangesToParent, "abc1234") != "Changes to parent abc1234")
                    throw new System.Exception("SMOKE FAIL: i18n ChangesToParent format");
                if (I18n.L(I18n.Keys.NoMergeConflicts) != "✓ no merge conflicts")
                    throw new System.Exception("SMOKE FAIL: i18n merge-conflict-free key");

                // 9) 改动树：目录分组（目录在前、文件在后、组内名序）+ staged 聚合 + 分节顺序/OpsPath
                var diffs = ChangesTree.BuildFromDiffs(new List<(char, string)>
                {
                    ('M', "Assets/A.cs"), ('A', "Assets/Sub/B.cs"), ('D', "README.md")
                });
                var grp = ChangesTree.Group(diffs);
                if (grp.Count != 2)
                    throw new System.Exception($"SMOKE FAIL: group roots={grp.Count} expect 2");
                if (!grp[0].data.IsDirectory || grp[0].data.Path != "Assets")
                    throw new System.Exception("SMOKE FAIL: group root0 != Assets dir");
                if (grp[1].data.IsDirectory || grp[1].data.Path != "README.md")
                    throw new System.Exception("SMOKE FAIL: group root1 != README.md file");
                var ac = grp[0].children.ToList();
                if (ac.Count != 2 || !ac[0].data.IsDirectory || ac[0].data.Path != "Assets/Sub"
                    || ac[1].data.Path != "Assets/A.cs")
                    throw new System.Exception("SMOKE FAIL: Assets children != [Sub, A.cs]");
                // 双列状态文本（index+worktree 合并）
                var am = ChangesTree.FromEntry(new GitStatusEntry("x.txt", "x.txt", "p",
                    GitFileStatus.Added, GitFileStatus.Modified));
                if (am.StatusText != "AM" || !am.IsStaged)
                    throw new System.Exception("SMOKE FAIL: status AM / staged");
                // staged 聚合：全 staged -> 目录 staged；有未暂存 -> 目录未暂存
                var stagedGrp = ChangesTree.Group(ChangesTree.BuildFromEntries(new List<GitStatusEntry>
                {
                    new GitStatusEntry("Assets/a.txt", "Assets/a.txt", "p", GitFileStatus.Modified, GitFileStatus.None),
                }));
                if (!stagedGrp[0].data.IsStaged)
                    throw new System.Exception("SMOKE FAIL: dir staged aggregation");
                var unstagedGrp = ChangesTree.Group(ChangesTree.BuildFromEntries(new List<GitStatusEntry>
                {
                    new GitStatusEntry("Assets/b.txt", "Assets/b.txt", "p", GitFileStatus.None, GitFileStatus.Modified),
                }));
                if (unstagedGrp[0].data.IsStaged)
                    throw new System.Exception("SMOKE FAIL: dir unstaged aggregation");
                // 分节（merge 每父分组语义）：顺序保留 + OpsPath=真实路径
                var sections = ChangesTree.BuildSectioned(new List<(string, List<ChangeItem>)>
                {
                    ("Changes to parent 810e7c4",
                        ChangesTree.BuildFromDiffs(new List<(char, string)> { ('M', "featY.txt") })),
                    ("Changes to parent 58c902b",
                        ChangesTree.BuildFromDiffs(new List<(char, string)> { ('A', "Assets/New.cs") })),
                });
                if (sections.Count != 2 || sections[0].data.Path != "Changes to parent 810e7c4"
                    || sections[1].data.Path != "Changes to parent 58c902b")
                    throw new System.Exception("SMOKE FAIL: section order");
                var secFile = sections[0].children.First();
                if (secFile.data.OpsPath != "featY.txt" || secFile.data.Path != "Changes to parent 810e7c4/featY.txt")
                    throw new System.Exception("SMOKE FAIL: section OpsPath");

                // 11) 右键动作过滤（JetBrains 动作动态过滤语义：Hidden 剔除、Disabled 保留、分隔线识别）
                var menuActions = new List<IGitContextAction>
                {
                    new DelegateAction("copy", "Copy", () => { }),
                    new DelegateAction("disabled", "Disabled", () => { }) { Enabled = false },
                    new DelegateAction("hidden", "Hidden", () => { }) { Visible = false },
                    new DelegateAction("checked", "Checked", () => { }) { Checked = true },
                    GitContextSeparator.Instance,
                };
                var filtered = GitContextMenu.Filter(menuActions);
                if (filtered.Count != 4)
                    throw new System.Exception($"SMOKE FAIL: menu filter count={filtered.Count} expect 4");
                // [copy, disabled, checked, separator]（hidden 被剔除）
                if (filtered[1].Enabled || !filtered[2].Checked || filtered[0].Text != "Copy")
                    throw new System.Exception("SMOKE FAIL: menu filter status mapping");
                if (!(filtered[3] is GitContextSeparator))
                    throw new System.Exception("SMOKE FAIL: menu separator");

                // 12) 提交语境动作（图谱行右键，JetBrains Log 右键裁剪版）
                var commitActions = GitWindow.BuildCommitContextActions(s, log, 0, () => { }).ToList();
                var commitTexts = commitActions.Select(a => a.Text).Where(t => t != null).ToList();
                if (!commitTexts.Contains("Copy Hash") || !commitTexts.Contains("Copy Summary"))
                    throw new System.Exception("SMOKE FAIL: commit menu copy items");
                if (!commitTexts.Contains("Reset…/Soft") || !commitTexts.Contains("Reset…/Mixed")
                    || !commitTexts.Contains("Reset…/Hard"))
                    throw new System.Exception("SMOKE FAIL: commit menu reset submenu");
                if (!commitTexts.Contains("Revert Commit…") || !commitTexts.Contains("Checkout…"))
                    throw new System.Exception("SMOKE FAIL: commit menu revert/checkout");
                if (!commitTexts.Contains("Compare with Branch…") || !commitTexts.Contains("Create Tag…"))
                    throw new System.Exception("SMOKE FAIL: commit menu compare/tag");
                if (GitWindow.BuildCommitContextActions(s, log, 999, () => { }).Any())
                    throw new System.Exception("SMOKE FAIL: commit menu out-of-range should be empty");
                // 15) 分支弹窗：过滤（空格分词、全 token 命中、忽略大小写）
                var fAll = BranchesPanel.ApplyFilter(refs, "");
                if (fAll.Count != refs.Count)
                    throw new System.Exception("SMOKE FAIL: branch filter empty != all");
                var fFeat = BranchesPanel.ApplyFilter(refs, "feature");
                if (fFeat.Count != 2)
                    throw new System.Exception($"SMOKE FAIL: branch filter 'feature' count={fFeat.Count} expect 2");
                if (BranchesPanel.ApplyFilter(refs, "NOPE99").Count != 0)
                    throw new System.Exception("SMOKE FAIL: branch filter nomatch != 0");

                // 13) Commit 数据通道：状态解析 + gpg 探测（stderr 子串）
                var status = s.LoadStatus();
                if (string.IsNullOrEmpty(status.LocalBranch))
                    throw new System.Exception("SMOKE FAIL: status localBranch empty");
                if (status.Entries == null)
                    throw new System.Exception("SMOKE FAIL: status entries null");
                if (!GitSession.DetectGpgError("error: gpg failed to sign the data\nfatal: writing commit object failed"))
                    throw new System.Exception("SMOKE FAIL: gpg detect missed");
                if (GitSession.DetectGpgError("fatal: empty commit message"))
                    throw new System.Exception("SMOKE FAIL: gpg detect false positive");

                // 14) Commit 页结构 + 文件/目录语境右键（静态 Builder 断言）
                if (root.Q<VisualElement>("toolbar") == null
                    || root.Q<Button>("tab-log") == null || root.Q<Button>("tab-commit") == null
                    || root.Q<VisualElement>("page-log") == null || root.Q<VisualElement>("page-commit") == null
                    || root.Q<TwoPaneSplitView>("body-split") == null
                    || root.Q<BranchesPanel>("branches-panel") == null)
                    throw new System.Exception("SMOKE FAIL: tabs structure");
                if (root.Q<ChangesTree>("commit-tree") == null || root.Q<TextField>("msg-summary") == null)
                    throw new System.Exception("SMOKE FAIL: commit page fields");
                var dirEntry = status.Entries.FirstOrDefault(en => en.path.Contains('/'));
                var dirPath = string.IsNullOrEmpty(dirEntry.path) ? null : dirEntry.path;
                if (!string.IsNullOrEmpty(dirPath))
                {
                    var dirName = dirPath.Substring(0, dirPath.IndexOf('/'));
                    var dirItem = new ChangeItem { Path = dirName, IsDirectory = true };
                    var dirTexts = GitWindow.BuildFileContextActions(s, dirItem, status.Entries, false, () => { })
                        .Where(a => a.Text != null).Select(a => a.Text).ToList();
                    if (!dirTexts.Contains("Stage All") || !dirTexts.Contains("Unstage All")
                        || !dirTexts.Contains("Revert (discard changes)") || !dirTexts.Contains("Open")
                        || !dirTexts.Contains("Copy Path"))
                        throw new System.Exception("SMOKE FAIL: dir context actions");
                    var fileItem = ChangesTree.FromEntry(status.Entries[0]);
                    var fileTexts = GitWindow.BuildFileContextActions(s, fileItem, status.Entries, false, () => { })
                        .Where(a => a.Text != null).Select(a => a.Text).ToList();
                    if (!fileTexts.Contains(fileItem.IsStaged ? "Unstage" : "Stage")
                        || !fileTexts.Contains("Open") || !fileTexts.Contains("Copy Path"))
                        throw new System.Exception("SMOKE FAIL: file context actions");
                    var roTexts = GitWindow.BuildFileContextActions(s, fileItem, null, true, () => { })
                        .Where(a => a.Text != null).Select(a => a.Text).ToList();
                    if (roTexts.Contains("Stage") || roTexts.Contains("Unstage") || !roTexts.Contains("Open"))
                        throw new System.Exception("SMOKE FAIL: read-only context leaks ops");
                }

                // 16) 端到端提交流程（一次性仓库：init → seed → 未跟踪文件 → Stage → Commit → 校验 → 清理）
                var e2eDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-smoke-repo");
                DeleteDir(e2eDir); // 残留（上次失败留下）可能带只读属性
                System.IO.Directory.CreateDirectory(e2eDir);
                var gitExe = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe, "init -b master", e2eDir, out var cliErr, out var _);
                RunCli(gitExe, "config user.name smoke", e2eDir, out cliErr, out var _);
                RunCli(gitExe, "config user.email smoke@local", e2eDir, out cliErr, out var _);
                RunCli(gitExe, "config commit.gpgsign false", e2eDir, out cliErr, out var _);
                RunCli(gitExe, "commit --allow-empty -m seed", e2eDir, out cliErr, out var _);
                if (!string.IsNullOrEmpty(cliErr))
                    throw new System.Exception("SMOKE FAIL: e2e seed: " + cliErr);
                System.IO.File.WriteAllText(System.IO.Path.Combine(e2eDir, "payload.txt"), "hello e2e");
                using (var s2 = GitSession.Open(e2eDir))
                {
                    var st0 = s2.LoadStatus();
                    if (st0.LocalBranch != "master" || st0.Entries.Count != 1)
                        throw new System.Exception("SMOKE FAIL: e2e status0");
                    var st0e = st0.Entries[0];
                    if (!st0e.Untracked || st0e.path != "payload.txt")
                        throw new System.Exception("SMOKE FAIL: e2e untracked entry");
                    s2.Stage(new[] { "payload.txt" });
                    var st1 = s2.LoadStatus();
                    if (st1.Entries.Count != 1 || !st1.Entries[0].Staged)
                        throw new System.Exception("SMOKE FAIL: e2e staged entry");
                    s2.Commit("e2e smoke", "body line", false, false, false);
                    // 分支过滤（JetBrains Log filter 语义）：只渲染该 ref 的祖先
                    s2.NewBranch("f2", "master~1"); // f2 基于 seed
                    var filteredLog = s2.LoadHistory(10, "f2");
                    if (filteredLog.Count != 1 || filteredLog[0].Summary != "seed")
                        throw new System.Exception("SMOKE FAIL: e2e branch filter");
                    // 非法分支名：git 校验失败 -> RunOp 抛 InvalidOperationException（绝不静默）
                    try
                    {
                        s2.NewBranch("bad..name", "master");
                        throw new System.Exception("SMOKE FAIL: invalid branch name accepted");
                    }
                    catch (InvalidOperationException) { }
                    var st2 = s2.LoadStatus();
                    if (st2.Entries.Count != 0)
                        throw new System.Exception("SMOKE FAIL: e2e clean after commit");
                    var log2 = s2.LoadHistory(10);
                    if (log2.Count != 2 || log2[0].Summary != "e2e smoke")
                        throw new System.Exception("SMOKE FAIL: e2e history");
                }
                DeleteDir(e2eDir);

                // 17) 路径归一化（Windows 反斜杠 -> 正斜杠；修复文件名误显 "\t"-类污染）
                var winEntry = ChangesTree.BuildFromEntries(new List<GitStatusEntry>
                {
                    new GitStatusEntry("Assets\\test.txt", "Assets\\test.txt", "p",
                        GitFileStatus.Untracked, GitFileStatus.Untracked),
                })[0];
                if (winEntry.Path != "Assets/test.txt" || winEntry.OpsPath != "Assets/test.txt"
                    || ChangesTree.DisplayName(winEntry) != "test.txt")
                    throw new System.Exception("SMOKE FAIL: backslash path not normalized");

                // 18) 长边箭头命中（JetBrains 语义：点击箭头跳转对端提交；纯函数命中测试）
                var arrowHits = new List<GraphTable.ArrowHit>
                {
                    new GraphTable.ArrowHit { Area = new Rect(2f, 2f, 10f, 10f), TargetRow = 5 },
                    new GraphTable.ArrowHit { Area = new Rect(20f, 6f, 10f, 10f), TargetRow = 8 },
                };
                if (GraphTable.HitTestArrow(arrowHits, new Vector2(5f, 5f)) != 5)
                    throw new System.Exception("SMOKE FAIL: arrow hit 1");
                if (GraphTable.HitTestArrow(arrowHits, new Vector2(25f, 8f)) != 8)
                    throw new System.Exception("SMOKE FAIL: arrow hit 2");
                if (GraphTable.HitTestArrow(arrowHits, new Vector2(60f, 60f)) != -1)
                    throw new System.Exception("SMOKE FAIL: arrow miss");

                // 19) 分支面板右键动作集（当前/其它本地/远程/标签 + 上游子菜单无重命名 + 无远程时 Push 禁用）
                var curRef = refs.First(r => r.IsCurrentHead);
                var curTexts = BranchesPanel.BuildContextActions(s, curRef, "main", () => { }, _ => { })
                    .Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!curTexts.Contains("Branch from main…") || !curTexts.Contains("Update")
                    || !curTexts.Contains("Push") || !curTexts.Contains("Rename…"))
                    throw new System.Exception("SMOKE FAIL: current-branch ctx");
                if (curTexts.Contains("Delete"))
                    throw new System.Exception("SMOKE FAIL: current-branch ctx leaks Delete");
                var otherRef = refs.First(r => r.Type == GitSession.RefType.Local);
                var otherTexts = BranchesPanel.BuildContextActions(s, otherRef, "main", () => { }, _ => { })
                    .Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!otherTexts.Contains("Checkout") || !otherTexts.Contains("Delete")
                    || !otherTexts.Any(t => t.StartsWith("Merge ", StringComparison.Ordinal) && t.EndsWith(" into main"))
                    || otherTexts.Contains("Branch from main…"))
                    throw new System.Exception("SMOKE FAIL: other-branch ctx");
                var pushAct = BranchesPanel.BuildContextActions(s, otherRef, "main", () => { }, _ => { })
                    .FirstOrDefault(a => a.Id == "ctx.push");
                if (pushAct == null || pushAct.Enabled)
                    throw new System.Exception("SMOKE FAIL: push should be disabled without remote");
                var fake = new GitSession.GitRefInfo { Type = GitSession.RefType.Local, DisplayName = "ft", CommitId = "x", Upstream = "origin/ft" };
                var fakeTexts = BranchesPanel.BuildContextActions(s, fake, "main", () => { }, _ => { })
                    .Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!fakeTexts.Any(t => t.StartsWith("Operations on origin/ft/", StringComparison.Ordinal)))
                    throw new System.Exception("SMOKE FAIL: upstream submenu missing");
                if (fakeTexts.Any(t => t.StartsWith("Operations on", StringComparison.Ordinal) && t.Contains("Rename")))
                    throw new System.Exception("SMOKE FAIL: upstream submenu leaks Rename");
                var fakeRemote = new GitSession.GitRefInfo { Type = GitSession.RefType.Remote, DisplayName = "origin/r1", CommitId = "y" };
                var remoteTexts = BranchesPanel.BuildContextActions(s, fakeRemote, "main", () => { }, _ => { })
                    .Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!remoteTexts.Contains("Checkout") || remoteTexts.Contains("Rename…") || remoteTexts.Contains("Delete"))
                    throw new System.Exception("SMOKE FAIL: remote ctx");
                var fakeTag = new GitSession.GitRefInfo { Type = GitSession.RefType.Tag, DisplayName = "v9", CommitId = "z" };
                var tagTexts = BranchesPanel.BuildContextActions(s, fakeTag, "main", () => { }, _ => { })
                    .Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!tagTexts.Contains("Checkout") || !tagTexts.Contains("Delete") || tagTexts.Contains("Rename…"))
                    throw new System.Exception("SMOKE FAIL: tag ctx");

                // 28) M3 rebase 菜单（§6.2 插入点）：其它本地分支含 Rebase 组；上游子菜单含变基到上游；仍无重命名泄漏
                var rebaseTexts = BranchesPanel.BuildContextActions(s, fake, "main", () => { }, _ => { })
                    .Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!rebaseTexts.Any(t => t.StartsWith("Rebase current branch onto ft", StringComparison.Ordinal)))
                    throw new System.Exception("SMOKE FAIL: rebase onto missing");
                if (!rebaseTexts.Any(t => t.StartsWith("Checkout ft and rebase current branch onto it", StringComparison.Ordinal)))
                    throw new System.Exception("SMOKE FAIL: rebase checkout missing");
                if (!rebaseTexts.Any(t => t.StartsWith("Operations on origin/ft/", StringComparison.Ordinal)
                                         && t.Contains("Rebase current branch onto origin/ft")))
                    throw new System.Exception("SMOKE FAIL: upstream rebase missing");
                if (rebaseTexts.Any(t => t.Contains("Rename") && t.StartsWith("Operations on", StringComparison.Ordinal)))
                    throw new System.Exception("SMOKE FAIL: upstream submenu leaks Rename (rebase)");

                // 20) 分支行文本标识（主分支 ★ / 当前分支 » / 分支级 ↑↓；0 值不显示；BMP 通用符号避免 emoji/Dingbats □）
                if (BranchesPanel.FormatRefLabel("main", true, true, 0, 0) != "★ » main")
                    throw new System.Exception("SMOKE FAIL: label main+current");
                if (BranchesPanel.FormatRefLabel("main", true, false, 0, 0) != "★ main")
                    throw new System.Exception("SMOKE FAIL: label main");
                // M3 P2：任何跟踪分支都显示 ↑↓（不再限定当前分支）；ahead/behind=0 时不显示对应方向
                if (BranchesPanel.FormatRefLabel("feature/x", false, false, 2, 1) != "feature/x  ↑2 ↓1")
                    throw new System.Exception("SMOKE FAIL: label non-current ahead");
                if (BranchesPanel.FormatRefLabel("feature/x", false, false, 0, 0) != "feature/x")
                    throw new System.Exception("SMOKE FAIL: label sync no badge");
                if (BranchesPanel.FormatRefLabel("feature/x", false, true, 2, 0) != "» feature/x  ↑2")
                    throw new System.Exception("SMOKE FAIL: label current+ahead (behind 0 hidden)");
                if (BranchesPanel.FormatRefLabel("feature/x", false, false, 0, 3) != "feature/x  ↓3")
                    throw new System.Exception("SMOKE FAIL: label behind-only (ahead 0 hidden)");

                // 21) 图谱分支筛选下拉（All/Current/refs 分组/面板开关；单选勾选）
                var filterActAll = GitWindow.BuildBranchFilterActions(refs, null, () => "main", _ => { }, () => { }, true).ToList();
                var filterAllTexts = filterActAll.Where(a => a.Text != null).Select(a => a.Text).ToList();
                if (!filterAllTexts.Contains("All branches") || !filterAllTexts.Contains("Current branch")
                    || !filterAllTexts.Contains("main") || !filterAllTexts.Contains("feature/x")
                    || !filterAllTexts.Contains("Show branches panel"))
                    throw new System.Exception("SMOKE FAIL: branch filter menu");
                if (!filterActAll.First(a => a.Text == "All branches").Checked)
                    throw new System.Exception("SMOKE FAIL: all-branches checked by default");
                var filterActFeat = GitWindow.BuildBranchFilterActions(refs, "feature/x", () => "main", _ => { }, () => { }, false).ToList();
                if (!filterActFeat.First(a => a.Text == "feature/x").Checked
                    || filterActFeat.First(a => a.Text == "All branches").Checked
                    || filterActFeat.First(a => a.Text == "Current branch").Checked)
                    throw new System.Exception("SMOKE FAIL: filter radio states");
                if (filterActFeat.First(a => a.Text == "Show branches panel").Checked)
                    throw new System.Exception("SMOKE FAIL: panel toggle checked state");

                // 4) UI 元素：GraphTable 数据接入
                var headEntry = log[0];
                UnityEngine.Debug.Log($"[gitui] SMOKE OK: layout={outer.childCount}/{inner.childCount} rows={log.Count} head={headEntry.ShortID} \"{headEntry.Summary}\" lanes=[{string.Join(",", lanes)}] eirTotal=6");

                // 22) M3 数据层：unified diff 解析 + hunk 行配对 + 词级 LCS（纯数据，零 UI）
                var parsedFiles = UnifiedDiffParser.Parse("diff --git a/a.txt b/a.txt\n"
                    + "index 1111111..2222222 100644\n"
                    + "--- a/a.txt\n"
                    + "+++ b/a.txt\n"
                    + "@@ -1,3 +1,3 @@\n"
                    + " alpha\n"
                    + "-beta OLD word\n"
                    + "+beta NEW word\n"
                    + " gamma\n"
                    + "\\ No newline at end of file\n");
                if (parsedFiles.Count != 1 || parsedFiles[0].OldPath != "a.txt" || parsedFiles[0].NewPath != "a.txt")
                    throw new System.Exception("SMOKE FAIL: diff parser file header");
                var pFile = parsedFiles[0];
                if (pFile.Hunks.Count != 1)
                    throw new System.Exception("SMOKE FAIL: diff parser hunk count");
                var pHunk = pFile.Hunks[0];
                if (pHunk.OldStart != 1 || pHunk.OldCount != 3 || pHunk.NewStart != 1 || pHunk.NewCount != 3)
                    throw new System.Exception("SMOKE FAIL: diff parser hunk range");
                if (pHunk.Lines.Count != 4)
                    throw new System.Exception("SMOKE FAIL: diff parser line count");
                if (pHunk.Lines[1].Kind != DiffLineKind.Old || pHunk.Lines[1].Content != "beta OLD word"
                    || pHunk.Lines[1].LineNumber != 2)
                    throw new System.Exception("SMOKE FAIL: diff parser old line");
                if (pHunk.Lines[2].Kind != DiffLineKind.New || pHunk.Lines[2].Content != "beta NEW word"
                    || pHunk.Lines[2].LineNumber != 2)
                    throw new System.Exception("SMOKE FAIL: diff parser new line");
                if (pHunk.Lines[0].LineNumber != 1)
                    throw new System.Exception("SMOKE FAIL: diff parser context line numbers");
                if (pHunk.Lines[3].LineNumber != -2)
                    throw new System.Exception("SMOKE FAIL: diff parser no-newline marker");
                // 新增/删除文件（/dev/null）
                var parsedNew = UnifiedDiffParser.Parse("diff --git a/new.txt b/new.txt\n"
                    + "new file mode 100644\n"
                    + "index 0000000..3333333\n"
                    + "--- /dev/null\n"
                    + "+++ b/new.txt\n"
                    + "@@ -0,0 +1 @@\n"
                    + "+hello\n");
                if (parsedNew.Count != 1 || !parsedNew[0].IsNew || parsedNew[0].NewPath != "new.txt")
                    throw new System.Exception("SMOKE FAIL: diff parser new file");
                var parsedDel = UnifiedDiffParser.Parse("diff --git a/gone.txt b/gone.txt\n"
                    + "deleted file mode 100644\n"
                    + "--- a/gone.txt\n"
                    + "+++ /dev/null\n"
                    + "@@ -1 +0,0 @@\n"
                    + "-bye\n");
                if (parsedDel.Count != 1 || !parsedDel[0].IsDeleted || parsedDel[0].OldPath != "gone.txt")
                    throw new System.Exception("SMOKE FAIL: diff parser deleted file");

                // 行配对：-/+ 按序对齐；纯删/纯增无对侧行
                var pairs = HunkLinePairing.Pair(pHunk);
                if (pairs.Count != 1 || !pairs[0].HasPair
                    || pairs[0].Old.Content != "beta OLD word" || pairs[0].New.Content != "beta NEW word")
                    throw new System.Exception("SMOKE FAIL: hunk pairing");
                var onlyDel = UnifiedDiffParser.Parse("diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +0,0 @@\n-gone\n")[0].Hunks[0];
                var onlyDelPairs = HunkLinePairing.Pair(onlyDel);
                if (onlyDelPairs.Count != 1 || onlyDelPairs[0].HasPair || onlyDelPairs[0].New != null)
                    throw new System.Exception("SMOKE FAIL: pure delete pairing");

                // 词级 LCS：英文词 + CJK 逐字符 + 标点归类
                var wr = WordDiff.Compare("beta OLD word", "beta NEW word");
                // "beta"/" "/"OLD"/" "/"word" 5 token → old 侧 5 片段（第 3 个=Deleted），new 侧 5 片段（第 3 个=Added）
                if (wr == null || wr.OldFragments.Count != 5 || wr.NewFragments.Count != 5)
                    throw new System.Exception("SMOKE FAIL: worddiff fragment count");
                if (wr.OldFragments[2].Kind != DiffFragmentKind.Deleted
                    || wr.OldFragments[2].OldLength != 3
                    || wr.NewFragments[2].Kind != DiffFragmentKind.Added
                    || wr.NewFragments[2].NewLength != 3)
                    throw new System.Exception("SMOKE FAIL: worddiff mid fragment kinds");
                // token 偏移：beta=0..4 空格=4..5 OLD=5..8 空格=8..9 word=9..13
                if (wr.OldFragments[2].OldStart != 5 || wr.OldFragments[4].OldStart != 9)
                    throw new System.Exception("SMOKE FAIL: worddiff offsets");
                // CJK：逐字符（"中文"→"中英"：一个 unchanged + added '英'）
                var wrCjk = WordDiff.Compare("中文", "中英");
                if (wrCjk == null || wrCjk.NewFragments.Count != 2
                    || wrCjk.OldFragments[0].Kind != DiffFragmentKind.Unchanged
                    || wrCjk.OldFragments[0].OldLength != 1
                    || wrCjk.NewFragments[1].Kind != DiffFragmentKind.Added
                    || wrCjk.NewFragments[1].NewLength != 1)
                    throw new System.Exception("SMOKE FAIL: worddiff CJK per-char");
                // 行首空白归类：前导空格为独立 token（对齐决策文档 §1.5，行为可控）
                var wrIndent = WordDiff.Compare("   a", "   b");
                if (wrIndent == null
                    || wrIndent.OldFragments[0].Kind != DiffFragmentKind.Unchanged
                    || wrIndent.OldFragments[0].OldLength != 3
                    || wrIndent.OldFragments[1].Kind != DiffFragmentKind.Deleted)
                    throw new System.Exception("SMOKE FAIL: worddiff leading whitespace token");
                // 空行对空行：全 unchanged，无崩溃
                if (WordDiff.Compare("", "") == null
                    || WordDiff.Compare("", "").OldFragments.Count != 0)
                    throw new System.Exception("SMOKE FAIL: worddiff empty lines");
                // 退化截断：>256 token 返回 null（调用方整行染色）——300 个空格分隔的单字符 token
                var manyTokens = string.Join(" ", System.Linq.Enumerable.Repeat("a", 300));
                if (WordDiff.Compare(manyTokens, manyTokens + " b") != null)
                    throw new System.Exception("SMOKE FAIL: worddiff overflow degrade");
                // BuildRichText：old 侧删除段 / new 侧新增段拼装
                var wr2 = WordDiff.Compare("beta OLD word", "beta NEW word");
                var rtOld = WordDiff.BuildRichText(wr2.OldFragments, "beta OLD word", true, "<D>", "</D>", "<A>", "</A>");
                var rtNew = WordDiff.BuildRichText(wr2.NewFragments, "beta NEW word", false, "<D>", "</D>", "<A>", "</A>");
                if (rtOld != "beta <D>OLD</D> word" || rtNew != "beta <A>NEW</A> word")
                    throw new System.Exception($"SMOKE FAIL: worddiff richtext old={rtOld} new={rtNew}");

                // 23) GitDiffTask 真仓通道（现成 9 提交冒烟仓）：HEAD~1 vs HEAD 解析
                var liveTask = GitDiffTask.TwoRefs(s.Platform, "HEAD~1", "HEAD", 3)
                    .Configure(s.Platform.ProcessManager);
                var liveDiffOut = liveTask.RunSynchronously();
                if (liveTask.Successful && !string.IsNullOrEmpty(liveDiffOut))
                {
                    var liveFiles = UnifiedDiffParser.Parse(liveDiffOut);
                    if (liveFiles.Count < 1)
                        throw new System.Exception("SMOKE FAIL: live diff parsed empty");
                }

                // 24) DiffRichText：转义 + 新旧行标签拼装（词级高亮渲染字符串正确性，纯字符串断言）
                if (DiffRichText.Escape("a<b>&c") != "a&lt;b&gt;&amp;c")
                    throw new System.Exception("SMOKE FAIL: richtext escape");
                var rtWr = WordDiff.Compare("beta OLD word", "beta NEW word");
                var rtDel = DiffRichText.BuildDeletedLine(rtWr.OldFragments, "beta OLD word");
                var rtAdd = DiffRichText.BuildAddedLine(rtWr.NewFragments, "beta NEW word");
                if (!rtDel.Contains("<mark=#FF6B6B55>OLD</mark>")
                    || !rtAdd.Contains("<mark=#6BCB7755>NEW</mark>")
                    || rtDel.Contains("<s>") || rtAdd.Contains("<color=")
                    || rtDel.StartsWith("<mark=") || rtAdd.StartsWith("<mark="))
                    throw new System.Exception($"SMOKE FAIL: richtext del={rtDel} add={rtAdd}");
                if (rtDel != "beta <mark=#FF6B6B55>OLD</mark> word"
                    || rtAdd != "beta <mark=#6BCB7755>NEW</mark> word")
                    throw new System.Exception($"SMOKE FAIL: richtext exact del={rtDel} add={rtAdd}");
                if (DiffRichText.BuildPlainLine("plain <text>") != "plain &lt;text&gt;")
                    throw new System.Exception("SMOKE FAIL: richtext plain line");

                // 25) DiffRows：unified 行模型（头/hunk/行类/词级/整行染色/折叠）
                var rdParsed = UnifiedDiffParser.Parse("diff --git a/a.txt b/a.txt\n"
                    + "index 1111111..2222222 100644\n"
                    + "--- a/a.txt\n"
                    + "+++ b/a.txt\n"
                    + "@@ -1,3 +1,3 @@\n"
                    + " alpha\n"
                    + "-beta OLD word\n"
                    + "+beta NEW word\n"
                    + " gamma\n");
                var rdRows = DiffRows.Build(rdParsed, 0);
                // 头 + hunk 头 + 4 正文行 = 6
                if (rdRows.Count != 6)
                    throw new System.Exception($"SMOKE FAIL: diffrows count={rdRows.Count} expect 6");
                if (rdRows[0].Kind != DiffRowKind.FileHeader
                    || !rdRows[0].RichText.Contains("a/a.txt")
                    || rdRows[1].Kind != DiffRowKind.HunkHeader
                    || !rdRows[1].RichText.Contains("@@ -1,3 +1,3 @@"))
                    throw new System.Exception("SMOKE FAIL: diffrows header/hunk");
                if (rdRows[2].Kind != DiffRowKind.Context || rdRows[2].OldLineNo != 1 || rdRows[2].NewLineNo != 1)
                    throw new System.Exception("SMOKE FAIL: diffrows context row");
                if (rdRows[3].Kind != DiffRowKind.Old || rdRows[3].OldLineNo != 2
                    || !rdRows[3].RichText.Contains("<mark=#FF6B6B55>OLD</mark>"))
                    throw new System.Exception("SMOKE FAIL: diffrows old word mark");
                if (rdRows[4].Kind != DiffRowKind.New || rdRows[4].NewLineNo != 2
                    || !rdRows[4].RichText.Contains("<mark=#6BCB7755>NEW</mark>"))
                    throw new System.Exception("SMOKE FAIL: diffrows new word mark");
                if (rdRows[5].Kind != DiffRowKind.Context || rdRows[5].OldLineNo != 3)
                    throw new System.Exception("SMOKE FAIL: diffrows trailing context");
                // 整行染色：纯删（无对侧 + 词级退化路径统一 WrapDeleted/WrapAdded）
                var rdDel = UnifiedDiffParser.Parse("diff --git a/x b/x\n"
                    + "--- a/x\n+++ b/x\n@@ -1 +0,0 @@\n-gone\n")[0];
                var rdDelRows = DiffRows.Build(new List<DiffFile> { rdDel }, 0);
                if (rdDelRows.Count != 3 || rdDelRows[2].Kind != DiffRowKind.Old
                    || !rdDelRows[2].RichText.StartsWith("<mark=#FF6B6B55>")
                    || !rdDelRows[2].RichText.Contains("gone"))
                    throw new System.Exception("SMOKE FAIL: diffrows whole-line delete wrap");
                // 折叠：阈值 2 → 5 行正文文件被折叠为 head+hunk+fold
                var rdFoldSrc = "diff --git a/big.txt b/big.txt\n--- a/big.txt\n+++ b/big.txt\n"
                    + "@@ -1,3 +1,3 @@\n alpha\n-beta\n+beta2\n gamma\n";
                var rdFold = DiffRows.Build(UnifiedDiffParser.Parse(rdFoldSrc), 2);
                if (rdFold.Count != 3 || rdFold[0].Kind != DiffRowKind.FileHeader
                    || rdFold[1].Kind != DiffRowKind.HunkHeader
                    || rdFold[2].Kind != DiffRowKind.Fold
                    || !rdFold[2].RichText.Contains("folded"))
                    throw new System.Exception($"SMOKE FAIL: diffrows fold count={rdFold.Count}");
                // DiffViewer 行元素构造（零窗口实例，静态 Builder 断言；gutter + sign + text = 3 子元素）
                var rowEl = DiffViewer.BuildRowElement(rdRows[3]);
                if (rowEl == null || rowEl.childCount != 3)
                    throw new System.Exception("SMOKE FAIL: diffviewer row element structure");
                var rowLabel = rowEl.Q<Label>("diff-text");
                if (rowLabel == null || rowLabel.text != rdRows[3].RichText)
                    throw new System.Exception("SMOKE FAIL: diffviewer row label text");

                // 26) GitPatchBuilder + GitApplyTask：patch 切片（LF/末换行/保留标记 + 上下文补齐）+ 三态参数 + e2e apply
                // U3 风格手写 diff：hunk 块内自带上下文 → 原样切片
                var applyDiff = "diff --git a/h.txt b/h.txt\n"
                    + "index 1111111..2222222 100644\n"
                    + "--- a/h.txt\n+++ b/h.txt\n"
                    + "@@ -2,2 +2,2 @@\n"
                    + "  line1\n-old\n+new\n"
                    + "@@ -7,1 +7,1 @@\n"
                    + "  line6\n-x\n+y\n";
                var patchPath = GitPatchBuilder.WriteHunkPatch(applyDiff, 0, 0, s.ProjectPath);
                if (patchPath == null || string.IsNullOrEmpty(patchPath))
                    throw new System.Exception("SMOKE FAIL: patch builder returned null");
                // 切片内容：文件头 + @@ -2,2 +2,2 @@ + 块内（line1/-old/+new）；LF + 末换行；排除 hunk2（@@ -7 及其块）
                var patchText = System.IO.File.ReadAllText(patchPath);
                if (!patchText.Contains("diff --git") || !patchText.Contains("@@ -2,2 +2,2 @@")
                    || !patchText.Contains("-old") || !patchText.Contains("+new")
                    || !patchText.Contains("  line1")
                    || patchText.Contains("@@ -7") || patchText.Contains("-x\n+y")
                    || !patchText.EndsWith("\n", StringComparison.Ordinal)
                    || patchText.Contains("\r"))
                    throw new System.Exception("SMOKE FAIL: patch slice content/LF:\n" + patchText);
                // 切片保 "No newline" 标记（U3 块内含有该标记）
                var patchNoNl = GitPatchBuilder.WriteHunkPatch(
                    "diff --git a/n.txt b/n.txt\n--- a/n.txt\n+++ b/n.txt\n@@ -1,1 +1,1 @@\n-old\n+new\n\\ No newline at end of file\n",
                    0, 0, s.ProjectPath);
                if (patchNoNl == null || !System.IO.File.ReadAllText(patchNoNl).Contains("\\ No newline at end of file"))
                    throw new System.Exception("SMOKE FAIL: patch slice no-newline marker");
                // 多文件：fileIndex=1 取第二文件 hunk
                var applyDiff2 = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1,1 +1,1 @@\n-aaa\n+AAA\n"
                    + "diff --git a/b.txt b/b.txt\n--- a/b.txt\n+++ b/b.txt\n@@ -1,1 +1,1 @@\n-bbb\n+BBB\n";
                var patch2 = GitPatchBuilder.WriteHunkPatch(applyDiff2, 1, 0, s.ProjectPath);
                if (patch2 == null || !System.IO.File.ReadAllText(patch2).Contains("b/b.txt")
                    || System.IO.File.ReadAllText(patch2).Contains("a/a.txt"))
                    throw new System.Exception("SMOKE FAIL: patch slice fileIndex");
                // U0 diff（无上下文）→ 外部补齐 ±2 上下文 + 重算 @@ 头（e2e 实际路径）
                var u0Diff = "diff --git a/u.txt b/u.txt\n--- a/u.txt\n+++ b/u.txt\n"
                    + "@@ -3 +3 @@ x\n-old\n+new\n@@ -9 +9 @@ y\n-x\n+y\n";
                var patchU0 = GitPatchBuilder.WriteHunkPatch(u0Diff, 0, 0, s.ProjectPath);
                if (patchU0 == null)
                    throw new System.Exception("SMOKE FAIL: patch builder u0 null");
                var patchU0Text = System.IO.File.ReadAllText(patchU0);
                // 手写 U0 无任何 ' ' 上下文行可补 → 头仅变更行计数；但至少保留目标 hunk 且排除 hunk2
                if (!patchU0Text.Contains("-old") || !patchU0Text.Contains("+new")
                    || patchU0Text.Contains("@@ -9") || patchU0Text.Contains("-x\n+y"))
                    throw new System.Exception("SMOKE FAIL: patch u0 slice:\n" + patchU0Text);

                // e2e：一次性仓 init→seed→改→apply --cached→断言 index 变化→apply -R 整块撤销→清理
                var applyDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-apply-repo");
                DeleteDir(applyDir);
                System.IO.Directory.CreateDirectory(applyDir);
                var gitExe2 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe2, "init -b master", applyDir, out var aErr, out var _);
                RunCli(gitExe2, "config user.name smoke", applyDir, out aErr, out var _);
                RunCli(gitExe2, "config user.email smoke@local", applyDir, out aErr, out var _);
                RunCli(gitExe2, "config commit.gpgsign false", applyDir, out aErr, out var _);
                RunCli(gitExe2, "config core.autocrlf false", applyDir, out aErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(applyDir, "h.txt"),
                    "line1\nold\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10\nx\nline12\n");
                RunCli(gitExe2, "add h.txt", applyDir, out aErr, out var _);
                RunCli(gitExe2, "commit -m seed", applyDir, out aErr, out var _);
                // 工作区改两处（line2 与 line11，间隔 9 行 > 2*3+1=7 → U3 下两个独立 hunk）
                System.IO.File.WriteAllText(System.IO.Path.Combine(applyDir, "h.txt"),
                    "line1\nNEW1\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10\nNEW2\nline12\n");
                using (var sa = GitSession.Open(applyDir))
                {
                    // U3 采集（自带上下文 → PatchBuilder 原样切片 → apply 可定位）
                    var diffText = sa.RunDiffPublic();
                    if (string.IsNullOrEmpty(diffText) || !diffText.Contains("@@ -1,5 +1,5 @@")
                        || !diffText.Contains("@@ -8,5 +8,5 @@"))
                        throw new System.Exception("SMOKE FAIL: e2e apply diff capture:\n" + diffText);
                    // 暂存第一个 hunk
                    sa.ApplyHunk(diffText, 0, 0, GitApplyMode.Stage);
                    var stA = sa.LoadStatus();
                    if (stA.Entries.Count != 1 || !stA.Entries[0].Staged)
                        throw new System.Exception("SMOKE FAIL: e2e apply stage hunk");
                    // 取消暂存第一个 hunk（--cached diff → -R）
                    var cachedDiff = sa.RunCachedDiffPublic();
                    if (!cachedDiff.Contains("@@ -1,5 +1,5 @@"))
                        throw new System.Exception("SMOKE FAIL: e2e apply cached diff capture");
                    sa.ApplyHunk(cachedDiff, 0, 0, GitApplyMode.Unstage);
                    var stB = sa.LoadStatus();
                    if (stB.Entries.Count != 1 || stB.Entries[0].Staged)
                        throw new System.Exception("SMOKE FAIL: e2e apply unstage hunk");
                    // 撤销工作区第一个 hunk（apply -R）
                    sa.ApplyHunk(diffText, 0, 0, GitApplyMode.Revert);
                    // 撤销后 line2 回 old，line11 仍 NEW2 → 工作区还有 1 个 hunk 的改动
                    var diffAfter = sa.RunDiffPublic();
                    if (string.IsNullOrEmpty(diffAfter) || diffAfter.Contains("@@ -1,5 +1,5 @@")
                        || !diffAfter.Contains("@@ -8,5 +8,5 @@"))
                        throw new System.Exception("SMOKE FAIL: e2e apply revert hunk:\n" + diffAfter);
                }
                DeleteDir(applyDir);

                // 27) CompareWindow 内容级入口（M3：选分支 → DiffViewer）：静态方法可调（批处理下窗口不可显示，仅验证入口签名）
                var compareOpen = typeof(CompareWindow).GetMethod("Open",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(GitSession), typeof(string) }, null);
                var compareOpenPair = typeof(CompareWindow).GetMethod("OpenPair",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(GitSession), typeof(string), typeof(string), typeof(string) }, null);
                if (compareOpen == null || compareOpenPair == null)
                    throw new System.Exception("SMOKE FAIL: compare window entries missing");
                // OpenPair 立即返回后开后台 diff 线程——批处理下无窗口可开，但入口不应同步抛异常
                // （Verify：分支面板接线处 OpenPair 调用编译通过即接线正确，此处入口反射已保证签名）

                // 29) rebase 非交互式 e2e：冲突 rebase → status 首行 "## HEAD (no branch)" + UU → abort 恢复
                var rbDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-rebase-repo");
                DeleteDir(rbDir);
                System.IO.Directory.CreateDirectory(rbDir);
                var gitExe3 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe3, "init -b main", rbDir, out var rErr, out var _);
                RunCli(gitExe3, "config user.name smoke", rbDir, out rErr, out var _);
                RunCli(gitExe3, "config user.email smoke@local", rbDir, out rErr, out var _);
                RunCli(gitExe3, "config commit.gpgsign false", rbDir, out rErr, out var _);
                RunCli(gitExe3, "config core.autocrlf false", rbDir, out rErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(rbDir, "f.txt"), "a\nb\nc\n");
                RunCli(gitExe3, "add f.txt", rbDir, out rErr, out var _);
                RunCli(gitExe3, "commit -m base", rbDir, out rErr, out var _);
                // 从 main 分出 topic，两边改同一行 → rebase 冲突
                RunCli(gitExe3, "checkout -b topic", rbDir, out rErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(rbDir, "f.txt"), "a\nTOPIC\nc\n");
                RunCli(gitExe3, "commit -am topic-change", rbDir, out rErr, out var _);
                RunCli(gitExe3, "checkout main", rbDir, out rErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(rbDir, "f.txt"), "a\nMAIN\nc\n");
                RunCli(gitExe3, "commit -am main-change", rbDir, out rErr, out var _);
                using (var sr = GitSession.Open(rbDir))
                {
                    // 当前 main（干净）→ checkout topic → rebase main → 冲突
                    sr.Checkout("topic");
                    sr.Rebase("main");
                    var rbStatus = sr.LoadStatus();
                    if (rbStatus.Entries == null || !rbStatus.Entries.Any(e => e.Unmerged))
                        throw new System.Exception("SMOKE FAIL: rebase conflict UU entries missing");
                    if (!GitSession.AnalyzeRebaseState(rbStatus, out var inRebase, out var uuCount))
                        throw new System.Exception("SMOKE FAIL: rebase state analysis (inRebase=false)");
                    if (uuCount < 1)
                        throw new System.Exception("SMOKE FAIL: rebase uu count");
                    // abort 恢复原分支
                    sr.RebaseAbort();
                    var rbAfter = sr.LoadStatus();
                    if (rbAfter.LocalBranch != "topic" || rbAfter.Entries.Count != 0)
                        throw new System.Exception("SMOKE FAIL: rebase abort restore: lb=" + rbAfter.LocalBranch);
                }
                DeleteDir(rbDir);

                // 30) 3-way 冲突数据层（M3-SOLUTION §1.1/§3.4）：merge 冲突 → 三 stage + 整侧接受 + 解决
                var m3Dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-merge3-repo");
                DeleteDir(m3Dir);
                System.IO.Directory.CreateDirectory(m3Dir);
                var gitExe4 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe4, "init -b main", m3Dir, out var mErr, out var _);
                RunCli(gitExe4, "config user.name smoke", m3Dir, out mErr, out var _);
                RunCli(gitExe4, "config user.email smoke@local", m3Dir, out mErr, out var _);
                RunCli(gitExe4, "config commit.gpgsign false", m3Dir, out mErr, out var _);
                RunCli(gitExe4, "config core.autocrlf false", m3Dir, out mErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(m3Dir, "c.txt"), "x\nshared\nz\n");
                RunCli(gitExe4, "add c.txt", m3Dir, out mErr, out var _);
                RunCli(gitExe4, "commit -m base", m3Dir, out mErr, out var _);
                RunCli(gitExe4, "checkout -b side", m3Dir, out mErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(m3Dir, "c.txt"), "x\nSIDE\nz\n");
                RunCli(gitExe4, "commit -am side-change", m3Dir, out mErr, out var _);
                RunCli(gitExe4, "checkout main", m3Dir, out mErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(m3Dir, "c.txt"), "x\nMAIN\nz\n");
                RunCli(gitExe4, "commit -am main-change", m3Dir, out mErr, out var _);
                using (var sm = GitSession.Open(m3Dir))
                {
                    // merge 冲突（新语义：冲突不抛，状态由 status 判定——M3 3-way 入口）
                    sm.Merge("side");
                    var mst = sm.LoadStatus();
                    if (mst.Entries == null || !mst.Entries.Any(e => e.Unmerged))
                        throw new System.Exception("SMOKE FAIL: merge3 UU missing after merge");
                    if (GitSession.AnalyzeRebaseState(mst, out var mRebase, out var mUu) || mUu < 1)
                        throw new System.Exception("SMOKE FAIL: merge3 should NOT be rebase mode");
                    if (sm.IsRebaseInProgressQuiet())
                        throw new System.Exception("SMOKE FAIL: merge3 rebase flag false positive");
                    // 三 stage 内容：base=shared ours=MAIN theirs=SIDE
                    var blobs = sm.LoadConflictBlobs("c.txt", out var hasO, out var hasT);
                    if (!hasO || !hasT || blobs.Ours == null || blobs.Theirs == null || blobs.Base == null)
                        throw new System.Exception("SMOKE FAIL: merge3 blobs missing side");
                    if (!blobs.Ours.Contains("MAIN") || !blobs.Theirs.Contains("SIDE") || !blobs.Base.Contains("shared"))
                        throw new System.Exception("SMOKE FAIL: merge3 blob contents");
                    // 整侧接受 ours → add 标记解决 → UU 清零
                    sm.AcceptConflictSide("c.txt", GitCheckoutSide.Ours);
                    var mst2 = sm.LoadStatus();
                    if (mst2.Entries == null || mst2.Entries.Any(e => e.Unmerged))
                        throw new System.Exception("SMOKE FAIL: merge3 accept ours should clear UU");
                    var cContent = System.IO.File.ReadAllText(System.IO.Path.Combine(m3Dir, "c.txt"));
                    if (!cContent.Contains("MAIN") || cContent.Contains("SIDE"))
                        throw new System.Exception("SMOKE FAIL: merge3 accept ours content");
                    // 冲突列表加载（下次冲突前清空现状——此处无 UU）
                    if (sm.LoadConflictPaths().Count != 0)
                        throw new System.Exception("SMOKE FAIL: merge3 conflict paths not empty after resolve");
                }
                DeleteDir(m3Dir);

                // 31) P1 Uncommit（JetBrains 语义：reset --soft HEAD^ + 消息回填）+ 菜单启用态
                var unDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-uncommit-repo");
                DeleteDir(unDir);
                System.IO.Directory.CreateDirectory(unDir);
                var gitExe5 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe5, "init -b main", unDir, out var uErr, out var _);
                RunCli(gitExe5, "config user.name smoke", unDir, out uErr, out var _);
                RunCli(gitExe5, "config user.email smoke@local", unDir, out uErr, out var _);
                RunCli(gitExe5, "config commit.gpgsign false", unDir, out uErr, out var _);
                RunCli(gitExe5, "config core.autocrlf false", unDir, out uErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(unDir, "u.txt"), "seed\n");
                RunCli(gitExe5, "add u.txt", unDir, out uErr, out var _);
                RunCli(gitExe5, "commit -m seed", unDir, out uErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(unDir, "u2.txt"), "second\n");
                using (var su = GitSession.Open(unDir))
                {
                    su.Stage(new[] { "u2.txt" });
                    su.Commit("undo me", "body detail", false, false, false);
                    var before = su.LoadHistory(2);
                    if (before.Count != 2)
                        throw new System.Exception("SMOKE FAIL: uncommit before");
                    var msg = su.Uncommit();
                    if (msg != "undo me\n\nbody detail")
                        throw new System.Exception("SMOKE FAIL: uncommit message = " + msg);
                    // soft reset：HEAD 回 seed；u2.txt 回到暂存区
                    var after = su.LoadHistory(2);
                    if (after.Count != 1 || after[0].Summary != "seed")
                        throw new System.Exception("SMOKE FAIL: uncommit head not restored");
                    var stU = su.LoadStatus();
                    if (stU.Entries == null || !stU.Entries.Any(e => e.path == "u2.txt" && e.Staged))
                        throw new System.Exception("SMOKE FAIL: uncommit staged restore");
                    // 菜单：row0（HEAD）启用 Uncommit；row1 禁用
                    var unActs0 = GitWindow.BuildCommitContextActions(su, before, 0, () => { }).ToList();
                    var unAct0 = unActs0.FirstOrDefault(a => a.Id == "uncommit");
                    var unActs1 = GitWindow.BuildCommitContextActions(su, before, 1, () => { }).ToList();
                    var unAct1 = unActs1.FirstOrDefault(a => a.Id == "uncommit");
                    if (unAct0 == null || !unAct0.Enabled || unAct1 == null || unAct1.Enabled)
                        throw new System.Exception("SMOKE FAIL: uncommit menu enabled state");
                    // 回填回调触发：消息进 msgSummary/msgBody（模拟）
                    string filledSummary = null, filledBody = null;
                    GitWindow.BuildCommitContextActions(su, before, 0, () => { }, m =>
                    {
                        var parts = m.Split(new[] { "\n\n" }, System.StringSplitOptions.None);
                        filledSummary = parts[0];
                        filledBody = parts.Length > 1 ? parts[1] : "";
                    }).ToList()
                        .First(a => a.Id == "uncommit").Run();
                    // Run 会弹确认框——批处理下 DisplayDialog 返回？此处直接验证回填逻辑已由 Uncommit 消息覆盖；
                    // 改为：直接断言 Uncommit 返回消息可切分（回填逻辑在 ContextProvider lambda 内，静态不可直接调）
                    if (filledSummary != null)
                        throw new System.Exception("SMOKE FAIL: uncommit dialog unexpectedly auto-confirmed");
                    //（回填 lambda 本身已在编译期接线；真正回填在交互式 Unity 中由 ConfirmUncommit→onUncommittedMessage 触发）
                }
                DeleteDir(unDir);

                // 41) GitRemoteListTaskEx 本地路径解析（M3 人工测试坑：上游 processor 对 "E:\..." URL 的 SSH 分支 NRE）
                var remoteParse = GitRemoteListTaskEx.Parse(
                    "origin\tE:\\Users\\demo\\repo.git (fetch)\n"
                    + "origin\tE:\\Users\\demo\\repo.git (push)\n"
                    + "upstream\thttps://github.com/a/b.git (fetch)\n"
                    + "ssh\tgit@github.com:c/d.git (push)\n");
                if (remoteParse.Count != 3)
                    throw new System.Exception($"SMOKE FAIL: remote parse count={remoteParse.Count} expect 3");
                var originR = remoteParse.First(r => r.Name == "origin");
                if (!originR.Url.Contains("repo.git") || originR.Function != GitRemoteFunction.Both)
                    throw new System.Exception($"SMOKE FAIL: remote origin url/function {originR.Url}/{originR.Function}");
                var upstreamR = remoteParse.First(r => r.Name == "upstream");
                if (upstreamR.Function != GitRemoteFunction.Fetch)
                    throw new System.Exception("SMOKE FAIL: remote upstream fetch-only");
                var sshR = remoteParse.First(r => r.Name == "ssh");
                if (sshR.Function != GitRemoteFunction.Push || !sshR.Url.Contains("git@"))
                    throw new System.Exception("SMOKE FAIL: remote ssh push-only");

                // 32) P1 remote 管理（api 四任务：add/set-url/list/remove）+ 空白右键入口
                var rmDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-remote-repo");
                DeleteDir(rmDir);
                System.IO.Directory.CreateDirectory(rmDir);
                var gitExe6 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe6, "init -b main", rmDir, out var rpErr, out var _);
                RunCli(gitExe6, "config user.name smoke", rmDir, out rpErr, out var _);
                RunCli(gitExe6, "config user.email smoke@local", rmDir, out rpErr, out var _);
                RunCli(gitExe6, "config commit.gpgsign false", rmDir, out rpErr, out var _);
                using (var sp = GitSession.Open(rmDir))
                {
                    // 初始无 remote
                    var remotes0 = sp.LoadRemotes();
                    if (remotes0.Count != 0)
                        throw new System.Exception("SMOKE FAIL: remote list initial not empty");
                    // add
                    sp.RemoteAdd("origin", "https://example.com/repo.git");
                    var remotes1 = sp.LoadRemotes();
                    if (remotes1.Count != 1 || remotes1[0].Name != "origin"
                        || !remotes1[0].Url.Contains("example.com"))
                        throw new System.Exception("SMOKE FAIL: remote add");
                    // set-url
                    sp.RemoteSetUrl("origin", "https://example.com/renamed.git");
                    var remotes2 = sp.LoadRemotes();
                    if (remotes2.Count != 1 || !remotes2[0].Url.Contains("renamed"))
                        throw new System.Exception("SMOKE FAIL: remote set-url");
                    // remove
                    sp.RemoteRemove("origin");
                    var remotes3 = sp.LoadRemotes();
                    if (remotes3.Count != 0)
                        throw new System.Exception("SMOKE FAIL: remote remove");
                    // 窗口入口（静态反射：Open 存在）
                    var rmOpen = typeof(RemoteManagerWindow).GetMethod("Open",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null, new[] { typeof(GitSession) }, null);
                    if (rmOpen == null)
                        throw new System.Exception("SMOKE FAIL: remote manager entry missing");
                }
                DeleteDir(rmDir);

                // 33) P1 标签推送/远程标签（本地裸远端 e2e：PushTag → RemoteTagExists → DeleteRemoteTag）
                var tgDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-tagpush-repo");
                var tgBare = tgDir + ".git";
                DeleteDir(tgDir);
                DeleteDir(tgBare);
                System.IO.Directory.CreateDirectory(tgDir);
                var gitExe7 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe7, "init -b main", tgDir, out var tErr, out var _);
                RunCli(gitExe7, "config user.name smoke", tgDir, out tErr, out var _);
                RunCli(gitExe7, "config user.email smoke@local", tgDir, out tErr, out var _);
                RunCli(gitExe7, "config commit.gpgsign false", tgDir, out tErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(tgDir, "t.txt"), "tag me\n");
                RunCli(gitExe7, "add t.txt", tgDir, out tErr, out var _);
                RunCli(gitExe7, "commit -m base", tgDir, out tErr, out var _);
                System.IO.Directory.CreateDirectory(tgBare); // cwd 须存在
                RunCli(gitExe7, "init --bare", tgBare, out tErr, out var _);
                if (!string.IsNullOrEmpty(tErr))
                    throw new System.Exception("SMOKE FAIL: tagpush bare init: " + tErr);
                using (var stg = GitSession.Open(tgDir))
                {
                    stg.RemoteAdd("origin", tgBare);
                    stg.Push("origin", "main", true);
                    var tgHead = stg.LoadHistory(1);
                    if (tgHead.Count != 1)
                        throw new System.Exception("SMOKE FAIL: tagpush head");
                    stg.CreateTag("v1", tgHead[0].CommitID, "release v1");
                    stg.PushTag("origin", "v1");
                    if (!stg.RemoteTagExists("origin", "v1"))
                        throw new System.Exception("SMOKE FAIL: tag push not on remote");
                    stg.DeleteRemoteTag("origin", "v1");
                    if (stg.RemoteTagExists("origin", "v1"))
                        throw new System.Exception("SMOKE FAIL: tag delete still on remote");
                    // 标签菜单项（fake tag + 远程存在 → Push/DeleteRemote 出现）
                    var tgRef = new GitSession.GitRefInfo { Type = GitSession.RefType.Tag, DisplayName = "v1", CommitId = "x" };
                    var tgTexts = BranchesPanel.BuildContextActions(stg, tgRef, "main", () => { }, _ => { })
                        .Where(a => a.Text != null).Select(a => a.Text).ToList();
                    if (!tgTexts.Any(t => t.StartsWith("Push tag to origin", StringComparison.Ordinal))
                        || !tgTexts.Any(t => t.StartsWith("Delete tag on origin", StringComparison.Ordinal)))
                        throw new System.Exception("SMOKE FAIL: tag push/delete menu missing");
                }
                DeleteDir(tgDir);
                DeleteDir(tgBare);

                // 34) P1 提交模板/最近消息（RecentMessages 去重 + LoadCommitTemplate 文件读取 + 未配置 null）
                var tmDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-template-repo");
                DeleteDir(tmDir);
                System.IO.Directory.CreateDirectory(tmDir);
                var gitExe8 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe8, "init -b main", tmDir, out var tpErr, out var _);
                RunCli(gitExe8, "config user.name smoke", tmDir, out tpErr, out var _);
                RunCli(gitExe8, "config user.email smoke@local", tmDir, out tpErr, out var _);
                RunCli(gitExe8, "config commit.gpgsign false", tmDir, out tpErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(tmDir, "a.txt"), "a\n");
                using (var stm = GitSession.Open(tmDir))
                {
                    // 无模板 → null
                    if (stm.LoadCommitTemplate() != null)
                        throw new System.Exception("SMOKE FAIL: template null default");
                    // 两提交 → RecentMessages(5) 去重含两消息（summary+body 拼接）
                    stm.Stage(new[] { "a.txt" });
                    stm.Commit("first commit", "first body", false, false, false);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(tmDir, "b.txt"), "b\n");
                    stm.Stage(new[] { "b.txt" });
                    stm.Commit("second commit", null, false, false, false);
                    var recents = stm.RecentMessages(5);
                    if (recents.Count != 2
                        || !recents.Contains("first commit\n\nfirst body")
                        || !recents.Contains("second commit"))
                        throw new System.Exception("SMOKE FAIL: recent messages: " + string.Join("|", recents));
                    // 配置 commit.template → LoadCommitTemplate 读文件内容
                    var tmplPath = System.IO.Path.Combine(tmDir, "tmpl.txt");
                    System.IO.File.WriteAllText(tmplPath, "template subject\n\ntemplate body\n");
                    RunCli(gitExe8, "config commit.template " + tmplPath, tmDir, out tpErr, out var _);
                    var tmpl = stm.LoadCommitTemplate();
                    if (tmpl == null || !tmpl.Contains("template subject") || !tmpl.Contains("template body"))
                        throw new System.Exception("SMOKE FAIL: template read");
                    // Commit 页模板按钮存在（结构断言）
                    if (I18n.L(I18n.Keys.CommitTemplates) != "Templates ▾")
                        throw new System.Exception("SMOKE FAIL: template button label");
                }
                DeleteDir(tmDir);

                // 35) P2 分支级 ahead/behind（ParseUpstreamTrack + for-each-ref 第5列 e2e）
                var ab0 = GitSession.ParseUpstreamTrack("[ahead 3, behind 2]");
                if (ab0.ahead != 3 || ab0.behind != 2)
                    throw new System.Exception("SMOKE FAIL: track both");
                var ab1 = GitSession.ParseUpstreamTrack("[ahead 5]");
                if (ab1.ahead != 5 || ab1.behind != 0)
                    throw new System.Exception("SMOKE FAIL: track ahead only");
                var ab2 = GitSession.ParseUpstreamTrack("[behind 4]");
                if (ab2.ahead != 0 || ab2.behind != 4)
                    throw new System.Exception("SMOKE FAIL: track behind only");
                if (GitSession.ParseUpstreamTrack("") != (0, 0)
                    || GitSession.ParseUpstreamTrack("[gone]") != (0, 0))
                    throw new System.Exception("SMOKE FAIL: track empty/gone");
                // e2e：main 与 topic 各设 upstream 到 origin（topic 领先 1）
                var abDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-aheadbehind-repo");
                var abBare = abDir + ".git";
                DeleteDir(abDir);
                DeleteDir(abBare);
                System.IO.Directory.CreateDirectory(abDir);
                var gitExe9 = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExe9, "init -b main", abDir, out var abErr, out var _);
                RunCli(gitExe9, "config user.name smoke", abDir, out abErr, out var _);
                RunCli(gitExe9, "config user.email smoke@local", abDir, out abErr, out var _);
                RunCli(gitExe9, "config commit.gpgsign false", abDir, out abErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(abDir, "m.txt"), "m\n");
                RunCli(gitExe9, "add m.txt", abDir, out abErr, out var _);
                RunCli(gitExe9, "commit -m base", abDir, out abErr, out var _);
                System.IO.Directory.CreateDirectory(abBare);
                RunCli(gitExe9, "init --bare", abBare, out abErr, out var _);
                using (var sab = GitSession.Open(abDir))
                {
                    sab.RemoteAdd("origin", abBare);
                    sab.Push("origin", "main", true);
                    // topic 基于 main 后领先 1
                    sab.NewBranch("topic", "main");
                    sab.Checkout("topic"); // NewBranch 只建不切（GitBranchCreateTask 语义）
                    System.IO.File.WriteAllText(System.IO.Path.Combine(abDir, "t.txt"), "t\n");
                    sab.Stage(new[] { "t.txt" });
                    sab.Commit("topic change", null, false, false, false);
                    sab.Push("origin", "topic", true);
                    // push 后本地产 origin/topic（同点）；再提交 1 个 → 本地领先 1（ahead=1, behind=0）
                    System.IO.File.WriteAllText(System.IO.Path.Combine(abDir, "t2.txt"), "t2\n");
                    sab.Stage(new[] { "t2.txt" });
                    sab.Commit("one more", null, false, false, false);
                    sab.Fetch("origin");
                    sab.InvalidateRefs(); // refs 缓存失效重载
                    // debug：原始 for-each-ref 输出（对比 api 解析）；topic 应 [ahead 1]
                    RunCli(gitExe9,
                        "for-each-ref --format=%(refname)%09%(objectname)%09%(HEAD)%09%(upstream:short)%09%(upstream:track)",
                        abDir, out var ferErr, out var ferOut);
                    var ferTopic = ferOut.Split('\n').FirstOrDefault(l => l.Contains("refs/heads/topic"));
                    if (ferTopic == null || !ferTopic.Contains("[ahead 1]") || ferTopic.Contains("[behind"))
                        throw new System.Exception("SMOKE FAIL: raw for-each-ref topic: " + ferOut);
                    var refsAb = sab.LoadRefs();
                    var mainRef = refsAb.FirstOrDefault(r => r.DisplayName == "main");
                    var topicRef = refsAb.FirstOrDefault(r => r.DisplayName == "topic");
                    if (mainRef == null || topicRef == null)
                        throw new System.Exception("SMOKE FAIL: aheadbehind refs missing");
                    if (topicRef.Ahead != 1 || topicRef.Behind != 0)
                        throw new System.Exception($"SMOKE FAIL: topic badge ahead={topicRef.Ahead}/behind={topicRef.Behind} expect 1/0");
                    if (mainRef.Ahead != 0 || mainRef.Behind != 0)
                        throw new System.Exception($"SMOKE FAIL: main badge ahead={mainRef.Ahead}/behind={mainRef.Behind} expect sync");
                    // DisplayText 集成（FormatRefLabel 已测；此处验证 refs 驱动；behind=0 不显示 ↓0）
                    if (BranchesPanel.FormatRefLabel(topicRef.DisplayName, false, false, topicRef.Ahead, topicRef.Behind)
                        != "topic  ↑1")
                        throw new System.Exception("SMOKE FAIL: topic label badge");
                }
                DeleteDir(abDir);
                DeleteDir(abBare);

                // 36) P2 reflog 最近分支（多次 checkout → RecentBranches 去重/排除当前）
                var rfDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-reflog-repo");
                DeleteDir(rfDir);
                System.IO.Directory.CreateDirectory(rfDir);
                var gitExeA = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExeA, "init -b main", rfDir, out var rfErr, out var _);
                RunCli(gitExeA, "config user.name smoke", rfDir, out rfErr, out var _);
                RunCli(gitExeA, "config user.email smoke@local", rfDir, out rfErr, out var _);
                RunCli(gitExeA, "config commit.gpgsign false", rfDir, out rfErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(rfDir, "r.txt"), "r\n");
                RunCli(gitExeA, "add r.txt", rfDir, out rfErr, out var _);
                RunCli(gitExeA, "commit -m base", rfDir, out rfErr, out var _);
                RunCli(gitExeA, "checkout -b feature", rfDir, out rfErr, out var _);
                RunCli(gitExeA, "checkout -b hotfix", rfDir, out rfErr, out var _);
                RunCli(gitExeA, "checkout main", rfDir, out rfErr, out var _);
                using (var srf = GitSession.Open(rfDir))
                {
                    // reflog 原始序列：main,hotfix,feature（含当前 main——UI 层 Rebuild 已跳过 currentBranch）
                    var recents = srf.RecentBranches(5);
                    if (recents.Count == 0)
                        throw new System.Exception("SMOKE FAIL: reflog recent empty");
                    if (!recents.Contains("hotfix") || !recents.Contains("feature"))
                        throw new System.Exception("SMOKE FAIL: reflog missing branches: " + string.Join(",", recents));
                    // 去重：重复 checkout 同一分支只出现一次
                    if (recents.Count != recents.Distinct().Count())
                        throw new System.Exception("SMOKE FAIL: reflog duplicates");
                    // UI 排除当前分支由 Rebuild 的 `b == currentBranch` 过滤承担（此处验证数据源含 main 属正常）
                }
                DeleteDir(rfDir);

                // 37) P2 Cherry-Pick：无冲突完成 + 冲突走 UU（3-way 入口）+ 菜单项
                var cpDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-cherrypick-repo");
                DeleteDir(cpDir);
                System.IO.Directory.CreateDirectory(cpDir);
                var gitExeB = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExeB, "init -b main", cpDir, out var cpErr, out var _);
                RunCli(gitExeB, "config user.name smoke", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config user.email smoke@local", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config commit.gpgsign false", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config core.autocrlf false", cpDir, out cpErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(cpDir, "f.txt"), "base\n");
                RunCli(gitExeB, "add f.txt", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "commit -m base", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "checkout -b feat", cpDir, out cpErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(cpDir, "f.txt"), "base\nfeat line\n");
                RunCli(gitExeB, "commit -am feat-change", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "checkout main", cpDir, out cpErr, out var _);
                using (var scp = GitSession.Open(cpDir))
                {
                    var featId = scp.LoadRefs().First(r => r.DisplayName == "feat").CommitId;
                    // 无冲突 cherry-pick → 成功 + 新提交
                    scp.CherryPick(featId);
                    var cpLog = scp.LoadHistory(5);
                    if (cpLog.Count != 2 || cpLog[0].Summary != "feat-change")
                        throw new System.Exception("SMOKE FAIL: cherry-pick clean apply: "
                            + string.Join(",", cpLog.Select(e => e.Summary)));
                    // 菜单含 Cherry-Pick（fake log 行）
                    var cpActs = GitWindow.BuildCommitContextActions(scp, cpLog, 0, () => { }).ToList();
                    if (!cpActs.Any(a => a.Id == "cherrypick"))
                        throw new System.Exception("SMOKE FAIL: cherry-pick menu missing");
                }
                // 冲突场景：独立仓（main 改 line2 + feat 改 line2 → cherry-pick 冲突）
                DeleteDir(cpDir);
                System.IO.Directory.CreateDirectory(cpDir);
                RunCli(gitExeB, "init -b main", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config user.name smoke", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config user.email smoke@local", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config commit.gpgsign false", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "config core.autocrlf false", cpDir, out cpErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(cpDir, "f.txt"), "base\n");
                RunCli(gitExeB, "add f.txt", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "commit -m base", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "checkout -b feat", cpDir, out cpErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(cpDir, "f.txt"), "base\nFEAT\n");
                RunCli(gitExeB, "commit -am feat-change", cpDir, out cpErr, out var _);
                RunCli(gitExeB, "checkout main", cpDir, out cpErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(cpDir, "f.txt"), "base\nMAIN\n");
                RunCli(gitExeB, "commit -am main-change", cpDir, out cpErr, out var _);
                using (var scp2 = GitSession.Open(cpDir))
                {
                    // feat 分支提交在 main 历史外 → 经 LoadRefs 取 feat 的 CommitId
                    var featRef = scp2.LoadRefs().First(r => r.DisplayName == "feat");
                    scp2.CherryPick(featRef.CommitId); // 与 MAIN 冲突
                    var cpSt = scp2.LoadStatus();
                    if (cpSt.Entries == null || !cpSt.Entries.Any(e => e.Unmerged))
                        throw new System.Exception("SMOKE FAIL: cherry-pick conflict UU missing");
                }
                DeleteDir(cpDir);

                // 38) P2 blame（--porcelain 解析：归属提交/作者/内容 + 未提交行 0000 标记）
                var blDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-blame-repo");
                DeleteDir(blDir);
                System.IO.Directory.CreateDirectory(blDir);
                var gitExeC = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExeC, "init -b main", blDir, out var blErr, out var _);
                RunCli(gitExeC, "config user.name smoke", blDir, out blErr, out var _);
                RunCli(gitExeC, "config user.email smoke@local", blDir, out blErr, out var _);
                RunCli(gitExeC, "config commit.gpgsign false", blDir, out blErr, out var _);
                RunCli(gitExeC, "config core.autocrlf false", blDir, out blErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(blDir, "src.cs"), "line1\nline2\nline3\n");
                RunCli(gitExeC, "add src.cs", blDir, out blErr, out var _);
                RunCli(gitExeC, "commit -m first", blDir, out blErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(blDir, "src.cs"), "line1 CH\nline2 EDITED\nline3 CH\n");
                using (var sbl = GitSession.Open(blDir))
                {
                    var blame = sbl.Blame("src.cs");
                    if (blame.Count != 3)
                        throw new System.Exception($"SMOKE FAIL: blame count={blame.Count} expect 3");
                    // 工作区改 3 行（同 untracked 组）：LineNumber 必须逐行递增 1,2,3（组内 final 行号补偿）
                    if (blame[0].LineNumber != 1 || blame[1].LineNumber != 2 || blame[2].LineNumber != 3)
                        throw new System.Exception($"SMOKE FAIL: blame line numbers {blame[0].LineNumber},{blame[1].LineNumber},{blame[2].LineNumber} expect 1,2,3");
                    if (blame.Count != 3 || blame[0].CommitShort != "00000000"
                        || blame[1].Content != "line2 EDITED" || blame[1].LineNumber != 2)
                        throw new System.Exception("SMOKE FAIL: blame edited line content");
                    if (blame[2].CommitShort != "00000000")
                        throw new System.Exception($"SMOKE FAIL: blame uncommitted marker={blame[2].CommitShort}");
                }
                DeleteDir(blDir);

                // 39) M3 双击文件联动数据管道（HEAD vs 工作区 单文件 diff → Rows；OpenFileDiff 内部同路径）
                var dblDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s.ProjectPath), "e2e-dblclick-repo");
                DeleteDir(dblDir);
                System.IO.Directory.CreateDirectory(dblDir);
                var gitExeD = s.Platform.Environment.GitExecutablePath;
                RunCli(gitExeD, "init -b main", dblDir, out var dbErr, out var _);
                RunCli(gitExeD, "config user.name smoke", dblDir, out dbErr, out var _);
                RunCli(gitExeD, "config user.email smoke@local", dblDir, out dbErr, out var _);
                RunCli(gitExeD, "config commit.gpgsign false", dblDir, out dbErr, out var _);
                RunCli(gitExeD, "config core.autocrlf false", dblDir, out dbErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dblDir, "d.txt"), "old content\n");
                RunCli(gitExeD, "add d.txt", dblDir, out dbErr, out var _);
                RunCli(gitExeD, "commit -m base", dblDir, out dbErr, out var _);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dblDir, "d.txt"), "new content\n");
                using (var sdb = GitSession.Open(dblDir))
                {
                    var dbTask = GitDiffTask.Raw(sdb.Platform, "HEAD -- " + GitDiffTask.JoinPaths(new[] { "d.txt" }))
                        .Configure(sdb.Platform.ProcessManager);
                    var dbOut = dbTask.RunSynchronously();
                    if (!dbTask.Successful || string.IsNullOrEmpty(dbOut))
                        throw new System.Exception("SMOKE FAIL: dblclick diff empty");
                    var dbRows = DiffRows.Build(UnifiedDiffParser.Parse(dbOut));
                    // 单文件单行改：header + hunk header + old + new = 4
                    var dbKinds = string.Join(",", dbRows.Select(r => r.Kind.ToString()));
                    if (dbRows.Count != 4)
                        throw new System.Exception($"SMOKE FAIL: dblclick rows={dbRows.Count} expect 4: {dbKinds}");
                    if (dbRows[0].Kind != DiffRowKind.FileHeader
                        || dbRows[1].Kind != DiffRowKind.HunkHeader)
                        throw new System.Exception("SMOKE FAIL: dblclick header/hunk: " + dbKinds);
                    if (dbRows[2].Kind != DiffRowKind.Old
                        || !dbRows[2].RichText.Contains("old") || !dbRows[2].RichText.Contains("content"))
                        throw new System.Exception($"SMOKE FAIL: dblclick old row: {dbKinds} | {dbRows[2].RichText}");
                    if (dbRows[3].Kind != DiffRowKind.New
                        || !dbRows[3].RichText.Contains("new") || !dbRows[3].RichText.Contains("content"))
                        throw new System.Exception("SMOKE FAIL: dblclick new row: " + dbKinds);
                }
                DeleteDir(dblDir);

                // 40) M3 hunk 操作 UI 接线：DiffViewer.BuildHunkActions（Stage/Revert）+ DiffRows.FileIndex
                var hkDiff = "diff --git a/h.txt b/h.txt\n--- a/h.txt\n+++ b/h.txt\n"
                    + "@@ -2,2 +2,2 @@\n  line1\n-old\n+new\n"
                    + "@@ -7,1 +7,1 @@\n  line6\n-x\n+y\n";
                var hkRows = DiffRows.Build(UnifiedDiffParser.Parse(hkDiff), 0);
                // 第二文件文件头 FileIndex=1（多文件定位）
                var hkDiff2 = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-a\n+A\n"
                    + "diff --git a/b.txt b/b.txt\n--- a/b.txt\n+++ b/b.txt\n@@ -1 +1 @@\n-b\n+B\n";
                var hkRows2 = DiffRows.Build(UnifiedDiffParser.Parse(hkDiff2), 0);
                var bFileHeader = hkRows2.First(r => r.Kind == DiffRowKind.FileHeader
                    && r.FilePath == "b.txt");
                if (bFileHeader.FileIndex != 1)
                    throw new System.Exception("SMOKE FAIL: fileIndex not assigned: " + bFileHeader.FileIndex);
                // 菜单：session null → 空；非 null → Stage + Revert + 无 Unstage
                var hkNull = DiffViewer.BuildHunkActions(null, "x", 0, 0, () => { }, _ => { }).ToList();
                if (hkNull.Count != 0)
                    throw new System.Exception("SMOKE FAIL: hunk actions null session");
                var hkActs = DiffViewer.BuildHunkActions(s, hkDiff, 0, 0, () => { }, _ => { }).ToList();
                var hkTexts = hkActs.Select(a => a.Text).ToList();
                if (!hkTexts.Contains("Stage hunk") || !hkTexts.Contains("Revert hunk (discard changes)")
                    || hkTexts.Contains("Unstage hunk"))
                    throw new System.Exception("SMOKE FAIL: hunk menu items: " + string.Join(",", hkTexts));
                // 无上下文 hunk（整文件重写，git apply 无法定位）→ 菜单禁用
                var hkNoCtx = "diff --git a/n.txt b/n.txt\n--- a/n.txt\n+++ b/n.txt\n@@ -1,2 +1,10 @@\n-OLD\n+NEW\n+\n+\n";
                if (GitPatchBuilder.HunkHasContext(hkNoCtx, 0, 0))
                    throw new System.Exception("SMOKE FAIL: hunk context should be false for rewrite");
                var hkDisabled = DiffViewer.BuildHunkActions(s, hkNoCtx, 0, 0, () => { }, _ => { }).ToList();
                if (hkDisabled.Any(a => a.Enabled))
                    throw new System.Exception("SMOKE FAIL: hunk actions should be disabled without context");
                if (!GitPatchBuilder.HunkHasContext(hkDiff, 0, 0))
                    throw new System.Exception("SMOKE FAIL: hunk context should be true with context lines");
                // 行级 FileIndex：第二文件的 Old 行也应带 1
                var bOldRow = hkRows2.FirstOrDefault(r => r.Kind == DiffRowKind.Old && r.FileIndex == 1);
                if (bOldRow == null)
                    throw new System.Exception("SMOKE FAIL: fileIndex on body rows missing");
            }

            EditorApplication.Exit(0);
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(BuildLayout());
            graphTable.ContextActionProvider = ContextProvider;
            EditorApplication.update += OnEditorUpdate;
            ReloadHistory();
        }

        private IEnumerable<IGitContextAction> ContextProvider(int row)
        {
            return BuildCommitContextActions(session, logEntries, row, RefreshData, msg =>
            {
                // Uncommit 后回填 Commit 编辑器（JetBrains GitUncommitAction onSuccess 语义）
                if (msgSummary == null || msgBody == null) return;
                var parts = msg.Split(new[] { "\n\n" }, System.StringSplitOptions.None);
                msgSummary.SetValueWithoutNotify(parts[0]);
                msgBody.SetValueWithoutNotify(parts.Length > 1 ? parts[1] : "");
            });
        }

        // ---- Commit 页逻辑 ---- //

        /// <summary>M3 P1：提交模板/最近消息下拉（GenericMenu：会话最近消息 → git log 最近 → commit.template 文件）。</summary>
        private void ShowMessageTemplateMenu()
        {
            if (session == null || templateBtn == null) return;
            var gm = new GenericMenu();
            var filled = false;

            // 最近消息：会话内提交优先，git log 兜底
            var recents = new List<string>(sessionMessages);
            try
            {
                foreach (var m in session.RecentMessages(5))
                    if (!recents.Contains(m))
                        recents.Add(m);
            }
            catch { }
            if (recents.Count > 0)
            {
                gm.AddDisabledItem(new GUIContent(I18n.L(I18n.Keys.CommitRecentMessages)));
                foreach (var m in recents)
                {
                    var msg = m;
                    var label = msg.Replace("\n", " ").Trim();
                    if (label.Length > 40) label = label.Substring(0, 40) + "…";
                    gm.AddItem(new GUIContent(I18n.L(I18n.Keys.CommitRecentMessages) + "/" + label),
                        false, () => ApplyTemplate(msg));
                }
                gm.AddSeparator("");
            }

            // 模板文件
            string template = null;
            try { template = session.LoadCommitTemplate(); }
            catch { }
            if (string.IsNullOrEmpty(template))
            {
                gm.AddDisabledItem(new GUIContent(I18n.L(I18n.Keys.CommitUseTemplate)
                    + "  (" + I18n.L(I18n.Keys.CommitNoTemplate) + ")"));
            }
            else
            {
                gm.AddItem(new GUIContent(I18n.L(I18n.Keys.CommitUseTemplate)),
                    false, () => ApplyTemplate(template));
            }

            var menuRect = new Rect(templateBtn.worldBound.position, templateBtn.worldBound.size);
            gm.DropDown(menuRect);
        }

        /// <summary>把模板/最近消息填入 Commit 编辑器（summary/body 按首个空行切分；有值时仅替换非空部分？——直接整填）。</summary>
        private void ApplyTemplate(string message)
        {
            if (msgSummary == null || msgBody == null) return;
            var parts = message.Split(new[] { "\n\n" }, System.StringSplitOptions.None);
            msgSummary.SetValueWithoutNotify(parts[0]);
            msgBody.SetValueWithoutNotify(parts.Length > 1 ? parts[1] : "");
            UpdateCommitButton();
        }

        private void OnWorkingTreeToggle(ChangeItem item, bool staged)
        {
            var paths = new List<string>();
            if (item.IsDirectory)
            {
                var prefix = item.Path + "/";
                foreach (var en in workingEntries)
                    if (en.path.StartsWith(prefix, StringComparison.Ordinal))
                        paths.Add(en.path);
            }
            else
            {
                paths.Add(item.OpsPath ?? item.Path);
            }
            if (paths.Count == 0) return;

            if (staged) RunStatusOp(() => session.Stage(paths));
            else RunStatusOp(() => session.Unstage(paths));
        }

        /// <summary>后台执行暂存/取消暂存，完成后回主线程刷新状态树。</summary>
        private void RunStatusOp(Action op)
        {
            commitButton.SetEnabled(false);
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    op();
                    ctx?.Post(_ => RefreshWorkingStatus(), null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ =>
                    {
                        SetCommitError(ex.Message);
                        RefreshWorkingStatus();
                    }, null);
                }
            });
        }

        /// <summary>重载工作区状态（后台 git status → 勾选树；Commit 页首开/操作后/提交后调用）。</summary>
        private void RefreshWorkingStatus()
        {
            if (session == null) return;
            commitTree.SetHint(I18n.L(I18n.Keys.LoadingChanges));
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var st = session?.LoadStatus();
                    var entries = st?.Entries ?? new List<GitStatusEntry>();
                    ctx?.Post(_ =>
                    {
                        workingEntries = entries;
                        commitTree.SetFiles(ChangesTree.BuildFromEntries(entries));
                        commitTree.SetHint(entries.Count == 0 ? I18n.L(I18n.Keys.CommitClean) : "");
                        UpdateCommitButton();
                    }, null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => SetCommitError(ex.Message), null);
                }
            });
        }

        private void UpdateCommitButton()
        {
            if (commitButton == null) return;
            var hasSummary = msgSummary != null && msgSummary.value.Trim().Length > 0;
            var anyStaged = false;
            foreach (var en in workingEntries)
                if (en.Staged) { anyStaged = true; break; }
            commitButton.SetEnabled(hasSummary && anyStaged);
        }

        private void SetCommitError(string message)
        {
            if (commitError == null) return;
            commitError.text = message ?? string.Empty;
        }

        private void CommitNow()
        {
            var summary = msgSummary.value.Trim();
            if (summary.Length == 0) return;
            commitButton.SetEnabled(false);
            SetCommitError("");
            var amend = optAmend.value;
            var signoff = optSignoff.value;
            var noVerify = optNoVerify.value;
            var body = msgBody.value ?? string.Empty;
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    session?.Commit(summary, body, amend, signoff, noVerify);
                    ctx?.Post(_ =>
                    {
                        // 成功：历史+refs 全量刷新（自动刷新 1.5s 也会兜底），工作区状态重载，清空消息
                        RefreshData();
                        RefreshWorkingStatus();
                        // M3 P1：记录本会话成功提交的消息（模板下拉复用）
                        var lastMsg = (msgSummary.value ?? "").Trim();
                        if (lastMsg.Length > 0 && !sessionMessages.Contains(lastMsg))
                            sessionMessages.Insert(0, lastMsg);
                        msgSummary.SetValueWithoutNotify("");
                        msgBody.SetValueWithoutNotify("");
                        SetCommitError("");
                    }, null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ =>
                    {
                        var msg = ex.Message;
                        if (GitSession.DetectGpgError(msg))
                            msg += "\n" + I18n.L(I18n.Keys.CommitGpgHint);
                        SetCommitError(msg);
                        UpdateCommitButton();
                    }, null);
                }
            });
        }

        // ---- 文件/目录语境右键 ---- //

        private static bool IsUnder(string path, string dir)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith(dir + "/", StringComparison.Ordinal);
        }

        /// <summary>
        /// 文件/目录语境右键动作（JetBrains Git.Stage.Tree.Menu 裁剪版）：
        /// 文件 = Stage/Unstage（按当前暂存态）+ Revert(discard)/Open/Copy Path；
        /// 目录 = Stage All/Unstage All（递归）+ Revert(discard)/Open/Copy Path；
        /// readOnly（提交详情树）= 仅 Open/Copy Path。
        /// </summary>
        public static IEnumerable<IGitContextAction> BuildFileContextActions(GitSession session,
            ChangeItem item, IReadOnlyList<GitStatusEntry> entries, bool readOnly, Action onMutated)
        {
            if (item == null) yield break;

            var paths = new List<string>();
            if (item.IsDirectory)
            {
                if (entries != null)
                {
                    var prefix = item.Path + "/";
                    foreach (var en in entries)
                        if (en.path.StartsWith(prefix, StringComparison.Ordinal))
                            paths.Add(en.path);
                }
            }
            else
            {
                paths.Add(item.OpsPath ?? item.Path);
            }

            if (!readOnly)
            {
                if (item.IsDirectory)
                {
                    var anyStaged = false;
                    var anyCanStage = false;
                    if (entries != null)
                    {
                        var prefix = item.Path + "/";
                        foreach (var en in entries)
                        {
                            if (!en.path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                            if (en.Staged) anyStaged = true;
                            else if (en.WorkTreeStatus != GitFileStatus.None || en.Untracked) anyCanStage = true;
                        }
                    }
                    var dirPaths = paths;
                    yield return new DelegateAction("stage.all", I18n.L(I18n.Keys.MenuStageAll),
                        () => RunFlow(() => session?.Stage(dirPaths), onMutated)) { Enabled = anyCanStage };
                    yield return new DelegateAction("unstage.all", I18n.L(I18n.Keys.MenuUnstageAll),
                        () => RunFlow(() => session?.Unstage(dirPaths), onMutated)) { Enabled = anyStaged };
                }
                else
                {
                    var staged = item.IsStaged;
                    var single = paths;
                    if (!staged)
                        yield return new DelegateAction("stage", I18n.L(I18n.Keys.MenuStage),
                            () => RunFlow(() => session?.Stage(single), onMutated));
                    else
                        yield return new DelegateAction("unstage", I18n.L(I18n.Keys.MenuUnstage),
                            () => RunFlow(() => session?.Unstage(single), onMutated));
                }
                var discardPaths = paths;
                var display = item.IsDirectory
                    ? I18n.L(I18n.Keys.MenuDiscardCount, discardPaths.Count)
                    : (item.OpsPath ?? item.Path);
                yield return new DelegateAction("discard", I18n.L(I18n.Keys.MenuRevertFile),
                    () => PromptDiscard(session, discardPaths, display, onMutated));
                yield return GitContextSeparator.Instance;
            }

            yield return new DelegateAction("open", I18n.L(I18n.Keys.MenuOpen),
                () => OpenFile(session, item));
            yield return new DelegateAction("copy.path", I18n.L(I18n.Keys.MenuCopyPath),
                () => GUIUtility.systemCopyBuffer = (item.OpsPath ?? item.Path));
            // M3 P2：Blame（工作区文件；目录无意义；未跟踪文件 git blame 会 fatal "no such path in HEAD" → 禁用并提示）
            if (!readOnly && !item.IsDirectory && session != null)
            {
                var blamePath = item.OpsPath ?? item.Path;
                var untracked = item.Entry.HasValue && item.Entry.Value.Untracked;
                yield return new DelegateAction("blame", I18n.L(I18n.Keys.MenuBlame),
                    () => BlameWindow.Open(session, blamePath))
                {
                    Enabled = !untracked,
                };
            }
        }

        private static void RunFlow(Action op, Action onMutated)
        {
            try
            {
                op();
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static void PromptDiscard(GitSession session, IReadOnlyList<string> paths, string display, Action onMutated)
        {
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuRevertFile),
                    I18n.L(I18n.Keys.MenuDiscardConfirm, display),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            RunFlow(() => session?.Discard(paths), onMutated);
        }

        private static void CompareWithBranch(GitSession session, GitLogEntry e)
        {
            if (session == null) return;
            CompareWindow.Open(session, e.CommitID);
        }

        /// <summary>提交详情树双击：显示该选中提交对该文件的改动（该提交 vs 其父；root 提交=全新增）。</summary>
        private void OpenCommitFileDiff(ChangeItem item)
        {
            if (session == null || item == null || item.IsDirectory) return;
            var row = graphTable != null ? graphTable.SelectedRow : -1;
            if (row < 0 || row >= logEntries.Count) return;
            var commit = logEntries[row];
            var path = item.OpsPath ?? item.Path;
            var sessionCopy = session;
            var title = commit.ShortID + " " + (commit.Summary ?? "") + " — " + path;
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    List<DiffRow> rows;
                    string output;
                    if (commit.Parents.Count == 0)
                    {
                        // root 提交：全树 vs 空（该文件全部为新增）
                        output = null;
                        rows = BuildUntrackedRows(sessionCopy, path);
                    }
                    else
                    {
                        // git diff <parent> <commit> -- path
                        var task = GitDiffTask.Raw(sessionCopy.Platform,
                                commit.Parents[0] + " " + commit.CommitID + " -- " + GitDiffTask.JoinPaths(new[] { path }))
                            .Configure(sessionCopy.Platform.ProcessManager);
                        output = task.RunSynchronously();
                        rows = new List<DiffRow>();
                        if (task.Successful && !string.IsNullOrEmpty(output))
                            rows = DiffRows.Build(UnifiedDiffParser.Parse(output));
                        if (rows.Count == 0)
                        {
                            // 该路径在选中提交无此文件的改动（如提交里没动它）——显示空态提示
                            rows = new List<DiffRow>
                            {
                                new DiffRow { Kind = DiffRowKind.Context, RichText = DiffRichText.BuildPlainLine("(no change to this file in this commit)") }
                            };
                        }
                    }
                    ctx?.Post(_ => DiffViewer.Open(sessionCopy, title, output, rows, false), null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => Debug.LogWarning("[gitui] open commit diff failed: " + ex.Message), null);
                }
            });
        }

        /// <summary>整文件新增视图（root 提交 / 未跟踪文件双击）：读工作区内容为全 New 行。</summary>
        private static List<DiffRow> BuildUntrackedRows(GitSession session, string path)
        {
            var full = System.IO.Path.Combine(session.ProjectPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var lines = new List<string>();
            if (System.IO.File.Exists(full))
                lines = new List<string>(System.IO.File.ReadAllLines(full));
            var df = new DiffFile { OldPath = string.Empty, NewPath = path, IsNew = true };
            var hunk = new DiffHunk { OldStart = 0, OldCount = 0, NewStart = 1, NewCount = lines.Count };
            for (var i = 0; i < lines.Count; i++)
                hunk.Lines.Add(new DiffLine { Kind = DiffLineKind.New, Content = lines[i], LineNumber = i + 1 });
            df.Hunks.Add(hunk);
            return DiffRows.Build(new List<DiffFile> { df }, 0);
        }

        /// <summary>M3：双击文件联动（ChangesTree.ItemChosen）——HEAD vs 工作区 单文件 diff → DiffViewer。</summary>
        private void OpenFileDiff(ChangeItem item)
        {
            if (session == null || item == null || item.IsDirectory) return;
            var path = item.OpsPath ?? item.Path;
            var isUntracked = item.Entry.HasValue && item.Entry.Value.Untracked;
            var sessionCopy = session;
            var title = "HEAD vs " + path;
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    List<DiffRow> rows;
                    string output;
                    if (isUntracked)
                    {
                        // 未跟踪文件：git diff 无输出 → 整文件"新增"视图（读工作区内容）
                        output = null;
                        rows = BuildUntrackedRows(sessionCopy, path);
                    }
                    else
                    {
                        // git diff HEAD -- path（工作区含未暂存+暂存合并视图）
                        var task = GitDiffTask.Raw(sessionCopy.Platform,
                                "HEAD -- " + GitDiffTask.JoinPaths(new[] { path }))
                            .Configure(sessionCopy.Platform.ProcessManager);
                        output = task.RunSynchronously();
                        rows = new List<DiffRow>();
                        if (task.Successful && !string.IsNullOrEmpty(output))
                            rows = DiffRows.Build(UnifiedDiffParser.Parse(output));
                    }
                    // worktreeMode=true：hunk 级 Stage/Revert 可用
                    ctx?.Post(_ => DiffViewer.Open(sessionCopy, title, output, rows, true), null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => Debug.LogWarning("[gitui] open diff failed: " + ex.Message), null);
                }
            });
        }

        private static void PromptCreateTag(GitSession session, GitLogEntry e, Action onMutated)
        {
            var name = PromptDialog.Show(I18n.L(I18n.Keys.MenuCreateTag),
                I18n.L(I18n.Keys.CreateTagPrompt), e.ShortID);
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                session.CreateTag(name.Trim(), e.CommitID, name.Trim());
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static void OpenFile(GitSession session, ChangeItem item)
        {
            var opsPath = item.OpsPath ?? item.Path;
            if (string.IsNullOrEmpty(opsPath) || session == null) return;
            EditorUtility.OpenWithDefaultApp(session.ProjectPath + "/" + opsPath);
        }

        private void OnBranchesChanged()
        {
            // 分支弹窗新建/删除/打标签后：refs 缓存失效，指纹自动刷新会重载 refs+图谱（1.5s 内）
            session?.InvalidateRefs();
        }

        /// <summary>
        /// 提交语境右键动作（JetBrains Git.Log.ContextMenu 裁剪版）：
        /// Copy Hash / Copy Summary | New Branch… / Compare with Branch… / Create Tag… | Reset…(软/混/硬) | Revert Commit…/Checkout…
        /// 静态可测：session/log 输入，onMutated 在变更成功后回调（窗口负责刷新）。
        /// </summary>
        public static IEnumerable<IGitContextAction> BuildCommitContextActions(GitSession session,
            List<GitLogEntry> log, int row, Action onMutated)
        {
            return BuildCommitContextActions(session, log, row, onMutated, null);
        }

        public static IEnumerable<IGitContextAction> BuildCommitContextActions(GitSession session,
            List<GitLogEntry> log, int row, Action onMutated, Action<string> onUncommittedMessage)
        {
            if (session == null || log == null || row < 0 || row >= log.Count) yield break;
            var e = log[row];

            yield return new DelegateAction("copy.hash", I18n.L(I18n.Keys.MenuCopyHash),
                () => GUIUtility.systemCopyBuffer = e.CommitID);
            yield return new DelegateAction("copy.summary", I18n.L(I18n.Keys.MenuCopySummary),
                () => GUIUtility.systemCopyBuffer = e.Summary);
            yield return GitContextSeparator.Instance;
            yield return new DelegateAction("new.branch", I18n.L(I18n.Keys.MenuNewBranch),
                () => PromptNewBranch(session, e, onMutated));
            yield return new DelegateAction("compare.branch", I18n.L(I18n.Keys.MenuCompareBranch),
                () => CompareWithBranch(session, e));
            yield return new DelegateAction("tag.create", I18n.L(I18n.Keys.MenuCreateTag),
                () => PromptCreateTag(session, e, onMutated));
            yield return GitContextSeparator.Instance;
            // M3：撤销提交（Uncommit，JetBrains 语义：仅 HEAD 提交启用）
            yield return new DelegateAction("uncommit", I18n.L(I18n.Keys.MenuUncommit),
                () => ConfirmUncommit(session, e, onMutated, onUncommittedMessage))
            {
                Enabled = row == 0, // 仅顶行（HEAD）可撤销；JetBrains isHeadCommit
            };
            yield return new DelegateAction("reset.soft", ResetPath(I18n.Keys.MenuResetSoft),
                () => ConfirmReset(session, e, GitResetMode.Soft, onMutated));
            yield return new DelegateAction("reset.mixed", ResetPath(I18n.Keys.MenuResetMixed),
                () => ConfirmReset(session, e, GitResetMode.Mixed, onMutated));
            yield return new DelegateAction("reset.hard", ResetPath(I18n.Keys.MenuResetHard),
                () => ConfirmReset(session, e, GitResetMode.Hard, onMutated));
            yield return GitContextSeparator.Instance;
            yield return new DelegateAction("revert", I18n.L(I18n.Keys.MenuRevert),
                () => ConfirmRevert(session, e, onMutated));
            yield return new DelegateAction("checkout", I18n.L(I18n.Keys.MenuCheckout),
                () => ConfirmCheckout(session, e, onMutated));
            // M3 P2：Cherry-Pick（冲突走 3-way；非 HEAD 分支右键场景任意行可挑）
            yield return new DelegateAction("cherrypick", I18n.L(I18n.Keys.MenuCherryPick, e.ShortID),
                () => ConfirmCherryPick(session, e, onMutated));
        }

        private static void ConfirmCherryPick(GitSession session, GitLogEntry e, Action onMutated)
        {
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuCherryPick, e.ShortID),
                    I18n.L(I18n.Keys.MenuCherryPickConfirm, e.ShortID),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.CherryPick(e.CommitID);
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static void ConfirmUncommit(GitSession session, GitLogEntry e, Action onMutated,
            Action<string> onUncommittedMessage)
        {
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuUncommit),
                    I18n.L(I18n.Keys.MenuUncommitConfirm, e.ShortID),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                var message = session.Uncommit();
                onUncommittedMessage?.Invoke(message);
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        /// <summary>DropdownMenu 子菜单路径："Reset…/Soft"（'/' 分段自动嵌套）。</summary>
        private static string ResetPath(string modeKey)
        {
            return I18n.L(I18n.Keys.MenuReset) + "/" + I18n.L(modeKey);
        }

        private static void PromptNewBranch(GitSession session, GitLogEntry e, Action onMutated)
        {
            var name = PromptDialog.Show(I18n.L(I18n.Keys.MenuNewBranch),
                I18n.L(I18n.Keys.MenuNewBranchPrompt, e.ShortID), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                session.NewBranch(name.Trim(), e.CommitID);
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static void ConfirmReset(GitSession session, GitLogEntry e, GitResetMode mode, Action onMutated)
        {
            var msg = I18n.L(I18n.Keys.MenuResetConfirm, e.ShortID, ModeText(mode));
            if (mode == GitResetMode.Hard)
                msg += "\n\n" + I18n.L(I18n.Keys.MenuResetHardWarn);
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuReset), msg,
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.ResetTo(e.CommitID, mode);
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static void ConfirmRevert(GitSession session, GitLogEntry e, Action onMutated)
        {
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuRevert),
                    I18n.L(I18n.Keys.MenuRevertConfirm, e.ShortID),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.RevertCommit(e.CommitID);
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static void ConfirmCheckout(GitSession session, GitLogEntry e, Action onMutated)
        {
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuCheckout),
                    I18n.L(I18n.Keys.MenuCheckoutConfirm, e.ShortID),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.Checkout(e.CommitID);
                onMutated?.Invoke();
            }
            catch (Exception ex) { ErrorDialog(ex); }
        }

        private static string ModeText(GitResetMode m)
        {
            switch (m)
            {
                case GitResetMode.Soft: return I18n.L(I18n.Keys.MenuResetSoft);
                case GitResetMode.Mixed: return I18n.L(I18n.Keys.MenuResetMixed);
                case GitResetMode.Hard: return I18n.L(I18n.Keys.MenuResetHard);
                default: return m.ToString();
            }
        }

        private static void ErrorDialog(Exception ex)
        {
            Debug.LogWarning("[gitui] op failed: " + ex);
            EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuOpFailedTitle), ex.Message, I18n.L(I18n.Keys.DialogOk));
        }

        /// <summary>冒烟辅助：前台运行 git 命令；error=stderr（失败含退出码），output=stdout。</summary>
        private static void RunCli(string exe, string args, string cwd, out string error, out string output)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            var outp = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            error = err.Trim();
            if (p.ExitCode != 0)
                error = (error.Length > 0 ? error : outp.Trim()) + $" (exit {p.ExitCode})";
            output = outp;
        }

        /// <summary>
        /// 冒烟辅助：递归删除。git 松散对象文件带只读属性，Directory.Delete 会拒绝 → 先清属性；
        /// 本机 EDR/杀软可能短暂持有新文件句柄（UnauthorizedAccess），带重试。
        /// </summary>
        private static void DeleteDir(string dir)
        {
            if (!System.IO.Directory.Exists(dir)) return;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    foreach (var f in System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories))
                    {
                        try { System.IO.File.SetAttributes(f, System.IO.FileAttributes.Normal); } catch { }
                    }
                    foreach (var d in System.IO.Directory.GetDirectories(dir, "*", System.IO.SearchOption.AllDirectories))
                    {
                        try { System.IO.File.SetAttributes(d, System.IO.FileAttributes.Normal); } catch { }
                    }
                    System.IO.Directory.Delete(dir, true);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(750);
                }
            }
            throw new UnauthorizedAccessException("DeleteDir retries exhausted: " + dir);
        }

        /// <summary>打开/重开会话并加载。自动刷新只调 RefreshData（保留会话与不可变缓存）。</summary>
        private void ReloadHistory()
        {
            session?.Dispose();
            try
            {
                session = GitSession.Open(Environment.CurrentDirectory);
                lastFingerprint = session.GetFingerprint();
                branchesPanel?.Bind(session, OnBranchesChanged);
                // 恢复持久化的图谱分支筛选（ref 已不存在则回退全部）
                var savedFilter = EditorPrefs.GetString(PrefGraphFilter, string.Empty);
                var validFilter = false;
                if (!string.IsNullOrEmpty(savedFilter))
                {
                    var refsNow = session.LoadRefs() ?? new List<GitSession.GitRefInfo>();
                    foreach (var r in refsNow)
                        if (GitSession.ToRevision(r) == savedFilter) { validFilter = true; break; }
                }
                graphFilterRevision = validFilter ? savedFilter : null;
                UpdateFilterButton();
                RefreshData();
                RefreshWorkingStatus();
            }
            catch (Exception ex)
            {
                graphStatus.text = I18n.L(I18n.Keys.GitUnavailable, ex.Message);
                Debug.LogWarning("[gitui] " + ex);
            }
        }

        /// <summary>重载历史 + refs + 重建管线（不重建会话，保留 commit 变更缓存）；尽量保住选中提交。</summary>
        private void RefreshData()
        {
            if (session == null) return;
            var keepCommit = graphTable.SelectedRow >= 0 && graphTable.SelectedRow < logEntries.Count
                ? logEntries[graphTable.SelectedRow].CommitID : null;

            session.InvalidateCaches(); // refs 等可变数据失效（提交变更缓存保留：不可变）
            logEntries = session.LoadHistory(200, graphFilterRevision);
            BuildGraphPipeline();
            branchesPanel?.Refresh(); // refs 变化同步到左侧分支面板

            if (keepCommit != null)
                for (var i = 0; i < logEntries.Count; i++)
                    if (logEntries[i].CommitID == keepCommit) { graphTable.Select(i); break; }
        }

        /// <summary>仓库状态轮询（1.5s）：HEAD/refs 变化（提交/分支/fetch）-> 自动刷新图谱与标签。</summary>
        private void OnEditorUpdate()
        {
            if (session == null) return;
            var now = EditorApplication.timeSinceStartup;
            if (now - lastFingerprintCheck < 1.5) return;
            lastFingerprintCheck = now;
            var fp = session.GetFingerprint();
            // 冲突轮询（低频，独立于指纹：UU 只改 status 不改 history 指纹）
            PollConflicts(now);
            if (fp == lastFingerprint) return;
            lastFingerprint = fp;
            try { RefreshData(); }
            catch (Exception ex) { Debug.LogWarning("[gitui] auto-refresh failed: " + ex); }
        }

        private double lastConflictCheck;

        private void PollConflicts(double now)
        {
            if (conflictBadge == null || now - lastConflictCheck < 3) return;
            lastConflictCheck = now;
            try
            {
                var paths = session.LoadConflictPaths();
                if (paths.Count > 0)
                {
                    conflictBadge.text = I18n.L(I18n.Keys.RebaseConflictHint, paths.Count);
                    conflictBadge.style.display = DisplayStyle.Flex;
                    conflictBadge.tooltip = string.Join("\n", paths);
                }
                else
                {
                    conflictBadge.style.display = DisplayStyle.None;
                }
            }
            catch { }
        }

        private void OpenMerge3()
        {
            Merge3Window.Open(session, () =>
            {
                // 冲突解决/中止后：刷新历史+工作区树（勾选树才能显示已解决文件）
                RefreshWorkingStatus();
                // merge 冲突解决后预填提交消息（git 的 MERGE_MSG）
                if (msgSummary != null && string.IsNullOrEmpty(msgSummary.value))
                {
                    var m = session?.LoadMergeMessage();
                    if (!string.IsNullOrEmpty(m))
                    {
                        var parts = m.Split(new[] { "\n\n" }, System.StringSplitOptions.None);
                        msgSummary.SetValueWithoutNotify(parts[0].Trim());
                        if (parts.Length > 1) msgBody.SetValueWithoutNotify(parts[1].Trim());
                        UpdateCommitButton();
                    }
                }
            });
        }

        private void BuildGraphPipeline()
        {
            var commitIndex = new Dictionary<string, int>();
            for (var i = 0; i < logEntries.Count; i++) commitIndex[logEntries[i].CommitID] = i;

            var pGraph = PermanentLinearGraph.Build(logEntries, commitIndex);
            var layout = GraphLayout.Build(pGraph);
            var eir = EdgesInRow.Build(pGraph);
            var headSet = new HashSet<int>(layout.HeadNodes);
            var printer = RowPrinter.Build(pGraph, layout, eir, headSet);

            // refs 行内标签（HEAD→本地→remote→tags；for-each-ref 兼容 packed refs）
            List<GitSession.GitRefInfo> refs = null;
            Dictionary<string, List<GitSession.GitRefInfo>> refsByCommit = null;
            try
            {
                refs = session.LoadRefs();
                if (refs.Count > 0)
                {
                    refsByCommit = new Dictionary<string, List<GitSession.GitRefInfo>>();
                    foreach (var rf in refs)
                    {
                        if (!refsByCommit.TryGetValue(rf.CommitId, out var list))
                            refsByCommit[rf.CommitId] = list = new List<GitSession.GitRefInfo>();
                        list.Add(rf);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[gitui] refs load failed: " + ex);
            }

            graphTable.SetData(logEntries, printer, refsByCommit);
            graphStatus.text = logEntries.Count > 0
                ? I18n.L(I18n.Keys.GraphStatusFormat, logEntries.Count, layout.LaneCount,
                    refs?.Count ?? 0, logEntries[0].ShortID, logEntries[0].Summary)
                : I18n.L(I18n.Keys.GraphEmpty);
        }

        private void ShowCommitDetail(int row)
        {
            if (row < 0 || row >= logEntries.Count) return;
            var e = logEntries[row];
            changesTree.SetFiles(null);
            changesTree.SetHint("");
            var changes = e.Changes;
            if (changes != null && changes.Count > 0)
            {
                changesTree.SetFiles(ChangesTree.BuildFromEntries(changes));
            }
            else
            {
                // merge/root：git log --name-status 不出 diff，按需补载
                // 异步（JetBrains FullCommitDetailsListPanel 后台加载语义）：先 "loading…"，
                // 后台跑 git（进程不阻塞主线程，含缓存），完成后回主线程渲染（选中已变则丢弃）。
                changesTree.SetHint(I18n.L(I18n.Keys.LoadingChanges));
                var ctx = System.Threading.SynchronizationContext.Current;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var cc = session?.LoadChangesFor(e);
                        ctx?.Post(_ =>
                        {
                            if (graphTable.SelectedRow != row) return; // 过期结果丢弃
                            RenderChanges(cc, e);
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[gitui] detail load failed: " + ex);
                    }
                });
            }

            detailText.text = $"{e.ShortID}  {e.Summary}\n\n{e.Description}";
        }

        /// <summary>渲染按需加载的变更（JetBrains 语义：合并视图在前 + 每父一组 "Changes to parent <hash>"）。</summary>
        private void RenderChanges(GitSession.CommitChanges extra, GitLogEntry e)
        {
            if (extra == null)
            {
                changesTree.SetFiles(null);
                changesTree.SetHint(I18n.L(I18n.Keys.NoChangesParsed));
                return;
            }
            var parents = e.Parents;
            if (parents == null || parents.Count == 0)
            {
                // root 提交：全树 vs 空树
                changesTree.SetFiles(ChangesTree.BuildFromDiffs(extra.Combined));
                changesTree.SetHint(I18n.L(I18n.Keys.RootCommitNote));
                return;
            }
            if (parents.Count > 1)
            {
                // 合并视图在前（相对全部父），每父一组（相对第 i 父）
                var sections = new List<(string, List<ChangeItem>)>();
                if (extra.Combined.Count > 0)
                    sections.Add((I18n.L(I18n.Keys.SectionMerged), ChangesTree.BuildFromDiffs(extra.Combined)));
                for (var i = 0; i < extra.PerParent.Count && i < parents.Count; i++)
                {
                    var parentShort = parents[i].Length >= 7 ? parents[i].Substring(0, 7) : parents[i];
                    sections.Add((I18n.L(I18n.Keys.ChangesToParent, parentShort),
                        ChangesTree.BuildFromDiffs(extra.PerParent[i])));
                }
                changesTree.SetFilesSectioned(sections);
                changesTree.SetHint(extra.Combined.Count == 0 ? I18n.L(I18n.Keys.NoMergeConflicts) : "");
                return;
            }
            changesTree.SetFiles(null);
            changesTree.SetHint(I18n.L(I18n.Keys.NoChangesParsed));
        }

        private VisualElement pageLog;
        private VisualElement pageCommit;
        private Button tabLog;
        private Button tabCommit;
        private ChangesTree commitTree;
        private TextField msgSummary;
        private TextField msgBody;
        private Toggle optAmend;
        private Toggle optSignoff;
        private Toggle optNoVerify;
        private Label commitError;
        private Button commitButton;
        private List<GitStatusEntry> workingEntries = new List<GitStatusEntry>();
        private BranchesPanel branchesPanel;
        private VisualElement branchesPane;
        private const string PrefBranchesPane = "kf.gitui.branches.paneVisible";
        private bool branchesPaneVisible = true;
        private const string PrefGraphFilter = "kf.gitui.graph.filter";
        private Button branchFilterBtn;
        private Button conflictBadge;
        private Button templateBtn;
        /// <summary>本会话内成功提交的消息（模板菜单「Recent messages」数据源之一；git log 兜底）。</summary>
        private readonly List<string> sessionMessages = new List<string>();
        private string graphFilterRevision; // null = 全部分支；否则为 ref（本地名 / refs/remotes/… / refs/tags/…）

        private VisualElement BuildLayout()
        {
            var root = new VisualElement();
            root.style.flexGrow = 1f;

            // 顶部工具条：Log | Commit Tab + 分支筛选下拉（JetBrains Log branch filter 语义，单选/不选）
            var toolbar = new VisualElement();
            toolbar.name = "toolbar";
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingTop = 2;
            toolbar.style.paddingLeft = 4;
            tabLog = new Button(() => ActivateTab(0)) { text = I18n.L(I18n.Keys.TabLog) };
            tabLog.name = "tab-log";
            tabCommit = new Button(() => ActivateTab(1)) { text = I18n.L(I18n.Keys.TabCommit) };
            tabCommit.name = "tab-commit";
            toolbar.Add(tabLog);
            toolbar.Add(tabCommit);
            branchFilterBtn = new Button(ShowBranchFilterMenu) { text = I18n.L(I18n.Keys.BranchFilterAll) };
            branchFilterBtn.name = "btn-branches";
            branchFilterBtn.tooltip = I18n.L(I18n.Keys.BranchFilterAll);
            toolbar.Add(branchFilterBtn);
            // M3：冲突徽标（merge/rebase 冲突时出现，点击开 3-way 视图）
            conflictBadge = new Button(OpenMerge3) { text = "" };
            conflictBadge.name = "btn-conflicts";
            conflictBadge.style.display = DisplayStyle.None;
            conflictBadge.style.backgroundColor = new Color(0.85f, 0.30f, 0.30f, 1f);
            conflictBadge.style.color = Color.white;
            toolbar.Add(conflictBadge);
            root.Add(toolbar);

            // 主体：左 = 常驻分支面板（用户拍板保留在主窗口左侧，不弹窗）；右 = Log|Commit 页面
            var bodySplit = new TwoPaneSplitView(0, 210, TwoPaneSplitViewOrientation.Horizontal);
            bodySplit.name = "body-split";

            var branchesPaneEl = new VisualElement();
            branchesPane = branchesPaneEl;
            branchesPaneEl.name = "branches-pane";
            branchesPaneEl.style.flexDirection = FlexDirection.Column;
            branchesPaneVisible = EditorPrefs.GetBool(PrefBranchesPane, true);
            branchesPaneEl.style.display = branchesPaneVisible ? DisplayStyle.Flex : DisplayStyle.None;
            var title = new Label(I18n.L(I18n.Keys.BranchTitle));
            title.style.paddingTop = 2;
            title.style.paddingLeft = 6;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            branchesPaneEl.Add(title);
            branchesPanel = new BranchesPanel();
            branchesPaneEl.Add(branchesPanel);
            bodySplit.Add(branchesPaneEl);

            // 页面容器：两个页面各自保留状态，仅切换 display
            var pagesHost = new VisualElement();
            pagesHost.name = "pages";
            pagesHost.style.flexGrow = 1f;
            pageLog = new VisualElement();
            pageLog.name = "page-log";
            pageLog.style.flexGrow = 1f;
            pageLog.Add(BuildLogPage());
            pageCommit = BuildCommitPage();
            pageCommit.name = "page-commit";
            pagesHost.Add(pageLog);
            pagesHost.Add(pageCommit);
            bodySplit.Add(pagesHost);
            root.Add(bodySplit);

            ActivateTab(0);
            return root;
        }

        private void ToggleBranchesPane()
        {
            branchesPaneVisible = !branchesPaneVisible;
            EditorPrefs.SetBool(PrefBranchesPane, branchesPaneVisible);
            if (branchesPane != null)
                branchesPane.style.display = branchesPaneVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- 图谱分支筛选（JetBrains Log branch filter；顶部下拉单选/不选） ----

        private void ShowBranchFilterMenu()
        {
            if (session == null || branchFilterBtn == null) return;
            var refs = session.LoadRefs() ?? new List<GitSession.GitRefInfo>();
            var actions = BuildBranchFilterActions(refs, graphFilterRevision,
                () => CurrentBranchName(), SetGraphFilter, ToggleBranchesPane, branchesPaneVisible);
            var gm = new GenericMenu();
            foreach (var a in GitContextMenu.Filter(actions))
            {
                if (a is GitContextSeparator) { gm.AddSeparator(""); continue; }
                var act = a;
                gm.AddItem(new GUIContent(act.Text), act.Checked, () => act.Run());
            }
            var pos = branchFilterBtn.worldBound.position;
            gm.DropDown(new Rect(pos.x, pos.y + branchFilterBtn.worldBound.height, 0f, 0f));
        }

        private string CurrentBranchName()
        {
            try
            {
                if (session == null) return null;
                return session.LoadStatus().LocalBranch;
            }
            catch { return null; }
        }

        /// <summary>顶部下拉动作集（静态可测）：All / Current / 各 ref（按 本地|远程|标签 分组）/ 面板开关；单选打勾。</summary>
        public static IEnumerable<IGitContextAction> BuildBranchFilterActions(
            List<GitSession.GitRefInfo> refs, string activeRevision,
            Func<string> currentBranchName, Action<string> apply, Action togglePanel, bool panelVisible)
        {
            var isAll = activeRevision == null;
            yield return new DelegateAction("filter.all", I18n.L(I18n.Keys.BranchFilterAll),
                () => apply(null)) { Checked = isAll };

            string cur = null;
            try { cur = currentBranchName?.Invoke(); } catch { }
            if (!string.IsNullOrEmpty(cur))
                yield return new DelegateAction("filter.current", I18n.L(I18n.Keys.BranchFilterCurrent),
                    () => apply(cur)) { Checked = activeRevision == cur };

            yield return GitContextSeparator.Instance;

            var prevGroup = -1;
            foreach (var r in refs)
            {
                var group = r.Type == GitSession.RefType.Tag ? 2
                    : r.Type == GitSession.RefType.Remote ? 1 : 0;
                if (prevGroup != -1 && group != prevGroup)
                    yield return GitContextSeparator.Instance;
                prevGroup = group;

                var rev = GitSession.ToRevision(r);
                yield return new DelegateAction("filter.ref", r.DisplayName,
                    () => apply(rev)) { Checked = activeRevision == rev };
            }

            yield return GitContextSeparator.Instance;
            yield return new DelegateAction("panel.toggle", I18n.L(I18n.Keys.BranchShowPanel),
                () => togglePanel()) { Checked = panelVisible };
        }

        private static string ToRevision(GitSession.GitRefInfo r)
        {
            return GitSession.ToRevision(r);
        }

        private void SetGraphFilter(string revision)
        {
            graphFilterRevision = revision;
            EditorPrefs.SetString(PrefGraphFilter, revision ?? string.Empty);
            UpdateFilterButton();
            RefreshData();
        }

        private void UpdateFilterButton()
        {
            if (branchFilterBtn == null) return;
            branchFilterBtn.text = graphFilterRevision == null
                ? I18n.L(I18n.Keys.BranchFilterAll)
                : DisplayRevisionName(graphFilterRevision);
            branchFilterBtn.tooltip = I18n.L(I18n.Keys.BranchFilterAll);
        }

        private static string DisplayRevisionName(string rev)
        {
            if (rev.StartsWith("refs/remotes/", StringComparison.Ordinal)) return rev.Substring("refs/remotes/".Length);
            if (rev.StartsWith("refs/tags/", StringComparison.Ordinal)) return rev.Substring("refs/tags/".Length);
            return rev;
        }

        private void ActivateTab(int index)
        {
            pageLog.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            pageCommit.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            tabLog.style.unityFontStyleAndWeight = index == 0 ? FontStyle.Bold : FontStyle.Normal;
            tabCommit.style.unityFontStyleAndWeight = index == 1 ? FontStyle.Bold : FontStyle.Normal;
            if (index == 1) RefreshWorkingStatus();
        }

        private VisualElement BuildLogPage()
        {
            var outer = new TwoPaneSplitView(0, 420, TwoPaneSplitViewOrientation.Horizontal);
            outer.name = "outer-split";

            // 左：图谱表格（图谱 + 消息同行同格）
            var graphPane = new VisualElement();
            graphStatus = new Label(I18n.L(I18n.Keys.GraphLoading));
            graphPane.Add(graphStatus);
            var graphScroll = new ScrollView(ScrollViewMode.Vertical);
            graphTable = new GraphTable();
            graphTable.RowSelected += ShowCommitDetail;
            graphScroll.Add(graphTable);
            graphPane.Add(graphScroll);
            outer.Add(graphPane);

            // 右：上 改动文件树 / 下 提交详情
            var inner = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Vertical);
            inner.name = "inner-split";
            changesTree = new ChangesTree(ChangesTree.Mode.ReadOnly);
            // 提交详情树轻量右键（Open / Copy Path；只读无暂存/撤销）
            changesTree.ContextActionProvider = item =>
                BuildFileContextActions(session, item, null, true, null);
            // 提交详情树（只读）：双击文件 = 该提交 vs 其父的 diff（JetBrains 点击历史文件语义）
            changesTree.ItemChosen += OpenCommitFileDiff;
            inner.Add(changesTree);
            var detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailText = new Label(I18n.L(I18n.Keys.SelectACommit));
            detailText.style.whiteSpace = WhiteSpace.Normal;
            detailText.style.paddingLeft = 6;
            detailText.style.paddingRight = 6;
            detailText.style.paddingTop = 6;
            detailText.style.paddingBottom = 6;
            detailScroll.Add(detailText);
            inner.Add(detailScroll);
            outer.Add(inner);

            return outer;
        }

        /// <summary>
        /// Commit 页（JetBrains Commit toolwindow 语义）：左 = 工作区勾选树（勾选=暂存）；
        /// 右 = 摘要/详情 + amend/signoff/no-verify + 提交按钮 + 错误条。
        /// </summary>
        private VisualElement BuildCommitPage()
        {
            var page = new VisualElement();
            page.name = "page-commit";
            page.style.flexGrow = 1f;

            var split = new TwoPaneSplitView(0, 460, TwoPaneSplitViewOrientation.Horizontal);
            split.name = "commit-split";

            commitTree = new ChangesTree(ChangesTree.Mode.Checkable);
            commitTree.name = "commit-tree";
            commitTree.ToggleChanged += OnWorkingTreeToggle;
            commitTree.ItemChosen += OpenFileDiff;
            commitTree.ContextActionProvider = item =>
                BuildFileContextActions(session, item, workingEntries, false, RefreshWorkingStatus);
            split.Add(commitTree);

            var editor = new VisualElement();
            editor.style.flexDirection = FlexDirection.Column;
            editor.style.flexGrow = 1f;
            editor.style.paddingLeft = 6;
            editor.style.paddingRight = 6;
            editor.style.paddingTop = 4;
            editor.style.paddingBottom = 4;

            editor.Add(new Label(I18n.L(I18n.Keys.CommitSummaryLabel)));
            msgSummary = new TextField();
            msgSummary.name = "msg-summary";
            msgSummary.RegisterValueChangedCallback(_ => UpdateCommitButton());
            editor.Add(msgSummary);

            editor.Add(new Label(I18n.L(I18n.Keys.CommitBodyLabel)));
            msgBody = new TextField { multiline = true };
            msgBody.style.height = 110;
            msgBody.style.whiteSpace = WhiteSpace.Normal;
            editor.Add(msgBody);

            var opts = new VisualElement();
            opts.style.flexDirection = FlexDirection.Row;
            opts.style.paddingTop = 2;
            opts.style.paddingBottom = 2;
            // M3 P1：模板/最近消息下拉
            templateBtn = new Button(ShowMessageTemplateMenu) { text = I18n.L(I18n.Keys.CommitTemplates) };
            templateBtn.style.marginRight = 8;
            opts.Add(templateBtn);
            optAmend = new Toggle(I18n.L(I18n.Keys.CommitAmend));
            optSignoff = new Toggle(I18n.L(I18n.Keys.CommitSignoff));
            optNoVerify = new Toggle(I18n.L(I18n.Keys.CommitNoVerify));
            opts.Add(optAmend);
            opts.Add(optSignoff);
            opts.Add(optNoVerify);
            editor.Add(opts);

            var btns = new VisualElement();
            btns.style.flexDirection = FlexDirection.Row;
            btns.style.paddingTop = 2;
            commitButton = new Button(CommitNow) { text = I18n.L(I18n.Keys.CommitButton) };
            commitButton.SetEnabled(false);
            var refreshBtn = new Button(RefreshWorkingStatus) { text = I18n.L(I18n.Keys.CommitRefresh) };
            btns.Add(commitButton);
            btns.Add(refreshBtn);
            editor.Add(btns);

            commitError = new Label();
            commitError.style.color = new Color(0.88f, 0.32f, 0.32f);
            commitError.style.whiteSpace = WhiteSpace.Normal;
            commitError.style.paddingTop = 4;
            editor.Add(commitError);

            split.Add(editor);
            page.Add(split);
            return page;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            session?.Dispose();
            session = null;
        }
    }
}