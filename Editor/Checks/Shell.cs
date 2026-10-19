// BuildKit —— 调外部命令（git / keytool）的小工具
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace BuildKit
{
    public static class Shell
    {
        public struct Result
        {
            public bool Started;   // false = 命令不存在
            public int Code;
            public string Output;
            public bool Ok => Started && Code == 0;
        }

        public static Result Run(string file, IEnumerable<string> args, string workDir = null,
            IDictionary<string, string> env = null, int timeoutMs = 60000)
        {
            var psi = new ProcessStartInfo(file, string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir ?? Paths.ProjectRoot,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            try
            {
                using (var p = Process.Start(psi))
                {
                    var sb = new StringBuilder();
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { /* 已退出 */ }
                        return new Result { Started = true, Code = -1, Output = $"{file} 超时（{timeoutMs / 1000}s）" };
                    }
                    p.WaitForExit();   // 等异步输出读完
                    return new Result { Started = true, Code = p.ExitCode, Output = sb.ToString().Trim() };
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is FileNotFoundException)
            {
                return new Result { Started = false, Code = -1, Output = e.Message };
            }
        }

        static string Quote(string a)
        {
            if (string.IsNullOrEmpty(a)) return "\"\"";
            if (a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            // Windows 命令行规则：引号前的反斜杠要加倍，引号本身转义成 \"
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in a)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }

    /// <summary>项目的 git 信息。不是 git 仓库或没装 git 时，各项为空。</summary>
    public static class GitInfo
    {
        static Shell.Result Git(params string[] args) => Shell.Run("git", args, Paths.ProjectRoot, timeoutMs: 20000);

        public static bool IsRepo => Git("rev-parse", "--git-dir").Ok;

        public static string Commit
        {
            get { var r = Git("rev-parse", "--short", "HEAD"); return r.Ok ? r.Output : ""; }
        }

        /// <summary>独立打包目录里是 detached HEAD，分支名由外层脚本通过 BK_GIT_BRANCH 传进来。</summary>
        public static string Branch
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("BK_GIT_BRANCH");
                if (!string.IsNullOrEmpty(env)) return env;
                var r = Git("rev-parse", "--abbrev-ref", "HEAD");
                return r.Ok && r.Output != "HEAD" ? r.Output : "";
            }
        }

        /// <summary>未提交的改动（git status --porcelain 的每一行）；不是 git 仓库时返回空。</summary>
        public static string[] Changes
        {
            get
            {
                var r = Git("status", "--porcelain");
                return r.Ok ? r.Output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray()
                            : new string[0];
            }
        }
    }
}
