# Runs the real app on a real Windows desktop: takes screenshots of each theme
# and drives the mouse (click a checkbox, drag, resize, menu, hide) to check the glass theme.
# Output: <Out>\*.png and <Out>\results.txt
param([string]$Exe, [string]$Out = "shots")

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class U {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
"@
[U]::SetProcessDPIAware() | Out-Null

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$ci = Join-Path $env:TEMP "ilhaera-ci"
New-Item -ItemType Directory -Force $ci | Out-Null
$dataFile = Join-Path $env:APPDATA "TodoWidget\todos.json"
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$script:results = New-Object System.Collections.Generic.List[string]
$script:app = $null

function Note($text) { Write-Host $text; $script:results.Add($text) }

function Shot($name) {
    $bmp = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $op = [System.Drawing.CopyPixelOperation]::SourceCopy -bor [System.Drawing.CopyPixelOperation]::CaptureBlt
    $g.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bmp.Size, $op)
    $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "shot $name"
}

function MoveTo($x, $y) {
    $nx = [uint32][math]::Round($x * 65535 / ($screen.Width - 1))
    $ny = [uint32][math]::Round($y * 65535 / ($screen.Height - 1))
    [U]::mouse_event(0x8001, $nx, $ny, 0, [UIntPtr]::Zero)   # absolute move
    Start-Sleep -Milliseconds 40
    [U]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 60
}
function Press { [U]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 120 }
function Release { [U]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250 }
function Click($x, $y) {
    MoveTo ($x - 4) ($y - 3)
    MoveTo $x $y
    Start-Sleep -Milliseconds 300
    Press; Release
}
function Drag($x, $y, $dx, $dy) {
    MoveTo ($x - 2) $y
    MoveTo $x $y
    Start-Sleep -Milliseconds 300
    Press
    for ($i = 1; $i -le 10; $i++) { MoveTo ([int]($x + $dx * $i / 10)) ([int]($y + $dy * $i / 10)) }
    Start-Sleep -Milliseconds 300
    Release
}

