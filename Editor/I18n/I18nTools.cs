using System;
using UnityEditor;
using UnityEngine;

namespace KF.GitUI
{
    /// <summary>
    /// 无界面入口（批处理 / CI / 纯命令行工作流）：导出语言骨架。
    ///
    ///   Unity -batchmode -nographics -projectPath &lt;宿主项目&gt; \
    ///         -executeMethod KF.GitUI.I18nTools.ExportSkeleton -lang zh-CN
    ///
    /// 可选 <c>-skeletonDir &lt;目录&gt;</c>，默认写项目内的 .gitui-i18n/。
    /// 退出码：0 = 成功；1 = 失败（原因打到日志）。
    ///
    /// 为什么需要它：语言窗口里的"导出骨架"按钮对交互用户够用，但贡献者在 CI 或
    /// 纯命令行环境里无法调用——这是把简体中文当作第一个真实贡献做压力测试时暴露的缺口。
    /// </summary>
    public static class I18nTools
    {
        public static void ExportSkeleton()
        {
            var args = Environment.GetCommandLineArgs();
            var lang = GetValue(args, "-lang");
            var dir = GetValue(args, "-skeletonDir");
            if (string.IsNullOrEmpty(dir)) dir = LanguagePackLibrary.ProjectSharedDirectory();

            if (string.IsNullOrEmpty(lang))
            {
                Debug.LogError("[i18n-tools] missing -lang <code>, e.g. -lang zh-CN");
                EditorApplication.Exit(1);
                return;
            }

            string packPath;
            string metaPath;
            string error;
            if (!LanguagePackLibrary.ExportSkeleton(lang, dir, out packPath, out metaPath, out error))
            {
                Debug.LogError("[i18n-tools] skeleton export failed: " + error);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[i18n-tools] skeleton written: {packPath} (keys={I18n.KeyCount})");
            Debug.Log($"[i18n-tools] meta template written: {metaPath}");
            EditorApplication.Exit(0);
        }

        private static string GetValue(string[] args, string name)
        {
            if (args == null) return null;
            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                return i + 1 < args.Length ? args[i + 1] : null;
            }
            return null;
        }
    }
}
