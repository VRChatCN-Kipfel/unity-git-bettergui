using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// Blame 窗口（M3 P2 文件历史/blame）：逐行显示 行号 | 提交短号 | 作者 | 内容。
    /// 数据：GitBlameTask（--porcelain）+ BlameParser；入口 = 提交详情/工作区文件树右键「Blame」。
    /// </summary>
    public sealed class BlameWindow : EditorWindow
    {
        private List<BlameLine> lines = new List<BlameLine>();
        private string pathText = string.Empty;
        private string error = string.Empty;
        private Vector2 scroll;

        public static void Open(GitSession session, string path)
        {
            if (session == null) return;
            var w = GetWindow<BlameWindow>(true, I18n.L(I18n.Keys.BlameTitle, path));
            w.pathText = path;
            w.Show();
            w.Load(session, path);
        }

        private void Load(GitSession session, string path)
        {
            error = string.Empty;
            var me = this;
            var ctx = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var result = session.Blame(path);
                    ctx?.Post(_ => { me.lines = result; me.Repaint(); }, null);
                }
                catch (Exception ex)
                {
                    ctx?.Post(_ => { me.error = ex.Message; me.Repaint(); }, null);
                }
            });
        }

        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var l in lines)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(l.LineNumber.ToString(), EditorStyles.miniLabel, GUILayout.Width(40));
                EditorGUILayout.LabelField(l.CommitShort, EditorStyles.miniLabel, GUILayout.Width(64));
                EditorGUILayout.LabelField(l.Author, EditorStyles.miniLabel, GUILayout.Width(110));
                EditorGUILayout.LabelField(l.Content, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}