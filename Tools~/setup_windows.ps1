# BuildKit —— Windows 一次性环境安装（可选）
# 装好 Ruby + fastlane 后，Windows 上也能上传 Google Play、查商店 build 号、打 tag、发通知。
# 不装也能用：build_android.bat 会退回精简模式，只出包不上传。
#   powershell -ExecutionPolicy Bypass -File BuildKit\setup_windows.ps1
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Kit = $PSScriptRoot
Set-Location $Kit

Write-Host "== 1/3 检查 Ruby"
if (-not (Get-Command ruby -ErrorAction SilentlyContinue)) {
    Write-Host "❌ 没有 Ruby。请安装 Ruby + DevKit（3.x），装完重新打开终端再运行本脚本：" -ForegroundColor Yellow
    Write-Host "   winget install RubyInstallerTeam.RubyWithDevKit.3.3"
    Write-Host "   或从 https://rubyinstaller.org 下载"
    exit 1
}
ruby -v

Write-Host "== 2/3 安装 bundler"
gem list -i bundler *> $null
if ($LASTEXITCODE -ne 0) { gem install bundler --no-document; if ($LASTEXITCODE -ne 0) { throw "安装 bundler 失败" } }

Write-Host "== 3/3 安装 fastlane（装在 BuildKit\vendor 里，不影响系统）"
bundle config set --local path vendor/bundle
bundle install
if ($LASTEXITCODE -ne 0) { throw "bundle install 失败" }

New-Item -ItemType Directory -Force (Join-Path $Kit "secrets") | Out-Null
if (-not (Test-Path (Join-Path $Kit "secrets\secrets.json"))) {
    Copy-Item (Join-Path $Kit "secrets.example\secrets.json") (Join-Path $Kit "secrets\secrets.json")
    Write-Host "⚠️  已生成 secrets\secrets.json，请填写密码和密钥（或从旧电脑拷贝整个 secrets 文件夹覆盖）" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "✅ 安装完成。之后双击 build_android.bat 就会自动走完整模式（可上传 Google Play）。" -ForegroundColor Green
