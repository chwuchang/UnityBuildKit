# UnityBuildKit

[English](README.en.md) | 中文

Unity 一键出包，并上传 Google Play / TestFlight。配置一次，以后发版只要一条命令。

```bash
bash BuildKit/build.sh all        # Android + iOS 出包并上传
```

## 解决的问题

| 痛点 | BuildKit 的做法 |
|---|---|
| 每次发版都要改版本号、build 号、签名设置，再到 Xcode 里点一遍 | 一条命令完成构建、签名、上传。build 号自动生成，并保证比商店里已有的大 |
| 换一台 Mac 就要重新装证书和描述文件 | 用 App Store Connect API Key 自动签名。换电脑只要拷贝 `secrets/` 文件夹 |
| keystore 密码错了，要等 IL2CPP 编译十几分钟后才报错 | 发包前自检：密码、alias、targetSdk、Xcode 版本、隐私描述、场景，几十秒内发现问题 |
| 打包时必须关掉编辑器；切换平台要重新导入资源，一等几十分钟 | 独立打包目录：在另一份 checkout 里打包，Android 和 iOS 各用一份，编辑器照常开着 |
| 上传失败只能重新打包 | 先把两端都构建完再上传；上传失败就只重传 |
| 每次打包 `ProjectSettings.asset` 都出现 diff | 打包时临时改的设置，打完原样还原 |
| 线上崩溃没有符号，堆栈还原不了 | 每个 build 单独一个文件夹，包和符号文件（Android symbols.zip / iOS dSYM）放在一起 |
| dev 包和 release 包设置不同，靠手动切换 | profiles：`profile:dev` 一个参数就能切换包名、宏、Development Build、上传目标 |
| 拿到一个包，不知道是哪个提交打的 | 版本号、build 号、提交号、profile 写进包里，运行时用 `BuildKitInfo` 读取 |
| 项目有自己的打包步骤（Addressables、热更、配置表……） | 实现 `IBuildKitStep` 就会被自动调用；也可以配置 shell hooks |
| 带资源 bundle 的 Pod 报签名错误、SSH 打包报 `errSecInternalComponent` | 自动给 Pods 工程设置开发团队；SSH 会话下自动解锁钥匙串 |

## 安装

要求：Unity 2021.3 及以上。iOS 只能在 Mac 上打包（需要 Xcode 26 及以上）。

1. Unity 菜单 **Window → Package Manager → + → Add package from git URL**，填入：
   ```
   https://github.com/chwuchang/UnityBuildKit.git
   ```
2. 菜单 **BuildKit → 初始化项目**。会在项目根目录生成 `BuildKit/` 文件夹（配置和命令行脚本，提交到 Git），并用当前的 Player Settings 预填配置
3. 安装命令行环境：
   - Mac：`bash BuildKit/setup_mac.sh`（安装 Ruby、CocoaPods、fastlane）
   - Windows（可选）：`powershell -ExecutionPolicy Bypass -File BuildKit\setup_windows.ps1`（安装 fastlane，用于上传 Google Play）。不装的话只能出包

## 一次性准备

