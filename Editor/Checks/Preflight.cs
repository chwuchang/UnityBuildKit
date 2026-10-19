// BuildKit —— 发包前自检：把常见的"打完包才发现配错了"提前拦下
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace BuildKit
{
    public enum Platform { Android, IOS }

    public class CheckResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;

        public string Report()
        {
            var sb = new StringBuilder();
            foreach (var e in Errors) sb.AppendLine("❌ " + e);
            foreach (var w in Warnings) sb.AppendLine("⚠️ " + w);
            if (sb.Length == 0) sb.AppendLine("✅ 全部检查通过");
            return sb.ToString();
        }
    }

    public static class Preflight
    {
        static readonly Regex AndroidId = new Regex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$");
        static readonly Regex IOSId = new Regex(@"^[a-zA-Z0-9\-]+(\.[a-zA-Z0-9\-]+)+$");
        static readonly Regex VersionRe = new Regex(@"^\d+(\.\d+){1,2}$");
        static readonly Regex TeamRe = new Regex(@"^[A-Z0-9]{10}$");

        public static bool IsInstalled(Platform p) => p == Platform.Android
            ? BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)
            : BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS);

        public static CheckResult Run(Config cfg, Secrets sec, params Platform[] platforms)
        {
            var r = new CheckResult();
            r.Warnings.AddRange(cfg.Warnings);
            CheckKitScripts(r);

            if (string.IsNullOrWhiteSpace(cfg.productName)) r.Errors.Add("productName 没填");
            if (string.IsNullOrWhiteSpace(cfg.companyName)) r.Warnings.Add("companyName 没填");
            if (!VersionRe.IsMatch(cfg.version ?? "")) r.Errors.Add($"version「{cfg.version}」格式不对，应为 1.0 或 1.0.0");

            var scenes = Builder.ResolveScenes(cfg);
            if (scenes.Length == 0) r.Errors.Add("没有任何要打包的场景（buildkit.json 的 scenes 为空，Build Settings 里也没勾选）");
            foreach (var s in scenes)
                if (!File.Exists(Path.Combine(Paths.ProjectRoot, s))) r.Errors.Add("场景不存在：" + s);

            CheckGit(cfg, r);
            if (platforms.Contains(Platform.Android)) CheckAndroid(cfg, sec, r);
            if (platforms.Contains(Platform.IOS)) CheckIOS(cfg, r);
            return r;
        }

        static void CheckKitScripts(CheckResult r)
        {
            var kitVersion = Installer.InstalledScriptsVersion;
            var pkgVersion = Installer.PackageVersion;
            if (kitVersion == null)
                r.Warnings.Add("项目里还没有 BuildKit 命令行脚本（Mac / Windows 命令行出包要用），点菜单 BuildKit/初始化项目");
            else if (pkgVersion != null && kitVersion != pkgVersion)
                r.Warnings.Add($"BuildKit 命令行脚本是 {kitVersion} 版，插件已经是 {pkgVersion} 版，点菜单 BuildKit/更新命令行脚本");
        }

        static void CheckGit(Config cfg, CheckResult r)
        {
            if (!GitInfo.IsRepo) return;
            var changes = GitInfo.Changes;
            if (changes.Length == 0) return;
            var sample = string.Join("\n    ", changes.Take(5)) + (changes.Length > 5 ? $"\n    …共 {changes.Length} 项" : "");
            var msg = $"工作区有未提交的改动，包里记录的提交号不能完全代表实际代码：\n    {sample}";
            if (cfg.git.requireClean) r.Errors.Add(msg + "\n  当前配置要求先提交（git.requireClean = true）");
            else r.Warnings.Add(msg);
        }

        static void CheckAndroid(Config cfg, Secrets sec, CheckResult r)
        {
            var a = cfg.android;
            if (!IsInstalled(Platform.Android))
                r.Errors.Add("这台电脑的 Unity 没装 Android Build Support 模块（Unity Hub → 安装 → 添加模块）");
            if (!AndroidId.IsMatch(a.packageName ?? ""))
                r.Errors.Add($"android.packageName「{a.packageName}」不合法（只能字母数字下划线，至少两段，不能有 -）");
            if (a.format != "aab" && a.format != "apk")
                r.Errors.Add("android.format 只能是 aab 或 apk");
            if (a.format == "apk" && a.uploadToPlay)
                r.Warnings.Add("Google Play 只收 AAB，format=apk 时不会上传");
            // Gradle / CMake 会在项目里生成很深的临时目录，Windows 上超过 260 字符就会报 "Filename longer than 260 characters"
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor && Paths.ProjectRoot.Length > 80)
                r.Warnings.Add($"项目路径较长（{Paths.ProjectRoot.Length} 个字符），Windows 上 Gradle 可能因路径超过 260 字符而失败。" +
                               "建议把项目放在短路径下（如 D:\\Proj\\MyGame），或配置独立打包目录");

            // 架构
            var arch = (a.architectures ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
            foreach (var t in arch)
                if (!System.Enum.GetNames(typeof(AndroidArchitecture)).Any(n => string.Equals(n, t, System.StringComparison.OrdinalIgnoreCase)))
                    r.Errors.Add($"android.architectures 里的「{t}」不认识，可选：ARM64, ARMv7, X86_64");
            bool arm64 = arch.Any(t => string.Equals(t, "ARM64", System.StringComparison.OrdinalIgnoreCase));
            if (!arm64) r.Errors.Add("Google Play 要求包含 64 位：android.architectures 必须含 ARM64");
            if (arm64 && !a.il2cpp) r.Errors.Add("ARM64 需要 IL2CPP，请设 android.il2cpp = true");

            // 签名
            var ks = Paths.Kit(a.keystoreFile);
            bool ksOk = true;
            if (!File.Exists(ks)) { ksOk = false; r.Errors.Add("签名文件不存在：" + ks + "（release keystore 丢了就再也无法更新应用，务必备份）"); }
            if (string.IsNullOrEmpty(a.keyAlias)) { ksOk = false; r.Errors.Add("android.keyAlias 没填"); }
            if (string.IsNullOrEmpty(sec.keystorePassword)) { ksOk = false; r.Errors.Add("secrets.json 里 keystorePassword 没填"); }
            if (string.IsNullOrEmpty(sec.keyAliasPassword)) { ksOk = false; r.Errors.Add("secrets.json 里 keyAliasPassword 没填"); }
            if (ksOk)
            {
                var err = AndroidTools.VerifyKeystore(ks, sec.keystorePassword, a.keyAlias, sec.keyAliasPassword);
                if (err == "") r.Warnings.Add("找不到 JDK 里的 keytool，没法提前校验 keystore 密码（装 Android 模块时勾选 OpenJDK）");
                else if (err != null) r.Errors.Add(err);
            }

            // targetSdk：Google Play 每年提高要求，不达标会被拒收
            var target = AndroidTools.EffectiveTargetSdk(a, out var source);
            bool forPlay = a.uploadToPlay && a.format == "aab";
            if (target < 0)
                r.Warnings.Add($"算不出实际的 targetSdk（来源：{source}），请确认不低于 Google Play 要求的 {a.playMinTargetSdk}");
            else if (target < a.playMinTargetSdk)
            {
                var msg = $"targetSdk 是 {target}（来源：{source}），低于 Google Play 要求的 {a.playMinTargetSdk}。" +
                          "在 buildkit.json 设置 android.targetSdk，并在 Unity 的 Android SDK 里装好对应版本；Unity 版本太老时需要升级";
                if (forPlay) r.Errors.Add(msg); else r.Warnings.Add(msg);
            }
            if (a.minSdk > 0 && target > 0 && a.minSdk > target) r.Errors.Add($"android.minSdk（{a.minSdk}）不能大于 targetSdk（{target}）");
        }

        static void CheckIOS(Config cfg, CheckResult r)
        {
            var i = cfg.ios;
            if (!IsInstalled(Platform.IOS))
                r.Errors.Add("这台电脑的 Unity 没装 iOS Build Support 模块");
            if (!IOSId.IsMatch(i.bundleId ?? "")) r.Errors.Add($"ios.bundleId「{i.bundleId}」不合法");
            if (!TeamRe.IsMatch(i.teamId ?? ""))
                r.Errors.Add($"ios.teamId「{i.teamId}」不对：应为 10 位大写字母数字（developer.apple.com → Membership 里查看）");
            if (!Regex.IsMatch(i.minIOS ?? "", @"^\d+\.\d+$")) r.Errors.Add("ios.minIOS 格式应为 13.0 这种");

            if (i.privacy == null || i.privacy.Length == 0)
                r.Warnings.Add("没配置任何 iOS 隐私权限描述。如果用了相机/相册/麦克风/广告追踪(ATT)，缺描述会闪退或被拒审");
            else
                foreach (var kv in i.privacy)
                {
                    if (string.IsNullOrWhiteSpace(kv.key) || !kv.key.StartsWith("NS"))
                        r.Errors.Add($"隐私权限 key「{kv.key}」看起来不对（应以 NS 开头，如 NSCameraUsageDescription）");
                    if (string.IsNullOrWhiteSpace(kv.value) || kv.value.Length < 6)
                        r.Errors.Add($"{kv.key} 的描述太短。苹果要求写清楚用途，如「用于保存剧情截图到相册」");
                }
        }

        public static void ThrowIfFailed(CheckResult r)
        {
            UnityEngine.Debug.Log("[BuildKit] 自检结果：\n" + r.Report());
            if (!r.Ok) throw new BuildKitException("发包前检查未通过：\n" + r.Report());
        }
    }
}
