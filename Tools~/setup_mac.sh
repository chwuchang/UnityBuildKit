#!/usr/bin/env bash
# BuildKit —— Mac 一次性环境安装（换新 Mac 时跑一次即可）
set -euo pipefail
KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$KIT"

echo "== 1/4 检查 Xcode"
if ! xcodebuild -version >/dev/null 2>&1; then
  echo "❌ 没检测到 Xcode。请："
  echo "   1) App Store 安装 Xcode，打开一次并同意协议"
  echo "   2) 终端执行：sudo xcode-select -s /Applications/Xcode.app"
  exit 1
fi
xcodebuild -version | head -1

echo "== 2/4 检查 Homebrew"
if ! command -v brew >/dev/null 2>&1; then
  echo "❌ 没有 Homebrew，请先安装（https://brew.sh），装完重新运行本脚本"
  exit 1
fi

echo "== 3/4 安装 Ruby、CocoaPods（系统自带 Ruby 太旧，不用它）"
brew list ruby >/dev/null 2>&1 || brew install ruby
export PATH="$(brew --prefix ruby)/bin:$PATH"
command -v pod >/dev/null 2>&1 || brew install cocoapods
ruby -v

echo "== 4/4 安装 fastlane（装在 BuildKit/vendor 里，不影响系统）"
gem list -i bundler >/dev/null 2>&1 || gem install bundler --no-document
bundle config set --local path vendor/bundle
bundle install

mkdir -p secrets
if [ ! -f secrets/secrets.json ]; then
  cp secrets.example/secrets.json secrets/secrets.json
  echo "⚠️  已生成 secrets/secrets.json，请填写密码和密钥（或从旧电脑拷贝整个 secrets 文件夹覆盖）"
fi

echo
echo "✅ 安装完成。接下来："
echo "   bash BuildKit/build.sh check    检查配置"
echo "   bash BuildKit/build.sh all      双端打包上传"