function Read-Json($path) {
    for ($i = 0; $i -lt 10; $i++) {
        try { return (Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { Start-Sleep -Milliseconds 200 }
    }
    throw "cannot read $path"
}
function Layout { Start-Sleep -Milliseconds 700; Read-Json (Join-Path $ci "layout.json") }
function Data { Read-Json $dataFile }
function DoneCount { @((Data).items | Where-Object { $_.done }).Count }

function Start-App($theme, $extra, $key) {
    $seedArgs = @((Join-Path $root "mac\ci\seed.py"), $theme) + $extra
    & python @seedArgs
    if ($LASTEXITCODE -ne 0) { throw "seed failed" }
    Remove-Item (Join-Path $ci "layout.json") -ErrorAction SilentlyContinue
    $env:ILHAERA_CI_DIR = $ci
    $env:ILHAERA_GLASS_KEY = $key
    $script:app = Start-Process -FilePath $Exe -PassThru
    for ($i = 0; $i -lt 40 -and !(Test-Path (Join-Path $ci "layout.json")); $i++) { Start-Sleep -Milliseconds 500 }
    if (!(Test-Path (Join-Path $ci "layout.json"))) { Note "!! app did not report its layout (theme=$theme)" }
    Start-Sleep -Seconds 3
}
function Stop-App {
    if ($script:app -and !$script:app.HasExited) { Stop-Process -Id $script:app.Id -Force }
    Start-Sleep -Seconds 1
    MoveTo 20 ($screen.Height - 60)
}

function Verdict($ok) { if ($ok) { "OK" } else { "FAIL" } }

# Click, drag, resize, open the menu and hide, and say whether each worked.
function Test-Mouse($label) {
    $l = Layout
    Note "[$label] glass=$($l.glass) widget=$($l.widget -join ',') pane=$($l.pane -join ',')"

    $before = DoneCount
    Click $l.check[0] $l.check[1]
    Start-Sleep -Milliseconds 800
    $after = DoneCount
    Note "[$label] checkbox click: done $before -> $after  $(Verdict ($after -eq $before + 1))"
    Shot "$label-checked"

    $a = (Layout).widget
    Drag $l.drag[0] $l.drag[1] (-120) 60
    $l = Layout; $b = $l.widget
    Note "[$label] drag by background: ($($a[0]),$($a[1])) -> ($($b[0]),$($b[1]))  $(Verdict (($b[0] - $a[0]) -eq -120 -and ($b[1] - $a[1]) -eq 60))"
    if ($l.glass) { Note "[$label] glass pane follows: pane=$($l.pane -join ',')  $(Verdict (($l.pane -join ',') -eq ($l.widget -join ',')))" }
    Shot "$label-moved"

    Drag $l.size[0] $l.size[1] 40 30
    $l = Layout; $c = $l.widget
    Note "[$label] resize by corner: $($b[2])x$($b[3]) -> $($c[2])x$($c[3])  $(Verdict (($c[2] - $b[2]) -eq 40 -and ($c[3] - $b[3]) -eq 30))"
    if ($l.glass) { Note "[$label] glass pane follows: pane=$($l.pane -join ',')  $(Verdict (($l.pane -join ',') -eq ($l.widget -join ',')))" }

    Click $l.menu[0] $l.menu[1]
    Start-Sleep -Milliseconds 900
    Shot "$label-menu"
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 500

    $l = Layout
    Click $l.hide[0] $l.hide[1]
    Start-Sleep -Milliseconds 900
    Note "[$label] hide button: trayHintShown=$((Data).settings.trayHintShown)  $(Verdict ((Data).settings.trayHintShown -eq $true))"
    Shot "$label-hidden"
    Note "[$label] mouse presses that went through the glass pane: $((Layout).forwarded)"
}

function Scene($name, $block) {
    try { & $block } catch { Note "!! $name failed: $_" } finally { Stop-App }
}

# ---- environment ----
$os = Get-CimInstance Win32_OperatingSystem
Note "os: $($os.Caption) build $($os.BuildNumber), screen $($screen.Width)x$($screen.Height)"
Note "explorer running: $([bool](Get-Process explorer -ErrorAction SilentlyContinue)), dwm running: $([bool](Get-Process dwm -ErrorAction SilentlyContinue))"

$pers = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
if (!(Test-Path $pers)) { New-Item -Path $pers -Force | Out-Null }
function Set-Look($lightApps) {
    Set-ItemProperty -Path $pers -Name EnableTransparency -Value 1 -Type DWord
    Set-ItemProperty -Path $pers -Name AppsUseLightTheme -Value $lightApps -Type DWord
    $r = [UIntPtr]::Zero
    [U]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, "ImmersiveColorSet", 2, 2000, [ref]$r) | Out-Null
    Start-Sleep -Seconds 1
}
Set-Look 1

$backdrop = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
    "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "backdrop.ps1"), "-Image", (Join-Path $root "mac\ci\backdrop.png"))
Start-Sleep -Seconds 4
Shot "backdrop"

# ---- scenes ----
Scene "light" { Start-App "light" @() ""; Shot "light"; Test-Mouse "light" }
Scene "glass" { Start-App "glass" @("glassTint=35") ""; Shot "glass"; Test-Mouse "glass" }
Scene "glass-symmetric" { Start-App "glass" @("glassTint=35") "symmetric"; Shot "glass-sym"; Test-Mouse "glass-sym" }
Scene "glass-10" { Start-App "glass" @("glassTint=10") ""; Shot "glass-10" }
Scene "glass-80" { Start-App "glass" @("glassTint=80") ""; Shot "glass-80" }
Scene "done" { Start-App "glass" @("glassTint=35", "showDone=true", "accentColor=#1E7A57", "markerColor=#FFEC85") ""; Shot "glass-default-colors" }
Set-Look 0
Scene "glass-dark" { Start-App "glass" @("glassTint=35") ""; Shot "glass-dark" }
Scene "dark" { Start-App "dark" @() ""; Shot "dark" }
Set-Look 1

if ($backdrop -and !$backdrop.HasExited) { Stop-Process -Id $backdrop.Id -Force }
$log = Join-Path $env:APPDATA "TodoWidget\error.log"
if (Test-Path $log) { Copy-Item $log (Join-Path $Out "error.log"); Note "!! the app logged errors (error.log)" }
$script:results | Set-Content (Join-Path $Out "results.txt") -Encoding UTF8
exit 0
