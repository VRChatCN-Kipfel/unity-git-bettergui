using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>
    /// remote 管理窗口（M3 P1；独立入口来自 BranchesPanel 空白右键「Manage Remotes…」）。
    /// 数据：GitRemoteListTask（name/url/fetch-push 功能）+ Add/SetUrl/Remove 三操作（api 现成任务）。
    /// 说明：BranchesPanel 的「Remotes」节是远程跟踪分支（refs/remotes/*），与 remote 定义（name→url）
    /// 是两层——本窗口管理后者。
    /// </summary>
    public sealed class RemoteManagerWindow : EditorWindow
    {
        private GitSession session;
        private List<GitRemote> remotes = new List<GitRemote>();
        private string nameInput = string.Empty;
        private string urlInput = string.Empty;
        private string error = string.Empty;
        private string status = string.Empty;
        private Vector2 scroll;

        public static void Open(GitSession session)
        {
            if (session == null) return;
            var w = GetWindow<RemoteManagerWindow>(true, I18n.L(I18n.Keys.RemoteManageTitle));
            w.session = session;
            w.Reload();
            w.Show();
        }

        private void Reload()
        {
            error = string.Empty;
            try { remotes = session.LoadRemotes(); }
            catch (Exception ex) { error = ex.Message; remotes = new List<GitRemote>(); }
            Repaint();
        }

        private void OnGUI()
        {
            if (session == null) { Close(); return; }

            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Error);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in remotes)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(r.Name, EditorStyles.boldLabel, GUILayout.Width(140));
                EditorGUILayout.LabelField(r.Url);
                var func = r.Function == GitRemoteFunction.Fetch ? "fetch"
                    : r.Function == GitRemoteFunction.Push ? "push" : "fetch+push";
                EditorGUILayout.LabelField(func, GUILayout.Width(80));
                if (GUILayout.Button(I18n.L(I18n.Keys.RemoteEditUrl), GUILayout.Width(70)))
                {
                    nameInput = r.Name;
                    urlInput = r.Url;
                }
                if (GUILayout.Button(I18n.L(I18n.Keys.RemoteRemove), GUILayout.Width(60)))
                    RemoveRemote(r);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(I18n.L(I18n.Keys.RemoteName), GUILayout.Width(80));
            nameInput = EditorGUILayout.TextField(nameInput);
            EditorGUILayout.LabelField(I18n.L(I18n.Keys.RemoteUrl), GUILayout.Width(80));
            urlInput = EditorGUILayout.TextField(urlInput);
            if (GUILayout.Button(I18n.L(I18n.Keys.RemoteAdd), GUILayout.Width(80)))
                AddRemote();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        private void AddRemote()
        {
            error = string.Empty;
            status = string.Empty;
            var name = nameInput.Trim();
            var url = urlInput.Trim();
            if (name.Length == 0 || url.Length == 0) { error = I18n.L(I18n.Keys.RemoteNameUrlRequired); return; }
            try
            {
                var exists = remotes.Exists(r => r.Name == name);
                if (exists) session.RemoteSetUrl(name, url);
                else session.RemoteAdd(name, url);
                status = exists ? I18n.L(I18n.Keys.RemoteUpdated, name) : I18n.L(I18n.Keys.RemoteAdded, name);
                nameInput = string.Empty;
                urlInput = string.Empty;
                Reload();
            }
            catch (Exception ex) { error = ex.Message; }
        }

        private void RemoveRemote(GitRemote r)
        {
            error = string.Empty;
            status = string.Empty;
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.RemoteRemove),
                    I18n.L(I18n.Keys.RemoteRemoveConfirm, r.Name),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.RemoteRemove(r.Name);
                status = I18n.L(I18n.Keys.RemoteRemoved, r.Name);
                Reload();
            }
            catch (Exception ex) { error = ex.Message; }
        }
    }
}