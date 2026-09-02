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
    ///   · 折叠长文件：DiffRows 构建时已把超限内容折叠（见 BuildOf 折叠参数）
    /// 打开入口：DiffViewer.Open(session, title, files, rows) —— 由调用方先算好数据（后台线程），
    /// 窗口只做同步渲染（避免窗口内跑 git 卡 UI；M2 CompareWindow 同步跑任务是被容忍的旧例，本视图从设计上规避）。
    /// </summary>
    public sealed class DiffViewer : EditorWindow
    {
        private List<DiffRow> rows = new List<DiffRow>();
        private string windowTitle = string.Empty;
        private ScrollView scrollView;

        public static void Open(string title, List<DiffRow> rows)
        {
            var w = GetWindow<DiffViewer>(true, title);
            w.windowTitle = title;
            w.rows = rows ?? new List<DiffRow>();
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
                scrollView.Add(BuildRowElement(row));
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