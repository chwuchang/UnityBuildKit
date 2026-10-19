// BuildKit —— Android 工具链：找 JDK / SDK、提前校验 keystore 密码、计算实际 targetSdk
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace BuildKit
{
    public static class AndroidTools
    {
        // AndroidExternalToolsSettings 在 Android 模块的程序集里，用反射取，免得没装模块时编译不过
        static string ExternalToolPath(string property)
        {
            var t = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            return t?.GetProperty(property)?.GetValue(null) as string ?? "";
        }

        public static string JdkRoot
        {
            get
            {
                var p = ExternalToolPath("jdkRootPath");
                if (Directory.Exists(p)) return p;
                p = Environment.GetEnvironmentVariable("JAVA_HOME");
                return Directory.Exists(p) ? p : "";
            }
        }

        public static string SdkRoot
        {
            get
            {
                var p = ExternalToolPath("sdkRootPath");
                if (Directory.Exists(p)) return p;
                p = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
                return Directory.Exists(p) ? p : "";
            }
        }

        public static string Keytool
        {
            get
            {
                var root = JdkRoot;
                if (root == "") return "";
                var exe = Path.Combine(root, "bin", Environment.OSVersion.Platform == PlatformID.Win32NT ? "keytool.exe" : "keytool");
                return File.Exists(exe) ? exe : "";
            }
        }

        /// <summary>
        /// 用 keytool 验证 keystore 密码、alias、key 密码。
        /// 签名是 Gradle 的最后一步，密码错了要等 IL2CPP 编译完（十几分钟）才报错，所以提前查。
        /// 返回 null = 通过；返回 "" = 找不到 keytool，没法查；其他 = 错误说明。
        /// </summary>
        public static string VerifyKeystore(string keystore, string storePass, string alias, string keyPass)
        {
            var keytool = Keytool;
            if (keytool == "") return "";
            // 密码走环境变量（keytool 的 :env 语法），不出现在命令行里；-J-Duser.language=en 避免中文系统下输出乱码
            var env = new Dictionary<string, string> { ["BK_KS_STOREPASS"] = storePass ?? "", ["BK_KS_KEYPASS"] = keyPass ?? "" };

            var list = Shell.Run(keytool, new[] { "-J-Duser.language=en", "-list", "-keystore", keystore, "-alias", alias, "-storepass:env", "BK_KS_STOREPASS" }, env: env);
            if (!list.Ok)
                return $"keystore 密码错误，或 alias「{alias}」不存在。keytool 输出：\n{Tail(list.Output)}";

            // certreq 需要读私钥，能顺便验证 key 密码；生成的 CSR 直接丢掉
            var tmp = Path.GetTempFileName();
            try
            {
                var req = Shell.Run(keytool, new[] { "-J-Duser.language=en", "-certreq", "-keystore", keystore, "-alias", alias,
                    "-storepass:env", "BK_KS_STOREPASS", "-keypass:env", "BK_KS_KEYPASS", "-file", tmp }, env: env);
                if (!req.Ok) return $"key alias「{alias}」的密码（keyAliasPassword）错误。keytool 输出：\n{Tail(req.Output)}";
            }
            finally { try { File.Delete(tmp); } catch { /* 忽略 */ } }
            return null;
        }

        /// <summary>
        /// 实际会用的 targetSdk。Unity 的 Automatic 取的是本机 Android SDK 里装的最高版本，
        /// Unity 版本越老、自带的 SDK 越老，这个值就越低。返回 -1 表示算不出来。
        /// </summary>
        public static int EffectiveTargetSdk(AndroidCfg a, out string source)
        {
            if (a.targetSdk > 0) { source = "buildkit.json 的 android.targetSdk"; return a.targetSdk; }
            var t = (int)PlayerSettings.Android.targetSdkVersion;
            if (t > 0) { source = "Player Settings"; return t; }
            source = "Automatic（本机 SDK 里最高的版本）";
            var platforms = Path.Combine(SdkRoot, "platforms");
            if (SdkRoot == "" || !Directory.Exists(platforms)) return -1;
            var levels = Directory.GetDirectories(platforms)
                .Select(d => Regex.Match(Path.GetFileName(d), @"^android-(\d+)"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .ToList();
            return levels.Count > 0 ? levels.Max() : -1;
        }

        static string Tail(string s)
        {
            var lines = (s ?? "").Split('\n');
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 5)));
        }
    }
}
