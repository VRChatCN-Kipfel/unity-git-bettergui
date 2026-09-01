using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// 轻量模态文本输入弹窗（EditorUtility 无 InputDialog；JetBrains 风格）。
    /// 返回 null = 取消；返回字符串 = 确认（可为空串，调用方自行校验）。
    /// </summary>
    public static class PromptDialog
    {
        public static string Show(string title, string message, string defaultValue)
        {
            var w = ScriptableObject.CreateInstance<Window>();
            w.titleContent = new GUIContent(title);
            w.message = message;
            w.value = defaultValue ?? string.Empty;
            w.ShowModal();
            return w.confirmed ? w.value : null;
        }

        private sealed class Window : EditorWindow
        {
            public string message;
            public string value;
            public bool confirmed;

            private void OnGUI()
            {
                GUILayout.Space(6);
                GUILayout.Label(message, EditorStyles.wordWrappedLabel);
                value = GUILayout.TextField(value ?? string.Empty);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.L(I18n.Keys.DialogOk)))
                {
                    confirmed = true;
                    Close();
                }
                if (GUILayout.Button(I18n.L(I18n.Keys.DialogCancel)))
                    Close();
                GUILayout.EndHorizontal();
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    confirmed = true;
                    Close();
                }
            }
        }
    }
}