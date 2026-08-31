using System;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.IO;
using Unity.VersionControl.Git.Tasks;
using UnityEditor;

namespace KF.GitUI
{
    /// <summary>
    /// api → 真实 git 链路冒烟测试（复用 git-for-unity 官方装配，跳过 Dugite 下载）。
    /// 启动方式：
    ///   Unity -batchmode -nographics -projectPath X -executeMethod KF.GitUI.ApiSmokeTest.Run -quit
    /// 目的：
    ///   1. 验证内嵌 com.spoiledcat.git.api 可在 Unity 编辑器中加载、Platform 能初始化
    ///   2. 验证 FindSystemGit 探活能找到系统 git（零捆绑策略：git version 即可）
    ///   3. 验证 GitLogTask 真实跑通：解析出 commits（含 parents 列表，覆盖 #48 修复）
    /// </summary>
    public static class ApiSmokeTest
    {
        public static void Run()
        {
            var projectPath = Environment.CurrentDirectory;
            var extensionPath = projectPath; // 本冒烟环境 api 就在包内
            UnityEngine.Debug.Log($"[api-smoke] projectPath={projectPath}");

            try
            {
                // 1) 环境装配（官方 ApplicationEnvironment 实现 IGitEnvironment）
                var env = new ApplicationEnvironment("unity-git-bettergui");
                env.Initialize(extensionPath.ToSPath(), projectPath);

                // 2) 平台初始化（编辑器主线程 SynchronizationContext）
                var platform = new Platform(env);
                platform.Initialize(SynchronizationContext.Current);

                // 3) 探活：只找系统 git + 验版本，绝不触发 Dugite 下载
                var installer = new GitInstaller(platform);
                var state = installer.FindSystemGit(new GitInstaller.GitInstallationState());
                env.GitInstallationState = state;
                UnityEngine.Debug.Log($"[api-smoke] git={state.GitExecutablePath} valid={state.GitIsValid} version={state.GitVersion}");

                if (!state.GitIsValid)
                    throw new InvalidOperationException("system git not found or below " + Constants.MinimumGitVersion);

                if (!env.InitializeRepositoryIfNeeded(projectPath))
                    throw new InvalidOperationException("project is not a git repository: " + projectPath);

                // 4) 真实跑 git log（GitLogTask 用 %P 输出全部 parents，LogEntryOutputProcessor 是我们 patch 后的版本）
                var logTask = new GitLogTask(platform, new GitObjectFactory(platform.Environment), null, 0, CancellationToken.None)
                    .Configure(platform.ProcessManager);
                var entries = logTask.RunSynchronously();
                if (logTask.Successful && entries != null)
                {
                    UnityEngine.Debug.Log($"[api-smoke] GIT LOG OK: {entries.Count} commits parsed");
                    for (var i = 0; i < Math.Min(3, entries.Count); i++)
                    {
                        var e = entries[i];
                        UnityEngine.Debug.Log($"[api-smoke]   #{i} {e.ShortID} \"{e.Summary}\" parents=\"{string.Join(",", e.Parents)}\" files={e.Changes?.Count ?? 0}");
                    }
                }
                else
                {
                    throw new InvalidOperationException("git log failed: " + logTask?.Errors?[0]);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[api-smoke] FAILED: " + ex);
                EditorApplication.Exit(1);
                return;
            }

            UnityEngine.Debug.Log("[api-smoke] ALL OK");
            EditorApplication.Exit(0);
        }
    }

    // ---- 少量扩展：官方 InitializeRepository 的简化桥接 ----
    public static class ApiSmokeExtensions
    {
        public static bool InitializeRepositoryIfNeeded(this IGitEnvironment env, string projectPath)
        {
            env.InitializeRepository();
            return env.RepositoryPath.IsInitialized && env.RepositoryPath.Exists(".git");
        }
    }
}