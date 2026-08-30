using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// unity-git-bettergui 主窗口：三栏布局骨架。
    ///   左（图谱区占位） | 右：上（文件/变更树占位）+ 下（提交详情占位）
    /// 本阶段仅验证：UI Toolkit 嵌套 TwoPaneSplitView 布局 + asmdef 引用链（com.kf.gitui.editor -> com.spoiledcat.git）可编译。
    /// </summary>
    public class GitWindow : EditorWindow
    {
        /// <summary>编译链验证占位：引用 api 程序集内真实类型（不实例化，IPlatform 由后续注入）。</summary>
        private static readonly System.Type ApiProbe = typeof(GitVersionTask);

        [MenuItem("Window/Git/Git Better GUI (wip)")]
        public static void Open()
        {
            var w = GetWindow<GitWindow>();
            w.titleContent = new GUIContent("Git Better GUI");
            w.minSize = new Vector2(900, 500);
            w.Show();
        }

        /// <summary>
        /// 批处理冒烟测试：-executeMethod KF.GitUI.GitWindow.SmokeTest
        /// 验证三栏布局树 + api 引用链在无 GUI 环境下可用。
        /// </summary>
        public static void SmokeTest()
        {
            var root = new GitWindow().BuildLayout();
            var outer = root.Q<TwoPaneSplitView>("outer-split");
            var inner = root.Q<TwoPaneSplitView>("inner-split");
            if (outer == null) throw new System.Exception("SMOKE FAIL: outer split missing");
            if (inner == null) throw new System.Exception("SMOKE FAIL: inner split missing");
            if (outer.childCount != 2 || inner.childCount != 2)
                throw new System.Exception($"SMOKE FAIL: child counts {outer.childCount}/{inner.childCount}");
            if (ApiProbe == null) throw new System.Exception("SMOKE FAIL: api probe null");
            UnityEngine.Debug.Log($"[gitui] SMOKE OK: outer={outer.childCount} inner={inner.childCount} api={ApiProbe.FullName}");
            UnityEditor.EditorApplication.Exit(0);
        }

        private void OnEnable()
        {
            // Probe 防裁剪/防优化告警：任何有效平台上都不执行
            if (ApiProbe == null)
                Debug.LogWarning("[gitui] api probe failed");

            rootVisualElement.Clear();
            rootVisualElement.Add(BuildLayout());
        }

        private VisualElement BuildLayout()
        {
            // 外层：图谱（固定 320px）| 右侧整体（弹性）
            var outer = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Horizontal);
            outer.name = "outer-split";

            // 左侧图谱占位
            outer.Add(Pane("Graph (WIP)", "placeholder: commit graph"));
            // 右侧：内层 上下分割（文件树 200px + 详情）
            var inner = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Vertical);
            inner.name = "inner-split";
            inner.Add(Pane("Changes (WIP)", "placeholder: file tree"));
            inner.Add(Pane("Commit details (WIP)", "placeholder: full message\n\napi probe: " + ApiProbe?.FullName));
            outer.Add(inner);

            return outer;
        }

        private static VisualElement Pane(string title, string body)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            var t = new Label(title);
            t.style.unityFontStyleAndWeight = FontStyle.Bold;
            t.style.paddingBottom = 4;
            sv.Add(t);
            var b = new Label(body);
            b.style.whiteSpace = WhiteSpace.Normal;
            sv.Add(b);
            return sv;
        }
    }
}