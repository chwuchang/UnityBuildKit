// BuildKit —— 配置读取
//   <项目>/BuildKit/buildkit.json          项目配置，提交 Git；支持 profiles（dev / release ...）
//   <项目>/BuildKit/secrets/secrets.json   密码和密钥，不提交；同名环境变量优先（方便接 CI）
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BuildKit
{
    // 注意：字段名就是 JSON 里的 key。改名要同步改文档、模板和 Fastfile。

    [Serializable] public class KV { public string key; public string value; }

    [Serializable]
    public class GitCfg
    {
        public bool requireClean = false;    // 工作区有未提交改动时禁止打包（release 推荐打开）
        public bool tagOnUpload = false;     // 上传商店成功后打 git tag：v{version}-{build}
        public bool pushTag = false;         // tag 同时 push 到 origin
    }

    [Serializable]
    public class AndroidCfg
    {
        public string packageName;
        public string format = "aab";            // aab（上 Google Play）或 apk（本机测试）
        public bool il2cpp = true;
        public string architectures = "ARM64";   // 逗号分隔：ARM64,ARMv7,X86_64
        public int minSdk = 0;                   // 0 = 用 Unity 设置
        public int targetSdk = 0;                // 0 = 用 Unity 设置（Automatic = 本机装的最高版本）
        public int playMinTargetSdk = 36;        // Google Play 当前要求的最低 targetSdk（2026-08-31 起为 36），随政策更新
        public string keystoreFile = "secrets/release.keystore";
        public string keyAlias = "release";
        public bool uploadToPlay = true;
        public string playTrack = "internal";
        public string playReleaseStatus = "draft";
    }

    [Serializable]
    public class IOSCfg
    {
        public string bundleId;
        public string teamId;
        public string minIOS = "13.0";
        public string minXcode = "26.0";         // App Store 当前要求的最低 Xcode（2026-04-28 起为 26），随政策更新
        public string exportMethod = "app-store"; // app-store（传 TestFlight）/ ad-hoc / development（装到自己设备上测试）
        public bool pushNotifications = false;
        public bool signInWithApple = false;
        public bool usesNonExemptEncryption = false;
        public KV[] privacy;
        public bool uploadToTestFlight = true;
    }

    [Serializable]
    public class NotifyCfg
    {
        public bool onSuccess = true;   // webhook 地址在 secrets.json 的 notifyWebhook
        public bool onFailure = true;
    }

    [Serializable]
    public class HooksCfg
    {
        public string postBuild = "";    // 每个平台构建成功后执行的 shell 命令（Fastfile 执行）
        public string postUpload = "";   // 每个平台上传成功后执行的 shell 命令
    }

    [Serializable]
    public class Config
    {
        public string productName;
        public string companyName;
        public string version = "1.0.0";
        public string[] scenes;                  // 留空 = 用 Build Settings 里勾选的场景
        public string outputDir = "Build";
        public bool development = false;         // Development Build
        public string[] defines;                 // 额外的脚本宏，只作用于本次打包，不改 Player Settings
        public bool embedBuildInfo = true;       // 把版本/提交号等写进包里，运行时用 BuildKitInfo 读取
        public GitCfg git = new GitCfg();
        public AndroidCfg android = new AndroidCfg();
        public IOSCfg ios = new IOSCfg();
        public NotifyCfg notify = new NotifyCfg();
        public HooksCfg hooks = new HooksCfg();
        public string defaultProfile = "";
        // "profiles" 由 ConfigLoader 处理，不在这里

        /// <summary>实际使用的 profile 名（没有 profile 时为空）。</summary>
        [NonSerialized] public string Profile = "";

        /// <summary>读取配置时发现的问题（如拼错的字段名），自检时会显示。</summary>
        [NonSerialized] public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public class Secrets
    {
        public string keystorePassword;
        public string keyAliasPassword;
        public string ascKeyId;
        public string ascIssuerId;
        public string ascKeyFile;
        public string playJsonKeyFile;
        public string unityPath;
        public string androidWorkspace;
        public string iosWorkspace;
        public string keychainPassword;
        public string notifyWebhook;
    }

    public class BuildKitException : Exception
    {
        public BuildKitException(string msg) : base(msg) { }
    }

    public static class Paths
    {
        public const string PackageName = "com.chwuchang.buildkit";

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string KitDir => Path.Combine(ProjectRoot, "BuildKit");
        public static string ConfigFile => Path.Combine(KitDir, "buildkit.json");
        public static string SecretsFile => Path.Combine(KitDir, "secrets", "secrets.json");
        public static string KitVersionFile => Path.Combine(KitDir, ".kit-version");

        /// <summary>相对路径一律相对 BuildKit 文件夹解析，这样换电脑不用改路径。</summary>
        public static string Kit(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return "";
            return Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(KitDir, rel));
        }

        /// <summary>命令行 -bkOutputDir：在独立打包目录里构建时，把产物放回主项目的输出目录。</summary>
        public static string OutputOverride;

        public static string Output(Config cfg)
        {
            if (!string.IsNullOrEmpty(OutputOverride)) return Path.GetFullPath(OutputOverride);
            var d = string.IsNullOrEmpty(cfg.outputDir) ? "Build" : cfg.outputDir;
            return Path.IsPathRooted(d) ? d : Path.GetFullPath(Path.Combine(ProjectRoot, d));
        }
    }

    public static class ConfigLoader
    {
        public static Config LoadConfig(string profile = null)
        {
            if (!File.Exists(Paths.ConfigFile))
                throw new BuildKitException("找不到配置文件：" + Paths.ConfigFile + "\n请先点菜单 BuildKit/初始化项目");
            if (string.IsNullOrEmpty(profile)) profile = Environment.GetEnvironmentVariable("BK_PROFILE");
            var cfg = Resolve(File.ReadAllText(Paths.ConfigFile), profile);

            var fmt = Environment.GetEnvironmentVariable("BK_FORMAT_OVERRIDE");   // 临时出 apk 测试用
            if (!string.IsNullOrEmpty(fmt)) { cfg.android.format = fmt; cfg.android.uploadToPlay = false; }
            return cfg;
        }

        /// <summary>buildkit.json 里定义的所有 profile 名。</summary>
        public static string[] ProfileNames()
        {
            try
            {
                if (!File.Exists(Paths.ConfigFile)) return new string[0];
                var root = MiniJson.Parse(File.ReadAllText(Paths.ConfigFile)) as Dictionary<string, object>;
                return root != null && root.TryGetValue("profiles", out var p) && p is Dictionary<string, object> d
                    ? d.Keys.ToArray() : new string[0];
            }
            catch { return new string[0]; }
        }

        /// <summary>
        /// 解析配置并合并 profile：先取顶层设置，再用 profiles[name] 覆盖。
        /// 对象逐字段深度合并；数组和普通值整体替换（例如 profile 里的 defines 会替换掉顶层的 defines）。
        /// Fastfile 里的 resolve_config 是同样的规则，改这里要同步改那边。
        /// </summary>
        public static Config Resolve(string json, string profile)
        {
            object rootObj;
            try { rootObj = MiniJson.Parse(json); }
            catch (MiniJson.ParseException e) { throw new BuildKitException("buildkit.json 格式错误，" + e.Message); }
            if (!(rootObj is Dictionary<string, object> root)) throw new BuildKitException("buildkit.json 最外层必须是 { } 对象");

            var errors = new List<string>();
            var warnings = new List<string>();

            Dictionary<string, object> profiles = null;
            if (root.TryGetValue("profiles", out var pObj))
            {
                profiles = pObj as Dictionary<string, object>;
                if (profiles == null) errors.Add("profiles 必须是 { } 对象");
            }

            var baseDict = new Dictionary<string, object>(root);
            baseDict.Remove("profiles");
            Validate(baseDict, typeof(Config), "", errors, warnings);
            if (profiles != null)
                foreach (var kv in profiles)
                {
                    if (kv.Value is Dictionary<string, object> pd)
                    {
                        if (pd.ContainsKey("profiles") || pd.ContainsKey("defaultProfile"))
                            errors.Add($"profiles.{kv.Key} 里不能再写 profiles / defaultProfile");
                        Validate(pd, typeof(Config), $"profiles.{kv.Key}.", errors, warnings);
                    }
                    else errors.Add($"profiles.{kv.Key} 必须是 {{ }} 对象");
                }

            var name = !string.IsNullOrEmpty(profile) ? profile
                : root.TryGetValue("defaultProfile", out var dp) ? dp as string : null;

            var merged = baseDict;
            if (!string.IsNullOrEmpty(name))
            {
                if (profiles == null || !profiles.TryGetValue(name, out var chosen) || !(chosen is Dictionary<string, object> chosenDict))
                {
                    var avail = profiles == null || profiles.Count == 0 ? "（没有定义任何 profile）" : string.Join(" / ", profiles.Keys);
                    errors.Add($"没有名为「{name}」的 profile，可用：{avail}");
                }
                else merged = DeepMerge(baseDict, chosenDict);
            }

            if (errors.Count > 0)
                throw new BuildKitException("buildkit.json 有错误：\n- " + string.Join("\n- ", errors));

            var cfg = JsonUtility.FromJson<Config>(MiniJson.Serialize(merged));
            if (cfg.git == null) cfg.git = new GitCfg();
            if (cfg.android == null) cfg.android = new AndroidCfg();
            if (cfg.ios == null) cfg.ios = new IOSCfg();
            if (cfg.notify == null) cfg.notify = new NotifyCfg();
            if (cfg.hooks == null) cfg.hooks = new HooksCfg();
            cfg.Profile = name ?? "";
            cfg.Warnings = warnings;
            return cfg;
        }

        public static Dictionary<string, object> DeepMerge(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            var r = new Dictionary<string, object>(a);
            foreach (var kv in b)
            {
                if (r.TryGetValue(kv.Key, out var old) && old is Dictionary<string, object> oa && kv.Value is Dictionary<string, object> ob)
                    r[kv.Key] = DeepMerge(oa, ob);
                else
                    r[kv.Key] = kv.Value;
            }
            return r;
        }

        // ───────────── 字段校验 ─────────────
        // JsonUtility 遇到拼错的字段名、类型不对的值都会悄悄用默认值，这里提前查出来。

        static void Validate(Dictionary<string, object> obj, Type type, string prefix, List<string> errors, List<string> warnings)
        {
            var fields = SerializedFields(type);
            foreach (var kv in obj)
            {
                var path = prefix + kv.Key;
                if (type == typeof(Config) && kv.Key == "profiles") continue;
                if (!fields.TryGetValue(kv.Key, out var f))
                {
                    var hint = Closest(kv.Key, fields.Keys);
                    warnings.Add($"未知字段 {path}，会被忽略" + (hint != null ? $"（是不是想写 {prefix}{hint}？）" : ""));
                    continue;
                }
                CheckValue(kv.Value, f.FieldType, path, errors, warnings);
            }
        }

        static void CheckValue(object v, Type t, string path, List<string> errors, List<string> warnings)
        {
            if (v == null)
            {
                if (t.IsValueType) errors.Add($"{path} 不能为 null");
                return;
            }
            if (t == typeof(string)) { if (!(v is string)) errors.Add($"{path} 应为字符串（用双引号括起来）"); return; }
            if (t == typeof(bool)) { if (!(v is bool)) errors.Add($"{path} 应为 true 或 false（不要加引号）"); return; }
            if (t == typeof(int)) { if (!(v is long)) errors.Add($"{path} 应为整数（不要加引号）"); return; }
            if (t.IsArray)
            {
                if (!(v is List<object> list)) { errors.Add($"{path} 应为数组 [ ]"); return; }
                for (int i = 0; i < list.Count; i++) CheckValue(list[i], t.GetElementType(), $"{path}[{i}]", errors, warnings);
                return;
            }
            if (t.IsClass)
            {
                if (v is Dictionary<string, object> d) Validate(d, t, path + ".", errors, warnings);
                else errors.Add($"{path} 应为对象 {{ }}");
            }
        }

        static Dictionary<string, FieldInfo> SerializedFields(Type t) =>
            t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsNotSerialized)
                .ToDictionary(f => f.Name, f => f);

        /// <summary>编辑距离 ≤ 2 的最接近字段名，用于"是不是拼错了"的提示。</summary>
        static string Closest(string key, IEnumerable<string> candidates)
        {
            string best = null;
            int bestD = 3;
            foreach (var c in candidates)
            {
                int d = Distance(key.ToLowerInvariant(), c.ToLowerInvariant());
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        static int Distance(string a, string b)
        {
            var dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return dp[a.Length, b.Length];
        }

        // ───────────── secrets ─────────────

        /// <summary>secrets.json 可缺省；同名环境变量优先（方便接 CI）。</summary>
        public static Secrets LoadSecrets()
        {
            Secrets s = null;
            if (File.Exists(Paths.SecretsFile))
            {
                try { s = JsonUtility.FromJson<Secrets>(File.ReadAllText(Paths.SecretsFile).TrimStart('﻿')); }
                catch (Exception e) { throw new BuildKitException("secrets.json 格式错误：" + e.Message); }
            }
            s = s ?? new Secrets();
            s.keystorePassword = Env("BK_KEYSTORE_PASSWORD", s.keystorePassword);
            s.keyAliasPassword = Env("BK_KEYALIAS_PASSWORD", s.keyAliasPassword);
            return s;
        }

        static string Env(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }
}
