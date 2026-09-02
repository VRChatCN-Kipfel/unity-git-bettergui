using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KF.GitUI
{
    /// <summary>
    /// 内容级 Diff 查看器（M3 核心 UI，D2 决策：unified 单栏）。
    /// 输入：行模型列表（DiffRows.Build 产物）+ 窗口标题。纯统一视图：
    ///   · 文件头/hunk 头 = 灰底粗体；上下文 = 等宽正常；Old = 浅红底；New = 浅绿底
    ///   · 行级背景之上叠加词级 rich text 高亮（DiffRichText 已注入标签）
    ///   · gutter 显示行号（旧/新）
    ///   · 折叠长文件：DiffRows 构建时已把超限内容折叠
    /// M3 hunk 操作：worktreeMode（HEAD vs 工作区）下 Old/New 行右键 =
    /// Stage / Revert 该 hunk（GitApplyTask；patch 切片经 GitPatchBuilder）。
    /// 打开入口：由调用方先算好数据（后台线程）——窗口只做同步渲染。
    /// </summary>
    public sealed class DiffViewer : EditorWindow
    {
        private List<DiffRow> rows = new List<DiffRow>();
        private string windowTitle = string.Empty;
        private ScrollView scrollView;
        private GitSession session;
        private string diffOutput;
        private bool worktreeMode;

        /// <summary>纯查看（两 ref 比较 / 提交详情）：无 hunk 操作。</summary>
        public static void Open(string title, List<DiffRow> rows)
        {
            Open(null, title, null, rows, false);
        }

        /// <summary>工作区视图（HEAD vs 工作区）：支持 hunk 级 Stage/Revert。</summary>
        public static void Open(GitSession session, string title, string diffOutput,
            List<DiffRow> rows, bool worktreeMode)
        {
            var w = GetWindow<DiffViewer>(true, title);
            w.windowTitle = title;
            w.rows = rows ?? new List<DiffRow>();
            w.session = session;
            w.diffOutput = diffOutput;
            w.worktreeMode = worktreeMode;
            w.Show();
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();

            var titleLabel = new Label(windowTitle);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.paddingTop = 4;
            titleLabel.style.paddingLeft = 6;
            rootVisualElement.Add(titleLabel);

            scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.name = "diff-scroll";
            RebuildRows();
            rootVisualElement.Add(scrollView);
        }

        private void RebuildRows()
        {
            if (scrollView == null) return;
            scrollView.Clear();

            foreach (var row in rows)
            {
                var el = BuildRowElement(row);
                // M3 hunk 操作：仅工作区视图 + 变更行（Old/New）可挂
                if (worktreeMode && session != null
                    && (row.Kind == DiffRowKind.Old || row.Kind == DiffRowKind.New))
                {
                    var fileIndex = row.FileIndex;
                    var hunkIndex = row.HunkIndex;
                    var sessionCopy = session;
                    var diffCopy = diffOutput;
                    GitContextMenu.Attach(el, () =>
                        BuildHunkActions(sessionCopy, diffCopy, fileIndex, hunkIndex, RefreshData, ShowError));
                }
                scrollView.Add(el);
            }
        }

        private void RefreshData()
        {
            try { EditorWindow.focusedWindow?.Repaint(); } catch { }
        }

        private static void ShowError(string msg)
        {
            Debug.LogWarning("[gitui] hunk op: " + msg);
        }

        /// <summary>hunk 级操作菜单（静态可测）：Stage / Revert 该 hunk。
        /// 视图语义 = HEAD vs 工作区（未暂存改动）→ 无 Unstage（Unstage 属于 --cached 视图，待后续）。</summary>
        public static IEnumerable<IGitContextAction> BuildHunkActions(GitSession session,
            string diffOutput, int fileIndex, int hunkIndex, Action onMutated, Action<string> onError)
        {
            if (session == null) return new List<IGitContextAction>();

            var actions = new List<IGitContextAction>
            {
                new DelegateAction("hunk.stage", I18n.L(I18n.Keys.DiffStageHunk),
                    () => RunHunk(session, diffOutput, fileIndex, hunkIndex, GitApplyMode.Stage, onMutated, onError)),
                new DelegateAction("hunk.revert", I18n.L(I18n.Keys.DiffRevertHunk),
                    () => RunHunk(session, diffOutput, fileIndex, hunkIndex, GitApplyMode.Revert, onMutated, onError)),
            };
            return actions;
        }

        private static void RunHunk(GitSession session, string diffOutput, int fileIndex,
            int hunkIndex, GitApplyMode mode, Action onMutated, Action<string> onError)
        {
            try
            {
                session.ApplyHunk(diffOutput, fileIndex, hunkIndex, mode);
                onMutated?.Invoke();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex.Message);
            }
        }

        /// <summary>单行元素：gutter 行号 + 文本 Label。静态以便冒烟可构造断言（不依赖窗口实例）。</summary>
        public static VisualElement BuildRowElement(DiffRow row)
        {
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.paddingLeft = 2;
            line.style.paddingRight = 2;

            var gutter = new Label(BuildGutter(row));
            gutter.style.unityFontStyleAndWeight = FontStyle.Bold;
            gutter.style.fontSize = 11;
            gutter.style.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            gutter.style.paddingRight = 8;
            gutter.style.unityTextAlign = TextAnchor.MiddleLeft;
            gutter.style.flexShrink = 0;

            var text = new Label(row.RichText);
            text.name = "diff-text";
            text.style.fontSize = 12;
            text.style.flexGrow = 1f;
            text.style.whiteSpace = WhiteSpace.Normal;

            switch (row.Kind)
            {
                case DiffRowKind.FileHeader:
                case DiffRowKind.HunkHeader:
                    text.style.backgroundColor = new Color(0.55f, 0.55f, 0.55f, 0.15f);
                    break;
                case DiffRowKind.Old:
                    text.style.backgroundColor = new Color(0.85f, 0.30f, 0.30f, 0.18f);
                    break;
                case DiffRowKind.New:
                    text.style.backgroundColor = new Color(0.30f, 0.80f, 0.35f, 0.16f);
                    break;
                case DiffRowKind.Binary:
                    text.style.color = new Color(0.7f, 0.5f, 0.1f, 1f);
                    text.style.unityFontStyleAndWeight = FontStyle.Italic;
                    break;
                case DiffRowKind.Fold:
                    text.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.08f);
                    text.style.unityFontStyleAndWeight = FontStyle.Italic;
                    text.style.color = new Color(0.5f, 0.5f, 0.6f, 1f);
                    break;
            }

            line.Add(gutter);
            line.Add(text);
            return line;
        }

        private static string BuildGutter(DiffRow row)
        {
            switch (row.Kind)
            {
                case DiffRowKind.Context:
                    return row.OldLineNo >= 0 ? row.OldLineNo.ToString() : "";
                case DiffRowKind.Old:
                    return row.OldLineNo >= 0 ? row.OldLineNo.ToString() : "";
                case DiffRowKind.New:
                    return row.NewLineNo >= 0 ? row.NewLineNo.ToString() : "";
                default:
                    return "";
            }
        }
    }
}