using System.Collections.Generic;
using System.Threading;
using Unity.Editor.Tasks;
using Unity.VersionControl.Git;
using Unity.VersionControl.Git.Tasks;

namespace KF.GitUI
{
    /// <summary>
    /// git remote -v 任务（自研，非子树内容）：解析 remote 定义。
    /// 上游 GitRemoteListTask 的 RemoteListOutputProcessor 对本地文件路径（无 @ 的 E:\... 形式）会走 SSH
    /// 分支并在无 @ 时 ReadUntil 返回 null → NullReferenceException（M3 人工测试实测）——
    /// 故自研容错解析：name \t url (fetch|push)，本地路径/https/ssh 一视同仁。
    /// </summary>
    public sealed class GitRemoteListTaskEx : GitProcessTask<string>
    {
        public GitRemoteListTaskEx(IPlatform platform, CancellationToken token = default)
            : base(platform, "remote -v", new StringOutputProcessor(), token)
        {
            Name = "git remote -v";
        }

        /// <summary>容错解析 remote -v 输出：行 = "name\turl (fetch|push)"；同 name 多行合并 fetch/push。</summary>
        public static List<GitRemote> Parse(string output)
        {
            var result = new List<GitRemote>();
            if (string.IsNullOrEmpty(output)) return result;

            var byName = new Dictionary<string, (string url, bool fetch, bool push)>();
            var order = new List<string>();

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var tab = line.IndexOf('\t');
                string name, rest;
                if (tab >= 0) { name = line.Substring(0, tab).Trim(); rest = line.Substring(tab + 1).Trim(); }
                else
                {
                    var sp = line.IndexOf(' ');
                    if (sp <= 0) continue;
                    name = line.Substring(0, sp).Trim();
                    rest = line.Substring(sp + 1).Trim();
                }
                if (name.Length == 0 || rest.Length == 0) continue;

                // rest = "url (fetch)" 或 "url (push)"
                var paren = rest.IndexOf('(');
                var url = (paren >= 0 ? rest.Substring(0, paren) : rest).Trim();
                var mode = paren >= 0 ? rest.Substring(paren + 1).TrimEnd(')').Trim().ToUpperInvariant() : "";

                if (!byName.TryGetValue(name, out var e))
                {
                    e = (url, false, false);
                    byName[name] = e;
                    order.Add(name);
                }
                if (mode == "FETCH") e.fetch = true;
                else if (mode == "PUSH") e.push = true;
                byName[name] = e;
            }

            foreach (var name in order)
            {
                var (url, fetch, push) = byName[name];
                var fn = fetch && push ? GitRemoteFunction.Both
                    : fetch ? GitRemoteFunction.Fetch
                    : push ? GitRemoteFunction.Push : GitRemoteFunction.Unknown;
                result.Add(new GitRemote(name, url) { function = fn });
            }
            return result;
        }
    }
}