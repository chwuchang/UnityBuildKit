# BuildKit —— Windows 打包入口（只能出 Android；iOS 必须在 Mac 上打）
#   双击 build_android.bat，或：
#   powershell -ExecutionPolicy Bypass -File BuildKit\build.ps1 [-Profile dev] [-Format apk] [-NoUpload] [-AllowDirty] [-Lite]
#
# 两种模式：
#   - 完整模式：跑过 setup_windows.ps1（装了 Ruby + fastlane）后自动启用。和 Mac 一样：查商店 build 号、上传 Google Play、打 tag、通知
#   - 精简模式：没装 Ruby 时（或加 -Lite），只直接调用 Unity 出包，不上传
param(
    [Alias("Profile")] [string]$BuildProfile = "",
    [ValidateSet("aab", "apk", "")] [string]$Format = "",
    [switch]$NoUpload,
    # 用独立打包目录时，主项目有未提交改动也继续（只打包已提交的内容）
    [switch]$AllowDirty,
    [switch]$Lite
)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Kit  = $PSScriptRoot
$Root = Split-Path $Kit -Parent
$SecretsFile = Join-Path $Kit "secrets\secrets.json"

if (-not (Test-Path $SecretsFile)) {
    New-Item -ItemType Directory -Force (Join-Path $Kit "secrets") | Out-Null
    Copy-Item (Join-Path $Kit "secrets.example\secrets.json") $SecretsFile
    throw "已生成 BuildKit\secrets\secrets.json，请填写 keystore 密码后重新运行"
}

# 临时覆盖输出格式（如本机测试要 apk）；Unity 里读这个环境变量，apk 不会上传
if ($Format) { $env:BK_FORMAT_OVERRIDE = $Format }

# ───────────── 完整模式：交给 fastlane ─────────────
$hasFastlane = (Test-Path (Join-Path $Kit "vendor\bundle")) -and (Get-Command bundle -ErrorAction SilentlyContinue)
if (-not $Lite -and $hasFastlane) {
    $fl = @("exec", "fastlane", "android", "build")
    if ($BuildProfile) { $fl += "profile:$BuildProfile" }
    if ($NoUpload -or $Format -eq "apk") { $fl += "upload:false" }
    if ($AllowDirty) { $fl += "allow_dirty:true" }
    Push-Location $Kit
    try {
        & bundle @fl
        if ($LASTEXITCODE -ne 0) { throw "fastlane 失败（退出码 $LASTEXITCODE）" }
    } finally { Pop-Location }
    return
}

# ───────────── 精简模式：直接调用 Unity ─────────────
$Cfg = Get-Content (Join-Path $Kit "buildkit.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$Sec = Get-Content $SecretsFile -Raw -Encoding UTF8 | ConvertFrom-Json

function Find-Unity {
    if ($env:UNITY_PATH) { return $env:UNITY_PATH }
    if ($Sec.unityPath) { return $Sec.unityPath }
    $verFile = Join-Path $Root "ProjectSettings\ProjectVersion.txt"
    $ver = (Select-String -Path $verFile -Pattern "m_EditorVersion:\s*(\S+)").Matches[0].Groups[1].Value
    $cands = @("$env:ProgramFiles\Unity\Hub\Editor\$ver\Editor\Unity.exe")
    # Unity Hub 改过安装目录时，读 Hub 的配置
    $hubCfg = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path $hubCfg) {
        $p = Get-Content $hubCfg -Raw | ConvertFrom-Json
        if ($p) { $cands += (Join-Path $p "$ver\Editor\Unity.exe") }
    }
    foreach ($c in $cands) { if (Test-Path $c) { return $c } }
    throw "找不到 Unity $ver。请用 Unity Hub 安装该版本（含 Android 模块），或在 secrets.json 里填 unityPath"
}

# git 的进度信息走 stderr，PowerShell 5.1 会把它当成错误，所以这里临时放宽，只看退出码
function Invoke-Git([string]$Dir, [string[]]$GitArgs) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & git -C $Dir @GitArgs 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE; $ErrorActionPreference = $prev
    if ($code -ne 0) { throw "git $($GitArgs -join ' ') 失败（$Dir）：`n$($out -join "`n")" }
    return ($out -join "`n").Trim()
}

