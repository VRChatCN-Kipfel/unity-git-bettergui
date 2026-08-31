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
                var log = s.LoadHistory(50);
                if (log.Count == 0) throw new System.Exception("SMOKE FAIL: no commits parsed");
                var head = log[0];
                UnityEngine.Debug.Log($"[gitui] SMOKE OK: layout={outer.childCount}/{inner.childCount} commits={log.Count} head={head.ShortID} \"{head.Summary}\" parents={head.Parents?.Count ?? 0}");
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