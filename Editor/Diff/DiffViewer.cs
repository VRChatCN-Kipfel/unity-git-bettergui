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
        private Action onWorktreeMutated; // 工作区被 hunk 操作改动后的回调（主窗口刷新删除树等）

        /// <summary>纯查看（两 ref 比较 / 提交详情）：无 hunk 操作。</summary>
        public static void Open(string title, List<DiffRow> rows)
        {
            Open(null, title, null, rows, false);
        }

        /// <summary>工作区视图（HEAD vs 工作区）：支持 hunk 级 Stage/Revert。</summary>
        public static void Open(GitSession session, string title, string diffOutput,
            List<DiffRow> rows, bool worktreeMode)
        {
            Open(session, title, diffOutput, rows, worktreeMode, null);
        }

        /// <summary>工作区视图 + 操作成功回调（hunk stage/revert 后主窗口同步刷新工作区树）。</summary>
        public static void Open(GitSession session, string title, string diffOutput,
            List<DiffRow> rows, bool worktreeMode, Action onWorktreeMutated)
        {
            // 每次 CreateInstance 新窗口：避免同 title 复用旧实例导致数据/状态残留（M3 人工测试：二次双击空白）
            var w = CreateInstance<DiffViewer>();
            // titleContent：窗口标题栏（CreateInstance 默认是类名，必须显式设；GetWindow 时代靠 GetWindow(title) 自动带）
            w.titleContent = new GUIContent(title);
            w.windowTitle = title;
            w.rows = rows ?? new List<DiffRow>();
            w.session = session;
            w.diffOutput = diffOutput;
            w.worktreeMode = worktreeMode;
            w.onWorktreeMutated = onWorktreeMutated;
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

        /// <summary>重建内容（Open 赋值后调用；OnEnable 首建时也走）。scrollView 未建时跳过（等 OnEnable）。</summary>
        public void RebuildRows()
        {
            if (scrollView == null) return;
            scrollView.Clear();

            if (rows == null || rows.Count == 0)
            {
                // 空 diff（文件无变更/已提交）→ 提示而非纯空白（M3 人工测试：双击已提交文件会得到空 rows）
                var empty = new Label(I18n.L(I18n.Keys.DiffNoChanges));
                empty.style.paddingLeft = 6;
                empty.style.paddingTop = 6;
                empty.style.color = new Color(0.55f, 0.55f, 0.6f, 1f);
                scrollView.Add(empty);
                return;
            }

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
                        BuildHunkActions(sessionCopy, diffCopy, fileIndex, hunkIndex,
                            () => { RefreshData(); onWorktreeMutated?.Invoke(); }, ShowError));
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
        /// 视图语义 = HEAD vs 工作区（未暂存改动）→ 无 Unstage（Unstage 属于 --cached 视图，待后续）。
        /// 无上下文 hunk（整文件重写，git apply 无法定位）→ 禁用并提示（实测 lb1 2→10 行场景）。</summary>
        public static IEnumerable<IGitContextAction> BuildHunkActions(GitSession session,
            string diffOutput, int fileIndex, int hunkIndex, Action onMutated, Action<string> onError)
        {
            if (session == null) return new List<IGitContextAction>();

            var usable = GitPatchBuilder.HunkHasContext(diffOutput, fileIndex, hunkIndex);
            var actions = new List<IGitContextAction>
            {
                new DelegateAction("hunk.stage", I18n.L(I18n.Keys.DiffStageHunk),
                    () => RunHunk(session, diffOutput, fileIndex, hunkIndex, GitApplyMode.Stage, onMutated, onError))
                {
                    Enabled = usable,
                },
                new DelegateAction("hunk.revert", I18n.L(I18n.Keys.DiffRevertHunk),
                    () => RunHunk(session, diffOutput, fileIndex, hunkIndex, GitApplyMode.Revert, onMutated, onError))
                {
                    Enabled = usable,
                },
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

            var sign = new Label(BuildSign(row));
            sign.style.fontSize = 12;
            sign.style.color = BuildSignColor(row);
            sign.style.paddingRight = 4;
            sign.style.flexShrink = 0;

            switch (row.Kind)
            {
                case DiffRowKind.FileHeader:
                case DiffRowKind.HunkHeader:
                    line.style.backgroundColor = new Color(0.55f, 0.55f, 0.55f, 0.15f);
                    break;
                case DiffRowKind.Old:
                    line.style.backgroundColor = new Color(0.85f, 0.30f, 0.30f, 0.14f);
                    break;
                case DiffRowKind.New:
                    line.style.backgroundColor = new Color(0.30f, 0.80f, 0.35f, 0.12f);
                    break;
                case DiffRowKind.Binary:
                    text.style.color = new Color(0.7f, 0.5f, 0.1f, 1f);
                    text.style.unityFontStyleAndWeight = FontStyle.Italic;
                    break;
                case DiffRowKind.Fold:
                    line.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.08f);
                    text.style.unityFontStyleAndWeight = FontStyle.Italic;
                    text.style.color = new Color(0.5f, 0.5f, 0.6f, 1f);
                    break;
            }

            line.Add(gutter);
            line.Add(sign);
            line.Add(text);
            return line;
        }

        /// <summary>行首符号：删除 - / 新增 + / 其它空（JetBrains 直觉；用户反馈"只需行首标 -"）。</summary>
        private static string BuildSign(DiffRow row)
        {
            switch (row.Kind)
            {
                case DiffRowKind.Old: return "-";
                case DiffRowKind.New: return "+";
                default: return "";
            }
        }

        private static Color BuildSignColor(DiffRow row)
        {
            switch (row.Kind)
            {
                case DiffRowKind.Old: return new Color(0.82f, 0.25f, 0.25f);
                case DiffRowKind.New: return new Color(0.20f, 0.62f, 0.28f);
                default: return new Color(0.45f, 0.45f, 0.45f);
            }
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