using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Editor.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// 分支管理弹窗（JetBrains GitBranchesTreePopup 简化版，IMGUI）：
    /// 过滤（空格分词、任一 token 必须命中）+ 当前徽标/ahead-behind + Checkout / 删除 / 新建 / 新建标签。
    /// 数据源 = GitSession.LoadRefs（for-each-ref，兼容 packed refs）。
    /// </summary>
    public sealed class BranchPopupWindow : EditorWindow
    {
        private GitSession session;
        private Action onChanged;
        private List<GitSession.GitRefInfo> allRefs = new List<GitSession.GitRefInfo>();
        private string filter = string.Empty;
        private string newBranchName = string.Empty;
        private string newTagName = string.Empty;
        private string error = string.Empty;
        private string currentBranch = string.Empty;
        private int currentAhead;
        private int currentBehind;
        private Vector2 scroll;
        // 分组折叠（JetBrains branches tree 语义：本地/远程/标签三类，各自可展开/收起）
        private bool localExpanded = true;
        private bool remoteExpanded = true;
        private bool tagsExpanded = true;

        public static void Open(GitSession session, Action onChanged)
        {
            if (session == null) return;
            var w = GetWindow<BranchPopupWindow>(true, I18n.L(I18n.Keys.BranchTitle));
            w.session = session;
            w.onChanged = onChanged;
            w.Reload();
            w.Show();
        }

        /// <summary>过滤（JetBrains GitBranchesSearcher 语义：空格分词，全部 token 子串命中，忽略大小写）。</summary>
        public static List<GitSession.GitRefInfo> ApplyFilter(IEnumerable<GitSession.GitRefInfo> all, string filter)
        {
            var tokens = (filter ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return all.ToList();
            return all.Where(r => tokens.All(t =>
                    r.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }

        private void Reload()
        {
            try
            {
                allRefs = session.LoadRefs() ?? new List<GitSession.GitRefInfo>();
                var status = session.LoadStatus();
                currentBranch = status.LocalBranch ?? string.Empty;
                currentAhead = status.Ahead;
                currentBehind = status.Behind;
            }
            catch (Exception ex) { error = ex.Message; }
        }

        private void OnGUI()
        {
            if (session == null) { Close(); return; }

            filter = EditorGUILayout.TextField(I18n.L(I18n.Keys.BranchFilter), filter);
            var filtered = ApplyFilter(allRefs, filter);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            var locals = filtered.Where(r => r.Type == GitSession.RefType.Local || r.Type == GitSession.RefType.Head).ToList();
            var remotes = filtered.Where(r => r.Type == GitSession.RefType.Remote).ToList();
            var tags = filtered.Where(r => r.Type == GitSession.RefType.Tag).ToList();

            DrawSection(I18n.L(I18n.Keys.BranchGroupLocal), ref localExpanded, locals);
            DrawSection(I18n.L(I18n.Keys.BranchGroupRemote), ref remoteExpanded, remotes);
            DrawSection(I18n.L(I18n.Keys.BranchGroupTags), ref tagsExpanded, tags);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            newBranchName = EditorGUILayout.TextField(I18n.L(I18n.Keys.BranchNewLabel), newBranchName);
            if (GUILayout.Button(I18n.L(I18n.Keys.BranchNew))) CreateBranch();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            newTagName = EditorGUILayout.TextField(I18n.L(I18n.Keys.BranchTagLabel), newTagName);
            if (GUILayout.Button(I18n.L(I18n.Keys.BranchTag))) CreateTag();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void DrawSection(string header, ref bool expanded, List<GitSession.GitRefInfo> refs)
        {
            if (refs.Count == 0) return;
            expanded = EditorGUILayout.Foldout(expanded, header + "  (" + refs.Count + ")", true);
            if (!expanded) return;
            foreach (var r in refs)
                RenderRow(r);
        }

        private void RenderRow(GitSession.GitRefInfo r)
        {
            var label = r.DisplayName;
            if (r.IsCurrentHead || r.Type == GitSession.RefType.Head)
                label += "  (" + I18n.L(I18n.Keys.BranchCurrent) + ")";
            if (r.DisplayName == currentBranch && (currentAhead > 0 || currentBehind > 0))
                label += string.Format("  ↑{0} ↓{1}", currentAhead, currentBehind);

            var isLocal = r.Type == GitSession.RefType.Local || r.Type == GitSession.RefType.Head;
            var canDelete = (isLocal && !r.IsCurrentHead) || r.Type == GitSession.RefType.Tag;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(label, canDelete ? EditorStyles.miniButton : EditorStyles.miniButtonLeft))
                CheckoutRef(r);
            if (canDelete && GUILayout.Button(I18n.L(I18n.Keys.BranchDelete),
                    EditorStyles.miniButtonRight, GUILayout.Width(48)))
                DeleteRef(r);
            EditorGUILayout.EndHorizontal();
        }

        private void CheckoutRef(GitSession.GitRefInfo r)
        {
            error = string.Empty;
            if (r.Type == GitSession.RefType.Tag
                && !EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuCheckout),
                    I18n.L(I18n.Keys.BranchCheckoutTagConfirm, r.DisplayName),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.Checkout(r.DisplayName);
                onChanged?.Invoke();
                Close();
            }
            catch (Exception ex) { error = ex.Message; }
        }

        private void DeleteRef(GitSession.GitRefInfo r)
        {
            error = string.Empty;
            if (r.Type == GitSession.RefType.Tag)
            {
                if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                        I18n.L(I18n.Keys.BranchDeleteTagConfirm, r.DisplayName),
                        I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                    return;
                try { session.DeleteTag(r.DisplayName); }
                catch (Exception ex) { error = ex.Message; }
                onChanged?.Invoke();
                Reload();
                return;
            }
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                    I18n.L(I18n.Keys.BranchDeleteConfirm, r.DisplayName),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.DeleteBranch(r.DisplayName, false);
            }
            catch (Exception)
            {
                if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                        I18n.L(I18n.Keys.BranchDeleteForce, r.DisplayName),
                        I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                    return;
                try { session.DeleteBranch(r.DisplayName, true); }
                catch (Exception ex) { error = ex.Message; }
            }
            onChanged?.Invoke();
            Reload();
        }

        private void CreateBranch()
        {
            error = string.Empty;
            var name = newBranchName.Trim();
            if (name.Length == 0) return;
            var baseRef = string.IsNullOrEmpty(currentBranch) ? "HEAD" : currentBranch;
            try
            {
                session.NewBranch(name, baseRef);
                newBranchName = string.Empty;
                onChanged?.Invoke();
                Reload();
            }
            catch (Exception ex) { error = ex.Message; }
        }

        private void CreateTag()
        {
            error = string.Empty;
            var name = newTagName.Trim();
            if (name.Length == 0) return;
            var head = allRefs.FirstOrDefault(r => r.Type == GitSession.RefType.Head)?.CommitId;
            if (string.IsNullOrEmpty(head)) return;
            try
            {
                session.CreateTag(name, head, name);
                newTagName = string.Empty;
                onChanged?.Invoke();
                Reload();
            }
            catch (Exception ex) { error = ex.Message; }
        }
    }

    /// <summary>
    /// Compare with Branch（M2 范围 = name-status 预览；内容级 M3）：
    /// 第一步选分支（同一过滤逻辑），第二步展示 git diff --name-status 结果。
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
        private Vector2 scroll;

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

        private void OnGUI()
        {
            if (session == null) { Close(); return; }

            if (!showingResult)
            {
                filter = EditorGUILayout.TextField(I18n.L(I18n.Keys.BranchFilter), filter);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var r in BranchPopupWindow.ApplyFilter(allRefs, filter))
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