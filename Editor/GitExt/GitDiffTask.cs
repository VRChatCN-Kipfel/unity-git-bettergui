using System.Collections.Generic;
using System.Text;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git diff 内容级任务（自研，非子树内容）：unified 全文通道。
    /// 供 DiffViewer / CompareWindow 内容级 / 3-way 冲突视图共用。
    /// 用法：
    ///   GitDiffTask.TwoRefs(platform, "main", "feature")          → git diff main feature
    ///   GitDiffTask.RefVsWorktree(platform, "HEAD")               → git diff HEAD
    ///   GitDiffTask.RefVsIndex(platform, "HEAD")                  → git diff --cached HEAD(ref 可为空=全部已暂存)
    /// 统一附加 --no-ext-diff（防外部 diff 驱动污染输出），unified 上下文行数可调。
    /// 路径限定：GitDiffTask 不做路径拼接，由调用方在 args 后追加 " -- path1 path2"（引号包裹，与 GitAddTask 同法）。
    /// </summary>
    public sealed class GitDiffTask : GitProcessTask<string>
    {
        private GitDiffTask(IPlatform platform, string arguments, CancellationToken token)
            : base(platform, "diff --no-ext-diff " + arguments, new StringOutputProcessor(), token)
        {
            Name = "git diff";
        }

        /// <summary>两个 ref 间全量 diff：git diff [--unified=N] a b</summary>
        public static GitDiffTask TwoRefs(IPlatform platform,
            string refA, string refB, int contextLines = 3,
            CancellationToken token = default)
        {
            return new GitDiffTask(platform, BuildUnified(contextLines) + " " + Quote(refA) + " " + Quote(refB), token);
        }

        /// <summary>ref vs 工作区：git diff [--unified=N] [ref]（ref 为空 = 全部未暂存+未跟踪内容受限场景）</summary>
        public static GitDiffTask RefVsWorktree(IPlatform platform,
            string reference, int contextLines = 3,
            CancellationToken token = default)
        {
            return new GitDiffTask(platform,
                BuildUnified(contextLines) + (string.IsNullOrEmpty(reference) ? "" : " " + Quote(reference)), token);
        }

        /// <summary>index vs ref / 已暂存内容：git diff [--unified=N] --cached [ref]</summary>
        public static GitDiffTask RefVsIndex(IPlatform platform,
            string reference, int contextLines = 3,
            CancellationToken token = default)
        {
            return new GitDiffTask(platform,
                "--cached " + BuildUnified(contextLines)
                + (string.IsNullOrEmpty(reference) ? "" : " " + Quote(reference)), token);
        }

        /// <summary>原始参数透传（内部使用，参数必须完全可信）。</summary>
        public static GitDiffTask Raw(IPlatform platform, string arguments,
            CancellationToken token = default)
        {
            return new GitDiffTask(platform, arguments, token);
        }

        private static string BuildUnified(int contextLines)
        {
            return contextLines == 0 ? "--unified=0" : "--unified=" + contextLines;
        }

        private static string Quote(string s)
        {
            // ref/commit id 不来自用户输入时不需要引号，但为稳妥统一包裹（无空格时等价）。
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>轻量路径拼接助手：把路径列表转成 "path1" "path2"（正斜杠/引号包裹，含空列表→空串）。</summary>
        public static string JoinPaths(IEnumerable<string> paths)
        {
            if (paths == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var p in paths)
                sb.Append(" \"").Append(p.Replace('\\', '/').Replace("\"", "\\\"")).Append('"');
            return sb.ToString();
        }
    }
}