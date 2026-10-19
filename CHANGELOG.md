# Changelog

## 1.0.0

- Android（AAB / APK）和 iOS 一键出包，自动签名并上传 Google Play / TestFlight
  One-command Android (AAB/APK) and iOS builds with automatic signing and upload to Google Play / TestFlight
- 双端共用 build 号，自动保证大于商店里已有的；先构建、后上传，上传失败可以只重传
  Shared build number, always higher than the stores'; build first, upload after; upload-only retry
- Profiles（dev / release …），配置字段拼写和类型校验
  Profiles with key/type validation
- 发包前自检：keystore 密码、targetSdk、Xcode 版本、隐私描述、场景、git 状态
  Preflight: keystore password, targetSdk, Xcode version, privacy strings, scenes, git status
- 打包时临时修改的设置打完自动还原，`ProjectSettings.asset` 不会出现 diff
  Temporary settings restored after build — no `ProjectSettings.asset` diffs
- 独立打包目录：不用关编辑器，不用切平台
  Build workspaces: keep the editor open, no platform switching
- 符号文件归档（Android symbols.zip / iOS dSYM），构建信息写进包里（`BuildKitInfo`）
  Symbols archived per build; build info embedded (`BuildKitInfo`)
- 扩展：`IBuildKitStep` C# 步骤、shell hooks
  Extensibility: `IBuildKitStep` C# steps, shell hooks
- 上传后打 git tag，通知（企业微信 / 钉钉 / 飞书 / Slack / Discord）
  Git tag after upload; notifications (WeCom / DingTalk / Feishu / Slack / Discord)
- 自动处理 Pods 签名团队、SSH 会话下解锁钥匙串
  Pods signing team fix; keychain unlock over SSH
