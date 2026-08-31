using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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
            EditorApplication.update += OnEditorUpdate;
            ReloadHistory();
        }

        /// <summary>打开/重开会话并加载。自动刷新只调 RefreshData（保留会话与不可变缓存）。</summary>
        private void ReloadHistory()
        {
            session?.Dispose();
            try
            {
                session = GitSession.Open(Environment.CurrentDirectory);
                lastFingerprint = session.GetFingerprint();
                RefreshData();
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
            logEntries = session.LoadHistory(200);
            BuildGraphPipeline();

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

        private VisualElement BuildLayout()
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

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            session?.Dispose();
            session = null;
        }
    }
}