# 独立打包目录：secrets.json 里配了 androidWorkspace 后，Unity 在那份单独的 checkout 里打包，
# 你正在开发的项目可以继续开着 Unity 编辑器。该目录由 BuildKit 管理，每次打包前强制同步到主项目当前提交。
function Sync-Workspace([string]$Ws) {
    try { Invoke-Git $Root @("rev-parse", "--git-dir") | Out-Null }
    catch { throw "主项目不是 git 仓库（或没装 git），无法使用独立打包目录" }

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $wsFull = [IO.Path]::GetFullPath($Ws).TrimEnd('\')
    if ($wsFull -eq $rootFull -or $wsFull.StartsWith($rootFull + "\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "独立打包目录必须在主项目文件夹之外：$Ws"
    }

    $dirty = Invoke-Git $Root @("status", "--porcelain")
    if ($dirty) {
        $msg = "主项目有未提交的改动，独立打包目录只会打包已提交的内容：`n" + (($dirty -split "`n") | Select-Object -First 10 | Out-String)
        if ($AllowDirty) { Write-Host $msg -ForegroundColor Yellow }
        else { throw ($msg + "先提交，或加 -AllowDirty 忽略") }
    }

    $mark = Join-Path $Ws ".git\buildkit-workspace"
    if (-not (Test-Path (Join-Path $Ws ".git"))) {
        if ((Test-Path $Ws) -and (Get-ChildItem $Ws -Force | Select-Object -First 1)) { throw "$Ws 已存在且不是 git 仓库，请换一个空目录" }
        $source = $Root
        try { $source = Invoke-Git $Root @("remote", "get-url", "origin") } catch { }
        Write-Host "== 首次使用，克隆独立打包目录：$source → $Ws"
        New-Item -ItemType Directory -Force (Split-Path $Ws -Parent) | Out-Null
        Invoke-Git (Split-Path $Ws -Parent) @("clone", "--no-checkout", $source, $Ws) | Out-Null
        Set-Content -Path $mark -Value "managed by BuildKit" -Encoding ASCII
    } elseif (-not (Test-Path $mark)) {
        throw "$Ws 不是 BuildKit 创建的打包目录，为防止覆盖里面的改动，拒绝使用。`n如果确认它是专用打包目录，创建空文件 $mark 后重试"
    }

    $sha = Invoke-Git $Root @("rev-parse", "HEAD")
    $env:GIT_LFS_SKIP_SMUDGE = "1"
    try {
        Invoke-Git $Ws @("fetch", "--no-tags", $Root, "HEAD") | Out-Null      # 直接从主项目取，没 push 的提交也能打
        Invoke-Git $Ws @("checkout", "--force", "--detach", $sha) | Out-Null
    } finally { Remove-Item Env:GIT_LFS_SKIP_SMUDGE -ErrorAction SilentlyContinue }
    Invoke-Git $Ws @("clean", "-fd") | Out-Null   # 不带 -x：Library 等被忽略的缓存会保留
    if (Test-Path (Join-Path $Ws ".gitmodules")) { Invoke-Git $Ws @("submodule", "update", "--init", "--recursive") | Out-Null }
    $attrs = Join-Path $Ws ".gitattributes"
    if ((Test-Path $attrs) -and (Select-String -Path $attrs -Pattern "filter=lfs" -Quiet)) {
        try { Invoke-Git $Ws @("lfs", "pull") | Out-Null }
        catch { throw "Git LFS 文件拉取失败（新加的 LFS 文件需要先 push 到服务器）：`n$_" }
    }

    # secrets 不进 Git，从主项目拷过去
    $wsSecrets = Join-Path $Ws "BuildKit\secrets"
    if (Test-Path $wsSecrets) { Remove-Item $wsSecrets -Recurse -Force }
    Copy-Item (Join-Path $Kit "secrets") $wsSecrets -Recurse

    # 独立目录里是 detached HEAD，分支名传给 Unity（写进包里的构建信息用）
    try { $env:BK_GIT_BRANCH = Invoke-Git $Root @("rev-parse", "--abbrev-ref", "HEAD") } catch { }
    Write-Host "== 独立打包目录已同步到 $($sha.Substring(0, 8))：$Ws"
}

$Unity = Find-Unity

$Project = $Root
$Workspace = if ($env:BK_ANDROID_WORKSPACE) { $env:BK_ANDROID_WORKSPACE } elseif ($Sec.androidWorkspace) { $Sec.androidWorkspace } else { "" }
if ($Workspace) {
    if (-not [IO.Path]::IsPathRooted($Workspace)) { $Workspace = Join-Path $Kit $Workspace }
    $Workspace = [IO.Path]::GetFullPath($Workspace)
    Sync-Workspace $Workspace
    $Project = $Workspace
}

$OutDir = if ($Cfg.outputDir) { $Cfg.outputDir } else { "Build" }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $Root $OutDir }
$OutDir = [IO.Path]::GetFullPath($OutDir).TrimEnd('\')

$LogDir = Join-Path $Kit "logs"; New-Item -ItemType Directory -Force $LogDir | Out-Null
$Log = Join-Path $LogDir "unity_android.log"
if (Test-Path $Log) { Remove-Item $Log }

Write-Host "== Unity: $Unity"
Write-Host "== 项目：$Project"
Write-Host "== 构建 Android（精简模式，不上传），日志：$Log"

$argList = @(
    "-batchmode",
    "-projectPath", "`"$Project`"",
    "-buildTarget", "Android",
    "-executeMethod", "BuildKit.Builder.CLI",
    "-bkPlatform", "android",
    "-bkOutputDir", "`"$OutDir`"",
    "-logFile", "`"$Log`""
)
if ($BuildProfile) { $argList += @("-bkProfile", $BuildProfile) }

$proc = Start-Process -FilePath $Unity -ArgumentList ($argList -join " ") -Wait -PassThru -NoNewWindow
$text = if (Test-Path $Log) { Get-Content $Log -Raw -Encoding UTF8 } else { "" }

if ($proc.ExitCode -ne 0) {
    if ($text -match "another Unity instance") {
        Write-Host "👉 项目正被 Unity 编辑器打开，请先关掉 Unity 再打包（或在 secrets.json 配置 androidWorkspace）" -ForegroundColor Yellow
    }
    Write-Host "----- 日志末尾 -----"
    ($text -split "`n") | Select-Object -Last 60 | ForEach-Object { Write-Host $_ }
    throw "Unity 构建失败（退出码 $($proc.ExitCode)）"
}

if ($text -match "\[BuildKit\] OUTPUT=(.+)") {
    $out = $Matches[1].Trim()
    Write-Host "✅ 完成：$out" -ForegroundColor Green
    $symbols = [IO.Path]::ChangeExtension($out, $null).TrimEnd('.') + ".symbols.zip"
    if (Test-Path $symbols) { Write-Host "   符号文件：$symbols" }
    else { Write-Host "⚠️ 没有生成符号文件，线上崩溃将无法还原堆栈" -ForegroundColor Yellow }
    Start-Process explorer.exe "/select,`"$out`""
} else {
    Write-Host "✅ 构建结束，但日志里没找到产物路径，请查看 $Log"
}
