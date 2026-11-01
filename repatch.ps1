# 批改邦喊话桥接 - app.asar 自动重注入脚本
# 用途：批改邦升级后 app.asar 会被覆盖，本脚本检测到注入标记缺失时自动重新注入。
# 由计划任务 PGB-ShoutBridge-Repatch 在登录时和每 30 分钟调用一次。
$ErrorActionPreference = 'Continue'

$res     = 'C:\Users\SKY\AppData\Local\Programs\pigaibang\resources\app.asar'
$exe     = 'C:\Users\SKY\AppData\Local\Programs\pigaibang\pigaibang.exe'
$toolDir = 'D:\DeepSeek HARNESS\pgb-bridge'
$node    = 'C:\Program Files\nodejs\node.exe'
$logFile = Join-Path $toolDir 'repatch.log'

function Log([string]$msg) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

if (-not (Test-Path $res)) { Log 'app.asar not found, skip'; exit 0 }

$txt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($res))
if ($txt.Contains('PGB Shout Bridge injection')) { exit 0 }

Log 'app.asar not patched (upgrade?), re-injecting'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bdir  = "D:\ClassIsland-Backup\pigaibang-$stamp"
New-Item -ItemType Directory -Path $bdir -Force | Out-Null
Copy-Item $res (Join-Path $bdir 'app.asar.orig') -Force

$tmp = Join-Path $env:TEMP 'pgb-app-current.bin'
$out = Join-Path $env:TEMP 'pgb-app-patched.asar'
Copy-Item $res $tmp -Force

Get-Process pigaibang -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

& $node (Join-Path $toolDir 'patch-asar.js') $tmp $out (Join-Path $toolDir 'injection.js') | Out-Null
if (-not (Test-Path $out)) { Log 'patch failed: no output'; exit 1 }

Copy-Item $out $res -Force
Log 'app.asar re-injected'

Remove-Item Env:ELECTRON_RUN_AS_NODE -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
Log 'pigaibang restarted'
