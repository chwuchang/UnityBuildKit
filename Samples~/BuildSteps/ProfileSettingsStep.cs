// 示例：按 profile 调整本次打包的设置
//   - ctx.Defines.Add(...)：只作用于本次打包的脚本宏，不改 Player Settings
//   - ctx.Set(get, set, value)：临时改一项设置，打包结束后自动改回原值
//   - ctx.AddCleanup(...)：打包结束后（成功失败都会）执行清理，比如删掉临时写进包里的文件
using System.IO;
using UnityEditor;

namespace BuildKit.Samples
{
    public class ProfileSettingsStep : BuildKitStep
    {
        const string ServerFile = "Assets/StreamingAssets/server.txt";

        public override void PreBuild(BuildContext ctx)
        {
            // 代码里可以用 #if PROFILE_DEV / #if PROFILE_RELEASE 区分
            if (!string.IsNullOrEmpty(ctx.Profile)) ctx.Defines.Add("PROFILE_" + ctx.Profile.ToUpperInvariant());

            if (!ctx.Development)
                ctx.Set(() => PlayerSettings.stripEngineCode, v => PlayerSettings.stripEngineCode = v, true);

            // 不同 profile 连不同服务器：打包时写进 StreamingAssets，打完删掉，不污染工程
            if (File.Exists(ServerFile)) return;   // 项目里已经有了就不动
            var server = ctx.Profile == "release" ? "https://api.example.com" : "https://dev-api.example.com";
            Directory.CreateDirectory(Path.GetDirectoryName(ServerFile));
            File.WriteAllText(ServerFile, server);
            AssetDatabase.ImportAsset(ServerFile);
            ctx.AddCleanup(() => AssetDatabase.DeleteAsset(ServerFile));
        }
    }
}
