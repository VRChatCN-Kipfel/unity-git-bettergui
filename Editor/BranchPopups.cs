using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.Editor.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// Compare with Branch（M3 内容级版）：第一步选分支，第二步打开内容级 DiffViewer（unified + 词级高亮）。
    /// 选分支后 CompareWindow 自行关闭——内容在独立 DiffViewer 窗口展示（D3 决策）。
    /// OpenPair（两个已明确 ref）一步到位：后台取 U3 diff → DiffRows → DiffViewer.Open。
    /// </summary>
    public sealed class CompareWindow : EditorWindow
    {
        private GitSession session;
        private string commitHash;
        private string commitHashShort = string.Empty;
        private List<GitSession.GitRefInfo> allRefs = new List<GitSession.GitRefInfo>();
        private string filter = string.Empty;
        private string error = string.Empty;
        private bool busy;

        public static void Open(GitSession session, string commitHash)
        {
            if (session == null) return;
            var w = GetWindow<CompareWindow>(true, I18n.L(I18n.Keys.MenuCompareBranch));
            w.session = session;
            w.commitHash = commitHash;
            w.commitHashShort = commitHash.Length >= 7 ? commitHash.Substring(0, 7) : commitHash;
            w.allRefs = session.LoadRefs() ?? new List<GitSession.GitRefInfo>();
            w.Show();
        }

        /// <summary>直接比较两个 ref（本地 vs 上游 / 分支 vs 当前）：免选分支，直接开 DiffViewer。</summary>
        public static void OpenPair(GitSession session, string title, string refA, string refB)
        {
            if (session == null) return;
            OpenDiff(session, title, refA, refB);
        }

        private static void OpenDiff(GitSession session, string title, string refA, string refB)
        {
            // 后台线程取 diff（避免窗口内同步跑 git 卡 UI）；完成后主线程开 DiffViewer
            var ctx = SynchronizationContextHolder.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var task = GitDiffTask.TwoRefs(session.Platform, refA, refB, 3)
                        .Configure(session.Platform.ProcessManager);
                    var output = task.RunSynchronously();
                    List<DiffRow> rows = new List<DiffRow>();
                    if (task.Successful && !string.IsNullOrEmpty(output))
                        rows = DiffRows.Build(UnifiedDiffParser.Parse(output));
                    ctx?.Post(_ => DiffViewer.Open(title, rows), null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => ShowError(title, ex.Message), null);
                }
            });
        }

        private static void ShowError(string title, string message)
        {
            var w = GetWindow<CompareWindow>(true, title);
            w.error = message;
            w.Show();
        }

        private void OnGUI()
        {
            if (session == null) { Close(); return; }

            if (busy)
            {
                EditorGUILayout.LabelField(I18n.L(I18n.Keys.LoadingChanges));
                return;
            }
            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }

            filter = EditorGUILayout.TextField(I18n.L(I18n.Keys.BranchFilter), filter);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in BranchesPanel.ApplyFilter(allRefs, filter))
                if (GUILayout.Button(r.DisplayName))
                    RunCompare(r);
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private UnityEngine.Vector2 scroll;

        private void RunCompare(GitSession.GitRefInfo r)
        {
            busy = true;
            error = string.Empty;
            var title = commitHashShort + " vs " + r.DisplayName;
            // 选完即关（内容进 DiffViewer）
            var self = this;
            var sessionCopy = session;
            var hash = commitHash;
            var ctx = SynchronizationContextHolder.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var task = GitDiffTask.TwoRefs(sessionCopy.Platform, hash, r.DisplayName, 3)
                        .Configure(sessionCopy.Platform.ProcessManager);
                    var output = task.RunSynchronously();
                    List<DiffRow> rows = new List<DiffRow>();
                    if (task.Successful && !string.IsNullOrEmpty(output))
                        rows = DiffRows.Build(UnifiedDiffParser.Parse(output));
                    ctx?.Post(_ =>
                    {
                        self.Close();
                        DiffViewer.Open(title, rows);
                    }, null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => { self.busy = false; self.error = ex.Message; }, null);
                }
            });
        }
    }

    /// <summary>线程上下文捕获（后台 diff → 主线程 UI 的标准通道；与 GitWindow.RunStatusOp 同法）。</summary>
    internal static class SynchronizationContextHolder
    {
        public static System.Threading.SynchronizationContext Current
            => System.Threading.SynchronizationContext.Current;
    }
}