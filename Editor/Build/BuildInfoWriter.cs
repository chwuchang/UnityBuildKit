// BuildKit —— 把构建信息（版本、build 号、git 提交、profile……）写进包里
// 打包前生成 Assets/BuildKit.Generated/Resources/BuildKitInfo.json，打完删除；运行时用 BuildKitInfo 读取。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BuildKit
{
    static class BuildInfoWriter
    {
        public const string Folder = "Assets/BuildKit.Generated";

        sealed class Cleanup : IDisposable
        {
            public void Dispose() => Remove();
        }

        public static IDisposable Write(BuildContext ctx)
        {
            Remove();   // 上次异常退出留下的
            if (!ctx.Config.embedBuildInfo) return null;

            var info = new Dictionary<string, object>
            {
                ["version"] = ctx.Config.version,
                ["buildNumber"] = ctx.BuildNumber,
                ["profile"] = ctx.Profile,
                ["commit"] = ctx.GitCommit,
                ["branch"] = ctx.GitBranch,
                ["dirty"] = ctx.GitDirty,
                ["development"] = ctx.Development,
                ["builtAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["unityVersion"] = Application.unityVersion,
            };
            var dir = Path.Combine(Paths.ProjectRoot, Folder, "Resources");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, BuildKitInfo.ResourceName + ".json"), MiniJson.Serialize(info, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ctx.Log("构建信息：" + MiniJson.Serialize(info));
            return new Cleanup();
        }

        static void Remove()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            var full = Path.Combine(Paths.ProjectRoot, Folder);
            if (Directory.Exists(full)) Directory.Delete(full, true);
            if (File.Exists(full + ".meta")) File.Delete(full + ".meta");
        }
    }
}
