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
    /// unity-git-bettergui 主窗口：三栏布局 + 真实提交数据接入。
    ///   左（图谱区）| 右：上（文件/变更树占位）+ 下（提交详情占位）
    /// 本阶段：GitSession 加载真实提交历史，左侧列表展示 commit 摘要（图谱自绘在下一阶段）。
    /// </summary>
    public class GitWindow : EditorWindow
    {
        private GitSession session;
        private List<GitLogEntry> logEntries = new List<GitLogEntry>();
        private CommitGraphElement graph;
        private Label graphStatus;
        private ScrollView commitList;

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
        /// 验证：三栏布局 + GitSession 装配 + 真实提交加载。
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

                // 图谱布局断言（测试仓库:9 提交,2 merge,总边 2*2+6*1=10）
                var graph = new CommitGraphElement();
                graph.SetData(log);
                var (rows, edges, headShort, mergeParents) = graph.LayoutInfo;
                if (rows != 9) throw new System.Exception($"SMOKE FAIL: rows={rows} expect 9");
                if (edges != 10) throw new System.Exception($"SMOKE FAIL: edges={edges} expect 10");
                if (headShort != "810e7c4") throw new System.Exception($"SMOKE FAIL: head={headShort} expect 810e7c4");
                if (mergeParents != 2) throw new System.Exception($"SMOKE FAIL: head parents={mergeParents} expect 2");

                // ---- 引擎层（JetBrains 管线移植）冒烟：PermanentLinearGraph + GraphLayout 泳道 ----
                // 期望泳道（按 GraphLayoutBuilder 栈式 DFS 手算，与 JetBrains layoutBuilder 测试同语义）：
                // main 线与两条 feature 分支各占一泳道 -> [0,0,2,0,1,1,0,0,0]，LaneCount=3，唯一 head=row0
                var commitIndex = new Dictionary<string, int>();
                for (var i = 0; i < log.Count; i++) commitIndex[log[i].CommitID] = i;
                var pGraph = PermanentLinearGraph.Build(log, commitIndex);
                var layout = GraphLayout.Build(pGraph);
                var expectedLanes = new[] { 0, 0, 2, 0, 1, 1, 0, 0, 0 };
                var lanes = new int[pGraph.NodesCount];
                for (var i = 0; i < pGraph.NodesCount; i++) lanes[i] = layout.GetLayoutIndex(i);
                if (string.Join(",", lanes) != string.Join(",", expectedLanes))
                {
                    // 不匹配时输出对账信息（父/隐式标记/泳道）
                    var dbg = new System.Text.StringBuilder("SMOKE FAIL: engine lanes=[" + string.Join(",", lanes) + "] expect [0,0,2,0,1,1,0,0,0]\n");
                    for (var i = 0; i < pGraph.NodesCount; i++)
                        dbg.AppendLine($"row{i} {log[i].ShortID}: parents=[{string.Join(",", pGraph.GetParentNodes(i))}] simple={pGraph.IsSimpleNode(i)} lane={lanes[i]}");
                    throw new System.Exception(dbg.ToString());
                }
                // JetBrains 语义：线(head)数与泳道槽位数解耦。本仓库唯一 head=row0（main 尖），
                // 但 DFS 回溯为 feature/x、feature/y 各开出新槽 -> 槽位 0..2。
                if (layout.LaneCount != 1) throw new System.Exception($"SMOKE FAIL: engine LaneCount={layout.LaneCount} expect 1 (single head)");
                var maxLane = 0;
                for (var i = 0; i < lanes.Length; i++) if (lanes[i] > maxLane) maxLane = lanes[i];
                if (maxLane != 2) throw new System.Exception($"SMOKE FAIL: engine maxLane={maxLane} expect 2 (3 lane slots)");
                if (layout.GetHeadNodeIndexForLane(0) != 0 || layout.GetHeadNodeIndexForLane(2) != 0)
                    throw new System.Exception("SMOKE FAIL: engine lane->head clamp broken");

                // 渲染回归防线：自绘元素必须拿到非零内容高度，否则在窗口里什么都不画
                //（布局数学测不出像素，这里直接断言 style.height 已按行数撑开）
                var contentHeight = graph.style.height.value.value;
                var expectHeight = rows * CommitGraphElement.RowHeight;
                if (contentHeight <= 0f || contentHeight != expectHeight)
                    throw new System.Exception($"SMOKE FAIL: graph contentHeight={contentHeight} expect {expectHeight}");

                var headEntry = log[0];
                UnityEngine.Debug.Log($"[gitui] SMOKE OK: layout={outer.childCount}/{inner.childCount} rows={rows} edges={edges} head={headEntry.ShortID} \"{headEntry.Summary}\" mergeParents={mergeParents}");
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
                RenderCommits();
            }
            catch (Exception ex)
            {
                graphStatus.text = "Git unavailable: " + ex.Message;
                Debug.LogWarning("[gitui] " + ex);
            }
        }

        private void RenderCommits()
        {
            graph.SetData(logEntries);
            graphStatus.text = $"{logEntries.Count} commits · head {logEntries[0].ShortID} \"{logEntries[0].Summary}\"";

            commitList.Clear();
            foreach (var e in logEntries)
            {
                var line = new Label($"{e.ShortID}  {e.Summary}");
                line.style.unityTextAlign = TextAnchor.MiddleLeft;
                line.tooltip = "parents: " + string.Join(",", e.Parents) + "\nfiles: " + (e.Changes?.Count ?? 0);
                commitList.Add(line);
            }
        }

        private VisualElement BuildLayout()
        {
            var outer = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Horizontal);
            outer.name = "outer-split";

            // 左：提交图谱（自绘泳道 + 节点/连线）
            var graphPane = new VisualElement();
            graphStatus = new Label("loading…");
            graphPane.Add(graphStatus);
            var graphScroll = new ScrollView(ScrollViewMode.Vertical);
            graph = new CommitGraphElement();
            graphScroll.Add(graph);
            graphPane.Add(graphScroll);
            outer.Add(graphPane);

            // 右：上 提交列表（文件树下一步） / 下 详情占位
            var inner = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Vertical);
            inner.name = "inner-split";
            commitList = new ScrollView(ScrollViewMode.Vertical);
            inner.Add(commitList);
            inner.Add(PlaceholderPane("Commit details (WIP)", "full message"));
            outer.Add(inner);

            return outer;
        }

        private static VisualElement PlaceholderPane(string title, string hint)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            var t = new Label(title);
            t.style.unityFontStyleAndWeight = FontStyle.Bold;
            t.style.paddingBottom = 4;
            sv.Add(t);
            var b = new Label(hint);
            b.style.whiteSpace = WhiteSpace.Normal;
            sv.Add(b);
            return sv;
        }

        private void OnDisable()
        {
            session?.Dispose();
            session = null;
        }
    }
}