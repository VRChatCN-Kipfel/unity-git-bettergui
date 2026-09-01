using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KF.GitUI
{
    /// <summary>
    /// 左侧常驻分支面板（JetBrains 旧式 Branches 工具窗语义；用户拍板：分支管理不再用弹窗）。
    /// 三节（本地/远程/标签）可折叠，行点击=checkout、行尾删除、底部新建分支/标签。
    /// 数据源 = GitSession.LoadRefs（for-each-ref，兼容 packed refs）。
    /// </summary>
    public sealed class BranchesPanel : VisualElement
    {
        private readonly ScrollView scroll;
        private readonly TextField filterField;
        private readonly TextField newBranchField;
        private readonly TextField newTagField;
        private readonly Label errorLabel;

        private GitSession session;
        private Action onChanged;
        private List<GitSession.GitRefInfo> allRefs = new List<GitSession.GitRefInfo>();
        private string currentBranch = string.Empty;
        private int currentAhead;
        private int currentBehind;

        private bool localExpanded = true;
        private bool remoteExpanded = true;
        private bool tagsExpanded = true;

        public BranchesPanel()
        {
            name = "branches-panel";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;
            style.minWidth = 150f;

            filterField = new TextField(I18n.L(I18n.Keys.BranchFilter));
            filterField.name = "branches-filter";
            filterField.RegisterValueChangedCallback(_ => Rebuild());
            Add(filterField);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "branches-scroll";
            scroll.style.flexGrow = 1f;
            Add(scroll);

            var bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Column;
            bottom.style.paddingTop = 4;

            var brRow = new VisualElement();
            brRow.style.flexDirection = FlexDirection.Row;
            newBranchField = new TextField(I18n.L(I18n.Keys.BranchNewLabel));
            newBranchField.style.flexGrow = 1f;
            var brBtn = new Button(CreateBranch) { text = I18n.L(I18n.Keys.BranchNew) };
            brRow.Add(newBranchField);
            brRow.Add(brBtn);
            bottom.Add(brRow);

            var tagRow = new VisualElement();
            tagRow.style.flexDirection = FlexDirection.Row;
            newTagField = new TextField(I18n.L(I18n.Keys.BranchTagLabel));
            newTagField.style.flexGrow = 1f;
            var tagBtn = new Button(CreateTag) { text = I18n.L(I18n.Keys.BranchTag) };
            tagRow.Add(newTagField);
            tagRow.Add(tagBtn);
            bottom.Add(tagRow);

            errorLabel = new Label();
            errorLabel.style.color = new Color(0.85f, 0.3f, 0.3f);
            errorLabel.style.whiteSpace = WhiteSpace.Normal;
            bottom.Add(errorLabel);

            Add(bottom);
        }

        /// <summary>绑定会话（窗口 ReloadHistory 后调用）；之后可 Refresh。null 显示占位。</summary>
        public void Bind(GitSession gitSession, Action changed)
        {
            session = gitSession;
            onChanged = changed;
            Refresh();
        }

        /// <summary>重载 refs/状态并重建列表（分支操作/指纹刷新后调用）。</summary>
        public void Refresh()
        {
            errorLabel.text = string.Empty;
            if (session == null)
            {
                scroll.Clear();
                return;
            }
            try
            {
                allRefs = session.LoadRefs() ?? new List<GitSession.GitRefInfo>();
                var status = session.LoadStatus();
                currentBranch = status.LocalBranch ?? string.Empty;
                currentAhead = status.Ahead;
                currentBehind = status.Behind;
            }
            catch (Exception ex)
            {
                errorLabel.text = ex.Message;
                allRefs = new List<GitSession.GitRefInfo>();
            }
            Rebuild();
        }

        private void Rebuild()
        {
            scroll.Clear();
            if (session == null) return;

            var filtered = BranchPopupWindow.ApplyFilter(allRefs, filterField.value);
            var locals = filtered.Where(r => r.Type == GitSession.RefType.Local || r.Type == GitSession.RefType.Head).ToList();
            var remotes = filtered.Where(r => r.Type == GitSession.RefType.Remote).ToList();
            var tags = filtered.Where(r => r.Type == GitSession.RefType.Tag).ToList();

            DrawSection(I18n.L(I18n.Keys.BranchGroupLocal), localExpanded,
                () => { localExpanded = !localExpanded; Rebuild(); }, locals);
            DrawSection(I18n.L(I18n.Keys.BranchGroupRemote), remoteExpanded,
                () => { remoteExpanded = !remoteExpanded; Rebuild(); }, remotes);
            DrawSection(I18n.L(I18n.Keys.BranchGroupTags), tagsExpanded,
                () => { tagsExpanded = !tagsExpanded; Rebuild(); }, tags);
        }

        private void DrawSection(string title, bool expanded, Action toggle, List<GitSession.GitRefInfo> refs)
        {
            if (refs.Count == 0) return;
            var header = new Button(toggle);
            header.text = (expanded ? "▾ " : "▸ ") + title + "  (" + refs.Count + ")";
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.style.backgroundColor = new Color(0.25f, 0.25f, 0.28f, 0.55f);
            scroll.Add(header);

            if (!expanded) return;
            foreach (var r in refs)
                scroll.Add(RenderRow(r));
        }

        private VisualElement RenderRow(GitSession.GitRefInfo r)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 20f;

            var label = r.DisplayName;
            if (r.Type == GitSession.RefType.Head)
                label += "  (" + I18n.L(I18n.Keys.BranchCurrent) + ")";
            if (r.DisplayName == currentBranch && (currentAhead > 0 || currentBehind > 0))
                label += string.Format("  ↑{0} ↓{1}", currentAhead, currentBehind);

            var go = new Button(() => Checkout(r)) { text = label };
            go.style.unityTextAlign = TextAnchor.MiddleLeft;
            go.style.flexGrow = 1f;
            row.Add(go);

            var isLocal = r.Type == GitSession.RefType.Local || r.Type == GitSession.RefType.Head;
            var canDelete = (isLocal && !r.IsCurrentHead) || r.Type == GitSession.RefType.Tag;
            if (canDelete)
            {
                var del = new Button(() => Delete(r)) { text = I18n.L(I18n.Keys.BranchDelete) };
                del.style.width = 44f;
                row.Add(del);
            }
            return row;
        }

        private void Checkout(GitSession.GitRefInfo r)
        {
            if (session == null) return;
            if (r.Type == GitSession.RefType.Tag
                && !EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuCheckout),
                    I18n.L(I18n.Keys.BranchCheckoutTagConfirm, r.DisplayName),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                session.Checkout(r.DisplayName);
                onChanged?.Invoke();
                Refresh();
            }
            catch (Exception ex) { errorLabel.text = ex.Message; }
        }

        private void Delete(GitSession.GitRefInfo r)
        {
            if (session == null) return;
            if (r.Type == GitSession.RefType.Tag)
            {
                if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                        I18n.L(I18n.Keys.BranchDeleteTagConfirm, r.DisplayName),
                        I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                    return;
                try { session.DeleteTag(r.DisplayName); }
                catch (Exception ex) { errorLabel.text = ex.Message; }
                onChanged?.Invoke();
                Refresh();
                return;
            }
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                    I18n.L(I18n.Keys.BranchDeleteConfirm, r.DisplayName),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try { session.DeleteBranch(r.DisplayName, false); }
            catch (Exception)
            {
                if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                        I18n.L(I18n.Keys.BranchDeleteForce, r.DisplayName),
                        I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                    return;
                try { session.DeleteBranch(r.DisplayName, true); }
                catch (Exception ex) { errorLabel.text = ex.Message; }
            }
            onChanged?.Invoke();
            Refresh();
        }

        private void CreateBranch()
        {
            if (session == null) return;
            var name = newBranchField.value.Trim();
            if (name.Length == 0) return;
            var baseRef = string.IsNullOrEmpty(currentBranch) ? "HEAD" : currentBranch;
            try
            {
                session.NewBranch(name, baseRef);
                newBranchField.SetValueWithoutNotify("");
                onChanged?.Invoke();
                Refresh();
            }
            catch (Exception ex) { errorLabel.text = ex.Message; }
        }

        private void CreateTag()
        {
            if (session == null) return;
            var name = newTagField.value.Trim();
            if (name.Length == 0) return;
            var head = allRefs.FirstOrDefault(r => r.Type == GitSession.RefType.Head)?.CommitId;
            if (string.IsNullOrEmpty(head)) return;
            try
            {
                session.CreateTag(name, head, name);
                newTagField.SetValueWithoutNotify("");
                onChanged?.Invoke();
                Refresh();
            }
            catch (Exception ex) { errorLabel.text = ex.Message; }
        }
    }
}