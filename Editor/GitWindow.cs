using System;
using System.Collections.Generic;
using System.Threading;
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
        private ScrollView changesList;
        private Label detailText;

        [MenuItem("Window/Git/Git Better GUI (wip)")]
        public static void Open()
        {
            var w = GetWindow<GitWindow>();
            w.titleContent = new GUIContent("Git Better GUI");
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
            ReloadHistory();
        }

        private void ReloadHistory()
        {
            session?.Dispose();
            try
            {
                session = GitSession.Open(Environment.CurrentDirectory);
                logEntries = session.LoadHistory(200);
                BuildGraphPipeline();
            }
            catch (Exception ex)
            {
                graphStatus.text = "Git unavailable: " + ex.Message;
                Debug.LogWarning("[gitui] " + ex);
            }
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

            graphTable.SetData(logEntries, printer);
            graphStatus.text = $"{logEntries.Count} commits · {layout.LaneCount} line(s) · head {logEntries[0].ShortID} \"{logEntries[0].Summary}\"";
        }

        private void ShowCommitDetail(int row)
        {
            if (row < 0 || row >= logEntries.Count) return;
            var e = logEntries[row];
            changesList.Clear();
            if (e.Changes != null && e.Changes.Count > 0)
            {
                foreach (var c in e.Changes)
                {
                    var l = new Label($"  {c.Status,-12} {c.path}");
                    l.style.fontSize = 12;
                    changesList.Add(l);
                }
            }
            else
            {
                changesList.Add(new Label("  (no file changes parsed — merge 需走 git diff 另行加载)"));
            }

            detailText.text = $"{e.ShortID}  {e.Summary}\n\n{e.Description}";
        }

        private VisualElement BuildLayout()
        {
            var outer = new TwoPaneSplitView(0, 420, TwoPaneSplitViewOrientation.Horizontal);
            outer.name = "outer-split";

            // 左：图谱表格（图谱 + 消息同行同格）
            var graphPane = new VisualElement();
            graphStatus = new Label("loading…");
            graphPane.Add(graphStatus);
            var graphScroll = new ScrollView(ScrollViewMode.Vertical);
            graphTable = new GraphTable();
            graphTable.RowSelected += ShowCommitDetail;
            graphScroll.Add(graphTable);
            graphPane.Add(graphScroll);
            outer.Add(graphPane);

            // 右：上 改动文件列表 / 下 提交详情
            var inner = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Vertical);
            inner.name = "inner-split";
            changesList = new ScrollView(ScrollViewMode.Vertical);
            inner.Add(changesList);
            var detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailText = new Label("select a commit");
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
            session?.Dispose();
            session = null;
        }
    }
}