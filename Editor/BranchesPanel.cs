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
    /// 纯列表展示（无行尾按钮/底部新建栏），操作统一走右键菜单：
    ///   当前分支：Branch from…/Update/Push/Rename（无 Delete）；
    ///   其它本地：Checkout/Branch from…/比较/合并到当前/Update(Fetch)/Push/对上游的操作(子菜单，无重命名)/Rename/Delete；
    ///   远程：Checkout/Branch from…/比较/Fetch/推送当前分支到该远程；
    ///   标签：Checkout/Branch from…/比较/Delete。
    /// 空白处右键 = New Branch…/New Tag…。rebase 系 → M3（落点见 docs/M2-SOLUTION.md §6）。
    /// </summary>
    public sealed class BranchesPanel : VisualElement
    {
        private const string PrefLocal = "kf.gitui.branches.localExpanded";
        private const string PrefRemote = "kf.gitui.branches.remoteExpanded";
        private const string PrefTags = "kf.gitui.branches.tagsExpanded";

        private readonly ScrollView scroll;
        private readonly TextField filterField;
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

            localExpanded = EditorPrefs.GetBool(PrefLocal, true);
            remoteExpanded = EditorPrefs.GetBool(PrefRemote, true);
            tagsExpanded = EditorPrefs.GetBool(PrefTags, true);

            filterField = new TextField(I18n.L(I18n.Keys.BranchFilter));
            filterField.name = "branches-filter";
            filterField.RegisterValueChangedCallback(_ => Rebuild());
            Add(filterField);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "branches-scroll";
            scroll.style.flexGrow = 1f;
            // 空白处右键：新建分支/标签
            GitContextMenu.Attach(scroll, () => BuildBlankActions());
            Add(scroll);

            errorLabel = new Label();
            errorLabel.style.color = new Color(0.85f, 0.3f, 0.3f);
            errorLabel.style.whiteSpace = WhiteSpace.Normal;
            Add(errorLabel);
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

        /// <summary>过滤（JetBrains GitBranchesSearcher 语义：空格分词，全部 token 子串命中，忽略大小写）。</summary>
        public static List<GitSession.GitRefInfo> ApplyFilter(IEnumerable<GitSession.GitRefInfo> all, string filter)
        {
            var tokens = (filter ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return all.ToList();
            return all.Where(r => tokens.All(t =>
                    r.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }

        private void Rebuild()
        {
            scroll.Clear();
            if (session == null) return;

            var filtered = ApplyFilter(allRefs, filterField.value);
            var locals = filtered.Where(r => r.Type == GitSession.RefType.Local || r.Type == GitSession.RefType.Head).ToList();
            var remotes = filtered.Where(r => r.Type == GitSession.RefType.Remote).ToList();
            var tags = filtered.Where(r => r.Type == GitSession.RefType.Tag).ToList();

            DrawSection(I18n.L(I18n.Keys.BranchGroupLocal), localExpanded,
                () => { localExpanded = !localExpanded; EditorPrefs.SetBool(PrefLocal, localExpanded); Rebuild(); }, locals);
            DrawSection(I18n.L(I18n.Keys.BranchGroupRemote), remoteExpanded,
                () => { remoteExpanded = !remoteExpanded; EditorPrefs.SetBool(PrefRemote, remoteExpanded); Rebuild(); }, remotes);
            DrawSection(I18n.L(I18n.Keys.BranchGroupTags), tagsExpanded,
                () => { tagsExpanded = !tagsExpanded; EditorPrefs.SetBool(PrefTags, tagsExpanded); Rebuild(); }, tags);
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

        private string DisplayText(GitSession.GitRefInfo r)
        {
            return FormatRefLabel(r.DisplayName, IsMainBranch(r), IsCurrentBranch(r), currentAhead, currentBehind);
        }

        /// <summary>
        /// 行文本（JetBrains 直觉）：主分支（main/master）左侧 ★；当前签出分支名前 »；当前分支带 ↑↓。
        /// 用最基础的 BMP 通用符号（Unity 默认字体缺 emoji 与部分 Dingbats 字形，U+2B50/U+1F3F7/U+276F 会渲染成 □）。
        /// 静态可测。
        /// </summary>
        public static string FormatRefLabel(string name, bool isMain, bool isCurrent, int ahead, int behind)
        {
            var sb = new System.Text.StringBuilder();
            if (isMain) sb.Append("★ ");
            if (isCurrent) sb.Append("» ");
            sb.Append(name);
            if (isCurrent && (ahead > 0 || behind > 0))
                sb.Append(string.Format("  ↑{0} ↓{1}", ahead, behind));
            return sb.ToString();
        }

        private bool IsCurrentBranch(GitSession.GitRefInfo r)
        {
            return r.Type == GitSession.RefType.Head || r.DisplayName == currentBranch;
        }

        private bool IsMainBranch(GitSession.GitRefInfo r)
        {
            if (r.Type != GitSession.RefType.Local && r.Type != GitSession.RefType.Head) return false;
            return r.DisplayName == "main" || r.DisplayName == "master";
        }

        private static readonly Color SelBg = new Color(0.25f, 0.45f, 0.75f, 0.55f);
        private static readonly Color HoverBg = new Color(0.35f, 0.55f, 0.85f, 0.30f);

        private string selectedName = string.Empty;

        /// <summary>单击选中（深蓝）；取消选中传 null。</summary>
        private void SetSelected(string name)
        {
            if (selectedName == name) return;
            selectedName = name ?? string.Empty;
            Rebuild();
        }

        private VisualElement RenderRow(GitSession.GitRefInfo r)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 20f;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6f;
            ApplyRowStyle(row, r, false);

            var label = new Label(DisplayText(r));
            label.style.flexGrow = 1f;
            label.style.fontSize = 12f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.tooltip = r.Upstream ?? r.DisplayName;
            row.Add(label);

            // 单击 = 选中（深蓝）；双击 = 签出（JetBrains 交互惯例）
            row.RegisterCallback<ClickEvent>(ev =>
            {
                if (ev.clickCount >= 2)
                    DoCheckout(session, r, onChanged, ShowError);
                else
                    SetSelected(r.DisplayName);
            });
            row.RegisterCallback<MouseEnterEvent>(_ => ApplyRowStyle(row, r, true));
            row.RegisterCallback<MouseLeaveEvent>(_ => ApplyRowStyle(row, r, false));
            GitContextMenu.Attach(row,
                () => BuildContextActions(session, r, currentBranch, onChanged, ShowError));
            return row;
        }

        /// <summary>行背景：选中=深蓝；悬停=浅蓝；否则透明（悬停不覆盖选中）。</summary>
        private void ApplyRowStyle(VisualElement row, GitSession.GitRefInfo r, bool hover)
        {
            if (selectedName == r.DisplayName)
                row.style.backgroundColor = SelBg;
            else
                row.style.backgroundColor = hover ? HoverBg : Color.clear;
        }

        // ---- 右键动作（静态构建，冒烟可断言） ----

        /// <summary>分支/标签行右键动作集。currentBranch = 当前分支名；onError 显示面板错误行。</summary>
        public static IEnumerable<IGitContextAction> BuildContextActions(GitSession session,
            GitSession.GitRefInfo target, string currentBranch, Action onChanged, Action<string> onError)
        {
            if (session == null || target == null) yield break;
            var cur = string.IsNullOrEmpty(currentBranch) ? "HEAD" : currentBranch;
            var isCurrent = target.Type == GitSession.RefType.Head || target.IsCurrentHead;

            if (target.Type == GitSession.RefType.Remote)
            {
                yield return new DelegateAction("ctx.checkout", I18n.L(I18n.Keys.BranchCtxCheckout),
                    () => DoCheckout(session, target, onChanged, onError));
                yield return new DelegateAction("ctx.newfrom", I18n.L(I18n.Keys.BranchCtxNewFrom, target.DisplayName),
                    () => DoNewBranchFrom(session, target.DisplayName, onChanged, onError));
                yield return GitContextSeparator.Instance;
                yield return new DelegateAction("ctx.compare", I18n.L(I18n.Keys.BranchCtxCompareWith, cur),
                    () => CompareWindow.OpenPair(session, target.DisplayName + " vs " + cur, target.DisplayName, cur));
                yield return new DelegateAction("ctx.fetch", I18n.L(I18n.Keys.BranchCtxFetch),
                    () => RunOp(session, () => session.Fetch(RemoteName(target)), onChanged, onError));
                yield return new DelegateAction("ctx.pushcur", I18n.L(I18n.Keys.BranchCtxPushCurrentTo, RemoteName(target)),
                    () => RunOp(session, () => session.Push(RemoteName(target), cur, false), onChanged, onError));
                yield break;
            }

            if (target.Type == GitSession.RefType.Tag)
            {
                yield return new DelegateAction("ctx.checkout", I18n.L(I18n.Keys.BranchCtxCheckout),
                    () => DoCheckout(session, target, onChanged, onError));
                yield return new DelegateAction("ctx.newfrom", I18n.L(I18n.Keys.BranchCtxNewFrom, target.DisplayName),
                    () => DoNewBranchFrom(session, target.DisplayName, onChanged, onError));
                yield return GitContextSeparator.Instance;
                yield return new DelegateAction("ctx.compare", I18n.L(I18n.Keys.BranchCtxCompareWith, cur),
                    () => CompareWindow.OpenPair(session, target.DisplayName + " vs " + cur, target.DisplayName, cur));
                yield return new DelegateAction("ctx.delete", I18n.L(I18n.Keys.BranchDelete),
                    () => DoDelete(session, target, onChanged, onError));
                yield break;
            }

            // ---- 本地/当前分支 ----
            yield return new DelegateAction("ctx.newfrom", I18n.L(I18n.Keys.BranchCtxNewFrom, target.DisplayName),
                () => DoNewBranchFrom(session, target.DisplayName, onChanged, onError));
            if (!isCurrent)
                yield return new DelegateAction("ctx.checkout", I18n.L(I18n.Keys.BranchCtxCheckout),
                    () => DoCheckout(session, target, onChanged, onError));
            yield return GitContextSeparator.Instance;
            yield return new DelegateAction("ctx.compare", I18n.L(I18n.Keys.BranchCtxCompareWith, cur),
                () => CompareWindow.OpenPair(session, target.DisplayName + " vs " + cur, target.DisplayName, cur));
            if (!isCurrent)
                yield return new DelegateAction("ctx.merge", I18n.L(I18n.Keys.BranchCtxMergeInto, target.DisplayName, cur),
                    () => DoMergeIntoCurrent(session, target.DisplayName, cur, onChanged, onError));
            // Update/Push
            if (isCurrent)
                yield return new DelegateAction("ctx.update", I18n.L(I18n.Keys.BranchCtxUpdate),
                    () => RunOp(session, () => session.Pull(), onChanged, onError));
            else
                yield return new DelegateAction("ctx.update", I18n.L(I18n.Keys.BranchCtxUpdate),
                    () => DoFetchDefault(session, target, onChanged, onError));
            yield return new DelegateAction("ctx.push", I18n.L(I18n.Keys.BranchCtxPush),
                () => DoPush(session, target, isCurrent, onChanged, onError))
            {
                Enabled = isCurrent || target.Upstream != null || HasOrigin(session),
            };
            // 对上游的操作（子菜单，无重命名——用户约束）
            if (target.Upstream != null)
            {
                var prefix = I18n.L(I18n.Keys.BranchCtxUpstreamOps, target.Upstream);
                var upstream = target.Upstream;
                var upRemote = RemoteNameOf(target.Upstream);
                yield return new DelegateAction("ctx.upstream.fetch", prefix + "/" + I18n.L(I18n.Keys.BranchCtxFetch),
                    () => RunOp(session, () => session.Fetch(upRemote), onChanged, onError));
                yield return new DelegateAction("ctx.upstream.push",
                    prefix + "/" + I18n.L(I18n.Keys.BranchCtxPushCurrentTo, upRemote),
                    () => RunOp(session, () => session.Push(upRemote, target.DisplayName, false), onChanged, onError));
                yield return new DelegateAction("ctx.upstream.compare",
                    prefix + "/" + I18n.L(I18n.Keys.BranchCtxUpstreamCompare),
                    () => CompareWindow.OpenPair(session, target.DisplayName + " vs " + upstream, target.DisplayName, upstream));
                yield return new DelegateAction("ctx.upstream.merge",
                    prefix + "/" + I18n.L(I18n.Keys.BranchCtxUpstreamMerge, cur),
                    () => DoMergeIntoCurrent(session, upstream, cur, onChanged, onError));
            }
            yield return GitContextSeparator.Instance;
            yield return new DelegateAction("ctx.rename", I18n.L(I18n.Keys.BranchCtxRename),
                () => DoRename(session, target, onChanged, onError));
            if (!isCurrent)
                yield return new DelegateAction("ctx.delete", I18n.L(I18n.Keys.BranchDelete),
                    () => DoDelete(session, target, onChanged, onError));
        }

        /// <summary>空白处右键：新建分支/标签（替代被删除的底部输入栏）。</summary>
        private IEnumerable<IGitContextAction> BuildBlankActions()
        {
            if (session == null) yield break;
            var cur = string.IsNullOrEmpty(currentBranch) ? "HEAD" : currentBranch;
            yield return new DelegateAction("blank.newbranch", I18n.L(I18n.Keys.MenuNewBranch),
                () => PromptNewBranchFrom(session, cur, onChanged, ShowError));
            yield return new DelegateAction("blank.newtag", I18n.L(I18n.Keys.MenuCreateTag),
                () => PromptNewTag(session, onChanged, ShowError));
        }

        private void ShowError(string msg) => errorLabel.text = msg ?? string.Empty;

        // ---- 动作执行（静态 helper） ----

        private static string RemoteName(GitSession.GitRefInfo remoteRef)
        {
            var i = remoteRef.DisplayName.IndexOf('/');
            return i > 0 ? remoteRef.DisplayName.Substring(0, i) : remoteRef.DisplayName;
        }

        private static string RemoteNameOf(string upstream)
        {
            var i = upstream.IndexOf('/');
            return i > 0 ? upstream.Substring(0, i) : upstream;
        }

        private static bool HasOrigin(GitSession session)
        {
            return session != null && !string.IsNullOrEmpty(DefaultRemote(session));
        }

        private static string DefaultRemote(GitSession session)
        {
            // 简单探测：取当前 refs 里第一个远程前缀（"origin/…"）。
            try
            {
                foreach (var r in session.LoadRefs())
                    if (r.Type == GitSession.RefType.Remote)
                        return RemoteName(r);
            }
            catch { }
            return null;
        }

        private static void RunOp(GitSession session, Action op, Action onChanged, Action<string> onError)
        {
            try
            {
                op();
                onChanged?.Invoke();
            }
            catch (Exception ex) { onError?.Invoke(ex.Message); }
        }

        private static void DoCheckout(GitSession session, GitSession.GitRefInfo target, Action onChanged, Action<string> onError)
        {
            if (session == null) return;
            if (target.Type == GitSession.RefType.Tag
                && !EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MenuCheckout),
                    I18n.L(I18n.Keys.BranchCheckoutTagConfirm, target.DisplayName),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            RunOp(session, () => session.Checkout(target.DisplayName), onChanged, onError);
        }

        private static void DoNewBranchFrom(GitSession session, string baseRef, Action onChanged, Action<string> onError)
        {
            var name = PromptDialog.Show(I18n.L(I18n.Keys.BranchCtxNewBranchTitle),
                I18n.L(I18n.Keys.BranchCtxNewFrom, baseRef), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            RunOp(session, () => session.NewBranch(name.Trim(), baseRef), onChanged, onError);
        }

        private static void DoRename(GitSession session, GitSession.GitRefInfo target, Action onChanged, Action<string> onError)
        {
            if (session == null || target.Type == GitSession.RefType.Remote) return; // 远程不重命名
            var name = PromptDialog.Show(I18n.L(I18n.Keys.BranchCtxRename),
                I18n.L(I18n.Keys.BranchCtxRenamePrompt, target.DisplayName), target.DisplayName);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == target.DisplayName) return;
            RunOp(session, () => session.RenameBranch(target.DisplayName, name.Trim()), onChanged, onError);
        }

        private static void DoDelete(GitSession session, GitSession.GitRefInfo target, Action onChanged, Action<string> onError)
        {
            if (session == null) return;
            if (target.Type == GitSession.RefType.Tag)
            {
                if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                        I18n.L(I18n.Keys.BranchDeleteTagConfirm, target.DisplayName),
                        I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                    return;
                RunOp(session, () => session.DeleteTag(target.DisplayName), onChanged, onError);
                return;
            }
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                    I18n.L(I18n.Keys.BranchDeleteConfirm, target.DisplayName),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try { session.DeleteBranch(target.DisplayName, false); onChanged?.Invoke(); }
            catch (Exception)
            {
                if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchDelete),
                        I18n.L(I18n.Keys.BranchDeleteForce, target.DisplayName),
                        I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                    return;
                RunOp(session, () => session.DeleteBranch(target.DisplayName, true), onChanged, onError);
            }
        }

        private static void DoMergeIntoCurrent(GitSession session, string srcRef, string cur, Action onChanged, Action<string> onError)
        {
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.BranchCtxMergeInto, srcRef, cur),
                    I18n.L(I18n.Keys.BranchCtxMergeConfirm, srcRef, cur),
                    I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            RunOp(session, () => session.Merge(srcRef), onChanged, onError);
        }

        private static void DoFetchDefault(GitSession session, GitSession.GitRefInfo target, Action onChanged, Action<string> onError)
        {
            var remote = target.Upstream != null ? RemoteNameOf(target.Upstream) : DefaultRemote(session);
            var remoteName = remote;
            RunOp(session, () => session.Fetch(remoteName), onChanged, onError);
        }

        private static void DoPush(GitSession session, GitSession.GitRefInfo target, bool isCurrent, Action onChanged, Action<string> onError)
        {
            if (session == null) return;
            if (isCurrent)
            {
                RunOp(session, () => session.Push(), onChanged, onError);
                return;
            }
            var remote = target.Upstream != null ? RemoteNameOf(target.Upstream) : DefaultRemote(session);
            if (string.IsNullOrEmpty(remote))
            {
                onError?.Invoke(I18n.L(I18n.Keys.BranchCtxNoRemoteHint));
                return;
            }
            RunOp(session, () => session.Push(remote, target.DisplayName, target.Upstream == null), onChanged, onError);
        }

        private static void PromptNewBranchFrom(GitSession session, string baseRef, Action onChanged, Action<string> onError)
        {
            var name = PromptDialog.Show(I18n.L(I18n.Keys.MenuNewBranch),
                I18n.L(I18n.Keys.BranchCtxNewFrom, baseRef), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            RunOp(session, () => session.NewBranch(name.Trim(), baseRef), onChanged, onError);
        }

        private static void PromptNewTag(GitSession session, Action onChanged, Action<string> onError)
        {
            var name = PromptDialog.Show(I18n.L(I18n.Keys.MenuCreateTag),
                I18n.L(I18n.Keys.CreateTagPrompt), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var head = session.LoadRefs().FirstOrDefault(r => r.Type == GitSession.RefType.Head)?.CommitId;
            if (string.IsNullOrEmpty(head)) return;
            RunOp(session, () => session.CreateTag(name.Trim(), head, name.Trim()), onChanged, onError);
        }
    }
}