// BuildKit —— 运行时读取构建信息（版本号、build 号、git 提交、profile……）
// 打包时 BuildKit 会把这些信息写进包里，游戏里可以显示在设置页、附在崩溃/反馈日志里，
// 拿到一个包就能知道它是哪次提交、哪个 profile 打出来的。
using System;
using UnityEngine;

namespace BuildKit
{
    public static class BuildKitInfo
    {
        /// <summary>Resources 里的文件名（打包时生成，打完删除）。</summary>
        public const string ResourceName = "BuildKitInfo";

        [Serializable]
        class Data
        {
            public string version = "";
            public int buildNumber;
            public string profile = "";
            public string commit = "";
            public string branch = "";
            public bool dirty;
            public bool development;
            public string builtAt = "";
            public string unityVersion = "";
        }

        static Data data;

        static Data Get()
        {
            if (data != null) return data;
            data = new Data();
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset != null)
            {
                JsonUtility.FromJsonOverwrite(asset.text, data);
                Resources.UnloadAsset(asset);
                IsAvailable = true;
            }
            else
            {
                // 编辑器里直接运行、或者不是用 BuildKit 打的包
                data.version = Application.version;
                data.unityVersion = Application.unityVersion;
            }
            return data;
        }

        /// <summary>这个包是否由 BuildKit 打出（编辑器里运行时为 false）。</summary>
        public static bool IsAvailable { get; private set; }

        /// <summary>对外版本号，如 1.2.0。</summary>
        public static string Version => Get().version;

        /// <summary>build 号（Android versionCode / iOS CFBundleVersion）。</summary>
        public static int BuildNumber => Get().buildNumber;

        /// <summary>打包使用的 profile，如 dev / release。</summary>
        public static string Profile => Get().profile;

        /// <summary>git 提交号（短）。不是 git 仓库时为空。</summary>
        public static string Commit => Get().commit;

        /// <summary>git 分支。</summary>
        public static string Branch => Get().branch;

        /// <summary>打包时工作区是否有未提交的改动（为 true 时 Commit 不能完全代表包里的代码）。</summary>
        public static bool Dirty => Get().dirty;

        /// <summary>是否为 Development Build。</summary>
        public static bool Development => Get().development;

        /// <summary>打包时间（ISO 8601，UTC）。</summary>
        public static string BuiltAt => Get().builtAt;

        /// <summary>打包使用的 Unity 版本。</summary>
        public static string UnityVersion => Get().unityVersion;

        /// <summary>一行摘要，适合显示在设置页角落或附在日志里，如「1.2.0 (1450123) release a1b2c3d」。</summary>
        public static string Summary
        {
            get
            {
                var d = Get();
                if (!IsAvailable) return $"{d.version} (editor)";
                var s = $"{d.version} ({d.buildNumber})";
                if (!string.IsNullOrEmpty(d.profile)) s += " " + d.profile;
                if (!string.IsNullOrEmpty(d.commit)) s += " " + d.commit + (d.dirty ? "*" : "");
                return s;
            }
        }
    }
}
