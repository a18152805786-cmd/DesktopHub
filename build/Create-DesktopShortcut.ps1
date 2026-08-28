<#
.SYNOPSIS
    Creates or updates the DesktopHub shortcut on the current user's Desktop.

.DESCRIPTION
    DesktopHub runs from its build output rather than an installed location, so
    there is no installer to place a launcher. This script creates
    %USERPROFILE%\Desktop\DesktopHub.lnk pointing at the Release executable.

    It is idempotent and refuses to clobber someone else's shortcut: if a
    DesktopHub.lnk already exists but points somewhere other than our executable,
    the script reports the conflict and exits without writing.

    Only the current user's Desktop is touched. Nothing is written to the Public
    Desktop, the Start Menu, the taskbar, or the Startup folder.

.PARAMETER ExePath
    The DesktopHub executable to point at. Defaults to the Release build inside
    this repository. The internal executable name is still "Desktop Frames.exe";
    only the shortcut the user sees is named DesktopHub.

.PARAMETER Force
    Overwrite a conflicting DesktopHub.lnk that points at a different target.
#>
[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\Code\Desktop Frames\bin\Release\net8.0-windows7.0\Desktop Frames.exe'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$exe = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Target executable not found: $exe`nBuild the Release configuration first."
    exit 1
}

$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DesktopHub.lnk'
$shell = New-Object -ComObject WScript.Shell

if (Test-Path -LiteralPath $lnk) {
    $existing = $shell.CreateShortcut($lnk)
    if ($existing.TargetPath -ne $exe -and -not $Force) {
        Write-Warning "DesktopHub.lnk already exists and points elsewhere:"
        Write-Warning "  existing target: $($existing.TargetPath)"
        Write-Warning "  our target:      $exe"
        Write-Warning "Refusing to overwrite. Re-run with -Force if this is intended."
        exit 2
    }
    # Recreate rather than edit in place, so Windows re-resolves the icon
    # instead of serving a cached one from the previous target.
    Remove-Item -LiteralPath $lnk -Force
    Start-Sleep -Milliseconds 300
}

$sc = $shell.CreateShortcut($lnk)
$sc.TargetPath       = $exe
$sc.WorkingDirectory = Split-Path $exe -Parent
$sc.Arguments        = ''
$sc.Description      = 'DesktopHub'
$sc.IconLocation     = "$exe,0"   # the exe embeds Resources\DesktopHubIcon.ico
$sc.Save()

# Ask the shell to re-read icon associations, otherwise a previously cached
# icon can survive on the desktop even though the .lnk is correct.
if (-not ('Shell32.Api' -as [type])) {
    Add-Type -Namespace Shell32 -Name Api -MemberDefinition @'
[DllImport("shell32.dll", CharSet=CharSet.Auto, SetLastError=true)]
public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
'@
}
[Shell32.Api]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)  # SHCNE_ASSOCCHANGED

$v = $shell.CreateShortcut($lnk)
[PSCustomObject]@{
    ShortcutPath     = $lnk
    Name             = [System.IO.Path]::GetFileNameWithoutExtension($lnk)
    TargetPath       = $v.TargetPath
    WorkingDirectory = $v.WorkingDirectory
    IconLocation     = $v.IconLocation
    TargetExists     = Test-Path -LiteralPath $v.TargetPath
}
