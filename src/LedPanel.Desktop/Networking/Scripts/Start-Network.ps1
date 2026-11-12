param([Parameter(Mandatory)][string]$ConfigPath,[Parameter(Mandatory)][int]$OwnerId,
      [Parameter(Mandatory)][string]$StopFile,[Parameter(Mandatory)][string]$StatusFile)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $StopFile) { return }
# Values are local application-generated paths; reject quoting characters before
# composing the Windows process argument string.
foreach($value in @($ConfigPath,$StopFile,$StatusFile,$PSScriptRoot)) {
    if($value.Contains('"') -or $value.Contains("`n")) { throw 'Invalid path.' }
}
$script=Join-Path $PSScriptRoot 'Network-Session.ps1'
$arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}" -OwnerId {2} -StopFile "{3}" -StatusFile "{4}"' -f $script,$ConfigPath,$OwnerId,$StopFile,$StatusFile
Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments | Out-Null
