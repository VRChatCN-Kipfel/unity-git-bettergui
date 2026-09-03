using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KF.GitUI
{
    /// <summary>
    /// 3-way 冲突视图（M3 核心验收：带冲突解决的真实 merge；M3-SOLUTION §3.4/D3 独立窗口）。
    /// 布局：上 = 冲突文件横向条（可点击切换）；中 = Ours | Theirs 两栏文本（词级高亮 + 行级底色）；
    /// 下 = [Accept Ours] [Accept Theirs]（整侧接受 = checkout --ours/--theirs + git add）。
    /// rebase 冲突（AnalyzeRebaseState）时 Ours/Theirs 语义对调，标题栏给出提示（§1.3 isReverseRoot）。
    /// </summary>
    public sealed class Merge3Window : EditorWindow
    {
        private GitSession session;
        private List<string> conflictPaths = new List<string>();
        private string currentPath;
        private bool loading;
        private string error = string.Empty;
        private bool rebaseMode;
        private Label oursLabel;
        private Label theirsLabel;
        private Label headerLabel;
        private ListView fileList;
        private Dictionary<string, GitSession.ConflictBlobs> cache = new Dictionary<string, GitSession.ConflictBlobs>();
        private Action onResolved; // 冲突解决/中止后通知主窗口刷新（workingEntries 等）

        public static void Open(GitSession session)
        {
            Open(session, null);
        }

        public static void Open(GitSession session, Action onResolved)
        {
            if (session == null) return;
            var w = GetWindow<Merge3Window>(true, I18n.L(I18n.Keys.Merge3Title));
            w.session = session;
            w.onResolved = onResolved;
            w.Reload();
            w.Show();
        }

        private void Reload()
        {
            conflictPaths = new List<string>();
            currentPath = null;
            cache.Clear();
            try
            {
                var st = session.LoadStatus();
                rebaseMode = session.IsRebaseInProgressQuiet();
                conflictPaths = session.LoadConflictPaths();
            }
            catch (Exception ex) { error = ex.Message; }
            BuildUI();
            if (conflictPaths.Count > 0)
                SelectFile(0);
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            error = string.Empty;

            headerLabel = new Label();
            headerLabel.style.paddingTop = 4;
            headerLabel.style.paddingLeft = 6;
            rootVisualElement.Add(headerLabel);

            if (conflictPaths.Count == 0)
            {
                // 全部解决（或本就无冲突）：仍保留操作条——merge/rebase 完成或中止前按钮不消失（用户要求）
                rootVisualElement.Add(new Label(I18n.L(I18n.Keys.Merge3AllResolved)));
                rootVisualElement.Add(BuildOperationBar());
                return;
            }

            // 文件条
            fileList = new ListView(conflictPaths, 22, () => new Label(), (el, i) =>
            {
                ((Label)el).text = conflictPaths[i];
                ((Label)el).style.paddingLeft = 4;
            });
            fileList.style.maxHeight = 90;
            fileList.selectionChanged += items =>
            {
                var first = items.FirstOrDefault() as string;
                if (first != null) SelectFile(conflictPaths.IndexOf(first));
            };
            rootVisualElement.Add(fileList);

            // Ours | Theirs 两栏
            var split = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            oursLabel = new Label { name = "ours-label", enableRichText = true };
            theirsLabel = new Label { name = "theirs-label", enableRichText = true };
            oursLabel.style.paddingTop = 4;
            oursLabel.style.paddingLeft = 4;
            oursLabel.style.paddingRight = 4;
            theirsLabel.style.paddingTop = 4;
            theirsLabel.style.paddingLeft = 4;
            theirsLabel.style.paddingRight = 4;
            oursLabel.style.whiteSpace = WhiteSpace.Normal;
            theirsLabel.style.whiteSpace = WhiteSpace.Normal;
            var oursPane = WrapLabel("merge3-ours-pane", oursLabel, rebaseMode ? I18n.L(I18n.Keys.Merge3Theirs) : I18n.L(I18n.Keys.Merge3Yours));
            var theirsPane = WrapLabel("merge3-theirs-pane", theirsLabel, rebaseMode ? I18n.L(I18n.Keys.Merge3Yours) : I18n.L(I18n.Keys.Merge3Theirs));
            split.Add(oursPane);
            split.Add(theirsPane);
            rootVisualElement.Add(split);

            // 操作条（Accept/导航 + Abort/Continue；空态也保留以便反悔）
            rootVisualElement.Add(BuildOperationBar());
        }

        /// <summary>操作条：Accept Yours/Theirs + ‹› 导航 + Abort（merge/rebase 通吃）+ rebase 时 Continue。</summary>
        private VisualElement BuildOperationBar()
        {
            var ops = new VisualElement();
            ops.style.flexDirection = FlexDirection.Row;
            ops.style.paddingTop = 4;
            // 注意：rebase 时 Ours/Theirs 标签对调，按钮语义随标签（"Accept Ours" 操作对应当前侧）
            var acceptOurs = new Button(() => Accept(GitCheckoutSide.Ours)) { text = I18n.L(I18n.Keys.Merge3AcceptOurs) };
            var acceptTheirs = new Button(() => Accept(GitCheckoutSide.Theirs)) { text = I18n.L(I18n.Keys.Merge3AcceptTheirs) };
            var prev = new Button(() => Navigate(-1)) { text = "‹" };
            var next = new Button(() => Navigate(1)) { text = "›" };
            ops.Add(prev);
            ops.Add(next);
            ops.Add(acceptOurs);
            ops.Add(acceptTheirs);
            var spacer = new Label("    ");
            ops.Add(spacer);
            // M3 冲突流程控制：Abort（merge/rebase 通吃）+ rebase 冲突可 Continue
            var abort = new Button(AbortOperation) { text = I18n.L(I18n.Keys.MergeAbort) };
            ops.Add(abort);
            if (rebaseMode)
            {
                var cont = new Button(ContinueRebase) { text = I18n.L(I18n.Keys.RebaseContinue) };
                ops.Add(cont);
            }
            return ops;
        }

        private void AbortOperation()
        {
            if (session == null) return;
            if (!EditorUtility.DisplayDialog(I18n.L(I18n.Keys.MergeAbort),
                    I18n.L(I18n.Keys.MergeAbortConfirm), I18n.L(I18n.Keys.DialogOk), I18n.L(I18n.Keys.DialogCancel)))
                return;
            try
            {
                if (rebaseMode) session.RebaseAbort();
                else session.MergeAbort();
                onResolved?.Invoke();
                Close();
            }
            catch (Exception ex) { headerLabel.text = ex.Message; }
        }

        private void ContinueRebase()
        {
            if (session == null) return;
            try
            {
                session.RebaseContinue();
                onResolved?.Invoke();
                Close();
            }
            catch (Exception ex) { headerLabel.text = ex.Message; }
        }

        private static VisualElement WrapLabel(string name, Label label, string title)
        {
            var pane = new VisualElement();
            pane.name = name;
            var titleLabel = new Label(title);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.paddingLeft = 4;
            label.style.flexGrow = 1f;
            pane.Add(titleLabel);
            pane.Add(label);
            return pane;
        }

        private void SelectFile(int index)
        {
            if (index < 0 || index >= conflictPaths.Count) return;
            currentPath = conflictPaths[index];
            loading = true;
            headerLabel.text = string.Format(I18n.L(I18n.Keys.Merge3File), currentPath)
                + (rebaseMode ? "   " + I18n.L(I18n.Keys.Merge3RebaseSwapNote) : "");
            if (fileList != null) fileList.SetSelectionWithoutNotify(new[] { index });

            var me = this;
            var sessionCopy = session;
            var pathCopy = currentPath;
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var blobs = sessionCopy.LoadConflictBlobs(pathCopy, out _, out _);
                    ctx?.Post(_ => me.ShowBlobs(pathCopy, blobs), null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => { loading = false; error = ex.Message; headerLabel.text = error; }, null);
                }
            });
        }

        private void ShowBlobs(string path, GitSession.ConflictBlobs blobs)
        {
            if (path != currentPath) return;
            cache[path] = blobs;
            loading = false;
            if (oursLabel == null || theirsLabel == null) return;

            // 词级高亮：对 ours↔theirs 行做逐行配对（DiffRows 风格）；两侧文本 + 行级底色由 label 样式承担
            oursLabel.text = BuildSideText(blobs.Ours ?? string.Empty, blobs.Theirs ?? string.Empty, sideIsOurs: true);
            theirsLabel.text = BuildSideText(blobs.Ours ?? string.Empty, blobs.Theirs ?? string.Empty, sideIsOurs: false);
        }

        /// <summary>把一侧文本按"与对侧的词级差异"染成 rich text：ours 侧染删除、theirs 侧染新增。</summary>
        private static string BuildSideText(string ours, string theirs, bool sideIsOurs)
        {
            var linesO = SplitLines(ours);
            var linesT = SplitLines(theirs);
            var sb = new System.Text.StringBuilder();
            var count = Math.Max(linesO.Count, linesT.Count);
            for (var i = 0; i < count; i++)
            {
                var lo = i < linesO.Count ? linesO[i] : string.Empty;
                var lt = i < linesT.Count ? linesT[i] : string.Empty;
                if (sideIsOurs)
                {
                    if (lo == lt) sb.Append(DiffRichText.BuildPlainLine(lo));
                    else
                    {
                        var wr = WordDiff.Compare(lo, lt);
                        sb.Append(wr != null
                            ? DiffRichText.BuildDeletedLine(wr.OldFragments, lo)
                            : DiffRichText.WrapDeleted(lo));
                    }
                }
                else
                {
                    if (lo == lt) sb.Append(DiffRichText.BuildPlainLine(lt));
                    else
                    {
                        var wr = WordDiff.Compare(lo, lt);
                        sb.Append(wr != null
                            ? DiffRichText.BuildAddedLine(wr.NewFragments, lt)
                            : DiffRichText.WrapAdded(lt));
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static List<string> SplitLines(string text)
        {
            return new List<string>(text.Split(new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.None));
        }

        private void Navigate(int delta)
        {
            if (conflictPaths.Count == 0 || currentPath == null) return;
            var idx = conflictPaths.IndexOf(currentPath);
            SelectFile((idx + delta + conflictPaths.Count) % conflictPaths.Count);
        }

        private void Accept(GitCheckoutSide side)
        {
            if (session == null || string.IsNullOrEmpty(currentPath)) return;
            try
            {
                // rebase 模式标签已对调（BuildUI），这里按实际语义传递：rebase 时按钮文字侧=Merge3Yours 对应的
                // 实际操作仍是 checkout --ours/--theirs（git 语义），标签对调只影响展示文案。
                session.AcceptConflictSide(currentPath, side);
                // 重新加载：该文件已解决 → 从列表移除
                conflictPaths.Remove(currentPath);
                cache.Remove(currentPath);
                currentPath = null;
                if (conflictPaths.Count == 0)
                {
                    // 全部解决 → 提示继续 merge/rebase 或完成；通知主窗口刷新（勾选树+状态）
                    headerLabel.text = I18n.L(I18n.Keys.Merge3AllResolved);
                    oursLabel.text = "";
                    theirsLabel.text = "";
                    onResolved?.Invoke();
                    return;
                }
                SelectFile(0);
            }
            catch (Exception ex)
            {
                headerLabel.text = ex.Message;
            }
        }
    }
}