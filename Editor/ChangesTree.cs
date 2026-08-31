using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.VersionControl.Git;

namespace KF.GitUI
{
    /// <summary>改动树节点（目录 / 文件统一模型）。</summary>
    public sealed class ChangeItem
    {
        public string Path;          // 分组/显示路径（section 化时含 section 前缀）
        public string OpsPath;       // 真实仓库路径（右键/复制用；目录与 section 节点为 null）
        public bool IsDirectory;
        public string StatusText;    // 文件："M" / "AM" 等；目录：空串
        public bool IsStaged;        // 勾选显示（目录=子树全暂存）
        public GitStatusEntry? Entry; // 工作区原条目（右键/暂存操作使用；提交详情树为 null）
    }

    /// <summary>
    /// 改动树（UITK TreeView，用户拍板 2022.2+）：目录节点 + 文件叶子，目录在前、组内名序。
    /// ReadOnly = 提交详情树（无勾选）；Checkable = Commit 页勾选树（勾选=暂存语义，目录勾选递归子树）。
    /// 2022.3 实测：TreeView/BaseTreeView/TreeViewItemData 均在 UnityEngine.UIElements 程序集内。
    /// </summary>
    public sealed class ChangesTree : VisualElement
    {
        public enum Mode { ReadOnly, Checkable }

        private const float RowHeightPx = 20f;

        private readonly TreeView tree;
        private readonly Mode mode;
        private readonly Label hintLabel;

        /// <summary>Checkable：勾选/取消（dir=目录节点，调用方负责递归自身子树）。</summary>
        public event Action<ChangeItem, bool> ToggleChanged;

        /// <summary>双击/回车选中项。</summary>
        public event Action<ChangeItem> ItemChosen;

        /// <summary>单选变化。</summary>
        public event Action<ChangeItem> SelectionChanged;

        public ChangeItem SelectedItem { get; private set; }

        public ChangesTree(Mode mode)
        {
            this.mode = mode;
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            hintLabel = new Label();
            hintLabel.style.fontSize = 11f;
            hintLabel.style.color = new Color(0.62f, 0.62f, 0.62f);
            hintLabel.style.display = DisplayStyle.None;
            Add(hintLabel);

            tree = new TreeView();
            tree.fixedItemHeight = RowHeightPx;
            tree.selectionType = SelectionType.Single;
            tree.autoExpand = true;
            tree.makeItem = MakeRow;
            tree.bindItem = BindRow;
            tree.itemsChosen += OnItemsChosen;
            tree.selectedIndicesChanged += OnSelectionChanged;
            tree.style.flexGrow = 1f;
            Add(tree);
        }

