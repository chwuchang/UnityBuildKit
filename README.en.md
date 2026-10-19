# UnityBuildKit

English | [中文](README.md)

One-command Unity builds with upload to Google Play and TestFlight. Configure once, then every release is a single command.

```bash
bash BuildKit/build.sh all        # build Android + iOS and upload both
```

## What it solves

| Pain point | What BuildKit does |
|---|---|
| Every release means bumping version/build numbers, switching signing settings and clicking through Xcode | One command builds, signs and uploads. Build numbers are generated automatically and always higher than what the stores already have |
| A new Mac means reinstalling certificates and provisioning profiles | Automatic signing via an App Store Connect API key. Moving to a new machine = copying the `secrets/` folder |
| A wrong keystore password only shows up after 10+ minutes of IL2CPP compilation | Preflight checks passwords, alias, targetSdk, Xcode version, privacy strings and scenes up front, so problems surface within seconds |
| You must close the editor to build, and switching platforms re-imports everything for ages | Build workspaces: builds run in a separate checkout (one per platform) while your editor stays open |
| A failed upload means rebuilding | Both platforms are built before anything is uploaded; a failed upload can be retried on its own |
| Every build leaves a diff in `ProjectSettings.asset` | Settings changed for a build are restored afterwards |
| Production crashes can't be symbolicated | One folder per build with the package and its symbols (Android symbols.zip / iOS dSYM) |
| dev and release builds need different settings, switched by hand | Profiles: `profile:dev` switches package name, defines, Development Build and upload targets |
| You can't tell which commit a build came from | Version, build number, commit and profile are embedded and readable at runtime via `BuildKitInfo` |
| Project-specific build steps (Addressables, hot-update, data tables…) | Implement `IBuildKitStep` and it runs automatically; shell hooks are also available |
| Pods with resource bundles fail signing; `errSecInternalComponent` when building over SSH | The Pods project gets the development team set automatically; the keychain is unlocked in SSH sessions |

## Installation

Requirements: Unity 2021.3+. iOS builds require a Mac (Xcode 26+).

1. **Window → Package Manager → + → Add package from git URL**:
   ```
   https://github.com/chwuchang/UnityBuildKit.git
   ```
2. Menu **BuildKit → 初始化项目** (Initialize project). This creates a `BuildKit/` folder in the project root (config and command-line scripts, commit it to Git), pre-filled from your current Player Settings.
3. Command-line environment:
   - Mac: `bash BuildKit/setup_mac.sh` (installs Ruby, CocoaPods, fastlane)
   - Windows (optional): `powershell -ExecutionPolicy Bypass -File BuildKit\setup_windows.ps1` (installs fastlane for Google Play uploads). Without it you can still build, just not upload.

## One-time setup

