# Shows a picture full screen so the glass theme has something to blur.
param([string]$Image)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.ShowInTaskbar = $false
$form.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$form.BackgroundImage = [System.Drawing.Image]::FromFile($Image)
$form.BackgroundImageLayout = 'Stretch'
[System.Windows.Forms.Application]::Run($form)
