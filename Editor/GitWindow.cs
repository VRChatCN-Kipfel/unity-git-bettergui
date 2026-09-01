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

                // 20) 分支行文本标识（主分支 ★ / 当前分支 » / ↑↓；BMP 通用符号避免 emoji/Dingbats □）
                if (BranchesPanel.FormatRefLabel("main", true, true, 0, 0) != "★ » main")
                    throw new System.Exception("SMOKE FAIL: label main+current");
                if (BranchesPanel.FormatRefLabel("main", true, false, 0, 0) != "★ main")
                    throw new System.Exception("SMOKE FAIL: label main");
                if (BranchesPanel.FormatRefLabel("feature/x", false, false, 2, 1) != "feature/x")
                    throw new System.Exception("SMOKE FAIL: label plain");
                if (BranchesPanel.FormatRefLabel("feature/x", false, true, 2, 0) != "» feature/x  ↑2 ↓0")
                    throw new System.Exception("SMOKE FAIL: label current+ahead");

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
            return BuildCommitContextActions(session, logEntries, row, RefreshData);
        }

        // ---- Commit 页逻辑 ---- //

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
            if (fp == lastFingerprint) return;
            lastFingerprint = fp;
            try { RefreshData(); }
            catch (Exception ex) { Debug.LogWarning("[gitui] auto-refresh failed: " + ex); }
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
            graphStatus.text = I18n.L(I18n.Keys.GraphStatusFormat, logEntries.Count, layout.LaneCount,
                refs?.Count ?? 0, logEntries[0].ShortID, logEntries[0].Summary);
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