**1. Fill in `BuildKit/buildkit.json`**: package name, Team ID and so on. See [Configuration](#configuration).

**2. Secrets**: copy `BuildKit/secrets.example/secrets.json` to `BuildKit/secrets/` and fill it in. `secrets/` is never committed. Copy the whole folder when you move machines.

| File | Purpose | How to get it |
|---|---|---|
| `release.keystore` | Android signing | Use your existing one, or create one with `keytool -genkeypair -v -keystore release.keystore -alias release -keyalg RSA -keysize 2048 -validity 10000`. **Back it up.** |
| `AuthKey_XXXXXXXXXX.p8` | iOS signing and upload | App Store Connect → Users and Access → Integrations → App Store Connect API. Create a key with the Admin role (it can only be downloaded once) and put the Key ID and Issuer ID in secrets.json |
| `play-service-account.json` | Google Play upload | Create a service account in Google Cloud and download a JSON key, then invite that account in Play Console → Users and permissions with release permissions |

**3. Create the app in each store**: create the app in App Store Connect. For Google Play, the first AAB has to be uploaded manually in the web console (a Google requirement); after that, uploads are automatic.

**4. Check**: menu **BuildKit → 发包前检查** (Preflight check), or `bash BuildKit/build.sh check`.

## Usage

**Unity editor**: menu **BuildKit → 打开面板** (Open panel). Pick a profile and click Build Android / Export iOS. If the active platform is wrong, BuildKit switches it first and asks you to click again, because scripts must recompile for the new platform before platform-specific post-processors (including SDKs') will run.

**Mac** (both platforms, with upload):

```bash
bash BuildKit/build.sh all                     # Android + iOS: build both, then upload both
bash BuildKit/build.sh ios                     # iOS only
bash BuildKit/build.sh android profile:dev     # Android with the dev profile
bash BuildKit/build.sh ios upload:false        # build without uploading
bash BuildKit/build.sh upload ios              # re-upload the last iOS build (no rebuild)
bash BuildKit/build.sh check                   # check environment and config only
```

**Windows** (Android only):

```bat
BuildKit\build_android.bat                     :: AAB; uploads to Google Play if fastlane is installed and configured
BuildKit\build_android.bat -Profile dev        :: use the dev profile
BuildKit\build_android.bat -NoUpload           :: build only
BuildKit\build_android_apk.bat                 :: APK for device testing
```

**New release**: change `version` in `buildkit.json`, nothing else. The build number counts minutes since 2024-01-01, so every machine computes the same value, and all platforms share it.

**After updating the package**: menu **BuildKit → 更新命令行脚本** (Update scripts). `buildkit.json` and `secrets/` are left untouched.

## Configuration

`BuildKit/buildkit.json`, committed to Git. Misspelled keys and values of the wrong type are reported by the preflight check.

| Key | Description | Default |
|---|---|---|
| `productName` / `companyName` / `version` | App name / company / public version | |
| `scenes` | Scenes to build; empty = enabled scenes in Build Settings | `[]` |
| `outputDir` | Output folder (relative to the project root) | `Build` |
| `development` | Development Build | `false` |
| `defines` | Extra scripting defines for this build only (Player Settings untouched) | `[]` |
| `embedBuildInfo` | Embed build info into the build | `true` |
| `git.requireClean` | Refuse to build with uncommitted changes | `false` |
| `git.tagOnUpload` / `git.pushTag` | Tag `v{version}-{build}` after a successful upload / and push it | `false` |
| `android.packageName` | Package name | |
| `android.format` | `aab` (for Play) or `apk` | `aab` |
| `android.il2cpp` / `android.architectures` | Scripting backend / architectures, e.g. `ARM64,ARMv7` | `true` / `ARM64` |
| `android.minSdk` / `android.targetSdk` | 0 = keep Unity's setting | `0` |
| `android.playMinTargetSdk` | Minimum targetSdk Google Play currently requires; uploads are blocked below it | `36` |
| `android.keystoreFile` / `android.keyAlias` | Keystore (relative to `BuildKit/`) / alias | `secrets/release.keystore` / `release` |
| `android.uploadToPlay` / `playTrack` / `playReleaseStatus` | Upload or not / track / `draft` or `completed` | `true` / `internal` / `draft` |
| `ios.bundleId` / `ios.teamId` | Bundle ID / 10-character Team ID | |
| `ios.minIOS` / `ios.minXcode` | Minimum iOS / minimum Xcode the App Store requires | `13.0` / `26.0` |
| `ios.exportMethod` | `app-store` / `ad-hoc` / `development` | `app-store` |
| `ios.privacy` | Privacy usage strings, e.g. `[{"key": "NSCameraUsageDescription", "value": "Scan QR codes"}]` | `[]` |
| `ios.usesNonExemptEncryption` | Export compliance; `false` if you only use HTTPS, so TestFlight stops asking | `false` |
| `ios.pushNotifications` / `ios.signInWithApple` | Add the capability automatically | `false` |
| `ios.uploadToTestFlight` | Upload to TestFlight or not | `true` |
| `notify.onSuccess` / `notify.onFailure` | Send a notification on success / failure | `true` |
| `hooks.postBuild` / `hooks.postUpload` | Shell command to run after a successful build / upload | |

Store requirements rise every year; update `playMinTargetSdk` and `minXcode` when they do.

### Profiles

Top-level keys are shared; each profile lists only what differs. Objects merge key by key, while arrays and plain values are replaced as a whole.

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

Lives in `BuildKit/secrets/` and is never committed. Every entry can be overridden by an environment variable (handy for CI).

| Key | Environment variable | Description |
|---|---|---|
| `keystorePassword` / `keyAliasPassword` | `BK_KEYSTORE_PASSWORD` / `BK_KEYALIAS_PASSWORD` | Keystore / key password |
| `ascKeyId` / `ascIssuerId` / `ascKeyFile` | `BK_ASC_KEY_ID` / `BK_ASC_ISSUER_ID` / `BK_ASC_KEY_FILE` | App Store Connect API key |
| `playJsonKeyFile` | `BK_PLAY_JSON` | Google Play service account JSON |
| `unityPath` | `UNITY_PATH` | Empty = find the project's Unity version in Unity Hub |
| `androidWorkspace` / `iosWorkspace` | `BK_ANDROID_WORKSPACE` / `BK_IOS_WORKSPACE` | Build workspaces, see below |
| `keychainPassword` | `BK_KEYCHAIN_PASSWORD` | Unlocks the login keychain when building iOS over SSH |
| `notifyWebhook` | `BK_NOTIFY_WEBHOOK` | Notification webhook, see below |

## Build workspaces

Set local paths in secrets.json:

```json
"androidWorkspace": "D:/BuildWS/MyGame-android",
"iosWorkspace": "/Users/me/BuildWS/MyGame-ios"
```

With these set:
- The first run does a `git clone` into that folder. Before every build after that, the folder is force-synced to your project's current commit (fetched locally, so unpushed commits work too).
- The `Library` cache is kept, so only the first build is slow.
- `secrets/` is copied over, and output still goes to your project's `Build/`.

Notes:
- Only **committed** content is built. With uncommitted changes the build stops; add `allow_dirty:true` (Windows: `-AllowDirty`) to proceed anyway.
- BuildKit only touches folders it cloned itself, and the folder must be outside your project, so your working copy can't be overwritten.

## Extending

**C# build steps**: add a class to any Editor assembly; no registration needed:

```csharp
public class MyStep : BuildKit.BuildKitStep
{
    public override int Order => 0;                       // lower runs first

    public override void PreBuild(BuildKit.BuildContext ctx)
    {
        if (ctx.Profile == "release") ctx.Defines.Add("NO_CHEATS");               // define for this build only
        ctx.Set(() => PlayerSettings.stripEngineCode,                             // temporary setting, restored afterwards
                v => PlayerSettings.stripEngineCode = v, true);
        ctx.AddCleanup(() => { /* runs after the build, success or failure */ });
    }

    public override void PostBuild(BuildKit.BuildContext ctx) { /* ctx.OutputPath is the artifact */ }
}
```

Import the **Build Steps** sample from Package Manager for working examples: Addressables build, EDM4U dependency resolution, per-profile server URL.

**Shell hooks**: `hooks.postBuild` / `hooks.postUpload` run in the project root with these variables: `BK_PLATFORM`, `BK_PROFILE`, `BK_VERSION`, `BK_BUILD_NUMBER`, `BK_ARTIFACT`, `BK_SYMBOLS`, `BK_COMMIT`, `BK_OUTPUT_DIR`.

## Build info at runtime

```csharp
versionLabel.text = BuildKit.BuildKitInfo.Summary;   // e.g. 1.2.0 (1471834) release a1b2c3d
// Fields: Version / BuildNumber / Profile / Commit / Branch / Dirty / Development / BuiltAt
```

## Notifications

Put a bot webhook URL in `notifyWebhook`. WeCom, DingTalk, Feishu/Lark, Slack and Discord are detected from the URL; any other URL receives `{"text": "..."}`. If your DingTalk bot uses keyword security, set the keyword to `BuildKit`.

## Output

```
Build/
├─ last_build.json                        record of the last build (used by re-upload)
├─ Android/1.2.0_1471834_release/
│   ├─ MyGame_1.2.0_1471834_release.aab
│   └─ MyGame_1.2.0_1471834_release.symbols.zip
└─ iOS/
    ├─ xcode/                             exported Xcode project (overwritten each time)
    └─ 1.2.0_1471834_release/
        ├─ MyGame_1.2.0_1471834_release.ipa
        └─ MyGame_1.2.0_1471834_release.app.dSYM.zip
```

Symbols only match the **exact same build**, so archive the whole folder for every release. Since Unity 6, Android symbols are embedded in the AAB and Play picks them up automatically; with older versions, upload `.symbols.zip` manually in Play Console's App bundle explorer.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `another Unity instance is running` | The project is open in the editor. Close it, or set up a build workspace |
| iOS signing fails with `No signing certificate` / `No profiles` | The API key needs the Admin role. Check the Team ID and make sure the app exists in App Store Connect |
| Google Play: `Package not found` | Upload the first AAB manually in Play Console |
| Preflight says targetSdk is too low | Set `android.targetSdk` and install that platform in Unity's Android SDK; very old Unity versions need upgrading |
| Gradle on Windows: `Filename longer than 260 characters` | The project path is too deep. Move it to a short path (e.g. `D:\Proj\MyGame`) or point a build workspace at one |
| "Xcode post-process did not run" | Editor scripts weren't compiled for iOS. Switch to the iOS platform before building |
| Where are the logs? | `BuildKit/logs/unity_android.log`, `BuildKit/logs/unity_ios.log` |

## License

[MIT](LICENSE)
