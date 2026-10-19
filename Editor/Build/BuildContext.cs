// BuildKit —— 构建上下文和扩展点
// 项目里自己的打包步骤（构建 Addressables、生成热更 dll、导出配置表……）实现 IBuildKitStep 即可，
// BuildKit 会自动发现并在打包前后调用，不用改 BuildKit 本身。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildKit
{
    /// <summary>
    /// 自定义打包步骤。写一个类实现它（或继承 <see cref="BuildKitStep"/>），放在任意 Editor 程序集里即可，
    /// 不需要注册。需要有无参构造函数。抛出异常会让这次打包失败。
    /// </summary>
    public interface IBuildKitStep
    {
        /// <summary>执行顺序，小的先执行。</summary>
        int Order { get; }

        /// <summary>PlayerSettings 已经按配置改好、Unity 开始构建之前调用。</summary>
        void PreBuild(BuildContext ctx);

        /// <summary>Unity 构建成功之后调用（iOS 是导出 Xcode 工程之后，签名出 ipa 之前）。</summary>
        void PostBuild(BuildContext ctx);
    }

    /// <summary>方便继承的空实现，只重写需要的方法。</summary>
    public abstract class BuildKitStep : IBuildKitStep
    {
        public virtual int Order => 0;
        public virtual void PreBuild(BuildContext ctx) { }
        public virtual void PostBuild(BuildContext ctx) { }
    }

    public sealed class BuildContext
    {
        internal BuildContext(Config cfg, Secrets sec, Platform platform, int buildNumber, SettingsScope settings)
        {
            Config = cfg;
            Secrets = sec;
            Platform = platform;
            BuildNumber = buildNumber;
            this.settings = settings;
            // 在自定义步骤运行之前读取，免得步骤生成的临时文件被算成"未提交的改动"
            GitCommit = GitInfo.Commit;
            GitBranch = GitInfo.Branch;
            GitDirty = GitInfo.Changes.Length > 0;
        }

        /// <summary>打包开始时的 git 提交号（短）、分支、是否有未提交改动。不是 git 仓库时为空 / false。</summary>
        public string GitCommit { get; }
        public string GitBranch { get; }
        public bool GitDirty { get; }

        readonly SettingsScope settings;

        /// <summary>合并了 profile 之后的配置。</summary>
        public Config Config { get; }
        public Secrets Secrets { get; }
        public Platform Platform { get; }
        public BuildTarget Target => Platform == Platform.Android ? BuildTarget.Android : BuildTarget.iOS;
        public string Profile => Config.Profile;
        public int BuildNumber { get; }
        public bool Development => Config.development;

        /// <summary>产物路径：Android 是 .aab/.apk 文件，iOS 是导出的 Xcode 工程目录。</summary>
        public string OutputPath { get; internal set; }

        /// <summary>本次构建的输出目录。</summary>
        public string OutputDir { get; internal set; }

        /// <summary>只作用于本次构建的额外脚本宏。PreBuild 里往里加即可，不会改 Player Settings。</summary>
        public List<string> Defines { get; } = new List<string>();

        /// <summary>Unity 的构建报告，PostBuild 时可用。</summary>
        public BuildReport Report { get; internal set; }

        /// <summary>步骤之间传数据用。</summary>
        public Dictionary<string, object> Items { get; } = new Dictionary<string, object>();

        /// <summary>
        /// 临时修改一项设置，打包结束后（不管成功失败）自动改回原值。
        /// 例：ctx.Set(() => PlayerSettings.stripEngineCode, v => PlayerSettings.stripEngineCode = v, true);
        /// </summary>
        public void Set<T>(Func<T> get, Action<T> set, T value) => settings.Set(get, set, value);

        /// <summary>
        /// 注册一个清理动作：Unity 构建结束后（不管成功失败）执行，在 PostBuild 之前。
        /// 适合删除 PreBuild 里临时生成的文件。多个清理动作按注册的相反顺序执行。
        /// </summary>
        public void AddCleanup(Action cleanup) => settings.Add(cleanup);

        public void Log(string msg) => Debug.Log("[BuildKit] " + msg);
    }

    /// <summary>
    /// 打包时临时改设置，打完（不管成功失败）原样改回去。
    /// 否则每次打包都会改动 ProjectSettings.asset：build 号每分钟都不同、keystore 绝对路径每台电脑不同，
    /// git 里永远有 diff，多人协作时还会互相冲突。buildkit.json 才是这些值的唯一来源。
    /// </summary>
    sealed class SettingsScope : IDisposable
    {
        readonly List<Action> restores = new List<Action>();

        public void Set<T>(Func<T> get, Action<T> set, T value)
        {
            var old = get();
            restores.Add(() => set(old));
            set(value);
        }

        public void Add(Action cleanup) => restores.Add(cleanup);

        public void Dispose()
        {
            for (int i = restores.Count - 1; i >= 0; i--)
            {
                try { restores[i](); }
                catch (Exception e) { Debug.LogWarning("[BuildKit] 打包后还原设置 / 清理失败：" + e.Message); }
            }
            restores.Clear();
        }
    }

    static class BuildSteps
    {
        public static List<IBuildKitStep> Discover()
        {
            var steps = new List<IBuildKitStep>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<IBuildKitStep>())
            {
                if (t.IsAbstract || t.IsInterface || t.GetConstructor(Type.EmptyTypes) == null) continue;
                steps.Add((IBuildKitStep)Activator.CreateInstance(t));
            }
            return steps.OrderBy(s => s.Order).ThenBy(s => s.GetType().FullName).ToList();
        }
    }
}
