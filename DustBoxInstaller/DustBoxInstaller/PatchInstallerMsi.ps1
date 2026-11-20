param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
    throw "Installer package not found: $MsiPath"
}

$resolvedMsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($resolvedMsiPath, 1)

$query = $database.OpenView("SELECT `Property` FROM `Property` WHERE `Property` = 'DISABLEADVTSHORTCUTS'")
$query.Execute()
$existingProperty = $query.Fetch()
$query.Close()

if ($null -eq $existingProperty) {
    $query = $database.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES ('DISABLEADVTSHORTCUTS', '1')")
}
else {
    $query = $database.OpenView("UPDATE `Property` SET `Value` = '1' WHERE `Property` = 'DISABLEADVTSHORTCUTS'")
}

$query.Execute()
$query.Close()

$query = $database.OpenView('SELECT `Shortcut` FROM `Shortcut` WHERE `Shortcut` = ''_003A2787A59B4F41BCDDC9F7D3CD1D97''')
$query.Execute()
$shortcut = $query.Fetch()
$query.Close()

if ($null -eq $shortcut) {
    throw 'The DustBox desktop shortcut was not found in the installer package.'
}

$query = $database.OpenView('UPDATE `Shortcut` SET `Icon_` = NULL WHERE `Shortcut` = ''_003A2787A59B4F41BCDDC9F7D3CD1D97''')
$query.Execute()
$query.Close()

$database.Commit()
