// BuildKit —— 把命令行脚本（fastlane / build.sh / build.ps1 ...）从插件拷进项目的 BuildKit/ 文件夹
// 插件以 UPM 包安装，包目录是只读的，路径也不固定；而命令行脚本要提交到项目的 Git 里、在没开 Unity 时也能运行，
// 所以放在包的 Tools~ 里当模板，初始化 / 升级时拷贝过去。buildkit.json 和 secrets/ 永远不会被覆盖。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildKit
{
    public static class Installer
    {
        const string ConfigTemplate = "buildkit.json";
        static readonly string[] GitignoreLines = { "/Build/", "/Assets/BuildKit.Generated/", "/Assets/BuildKit.Generated.meta" };

        /// <summary>插件根目录（含 package.json 和 Tools~）。UPM 安装和直接拷进 Assets 都能找到。</summary>
        public static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{Paths.PackageName}/package.json");
                if (info != null && Directory.Exists(info.resolvedPath)) return info.resolvedPath;
                foreach (var guid in AssetDatabase.FindAssets("BuildKit.Editor t:AssemblyDefinitionAsset"))
                {
                    var asmdef = Path.GetFullPath(AssetDatabase.GUIDToAssetPath(guid));
                    var root = Path.GetDirectoryName(Path.GetDirectoryName(asmdef));
                    if (root != null && Directory.Exists(Path.Combine(root, "Tools~"))) return root;
                }
                return null;
            }
        }

        static string TemplateDir => PackageRoot == null ? null : Path.Combine(PackageRoot, "Tools~");

        public static string PackageVersion
        {
            get
            {
                var root = PackageRoot;
                if (root == null) return null;
                try
                {
                    var pkg = MiniJson.Parse(File.ReadAllText(Path.Combine(root, "package.json"))) as Dictionary<string, object>;
                    return pkg != null && pkg.TryGetValue("version", out var v) ? v as string : null;
                }
                catch { return null; }
            }
        }

        /// <summary>项目里命令行脚本的版本；没初始化过时为 null。</summary>
        public static string InstalledScriptsVersion
        {
            get
            {
                if (File.Exists(Paths.KitVersionFile)) return File.ReadAllText(Paths.KitVersionFile).Trim();
                return File.Exists(Path.Combine(Paths.KitDir, "fastlane", "Fastfile")) ? "旧版" : null;
            }
        }

        // ───────────── 初始化 ─────────────

        /// <summary>命令行：Unity -batchmode -projectPath . -executeMethod BuildKit.Installer.InitCLI -quit</summary>
        public static void InitCLI()
        {
            try { Init(false); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError("[BuildKit] " + e.Message); EditorApplication.Exit(1); }
        }

        public static void Init(bool interactive)
        {
            var tpl = TemplateDir ?? throw new BuildKitException("找不到 BuildKit 插件的 Tools~ 目录，插件安装不完整");
            bool fresh = !File.Exists(Paths.ConfigFile);
            CopyScripts(tpl);

            if (fresh)
            {
                // 用项目现有的 Player Settings 预填配置，省得用户从头填
                var text = File.ReadAllText(Path.Combine(tpl, ConfigTemplate))
                    .Replace("__PRODUCT_NAME__", JsonEscape(PlayerSettings.productName))
                    .Replace("__COMPANY_NAME__", JsonEscape(PlayerSettings.companyName))
                    .Replace("__VERSION__", JsonEscape(string.IsNullOrEmpty(PlayerSettings.bundleVersion) ? "1.0.0" : PlayerSettings.bundleVersion))
                    .Replace("__ANDROID_PACKAGE__", JsonEscape(PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android)))
                    .Replace("__IOS_BUNDLE__", JsonEscape(PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS)))
                    .Replace("__TEAM_ID__", JsonEscape(PlayerSettings.iOS.appleDeveloperTeamID ?? ""));
                File.WriteAllText(Paths.ConfigFile, text);
            }

            var addedIgnore = UpdateGitignore(interactive);
            var msg = fresh
                ? "已在项目里生成 BuildKit/ 文件夹。\n\n下一步：\n1. 检查并填写 BuildKit/buildkit.json\n2. 复制 BuildKit/secrets.example/secrets.json 到 BuildKit/secrets/ 并填写密码\n3. 把 release.keystore 等密钥放进 BuildKit/secrets/\n4. 点 BuildKit/发包前检查"
                : $"命令行脚本已更新到 {PackageVersion}。buildkit.json 和 secrets/ 没有改动。";
            if (addedIgnore) msg += "\n\n已在项目根目录 .gitignore 里加入：\n" + string.Join("\n", GitignoreLines);
            Debug.Log("[BuildKit] " + msg);
            if (interactive)
            {
                EditorUtility.DisplayDialog("BuildKit", msg, "好");
                if (fresh) EditorUtility.OpenWithDefaultApp(Paths.ConfigFile);
            }
        }

        static void CopyScripts(string tpl)
        {
            Directory.CreateDirectory(Paths.KitDir);
            foreach (var src in Directory.GetFiles(tpl, "*", SearchOption.AllDirectories))
            {
                var rel = src.Substring(tpl.Length).TrimStart('/', '\\');
                if (rel == ConfigTemplate) continue;   // buildkit.json 单独处理，已有的绝不覆盖
                var dst = Path.Combine(Paths.KitDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
                if (dst.EndsWith(".sh") && Application.platform != RuntimePlatform.WindowsEditor)
                    Shell.Run("/bin/chmod", new[] { "+x", dst });
            }
            Directory.CreateDirectory(Path.Combine(Paths.KitDir, "secrets"));
            File.WriteAllText(Paths.KitVersionFile, (PackageVersion ?? "unknown") + "\n");
        }

        /// <summary>产物目录和打包时临时生成的文件不该进 Git。</summary>
        static bool UpdateGitignore(bool interactive)
        {
            var path = Path.Combine(Paths.ProjectRoot, ".gitignore");
            var existing = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            var missing = GitignoreLines.Where(l => !existing.Any(e => e.Trim() == l || e.Trim() == l.TrimStart('/'))).ToList();
            if (missing.Count == 0) return false;
            if (interactive && !EditorUtility.DisplayDialog("BuildKit",
                    "要在项目根目录的 .gitignore 里加入下面几行吗？（打包产物和临时文件不该提交）\n\n" + string.Join("\n", missing), "加入", "跳过"))
                return false;
            var prefix = existing.Length > 0 && existing.Last().Trim() != "" ? "\n" : "";
            File.AppendAllText(path, prefix + "# BuildKit\n" + string.Join("\n", missing) + "\n");
            return true;
        }

        static string JsonEscape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