        /// <summary>顶部提示（"loading…"/空态文案）；空串隐藏。</summary>
        public void SetHint(string text)
        {
            hintLabel.text = text ?? string.Empty;
            hintLabel.style.display = string.IsNullOrEmpty(hintLabel.text)
                ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>重建树（目录自动合成，目录在前、文件在后、组内名序）。</summary>
        public void SetFiles(List<ChangeItem> files)
        {
            tree.SetRootItems(Group(files ?? new List<ChangeItem>()));
            SelectedItem = null;
        }

        /// <summary>
        /// 分节重建（JetBrains "合并视图 + 每父分组"语义：每节 = 顶层目录节点，节内按真实目录分组；
        /// 子项 Path 带节前缀仅作显示分组，OpsPath 保留真实路径供操作）。空节跳过。
        /// </summary>
        public void SetFilesSectioned(List<(string Header, List<ChangeItem> Items)> sections)
        {
            tree.SetRootItems(BuildSectioned(sections));
            SelectedItem = null;
        }

        // ---- 数据构建（纯静态，冒烟可断言） ----

        public static char StatusChar(GitFileStatus s)
        {
            switch (s)
            {
                case GitFileStatus.Modified: return 'M';
                case GitFileStatus.Added: return 'A';
                case GitFileStatus.Deleted: return 'D';
                case GitFileStatus.Renamed: return 'R';
                case GitFileStatus.Copied: return 'C';
                case GitFileStatus.Unmerged: return 'U';
                case GitFileStatus.TypeChange: return 'T';
                case GitFileStatus.Unknown: return 'X';
                case GitFileStatus.Broken: return 'B';
                case GitFileStatus.Untracked: return '?';
                case GitFileStatus.Ignored: return '!';
                default: return ' ';
            }
        }

        /// <summary>工作区条目 → 节点（X/Y 双列状态，如 "M" / "AM"；引用原条目供操作）。</summary>
        public static ChangeItem FromEntry(GitStatusEntry e)
        {
            var x = StatusChar(e.IndexStatus);
            var y = StatusChar(e.WorkTreeStatus);
            var st = x != ' ' && y != ' ' ? x.ToString() + y
                : x != ' ' ? x.ToString() : y.ToString();
            return new ChangeItem
            {
                Path = e.path,
                OpsPath = e.path,
                StatusText = st,
                IsStaged = e.Staged,
                Entry = e,
            };
        }

        public static ChangeItem FromDiff(char status, string path)
        {
            return new ChangeItem { Path = path, OpsPath = path, StatusText = status.ToString() };
        }

        public static List<ChangeItem> BuildFromEntries(List<GitStatusEntry> entries)
        {
            return entries.Select(FromEntry).ToList();
        }

        public static List<ChangeItem> BuildFromDiffs(List<(char, string)> diffs)
        {
            return diffs.Select(d => FromDiff(d.Item1, d.Item2)).ToList();
        }

        /// <summary>id 持有器：局部函数无法捕获 ref 参数，用引用类型贯穿递归/多节共用的唯一 id 空间。</summary>
        private sealed class IdGen
        {
            public int Value;
        }

        /// <summary>目录分组（JetBrains 语义）：目录节点在前、文件在后，组内名序；目录 IsStaged=子树全暂存。</summary>
        public static List<TreeViewItemData<ChangeItem>> Group(IReadOnlyList<ChangeItem> files)
        {
            return Group(files, new IdGen());
        }

        /// <summary>
        /// 分节分组（merge 详情）：每节一个顶层目录节点（顺序=传入顺序），节内复用 Group 的目录分组；
        /// 子项 Path 带 "节/真实路径" 前缀（仅显示），OpsPath 为真实路径。
        /// 空节跳过；供冒烟断言与 SetFilesSectioned 共用。
        /// </summary>
        public static List<TreeViewItemData<ChangeItem>> BuildSectioned(
            List<(string Header, List<ChangeItem> Items)> sections)
        {
            var roots = new List<TreeViewItemData<ChangeItem>>();
            var id = new IdGen();
            foreach (var s in sections)
            {
                if (s.Items == null || s.Items.Count == 0) continue;
                var header = s.Header ?? string.Empty;
                var secItem = new ChangeItem { Path = header, IsDirectory = true };
                var children = Group(s.Items.Select(it => new ChangeItem
                {
                    Path = header + "/" + it.Path,
                    OpsPath = it.OpsPath ?? it.Path,
                    StatusText = it.StatusText,
                    IsStaged = it.IsStaged,
                }).ToList(), id, header);
                roots.Add(new TreeViewItemData<ChangeItem>(id.Value++, secItem, children));
            }
            return roots;
        }

        /// <summary>目录分组（id 由调用方持有，保证多节共用唯一 id 空间）。</summary>
        private static List<TreeViewItemData<ChangeItem>> Group(IReadOnlyList<ChangeItem> files, IdGen id,
            string virtualRoot = "")
        {
            var filesByDir = new Dictionary<string, List<ChangeItem>>(StringComparer.Ordinal);
            var dirSet = new HashSet<string>(StringComparer.Ordinal);

            foreach (var f in files)
            {
                var dir = ParentDir(f.Path);
                if (!filesByDir.TryGetValue(dir, out var list))
                    filesByDir[dir] = list = new List<ChangeItem>();
                list.Add(f);
                var d = dir;
                while (!string.IsNullOrEmpty(d))
                {
                    dirSet.Add(d);
                    d = ParentDir(d);
                }
            }

            var stagedCache = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool DirStaged(string dir)
            {
                if (stagedCache.TryGetValue(dir, out var v)) return v;
                if (filesByDir.TryGetValue(dir, out var direct))
                    foreach (var f in direct)
                        if (!f.IsStaged) { stagedCache[dir] = false; return false; }
                foreach (var sub in dirSet.Where(d => !string.IsNullOrEmpty(d) && ParentDir(d) == dir))
                    if (!DirStaged(sub)) { stagedCache[dir] = false; return false; }
                stagedCache[dir] = true;
                return true;
            }

            List<TreeViewItemData<ChangeItem>> Emit(string dir)
            {
                var children = new List<TreeViewItemData<ChangeItem>>();
                foreach (var sub in dirSet.Where(d => !string.IsNullOrEmpty(d) && ParentDir(d) == dir)
                             .OrderBy(d => d, StringComparer.Ordinal))
                {
                    var dirItem = new ChangeItem
                    {
                        Path = sub,
                        IsDirectory = true,
                        IsStaged = DirStaged(sub),
                    };
                    children.Add(new TreeViewItemData<ChangeItem>(id.Value++, dirItem, Emit(sub)));
                }
                if (filesByDir.TryGetValue(dir, out var directFiles))
                    foreach (var f in directFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
                        children.Add(new TreeViewItemData<ChangeItem>(id.Value++, f, null));
                return children;
            }

            return Emit(virtualRoot);
        }

        private static string ParentDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var i = path.LastIndexOf('/');
            return i < 0 ? "" : path.Substring(0, i);
        }

        private static string Segment(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var i = path.LastIndexOf('/');
            return i < 0 ? path : path.Substring(i + 1);
        }

        // ---- 行模板 / 绑定（TreeView 行虚拟化复用：userData 携带当前节点） ----

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeightPx;
            row.style.overflow = Overflow.Hidden;

            var toggle = new Toggle { name = "chk" };
            toggle.style.marginRight = 2f;
            toggle.RegisterValueChangedCallback(ev =>
            {
                if (row.userData is ChangeItem ci)
                    ToggleChanged?.Invoke(ci, ev.newValue);
            });
            row.Add(toggle);

            var chip = new Label { name = "chip" };
            chip.style.fontSize = 10f;
            chip.style.minWidth = 20f;
            chip.style.unityTextAlign = TextAnchor.MiddleCenter;
            chip.style.color = new Color(0.78f, 0.78f, 0.9f);
            chip.style.marginRight = 4f;
            row.Add(chip);

            var nameLabel = new Label { name = "name" };
            nameLabel.style.fontSize = 12f;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.flexGrow = 1f;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(nameLabel);

            return row;
        }