**1. 填写 `BuildKit/buildkit.json`**：包名、Team ID 等，字段说明见[配置](#配置)。

**2. 准备密钥**：把 `BuildKit/secrets.example/secrets.json` 复制到 `BuildKit/secrets/` 并填写。`secrets/` 不会提交到 Git，换电脑时整个文件夹拷过去即可。

| 文件 | 用途 | 获取方式 |
|---|---|---|
| `release.keystore` | Android 签名 | 已有的直接放进来；没有就用 `keytool -genkeypair -v -keystore release.keystore -alias release -keyalg RSA -keysize 2048 -validity 10000` 生成，**务必备份** |
| `AuthKey_XXXXXXXXXX.p8` | iOS 自动签名和上传 | App Store Connect → 用户和访问 → 集成 → App Store Connect API，生成密钥，权限选「管理」。文件只能下载一次。Key ID 和 Issuer ID 填进 secrets.json |
| `play-service-account.json` | 上传 Google Play | Google Cloud 新建服务账号并下载 JSON 密钥，然后在 Play Console → 用户和权限里邀请这个服务账号，给发布权限 |

**3. 在商店里建好 App**：App Store Connect 里先创建 App；Google Play 的第一个 AAB 必须在网页上手动上传（Google 的限制），之后才能自动上传。

**4. 自检**：菜单 **BuildKit → 发包前检查**，或者运行 `bash BuildKit/build.sh check`。

## 使用

**Unity 编辑器**：菜单 **BuildKit → 打开面板**，选好 profile 后点「打包 Android」或「导出 iOS」。如果当前平台不对，BuildKit 会先切换平台，切换完需要再点一次（切换后脚本要按新平台重新编译，各 SDK 的打包后处理才会生效）。

**Mac**（双端出包并上传）：

```bash
bash BuildKit/build.sh all                     # Android + iOS：两端都构建成功后再统一上传
bash BuildKit/build.sh ios                     # 只打 iOS
bash BuildKit/build.sh android profile:dev     # 用 dev profile 打 Android
bash BuildKit/build.sh ios upload:false        # 只出包，不上传
bash BuildKit/build.sh upload ios              # 重传最近一次构建的 iOS 包（不重新打包）
bash BuildKit/build.sh check                   # 只检查环境和配置
```

**Windows**（只能出 Android 包）：

```bat
BuildKit\build_android.bat                     :: 出 AAB；装了 fastlane 会按配置上传 Google Play
BuildKit\build_android.bat -Profile dev        :: 用 dev profile
BuildKit\build_android.bat -NoUpload           :: 只出包
BuildKit\build_android_apk.bat                 :: 出 APK 装手机测试
```

**发新版本**：只需要改 `buildkit.json` 里的 `version`。build 号是按分钟递增的数字，任何电脑算出来都一致，所有平台共用。

**升级插件后**：菜单 **BuildKit → 更新命令行脚本**。`buildkit.json` 和 `secrets/` 不会被改动。

## 配置

`BuildKit/buildkit.json`，提交到 Git。拼错的字段名、类型不对的值会在自检时指出来。

| 字段 | 说明 | 默认 |
|---|---|---|
| `productName` / `companyName` / `version` | 应用名 / 公司名 / 对外版本号 | |
| `scenes` | 要打包的场景；为空时用 Build Settings 里勾选的场景 | `[]` |
| `outputDir` | 产物目录（相对项目根目录） | `Build` |
| `development` | Development Build | `false` |
| `defines` | 额外的脚本宏，只作用于本次打包，不改 Player Settings | `[]` |
| `embedBuildInfo` | 把构建信息写进包里 | `true` |
| `git.requireClean` | 有未提交的改动时禁止打包 | `false` |
| `git.tagOnUpload` / `git.pushTag` | 上传成功后打 tag `v{version}-{build}` / 并 push | `false` |
| `android.packageName` | 包名 | |
| `android.format` | `aab`（上传 Play）或 `apk` | `aab` |
| `android.il2cpp` / `android.architectures` | 脚本后端 / 架构，如 `ARM64,ARMv7` | `true` / `ARM64` |
| `android.minSdk` / `android.targetSdk` | 0 表示沿用 Unity 设置 | `0` |
| `android.playMinTargetSdk` | Google Play 当前要求的最低 targetSdk，低于它时禁止上传 | `36` |
| `android.keystoreFile` / `android.keyAlias` | 签名文件（相对 `BuildKit/`）/ alias | `secrets/release.keystore` / `release` |
| `android.uploadToPlay` / `playTrack` / `playReleaseStatus` | 是否上传 / 轨道 / `draft` 或 `completed` | `true` / `internal` / `draft` |
| `ios.bundleId` / `ios.teamId` | Bundle ID / 10 位 Team ID | |
| `ios.minIOS` / `ios.minXcode` | 最低 iOS 版本 / App Store 要求的最低 Xcode | `13.0` / `26.0` |
| `ios.exportMethod` | `app-store` / `ad-hoc` / `development` | `app-store` |
| `ios.privacy` | 隐私权限描述，如 `[{"key": "NSCameraUsageDescription", "value": "用于扫码"}]` | `[]` |
| `ios.usesNonExemptEncryption` | 出口合规。只用 HTTPS 填 `false`，上传 TestFlight 时就不用再手动回答 | `false` |
| `ios.pushNotifications` / `ios.signInWithApple` | 自动添加对应的 Capability | `false` |
| `ios.uploadToTestFlight` | 是否上传 TestFlight | `true` |
| `notify.onSuccess` / `notify.onFailure` | 成功 / 失败时发通知 | `true` |
| `hooks.postBuild` / `hooks.postUpload` | 构建 / 上传成功后执行的 shell 命令 | |

商店政策每年都会提高要求，`playMinTargetSdk` 和 `minXcode` 需要随之修改。

### Profiles

顶层是公共设置，`profiles` 里写各 profile 和公共设置不同的部分。对象逐字段合并，数组和普通值整体替换。

```json
"defaultProfile": "release",
"profiles": {
  "dev": {
    "development": true,
    "defines": ["BUILDKIT_DEV"],
    "android": { "packageName": "com.studio.game.dev", "format": "apk", "uploadToPlay": false },
    "ios": { "exportMethod": "development", "uploadToTestFlight": false }
  },
  "release": {
    "git": { "requireClean": true, "tagOnUpload": true }
  }
}
```

### secrets.json

放在 `BuildKit/secrets/`，不提交。每一项都可以用环境变量覆盖，方便接入 CI。

| 字段 | 环境变量 | 说明 |
|---|---|---|
| `keystorePassword` / `keyAliasPassword` | `BK_KEYSTORE_PASSWORD` / `BK_KEYALIAS_PASSWORD` | keystore 密码 / key 密码 |
| `ascKeyId` / `ascIssuerId` / `ascKeyFile` | `BK_ASC_KEY_ID` / `BK_ASC_ISSUER_ID` / `BK_ASC_KEY_FILE` | App Store Connect API Key |
| `playJsonKeyFile` | `BK_PLAY_JSON` | Google Play 服务账号 JSON |
| `unityPath` | `UNITY_PATH` | 留空时按项目的 Unity 版本自动查找 Unity Hub 的安装位置 |
| `androidWorkspace` / `iosWorkspace` | `BK_ANDROID_WORKSPACE` / `BK_IOS_WORKSPACE` | 独立打包目录，见下文 |
| `keychainPassword` | `BK_KEYCHAIN_PASSWORD` | 通过 SSH 打 iOS 包时用于解锁登录钥匙串 |
| `notifyWebhook` | `BK_NOTIFY_WEBHOOK` | 通知地址，见下文 |

## 独立打包目录

在 secrets.json 里配置本机路径：

```json
"androidWorkspace": "D:/BuildWS/MyGame-android",
"iosWorkspace": "/Users/me/BuildWS/MyGame-ios"
```

配置后：
- 第一次使用时自动 `git clone` 到这个目录。之后每次打包前，强制同步到主项目当前的提交（直接从本地取，没 push 的提交也能打包）
- `Library` 缓存会保留，所以只有第一次慢
- `secrets/` 自动拷过去，产物仍然输出到主项目的 `Build/`

注意：
- 只会打包**已提交**的内容。有未提交的改动时会停下，加 `allow_dirty:true`（Windows 用 `-AllowDirty`）可以忽略
- 只会操作 BuildKit 自己克隆出来的目录，并且这个目录必须在主项目文件夹之外，不会误覆盖你的代码

## 扩展

**C# 打包步骤**：在任意 Editor 程序集里写一个类，不需要注册：

```csharp
public class MyStep : BuildKit.BuildKitStep
{
    public override int Order => 0;                       // 小的先执行

    public override void PreBuild(BuildKit.BuildContext ctx)
    {
        if (ctx.Profile == "release") ctx.Defines.Add("NO_CHEATS");               // 只作用于本次打包的宏
        ctx.Set(() => PlayerSettings.stripEngineCode,                             // 临时改设置，打完自动还原
                v => PlayerSettings.stripEngineCode = v, true);
        ctx.AddCleanup(() => { /* 打包结束后执行，成功失败都会执行 */ });
    }

    public override void PostBuild(BuildKit.BuildContext ctx) { /* ctx.OutputPath 是产物路径 */ }
}
```

在 Package Manager 里可以导入示例 **Build Steps**：Addressables 构建、EDM4U 依赖解析、按 profile 切换服务器地址。

**Shell hooks**：`hooks.postBuild` / `hooks.postUpload` 在项目根目录执行，可以读取这些环境变量：`BK_PLATFORM`、`BK_PROFILE`、`BK_VERSION`、`BK_BUILD_NUMBER`、`BK_ARTIFACT`、`BK_SYMBOLS`、`BK_COMMIT`、`BK_OUTPUT_DIR`。

## 运行时读取构建信息

```csharp
versionLabel.text = BuildKit.BuildKitInfo.Summary;   // 例：1.2.0 (1471834) release a1b2c3d
// 单项：Version / BuildNumber / Profile / Commit / Branch / Dirty / Development / BuiltAt
```

## 通知

在 `notifyWebhook` 里填机器人地址，会根据地址自动识别是企业微信、钉钉、飞书、Slack 还是 Discord，其他地址按 `{"text": "..."}` 发送。钉钉机器人如果开启了关键词校验，关键词填 `BuildKit`。

## 产物

```
Build/
├─ last_build.json                        最近一次构建的记录，重传时会读取
├─ Android/1.2.0_1471834_release/
│   ├─ MyGame_1.2.0_1471834_release.aab
│   └─ MyGame_1.2.0_1471834_release.symbols.zip
└─ iOS/
    ├─ xcode/                             Unity 导出的 Xcode 工程（每次覆盖）
    └─ 1.2.0_1471834_release/
        ├─ MyGame_1.2.0_1471834_release.ipa
        └─ MyGame_1.2.0_1471834_release.app.dSYM.zip
```

符号文件必须和**同一个 build** 对应，正式版的整个文件夹建议归档保存。Unity 6 起 Android 符号会直接嵌进 AAB，Play 能自动识别；更早的版本需要在 Play Console 的 App Bundle 浏览器里手动上传 `.symbols.zip`。

## 常见问题

| 现象 | 解决 |
|---|---|
| `another Unity instance is running` | 项目正在被编辑器打开。关掉编辑器，或者配置独立打包目录 |
| iOS 签名失败 `No signing certificate` / `No profiles` | API Key 权限要选「管理」；检查 Team ID；确认 App Store Connect 里已经建好 App |
| Google Play 报 `Package not found` | 第一个 AAB 要在 Play Console 网页上手动上传 |
| 自检报 targetSdk 不达标 | 设置 `android.targetSdk`，并在 Unity 的 Android SDK 里装好对应版本；Unity 太旧时需要升级 |
| Windows 上 Gradle 报 `Filename longer than 260 characters` | 项目路径太深。把项目放到短路径下（如 `D:\Proj\MyGame`），或者配置独立打包目录到短路径 |
| 报「Xcode 工程后处理没有执行」 | 编辑器脚本不是按 iOS 平台编译的。先切换到 iOS 平台再打包 |
| 日志在哪 | `BuildKit/logs/unity_android.log`、`BuildKit/logs/unity_ios.log` |

## License

[MIT](LICENSE)
