# ============================================================
#  AWB 音频转换器 — 一键构建脚本
#
#  作用：把 tools\ 里的 vgmstream-cli 及其依赖打包成内嵌资源 awbtools.bin，
#        再用 C# 编译器生成单个 exe（含图标、清单、高 DPI 支持）。
#
#  用法：右键“使用 PowerShell 运行”，或在 PowerShell 里执行：
#        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#
#  依赖：Windows 自带的 .NET Framework 4.x（含 csc.exe），无需安装任何 SDK。
#
#  注意：本文件必须保存为【UTF-8 带 BOM】。PowerShell 5.1 会把无 BOM 的
#        UTF-8 当成 GBK 读取，导致中文路径乱码。
# ============================================================
$ErrorActionPreference = 'Stop'

# 脚本所在目录 = 仓库的 src\ 目录，仓库根目录是它的上一级
$srcDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root   = Split-Path -Parent $srcDir
$tools  = Join-Path $root 'tools'
$dist   = Join-Path $root 'dist'

$cs       = Join-Path $srcDir 'Program.cs'
$manifest = Join-Path $srcDir 'app.manifest'
$config   = Join-Path $srcDir 'app.config'
$icon     = Join-Path $srcDir 'app.ico'
$payload  = Join-Path $srcDir 'awbtools.bin'
$exeName  = 'AWB转换器.exe'
$exe      = Join-Path $dist $exeName

# 需要打进 exe 的文件
$names = @(
  'vgmstream-cli.exe',
  'avcodec-vgmstream-59.dll','avformat-vgmstream-59.dll','avutil-vgmstream-57.dll',
  'swresample-vgmstream-4.dll','libatrac9.dll','libcelt-0061.dll','libcelt-0110.dll',
  'libg719_decode.dll','libmpg123-0.dll','libspeex-1.dll','libvorbis.dll','xmp-vgmstream.dll'
)

Write-Host ''
Write-Host '=== AWB 音频转换器 构建 ===' -ForegroundColor Cyan
Write-Host ("  仓库根目录: " + $root)

# ---------- 1) 校验 ----------
Write-Host '[1/4] 校验 tools 目录'
$missing = @()
foreach ($n in $names) { if (-not (Test-Path (Join-Path $tools $n))) { $missing += $n } }
if ($missing.Count -gt 0) {
  Write-Host ('  缺少文件: ' + ($missing -join ', ')) -ForegroundColor Red
  Write-Host '  请从 vgmstream 官方发布包 vgmstream-win64.zip 补齐后再构建。' -ForegroundColor Yellow
  throw 'tools 目录不完整'
}
Write-Host ("  OK，共 " + $names.Count + " 个文件")

# ---------- 2) 生成内嵌资源包 ----------
Write-Host '[2/4] 打包内嵌资源 awbtools.bin'
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint32]0x31574241)          # 魔数 "ABW1"
$bw.Write([int32]$names.Count)
foreach ($n in $names) {
  $bytes = [System.IO.File]::ReadAllBytes((Join-Path $tools $n))
  $nb = [System.Text.Encoding]::UTF8.GetBytes($n)
  $bw.Write([int32]$nb.Length); $bw.Write($nb)
  $bw.Write([int32]$bytes.Length); $bw.Write($bytes)
  Write-Host ("  + {0,-30} {1,12:N0} 字节" -f $n, $bytes.Length)
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($payload, $ms.ToArray())
$bw.Close(); $ms.Close()
Write-Host ("  资源包大小: " + [math]::Round((Get-Item $payload).Length/1MB,2) + " MB")

# ---------- 3) 编译 ----------
Write-Host '[3/4] 编译 exe'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw '找不到 csc.exe，请确认已安装 .NET Framework 4.x' }
Write-Host ("  编译器: " + $csc)

# 用响应文件传参：路径含空格也不会被拆开。
# 注意：@( ) 数组里逗号的优先级低于 '+'，所以含拼接的元素必须用 $( ) 包起来，
#      否则一个参数会被拆成三个。
$rsp = Join-Path $srcDir 'csc.rsp'
$rspLines = @(
  '/nologo',
  '/target:winexe',
  '/platform:anycpu',
  '/optimize+',
  '/utf8output',
  '/codepage:65001',
  '/r:"System.dll"',
  '/r:"System.Drawing.dll"',
  '/r:"System.Windows.Forms.dll"',
  '/r:"System.Core.dll"',
  $('/out:"' + $exe + '"'),
  $('/resource:"' + $payload + '",awbtools.bin'),
  $('/win32manifest:"' + $manifest + '"')
)
if (Test-Path $icon) { $rspLines += $('/win32icon:"' + $icon + '"') }
$rspLines += $('"' + $cs + '"')
[System.IO.File]::WriteAllText($rsp, (($rspLines -join "`r`n") + "`r`n"), [System.Text.UTF8Encoding]::new($true))

$outLog = Join-Path $srcDir 'csc.out'
$errLog = Join-Path $srcDir 'csc.err'
$p = Start-Process -FilePath $csc -ArgumentList ('@' + $rsp) -Wait -PassThru -NoNewWindow `
       -RedirectStandardOutput $outLog -RedirectStandardError $errLog
$o = [System.IO.File]::ReadAllText($outLog); $e = [System.IO.File]::ReadAllText($errLog)
if ($o.Trim()) { Write-Host $o.Trim() }
if ($e.Trim()) { Write-Host $e.Trim() -ForegroundColor Red }
Remove-Item $outLog,$errLog,$rsp -Force -ErrorAction SilentlyContinue
if ($p.ExitCode -ne 0) { throw ('编译失败，csc 退出码 ' + $p.ExitCode) }

# ---------- 4) 收尾 ----------
Write-Host '[4/4] 完成'
if (Test-Path $config) {
  Copy-Item $config ($exe + '.config') -Force
  Write-Host ("  已生成 " + $exeName + ".config（高 DPI 支持，必须与 exe 同目录）")
}
$fi = Get-Item $exe
Write-Host ''
Write-Host ("  输出: " + $fi.FullName) -ForegroundColor Green
Write-Host ("  大小: " + [math]::Round($fi.Length/1MB,2) + " MB") -ForegroundColor Green
Write-Host ''
