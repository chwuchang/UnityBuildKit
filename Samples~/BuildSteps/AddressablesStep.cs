// 示例：打包前构建 Addressables 资源
// 如果 Addressables 设置里已经勾了 "Build Addressables on Player Build"，就不需要这个步骤，删掉即可。
#if BUILDKIT_HAS_ADDRESSABLES
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;

namespace BuildKit.Samples
{
    public class AddressablesStep : BuildKitStep
    {
        public override int Order => -100;   // 尽量早执行

        public override void PreBuild(BuildContext ctx)
        {
            ctx.Log("构建 Addressables…");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error))
                throw new BuildKitException("Addressables 构建失败：" + result.Error);
            ctx.Log($"Addressables 构建完成，用时 {result.Duration:F1}s");
        }
    }
}
#endif
