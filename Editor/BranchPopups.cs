using System;
using System.Collections.Generic;
using Unity.Editor.Tasks;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// Compare with Branch（M2 范围 = name-status 预览；内容级 M3）：
    /// 第一步选分支（同一过滤逻辑），第二步展示 git diff --name-status 结果。
    /// 静态过滤见 BranchesPanel.ApplyFilter（本文件不再重复定义，分支管理已移入左侧 BranchesPanel）。
    /// </summary>
    public sealed class CompareWindow : EditorWindow
    {
        private GitSession session;
        private string commitHash;
        private string commitHashShort = string.Empty;
        private List<GitSession.GitRefInfo> allRefs = new List<GitSession.GitRefInfo>();
        private string filter = string.Empty;
        private List<string> result = new List<string>();
        private bool showingResult;
        private string error = string.Empty;
        private UnityEngine.Vector2 scroll;

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

        /// <summary>直接比较两个 ref（如 目标分支 vs 当前分支、本地 vs 上游）：免去选分支一步，直接展示 name-status。</summary>
        public static void OpenPair(GitSession session, string title, string refA, string refB)
        {
            if (session == null) return;
            var w = GetWindow<CompareWindow>(true, title);
            w.session = session;
            w.commitHash = null;
            w.commitHashShort = title;
            w.allRefs = new List<GitSession.GitRefInfo>();
            w.runPair(refA + " " + refB);
            w.Show();
        }

        private void runPair(string args)
        {
            error = string.Empty;
            result.Clear();
            try
            {
                var task = new GitDiffNameStatusTask(session.Platform, "diff --name-status " + args)
                    .Configure(session.Platform.ProcessManager);
                var output = task.RunSynchronously();
                if (task.Successful && !string.IsNullOrEmpty(output))
                    result.AddRange(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                showingResult = true;
            }
            catch (Exception ex) { error = ex.Message; showingResult = false; }
        }

        private void OnGUI()
        {
            if (session == null) { Close(); return; }

            if (!showingResult)
            {
                filter = EditorGUILayout.TextField(I18n.L(I18n.Keys.BranchFilter), filter);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var r in BranchesPanel.ApplyFilter(allRefs, filter))
                    if (GUILayout.Button(r.DisplayName))
                        RunCompare(r);
                EditorGUILayout.EndScrollView();
                if (!string.IsNullOrEmpty(error))
                    EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField(I18n.L(I18n.Keys.CompareResultTitle, commitHashShort),
                    EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (result.Count == 0)
                    EditorGUILayout.LabelField(I18n.L(I18n.Keys.CompareNoChanges));
                else
                    foreach (var line in result)
                        EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button(I18n.L(I18n.Keys.CompareBack)))
                {
                    showingResult = false;
                    result.Clear();
                }
            }
        }

        private void RunCompare(GitSession.GitRefInfo r)
        {
            error = string.Empty;
            try
            {
                var task = new GitDiffNameStatusTask(session.Platform,
                        $"diff --name-status {commitHash} {r.DisplayName}")
                    .Configure(session.Platform.ProcessManager);
                var output = task.RunSynchronously();
                result.Clear();
                if (task.Successful && !string.IsNullOrEmpty(output))
                    result.AddRange(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                showingResult = true;
            }
            catch (Exception ex) { error = ex.Message; }
        }
    }
}