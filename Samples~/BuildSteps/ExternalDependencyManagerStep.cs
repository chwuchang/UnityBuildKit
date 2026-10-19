// 示例：打包前强制解析 Android 依赖（External Dependency Manager for Unity，Firebase / 广告 SDK 常用）
// 命令行打包时 EDM4U 的自动解析不一定会触发，缺 aar 或出现 duplicate class 时加上这个步骤。
// EDM4U 一般以 .unitypackage 方式放在 Assets 里，没有固定的程序集名，所以用反射调用，没装也不会编译报错。
using System;
using System.Linq;
using System.Reflection;

namespace BuildKit.Samples
{
    public class ExternalDependencyManagerStep : BuildKitStep
    {
        public override int Order => -50;

        public override void PreBuild(BuildContext ctx)
        {
            if (ctx.Platform != Platform.Android) return;
            var resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("GooglePlayServices.PlayServicesResolver", false))
                .FirstOrDefault(t => t != null);
            if (resolver == null) return;   // 项目没装 EDM4U

            var resolve = resolver.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (resolve == null) { ctx.Log("EDM4U 版本没有 ResolveSync(bool)，跳过"); return; }

            ctx.Log("EDM4U 解析 Android 依赖…");
            var ok = resolve.Invoke(null, new object[] { true });
            if (ok is bool b && !b) throw new BuildKitException("EDM4U 解析 Android 依赖失败，详见日志");
        }
    }
}