        private void BindRow(VisualElement el, int index)
        {
            var item = tree.GetItemDataForIndex<ChangeItem>(index);
            el.userData = item;
            if (item == null) return;

            var chip = el.Q<Label>("chip");
            var name = el.Q<Label>("name");
            var toggle = el.Q<Toggle>("chk");

            chip.text = item.StatusText;
            chip.style.visibility = string.IsNullOrEmpty(item.StatusText)
                ? Visibility.Hidden : Visibility.Visible;

            name.text = Segment(item.Path);
            name.tooltip = item.Path;

            if (mode == Mode.Checkable)
            {
                toggle.SetValueWithoutNotify(item.IsStaged);
                toggle.style.display = DisplayStyle.Flex;
            }
            else
            {
                toggle.style.display = DisplayStyle.None;
            }
        }

        private void OnItemsChosen(IEnumerable<object> objs)
        {
            foreach (var o in objs)
            {
                if (o is ChangeItem ci)
                {
                    ItemChosen?.Invoke(ci);
                    break;
                }
            }
        }

        private void OnSelectionChanged(IEnumerable<int> indices)
        {
            ChangeItem sel = null;
            foreach (var i in indices)
            {
                sel = tree.GetItemDataForIndex<ChangeItem>(i);
                if (sel != null) break;
            }
            SelectedItem = sel;
            if (sel != null) SelectionChanged?.Invoke(sel);
        }
    }
}