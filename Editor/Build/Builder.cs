// BuildKit —— 打包核心：Unity 菜单、面板和命令行（脚本 / fastlane 调用）共用同一套逻辑
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildKit
{
    public static class Builder
    {
        // ───────────── 命令行入口 ─────────────
        // Unity -batchmode -projectPath <项目> -buildTarget Android|iOS -executeMethod BuildKit.Builder.CLI
        //       -bkPlatform android|ios [-bkProfile dev] [-bkBuildNumber 123] [-bkOutputDir <目录>]

        public static void CLI()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                var platform = Arg(args, "-bkPlatform");
                var bnArg = Arg(args, "-bkBuildNumber");
                int bn = string.IsNullOrEmpty(bnArg) ? 0 : int.Parse(bnArg);
                Paths.OutputOverride = Arg(args, "-bkOutputDir");
                var profile = Arg(args, "-bkProfile");

                Platform p;
                if (platform == "android") p = Platform.Android;
                else if (platform == "ios") p = Platform.IOS;
                else throw new BuildKitException("-bkPlatform 必须是 android 或 ios，当前：" + platform);

                var output = Build(p, profile, bn);
                Debug.Log("[BuildKit] OUTPUT=" + output);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildKit] FAILED: " + e.Message + "\n" + e.StackTrace);
                EditorApplication.Exit(1);
            }
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
        }

        // ───────────── 构建 ─────────────

        /// <summary>
        /// 构建一个平台，返回产物路径（Android：.aab/.apk 文件；iOS：Xcode 工程目录）。
        /// profile 为空时用 buildkit.json 的 defaultProfile；buildNumber ≤ 0 时按时间自动生成。
        /// </summary>
        public static string Build(Platform platform, string profile = null, int buildNumber = 0)
        {
            var target = platform == Platform.Android ? BuildTarget.Android : BuildTarget.iOS;
            RequireActiveTarget(target);
            var cfg = ConfigLoader.LoadConfig(profile);
            var sec = ConfigLoader.LoadSecrets();
            if (buildNumber <= 0) buildNumber = BuildNumbers.Timestamp();
            Debug.Log($"[BuildKit] 平台 {platform}，profile「{cfg.Profile}」，build {buildNumber}");
            Preflight.ThrowIfFailed(Preflight.Run(cfg, sec, platform));

            var steps = BuildSteps.Discover();
            if (steps.Count > 0) Debug.Log("[BuildKit] 自定义步骤：" + string.Join(", ", steps.Select(s => s.GetType().Name)));

            BuildContext ctx;
            using (var scope = new SettingsScope())
            {
                ctx = new BuildContext(cfg, sec, platform, buildNumber, scope);
                ApplyCommon(ctx);
                if (platform == Platform.Android) PrepareAndroid(ctx); else PrepareIOS(ctx);

                foreach (var s in steps) s.PreBuild(ctx);

                var options = new BuildPlayerOptions
                {
                    scenes = ResolveScenes(cfg),
                    locationPathName = ctx.OutputPath,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    options = cfg.development ? BuildOptions.Development : BuildOptions.None,
                    extraScriptingDefines = (cfg.defines ?? new string[0]).Concat(ctx.Defines)
                        .Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToArray(),
                };
                IOSPostProcess.Applied = false;
                IOSPostProcess.Current = platform == Platform.IOS ? cfg : null;
                try
                {
                    using (BuildInfoWriter.Write(ctx))
                        ctx.Report = Run(options);
                }
                finally { IOSPostProcess.Current = null; }
                if (platform == Platform.IOS && !IOSPostProcess.Applied)
                    throw new BuildKitException("Xcode 工程后处理没有执行，签名、隐私描述、加密合规都没写进去。" +
                                                "通常是编辑器脚本没按 iOS 平台编译：确认当前平台是 iOS，并且装了 iOS Build Support");
            }

            if (platform == Platform.Android) CollectAndroidSymbols(ctx);
            foreach (var s in steps) s.PostBuild(ctx);
            return ctx.OutputPath;
        }

        // ───────────── Android ─────────────

        static void PrepareAndroid(BuildContext ctx)
        {
            var cfg = ctx.Config;
            var a = cfg.android;
            var sec = ctx.Secrets;
            var nt = NamedBuildTarget.Android;

            // 每个 build 一个文件夹：包 + 符号文件放一起，线上崩溃时按 build 号找回对应的符号
            ctx.OutputDir = Path.Combine(Paths.Output(cfg), "Android", ArtifactName(cfg, ctx.BuildNumber, false));
            if (Directory.Exists(ctx.OutputDir)) Directory.Delete(ctx.OutputDir, true);
            Directory.CreateDirectory(ctx.OutputDir);
            ctx.OutputPath = Path.Combine(ctx.OutputDir, ArtifactName(cfg, ctx.BuildNumber, true) + "." + a.format);

            ctx.Set(() => PlayerSettings.GetApplicationIdentifier(nt), v => PlayerSettings.SetApplicationIdentifier(nt, v), a.packageName);
            ctx.Set(() => PlayerSettings.Android.bundleVersionCode, v => PlayerSettings.Android.bundleVersionCode = v, ctx.BuildNumber);
            ctx.Set(() => PlayerSettings.GetScriptingBackend(nt), v => PlayerSettings.SetScriptingBackend(nt, v),
                a.il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            ctx.Set(() => PlayerSettings.Android.targetArchitectures, v => PlayerSettings.Android.targetArchitectures = v, ParseArch(a.architectures));
            if (a.minSdk > 0)
                ctx.Set(() => PlayerSettings.Android.minSdkVersion, v => PlayerSettings.Android.minSdkVersion = v, (AndroidSdkVersions)a.minSdk);
            if (a.targetSdk > 0)
                ctx.Set(() => PlayerSettings.Android.targetSdkVersion, v => PlayerSettings.Android.targetSdkVersion = v, (AndroidSdkVersions)a.targetSdk);

            ctx.Set(() => PlayerSettings.Android.useCustomKeystore, v => PlayerSettings.Android.useCustomKeystore = v, true);
            ctx.Set(() => PlayerSettings.Android.keystoreName, v => PlayerSettings.Android.keystoreName = v, Paths.Kit(a.keystoreFile));
            ctx.Set(() => PlayerSettings.Android.keystorePass, v => PlayerSettings.Android.keystorePass = v, sec.keystorePassword);
            ctx.Set(() => PlayerSettings.Android.keyaliasName, v => PlayerSettings.Android.keyaliasName = v, a.keyAlias);
            ctx.Set(() => PlayerSettings.Android.keyaliasPass, v => PlayerSettings.Android.keyaliasPass = v, sec.keyAliasPassword);

            ctx.Set(() => EditorUserBuildSettings.buildAppBundle, v => EditorUserBuildSettings.buildAppBundle = v, a.format == "aab");
            EnableAndroidSymbols(ctx, a.format == "aab");
        }

        public static AndroidArchitecture ParseArch(string s)
        {
            AndroidArchitecture result = 0;
            foreach (var token in (s ?? "ARM64").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))
                result |= (AndroidArchitecture)Enum.Parse(typeof(AndroidArchitecture), token, true);
            return result == 0 ? AndroidArchitecture.ARM64 : result;
        }

        /// <summary>
        /// 生成 IL2CPP 符号表（public symbols）。没有它，Play Console 里的崩溃堆栈只有地址看不到函数名。
        /// Unity 6 起还能直接嵌进 AAB，Play 自动识别；更早的版本要手动把 .symbols.zip 传到 Play Console。
        /// </summary>
        static void EnableAndroidSymbols(BuildContext ctx, bool aab)
        {
#if UNITY_ANDROID
#if UNITY_6000_0_OR_NEWER
            ctx.Set(() => UnityEditor.Android.UserBuildSettings.DebugSymbols.level,
                v => UnityEditor.Android.UserBuildSettings.DebugSymbols.level = v,
                Unity.Android.Types.DebugSymbolLevel.SymbolTable);
            ctx.Set(() => UnityEditor.Android.UserBuildSettings.DebugSymbols.format,
                v => UnityEditor.Android.UserBuildSettings.DebugSymbols.format = v,
                aab ? Unity.Android.Types.DebugSymbolFormat.Zip | Unity.Android.Types.DebugSymbolFormat.IncludeInBundle
                    : Unity.Android.Types.DebugSymbolFormat.Zip);
#else
            ctx.Set(() => EditorUserBuildSettings.androidCreateSymbols, v => EditorUserBuildSettings.androidCreateSymbols = v,
                AndroidCreateSymbols.Public);
#endif
#endif
        }

        /// <summary>Unity 生成的符号包名字带版本号后缀，统一改成和包同名，方便按 build 号对应。</summary>
        static void CollectAndroidSymbols(BuildContext ctx)
        {
            var target = Path.Combine(ctx.OutputDir, Path.GetFileNameWithoutExtension(ctx.OutputPath) + ".symbols.zip");
            var found = Directory.GetFiles(ctx.OutputDir, "*.symbols.zip").OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
            if (found == null)
            {
                Debug.LogWarning("[BuildKit] 没找到符号文件 *.symbols.zip，线上崩溃将无法还原堆栈");
                return;
            }
            if (!string.Equals(Path.GetFullPath(found), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Move(found, target);
            Debug.Log("[BuildKit] 符号文件：" + target);
        }

        // ───────────── iOS ─────────────

        static void PrepareIOS(BuildContext ctx)
        {
            var cfg = ctx.Config;
            var i = cfg.ios;
            ctx.OutputDir = Path.Combine(Paths.Output(cfg), "iOS");
            ctx.OutputPath = Path.Combine(ctx.OutputDir, "xcode");
            if (Directory.Exists(ctx.OutputPath)) Directory.Delete(ctx.OutputPath, true);   // 每次干净导出，避免旧文件残留
            Directory.CreateDirectory(ctx.OutputPath);

            ctx.Set(() => PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS), v => PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, v), i.bundleId);
            ctx.Set(() => PlayerSettings.iOS.buildNumber, v => PlayerSettings.iOS.buildNumber = v, ctx.BuildNumber.ToString());
            ctx.Set(() => PlayerSettings.iOS.appleDeveloperTeamID, v => PlayerSettings.iOS.appleDeveloperTeamID = v, i.teamId);
            ctx.Set(() => PlayerSettings.iOS.appleEnableAutomaticSigning, v => PlayerSettings.iOS.appleEnableAutomaticSigning = v, true);
            ctx.Set(() => PlayerSettings.iOS.targetOSVersionString, v => PlayerSettings.iOS.targetOSVersionString = v, i.minIOS);
            ctx.Set(() => PlayerSettings.iOS.sdkVersion, v => PlayerSettings.iOS.sdkVersion = v, iOSSdkVersion.DeviceSDK);
        }

        // ───────────── 公共 ─────────────

        /// <summary>
        /// 必须在"脚本已经按目标平台编译"的状态下打包，否则带 #if UNITY_IOS / UNITY_ANDROID 的
        /// 打包后处理（BuildKit 自己的和各家 SDK 的）都会被跳过，打出来的包缺配置却不报错。
        /// </summary>
        static void RequireActiveTarget(BuildTarget target)
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            if (active != target)
                throw new BuildKitException($"当前平台是 {active}，不是 {target}。命令行请带 -buildTarget {target}；编辑器里请先切换平台再打包");
        }

        static void ApplyCommon(BuildContext ctx)
        {
            var cfg = ctx.Config;
            ctx.Set(() => PlayerSettings.productName, v => PlayerSettings.productName = v, cfg.productName);
            if (!string.IsNullOrEmpty(cfg.companyName))
                ctx.Set(() => PlayerSettings.companyName, v => PlayerSettings.companyName = v, cfg.companyName);
            ctx.Set(() => PlayerSettings.bundleVersion, v => PlayerSettings.bundleVersion = v, cfg.version);
            ctx.Set(() => EditorUserBuildSettings.development, v => EditorUserBuildSettings.development = v, cfg.development);
        }

        /// <summary>产物命名：MyGame_1.2.0_1450123_dev（文件名）/ 1.2.0_1450123_dev（文件夹）。</summary>
        public static string ArtifactName(Config cfg, int buildNumber, bool withProduct)
        {
            var s = $"{cfg.version}_{buildNumber}";
            if (!string.IsNullOrEmpty(cfg.Profile)) s += "_" + Safe(cfg.Profile);
            return withProduct ? Safe(cfg.productName) + "_" + s : s;
        }

        public static string[] ResolveScenes(Config cfg)
        {
            if (cfg.scenes != null && cfg.scenes.Length > 0) return cfg.scenes;
            return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        }

        static BuildReport Run(BuildPlayerOptions opts)
        {
            Debug.Log($"[BuildKit] 开始构建 {opts.target} → {opts.locationPathName}" +
                      (opts.extraScriptingDefines.Length > 0 ? $"，额外宏：{string.Join(";", opts.extraScriptingDefines)}" : ""));
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
                throw new BuildKitException($"Unity 构建失败：{s.result}，错误 {s.totalErrors} 个（详见 Console / 日志）");
            Debug.Log($"[BuildKit] 构建成功，用时 {s.totalTime:mm\\:ss}，大小 {s.totalSize / 1024 / 1024} MB");
            return report;
        }

        static string Safe(string s) => string.Concat((s ?? "App").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
    }

    public static class BuildNumbers
    {
        /// <summary>
        /// 2024-01-01 起的分钟数：每分钟递增、任何电脑算出来都一致、不用记"上次是几号"。
        /// Android versionCode 和 iOS build 号都能用，几十年内不会溢出。
        /// </summary>
        public static int Timestamp() =>
            (int)(DateTime.UtcNow - new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
    }
}
