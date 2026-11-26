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
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
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
    $g.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bmp.Size)
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
function Pixel($x, $y) {
    $bmp = New-Object System.Drawing.Bitmap 1, 1
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $c = $bmp.GetPixel(0, 0)
    $g.Dispose(); $bmp.Dispose()
    "$($c.R),$($c.G),$($c.B)"
}
function Wheel($delta) {
    [U]::mouse_event(0x0800, 0, 0, [int]$delta, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
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

function Start-App($theme, $extra, $mode) {
    $seedArgs = @((Join-Path $root "mac\ci\seed.py"), $theme) + $extra
    & python @seedArgs
    if ($LASTEXITCODE -ne 0) { throw "seed failed" }
    Remove-Item (Join-Path $ci "layout.json") -ErrorAction SilentlyContinue
    $env:ILHAERA_CI_DIR = $ci
    $env:ILHAERA_GLASS_MODE = $mode   # "" = let the app find what works on this machine
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

function MaxDiff($a, $b) {
    $x = $a.Split(','); $y = $b.Split(',')
    $m = 0
    for ($i = 0; $i -lt 3; $i++) { $d = [math]::Abs([int]$x[$i] - [int]$y[$i]); if ($d -gt $m) { $m = $d } }
    $m
}
# The glass must still tint what is behind it: compare a spot on the glass with a spot just outside the widget.
function GlassVsOutside($label, $x, $y) {
    $in = Pixel ($x + 6) ($y + 200)
    $out = Pixel ($x - 8) ($y + 200)
    Note "[$label] glass $in vs outside $out  $(Verdict ((MaxDiff $in $out) -ge 12))"
}

# Click, drag, resize, open the menu and hide, and say whether each worked.
function Test-Mouse($label) {
    $l = Layout
    Note "[$label] glass=$($l.glass) mode=$($l.mode) widget=$($l.widget -join ',') pane=$($l.pane -join ',')"

    # hover a row on its empty part: the row should light up (in the glass theme this goes through the pane)
    $p0 = Pixel $l.row[0] $l.row[1]
    MoveTo ($l.row[0] - 5) $l.row[1]
    MoveTo $l.row[0] $l.row[1]
    Start-Sleep -Milliseconds 600
    $p1 = Pixel $l.row[0] $l.row[1]
    Note "[$label] row hover: pixel $p0 -> $p1  $(Verdict ($p0 -ne $p1))"
    Shot "$label-hover"

    # wheel over an empty part of the list (a section title row): the list should scroll
    $y0 = $l.check[1]
    MoveTo $l.row[0] ($l.row[1] - 30)
    Start-Sleep -Milliseconds 300
    Wheel (-120)
    $y1 = (Layout).check[1]
    Note "[$label] wheel over list: first row y $y0 -> $y1  $(Verdict ($y1 -lt $y0))"
    Wheel 120
    Wheel 120
    $l = Layout

    $before = DoneCount
    Click $l.check[0] $l.check[1]
    Start-Sleep -Milliseconds 800
    $after = DoneCount
    Note "[$label] checkbox click: done $before -> $after  $(Verdict ($after -eq $before + 1))"
    Shot "$label-checked"

    $a = (Layout).widget
    $dx = $l.drag[0]; $dy = $l.drag[1]
    MoveTo ($dx - 2) $dy
    MoveTo $dx $dy
    Start-Sleep -Milliseconds 300
    Press
    for ($i = 1; $i -le 5; $i++) { MoveTo ($dx - 12 * $i) ($dy + 6 * $i) }
    Start-Sleep -Milliseconds 600
    if ($l.glass) {
        # half way, mouse button still down. Where the widget is depends on the Windows setting
        # "show window contents while dragging" (off = only an outline moves), so ask the app.
        $mid = (Layout).widget
        GlassVsOutside "$label while dragging" $mid[0] $mid[1]
        Shot "$label-dragging"
    }
    for ($i = 6; $i -le 10; $i++) { MoveTo ($dx - 12 * $i) ($dy + 6 * $i) }
    Start-Sleep -Milliseconds 300
    Release
    $l = Layout; $b = $l.widget
    Note "[$label] drag by background: ($($a[0]),$($a[1])) -> ($($b[0]),$($b[1]))  $(Verdict (($b[0] - $a[0]) -eq -120 -and ($b[1] - $a[1]) -eq 60))"
    if ($l.glass) { Note "[$label] glass pane follows: pane=$($l.pane -join ',')  $(Verdict (($l.pane -join ',') -eq ($l.widget -join ',')))" }
    if ($l.glass) { GlassVsOutside "$label after move" $b[0] $b[1] }
    Shot "$label-moved"

    Drag $l.size[0] $l.size[1] 40 0
    $l = Layout; $c = $l.widget
    Note "[$label] resize by right edge: $($b[2])x$($b[3]) -> $($c[2])x$($c[3])  $(Verdict (($c[2] - $b[2]) -eq 40 -and $c[3] -eq $b[3]))"
    if ($l.glass) {
        Note "[$label] glass pane follows: pane=$($l.pane -join ',')  $(Verdict (($l.pane -join ',') -eq ($l.widget -join ',')))"
        GlassVsOutside "$label after resize" $c[0] $c[1]
    }

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
function GlassShot($name, $tint, $mode) {
    Scene $name { Start-App "glass" @("glassTint=$tint") $mode; Note "[$name] mode=$((Layout).mode)"; Shot $name }
}
Scene "light" { Start-App "light" @() ""; Shot "light"; Test-Mouse "light" }
Scene "glass" { Start-App "glass" @("glassTint=35") ""; Shot "glass"; Test-Mouse "glass" }
GlassShot "glass-10" 10 ""
GlassShot "glass-80" 80 ""
# each way of drawing the glass, forced, to see what this Windows does with it
GlassShot "mode-acrylic" 35 "acrylic"
GlassShot "mode-blur" 35 "blur"
GlassShot "mode-blur-10" 10 "blur"
GlassShot "mode-blur-80" 80 "blur"
GlassShot "mode-plain" 35 "plain"
GlassShot "mode-plain-10" 10 "plain"
# no blur: the desktop shows through as it is (tint 0 = fully clear). The mouse must still work on the empty parts.
Scene "clear" { Start-App "glass" @("glassTint=0", "glassBlur=false") ""; Note "[clear] mode=$((Layout).mode)"; Shot "clear"; Test-Mouse "clear" }
Scene "clear-35" { Start-App "glass" @("glassTint=35", "glassBlur=false") ""; Note "[clear-35] mode=$((Layout).mode)"; Shot "clear-35" }
Scene "done" { Start-App "glass" @("glassTint=35", "showDone=true", "accentColor=#1E7A57", "markerColor=#FFEC85") ""; Shot "glass-default-colors" }
Set-Look 0
Scene "clear-dark" { Start-App "glass" @("glassTint=0", "glassBlur=false") ""; Shot "clear-dark" }
Scene "glass-dark" { Start-App "glass" @("glassTint=35") ""; Note "[glass-dark] mode=$((Layout).mode)"; Shot "glass-dark" }
Scene "dark" { Start-App "dark" @() ""; Shot "dark" }
Set-Look 1

if ($backdrop -and !$backdrop.HasExited) { Stop-Process -Id $backdrop.Id -Force }
$log = Join-Path $env:APPDATA "TodoWidget\error.log"
if (Test-Path $log) { Copy-Item $log (Join-Path $Out "error.log"); Note "!! the app logged errors (error.log)" }
$script:results | Set-Content (Join-Path $Out "results.txt") -Encoding UTF8
exit 0
