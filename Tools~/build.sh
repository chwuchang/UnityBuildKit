#!/usr/bin/env bash
# BuildKit —— Mac 打包入口
#   bash BuildKit/build.sh all              双端：先构建 Android + iOS，都成功后再统一上传（默认）
#   bash BuildKit/build.sh ios              只打 iOS
#   bash BuildKit/build.sh android          只打 Android
#   bash BuildKit/build.sh upload [ios|android]   重传最近一次构建的包（上传失败时用，不用重新打包）
#   bash BuildKit/build.sh check            只检查环境和配置
#   末尾可追加：profile:dev（用 dev profile）  upload:false（只出包不上传）
#              allow_dirty:true（用独立打包目录时，主项目有未提交改动也继续）
set -euo pipefail
KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$KIT"

command -v brew >/dev/null 2>&1 && export PATH="$(brew --prefix ruby)/bin:$PATH"
if [ ! -d vendor/bundle ]; then
  echo "首次使用，请先运行：bash BuildKit/setup_mac.sh"
  exit 1
fi

export LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8   # fastlane 需要 UTF-8 环境
CMD="${1:-all}"; shift || true
case "$CMD" in
  all)     bundle exec fastlane all "$@" ;;
  ios)     bundle exec fastlane ios build "$@" ;;
  android) bundle exec fastlane android build "$@" ;;
  upload)
    ONLY="all"
    if [ $# -gt 0 ] && [[ "$1" != *:* ]]; then ONLY="$1"; shift; fi
    bundle exec fastlane upload only:"$ONLY" "$@" ;;
  check)   bundle exec fastlane check "$@" ;;
  *) sed -n '2,10p' "$0"; exit 1 ;;
esac
