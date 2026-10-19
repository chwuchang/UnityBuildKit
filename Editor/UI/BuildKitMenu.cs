// BuildKit —— Unity 菜单
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildKit
{
    public static class BuildKitMenu
    {
        [MenuItem("BuildKit/打开面板", priority = 0)]
        static void MenuWindow() => BuildKitWindow.Open();

        [MenuItem("BuildKit/发包前检查", priority = 1)]
        static void MenuCheck() => EditorUtility.DisplayDialog("BuildKit 自检", Check(SelectedProfile), "好");

        [MenuItem("BuildKit/打包 Android", priority = 20)]
        static void MenuAndroid() => BuildFromEditor(Platform.Android, SelectedProfile);

        [MenuItem("BuildKit/导出 iOS Xcode 工程", priority = 21)]
        static void MenuIOS() => BuildFromEditor(Platform.IOS, SelectedProfile);

        [MenuItem("BuildKit/打开配置文件", priority = 40)]
        static void MenuOpenConfig()
        {
            if (File.Exists(Paths.ConfigFile)) EditorUtility.OpenWithDefaultApp(Paths.ConfigFile);
            else EditorUtility.DisplayDialog("BuildKit", "还没有配置文件，请先点 BuildKit/初始化项目", "好");
        }

        [MenuItem("BuildKit/打开输出目录", priority = 41)]
        static void MenuOpenOutput()
        {
            try
            {
                var d = Paths.Output(ConfigLoader.LoadConfig(SelectedProfile));
                Directory.CreateDirectory(d);
                EditorUtility.RevealInFinder(d);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("BuildKit", e.Message, "好"); }
        }

        [MenuItem("BuildKit/初始化项目", priority = 60)]
        static void MenuInit() => RunSafe(() => Installer.Init(true));

        [MenuItem("BuildKit/初始化项目", true)]
        static bool MenuInitValidate() => !File.Exists(Paths.ConfigFile);

        [MenuItem("BuildKit/更新命令行脚本", priority = 61)]
        static void MenuUpdate() => RunSafe(() => Installer.Init(true));

        [MenuItem("BuildKit/更新命令行脚本", true)]
        static bool MenuUpdateValidate() => File.Exists(Paths.ConfigFile);

        // ───────────── 供菜单和面板共用 ─────────────

        /// <summary>编辑器里打包用的 profile（按项目记住）。空字符串 = 用 defaultProfile。</summary>
        public static string SelectedProfile
        {
            get => EditorPrefs.GetString(PrefKey, "");
            set => EditorPrefs.SetString(PrefKey, value ?? "");
        }

        static string PrefKey => "BuildKit.Profile." + Paths.ProjectRoot;

        /// <summary>只检查这台电脑装了的平台，免得没装 iOS 模块的 Windows 上一直显示 ❌。</summary>
        public static string Check(string profile)
        {
            try
            {
                var platforms = new[] { Platform.Android, Platform.IOS }.Where(Preflight.IsInstalled).ToArray();
                var r = Preflight.Run(ConfigLoader.LoadConfig(profile), ConfigLoader.LoadSecrets(), platforms);
                var head = platforms.Length == 0 ? "这台电脑的 Unity 没装 Android / iOS 模块\n" : $"检查平台：{string.Join(" / ", platforms)}\n\n";
                return head + r.Report();
            }
            catch (Exception e) { return "❌ " + e.Message; }
        }

        public static void BuildFromEditor(Platform platform, string profile)
        {
            var target = platform == Platform.Android ? BuildTarget.Android : BuildTarget.iOS;
            if (!EnsureActiveTarget(target)) return;
            try
            {
                var output = Builder.Build(platform, profile);
                EditorUtility.RevealInFinder(output);
                EditorUtility.DisplayDialog("BuildKit", "完成：\n" + output, "好");
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildKit] " + e);
                EditorUtility.DisplayDialog("BuildKit 失败", e.Message, "好");
            }
        }

        /// <summary>
        /// 切平台后，编辑器脚本要按新平台重新编译（UNITY_IOS 等宏、各 SDK 的打包后处理才会生效），
        /// 这要等到当前调用结束、域重载之后才发生。所以不能"切完马上打"——那样后处理会被悄悄跳过。
        /// 只能切完让用户再点一次。
        /// </summary>
        static bool EnsureActiveTarget(BuildTarget target)
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            if (active == target) return true;
            if (EditorUtility.DisplayDialog("BuildKit",
                    $"当前平台是 {active}，需要先切换到 {target}。\n\n切换会重新导入资源、重新编译脚本，可能要几分钟。\n切换完成后请再点一次打包。",
                    "切换平台", "取消"))
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target);
            return false;
        }

        static void RunSafe(Action a)
        {
            try { a(); }
            catch (Exception e) { EditorUtility.DisplayDialog("BuildKit", e.Message, "好"); }
        }
    }
}
