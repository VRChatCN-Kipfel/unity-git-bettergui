using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace KF.GitUI
{
    /// <summary>
    /// 右键动作（对齐 JetBrains 动作骨架语义：静态声明 + 弹出时 Visible/Enabled/Checked 动态过滤）。
    /// Visible=false → 不显示；Enabled=false → 灰显；Checked → 勾选标记。
    /// </summary>
    public interface IGitContextAction
    {
        string Id { get; }
        string Text { get; }
        bool Visible { get; }
        bool Enabled { get; }
        bool Checked { get; }
        void Run();
    }

    /// <summary>基础实现：Visible/Enabled/Checked 可写（用于动态菜单状态）。</summary>
    public abstract class GitContextAction : IGitContextAction
    {
        public abstract string Id { get; }
        public abstract string Text { get; }
        private bool visible = true;
        private bool enabled = true;
        private bool checkedFlag;
        public virtual bool Visible { get => visible; set => visible = value; }
        public virtual bool Enabled { get => enabled; set => enabled = value; }
        public virtual bool Checked { get => checkedFlag; set => checkedFlag = value; }
        public abstract void Run();
    }

    /// <summary>便捷实现：委托式动作。</summary>
    public sealed class DelegateAction : GitContextAction
    {
        private readonly Action runAction;
        public DelegateAction(string id, string text, Action run)
        {
            Id = id;
            Text = text;
            runAction = run;
        }
        public override string Id { get; }
        public override string Text { get; }
        public override void Run() => runAction?.Invoke();
    }

    /// <summary>分隔线（放在动作列表任意位置）。</summary>
    public sealed class GitContextSeparator : GitContextAction
    {
        public static readonly GitContextSeparator Instance = new GitContextSeparator();
        private GitContextSeparator() { }
        public override string Id => "---";
        public override string Text => null;
        public override void Run() { }
    }

    /// <summary>
    /// 右键菜单桥（Unity 2022.3 官方 ContextualMenuManipulator + DropdownMenu）：
    /// 每次弹出实时求值 provider（JetBrains update() 语义）。
    /// </summary>
    public static class GitContextMenu
    {
        /// <summary>给元素挂右键菜单；provider 每次弹出时调用（可含"先选中目标再返回动作"逻辑）。</summary>
        public static void Attach(VisualElement element, Func<IEnumerable<IGitContextAction>> provider)
        {
            element.AddManipulator(new ContextualMenuManipulator(evt => PopulateMenu(evt.menu, provider())));
        }

        /// <summary>过滤（供冒烟断言与菜单填充共用）：null 与 Visible=false 剔除，分隔线保留。</summary>
        public static List<IGitContextAction> Filter(IEnumerable<IGitContextAction> actions)
        {
            var result = new List<IGitContextAction>();
            if (actions == null) return result;
            foreach (var a in actions)
                if (a != null && (a is GitContextSeparator || a.Visible))
                    result.Add(a);
            return result;
        }

        /// <summary>把动作列表填进菜单（右键弹出与顶部下拉共用同一填充逻辑）。</summary>
        public static void PopulateMenu(DropdownMenu menu, IEnumerable<IGitContextAction> actions)
        {
            foreach (var a in Filter(actions))
            {
                if (a is GitContextSeparator)
                {
                    menu.AppendSeparator();
                    continue;
                }
                var action = a;
                menu.AppendAction(action.Text, _ => action.Run(), _ =>
                {
                    if (action.Checked) return DropdownMenuAction.Status.Checked;
                    return action.Enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
                });
            }
        }
    }
}