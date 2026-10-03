[CmdletBinding(SupportsShouldProcess=$true)]
param([switch]$Disable)
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'AIHOT.Desktop.exe'
$startup = [Environment]::GetFolderPath('Startup')
$link = Join-Path $startup 'AIHOT 桌面播报.lnk'
$shell = New-Object -ComObject WScript.Shell
if ($Disable) {
    if (!(Test-Path -LiteralPath $link)) { Write-Host '未设置开机启动。'; return }
    $existing = $shell.CreateShortcut($link)
    if ($existing.TargetPath -ne $app) { throw '启动项属于其他位置的 AIHOT，未作修改。请在对应程序目录中关闭。' }
    if ($PSCmdlet.ShouldProcess($link, '关闭 AIHOT 开机启动')) { Remove-Item -LiteralPath $link; Write-Host '已关闭开机启动。' }
    return
}
if (!(Test-Path -LiteralPath $app -PathType Leaf)) { throw '请将脚本放在 AIHOT.Desktop.exe 所在目录中运行。' }
if (Test-Path -LiteralPath $link) {
    $existing = $shell.CreateShortcut($link)
    if ($existing.TargetPath -ne $app) { throw '其他位置的 AIHOT 已设置开机启动。请先在原目录关闭，避免替换错误。' }
}
if ($PSCmdlet.ShouldProcess($link, '启用 AIHOT 开机启动')) {
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $app
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.IconLocation = (Join-Path $PSScriptRoot 'AIHOT.ico') + ',0'
    $shortcut.Description = '登录 Windows 后自动启动 AIHOT 桌面播报'
    $shortcut.Save()
    if ($shell.CreateShortcut($link).TargetPath -ne $app) { throw '启动项验证失败。' }
    Write-Host '已启用开机启动，下次登录 Windows 后生效。'
}
