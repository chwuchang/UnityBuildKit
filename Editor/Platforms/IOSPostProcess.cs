// BuildKit —— 导出 Xcode 工程后自动修改：签名、隐私权限、出口合规、能力项、dSYM
// 这些就是平时"在 Xcode 里手动点、换台电脑又忘了点"的东西
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace BuildKit
{
    public static class IOSPostProcess
    {
        /// <summary>后处理真正执行完会置 true。Builder 打完检查它，防止"被跳过了却没人发现"。</summary>
        public static bool Applied;

        /// <summary>BuildKit 打包期间由 Builder 设置（已合并 profile）；为 null 说明是手动 Build，不做任何修改。</summary>
        internal static Config Current;

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS || Current == null) return;
#if UNITY_IOS
            var i = Current.ios;

            // 1. Xcode 工程：自动签名 + Team + Release 生成 dSYM（崩溃还原堆栈用）
            var projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            var main = proj.GetUnityMainTargetGuid();
            var fw = proj.GetUnityFrameworkTargetGuid();
            foreach (var t in new[] { main, fw })
            {
                proj.SetBuildProperty(t, "CODE_SIGN_STYLE", "Automatic");
                proj.SetBuildProperty(t, "DEVELOPMENT_TEAM", i.teamId);
                var release = proj.BuildConfigByName(t, "Release");
                if (!string.IsNullOrEmpty(release))
                    proj.SetBuildPropertyForConfig(release, "DEBUG_INFORMATION_FORMAT", "dwarf-with-dsym");
            }
            proj.WriteToFile(projPath);

            // 2. Info.plist：隐私描述 + 出口合规（不填的话每次传 TestFlight 都要手动回答加密问题）
            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", i.usesNonExemptEncryption);
            if (i.privacy != null)
                foreach (var kv in i.privacy)
                    if (!string.IsNullOrEmpty(kv.key)) plist.root.SetString(kv.key, kv.value);
            plist.WriteToFile(plistPath);

            // 3. 能力项（Capabilities）
            if (i.pushNotifications || i.signInWithApple)
            {
                var cap = new ProjectCapabilityManager(projPath, "Unity-iPhone/app.entitlements", null, main);
                if (i.pushNotifications) cap.AddPushNotifications(false);
                if (i.signInWithApple) cap.AddSignInWithApple();
                cap.WriteToFile();
            }

            // 4. 隐私清单：苹果要求使用了"需声明原因的 API"的 App / SDK 带 PrivacyInfo.xcprivacy，旧版 SDK 常常没有
            bool hasManifest = Directory.EnumerateFiles(path, "PrivacyInfo.xcprivacy", SearchOption.AllDirectories).Any();
            if (!hasManifest)
                Debug.LogWarning("[BuildKit] Xcode 工程里没有任何 PrivacyInfo.xcprivacy 隐私清单，上传时可能被苹果拒收。" +
                                 "请升级 Unity 和第三方 SDK 到带隐私清单的版本，或在 Assets/Plugins/iOS 下自己放一份");

            Applied = true;
            Debug.Log("[BuildKit] Xcode 工程已自动配置：签名 / 隐私权限 / 出口合规 / 能力项 / dSYM");
#else
            // 走到这里说明编辑器脚本不是按 iOS 平台编译的（没切平台，或没装 iOS 模块），Builder 会据此让构建失败
            Debug.LogError("[BuildKit] UNITY_IOS 未定义，Xcode 工程没有被配置。请先切换到 iOS 平台（并确认装了 iOS Build Support）再打包");
#endif
        }
    }
}
