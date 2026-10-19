// BuildKit —— 面板：选 profile、自检、打包
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildKit
{
    public class BuildKitWindow : EditorWindow
    {
        string report = "";
        Vector2 scroll;

        public static void Open()
        {
            var w = GetWindow<BuildKitWindow>("BuildKit");
            w.minSize = new Vector2(360, 320);
            w.Show();
        }

        void OnGUI()
        {
            if (!File.Exists(Paths.ConfigFile))
            {
                EditorGUILayout.HelpBox("这个项目还没有初始化 BuildKit。", MessageType.Info);
                if (GUILayout.Button("初始化项目", GUILayout.Height(28))) Installer.Init(true);
                return;
            }

            EditorGUILayout.LabelField("当前平台", EditorUserBuildSettings.activeBuildTarget.ToString());
            EditorGUILayout.LabelField("插件版本", Installer.PackageVersion ?? "?");

            var names = ConfigLoader.ProfileNames();
            var options = new[] { "（默认 defaultProfile）" }.Concat(names).ToArray();
            int idx = System.Array.IndexOf(names, BuildKitMenu.SelectedProfile) + 1;
            int newIdx = EditorGUILayout.Popup("Profile", Mathf.Max(0, idx), options);
            if (newIdx != idx) BuildKitMenu.SelectedProfile = newIdx == 0 ? "" : names[newIdx - 1];

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("发包前检查", GUILayout.Height(28))) report = BuildKitMenu.Check(BuildKitMenu.SelectedProfile);
                if (GUILayout.Button("打包 Android", GUILayout.Height(28))) Defer(() => BuildKitMenu.BuildFromEditor(Platform.Android, BuildKitMenu.SelectedProfile));
                if (GUILayout.Button("导出 iOS", GUILayout.Height(28))) Defer(() => BuildKitMenu.BuildFromEditor(Platform.IOS, BuildKitMenu.SelectedProfile));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开配置")) EditorUtility.OpenWithDefaultApp(Paths.ConfigFile);
                if (GUILayout.Button("打开输出目录")) EditorApplication.ExecuteMenuItem("BuildKit/打开输出目录");
                if (GUILayout.Button("更新命令行脚本")) Installer.Init(true);
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(report) ? "点「发包前检查」查看结果。" : report, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        // 打包会阻塞很久并可能触发域重载，不要在 OnGUI 里直接跑
        static void Defer(System.Action a) => EditorApplication.delayCall += () => a();
    }
}
