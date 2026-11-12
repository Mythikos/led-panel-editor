param([Parameter(Mandatory)][string]$ConfigPath,
      [Parameter(Mandatory)][int]$OwnerId,
      [Parameter(Mandatory)][string]$StopFile,
      [Parameter(Mandatory)][string]$StatusFile)
$ErrorActionPreference='Stop'
$config=Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$report=[ordered]@{State='starting';StartedAt=(Get-Date).ToString('o')}
$changed=$false
$adapterIndex=$null
function Save-Report { $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StatusFile -Encoding utf8 }
function Other-State($excludedIndex) {
    [ordered]@{
        Addresses=@(Get-NetIPAddress -AddressFamily IPv4 | Where-Object InterfaceIndex -ne $excludedIndex | Select-Object InterfaceIndex,IPAddress,PrefixLength | Sort-Object InterfaceIndex,IPAddress)
        Routes=@(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' | Select-Object InterfaceIndex,NextHop,RouteMetric | Sort-Object InterfaceIndex,NextHop)
    } | ConvertTo-Json -Depth 4 -Compress
}
try {
    if(Test-Path -LiteralPath $StopFile) { return }
    $owner=Get-Process -Id $OwnerId
    $ownerStarted=$owner.StartTime
    $localAddress=[System.Net.IPAddress]::Parse($config.source)
    $panelAddress=[System.Net.IPAddress]::Parse($config.host)
    if ($localAddress.AddressFamily -ne 'InterNetwork' -or $panelAddress.AddressFamily -ne 'InterNetwork') { throw 'IPv4 addresses required.' }
    $subnet=($config.source -split '\.')[0..2] -join '.'
    if (-not $config.host.StartsWith($subnet+'.') -or $config.source -eq $config.host) { throw 'The panel and adapter require distinct addresses on the same /24 subnet.' }
    $adapter=@(Get-NetAdapter | Where-Object PnPDeviceID -eq $config.adapter_id)
    if ($adapter.Count -ne 1 -or $adapter[0].Status -ne 'Up') { throw 'Connect the selected USB Ethernet adapter and power on the panel.' }
    $adapterIndex=$adapter[0].ifIndex
    $adapterGuid=$adapter[0].InterfaceGuid
    $report.Adapter=$adapter[0].Name
    $report.InterfaceIndex=$adapterIndex
    $beforeOther=Other-State $adapterIndex
    $addresses=@(Get-NetIPAddress -AddressFamily IPv4 | Where-Object InterfaceIndex -eq $adapterIndex)
    if (-not @($addresses | Where-Object IPAddress -eq $config.source).Count) {
        if ((Get-NetIPInterface -InterfaceIndex $adapterIndex -AddressFamily IPv4).Dhcp -ne 'Enabled') { throw 'Selected adapter has custom settings; no changes were made.' }
        if (@($addresses | Where-Object IPAddress -notlike '169.254.*').Count) { throw 'Selected adapter already has a network address; no changes were made.' }
        if (@(Get-NetRoute -AddressFamily IPv4 | Where-Object DestinationPrefix -like ($subnet+'.*')).Count) { throw 'The panel subnet is already routed; no changes were made.' }
        if (@(Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -in @($config.host,$config.source)).Count) { throw 'A panel test address is already assigned.' }
        $changed=$true
        Save-Report
        New-NetIPAddress -InterfaceIndex $adapterIndex -IPAddress $config.source -PrefixLength 24 -PolicyStore ActiveStore | Out-Null
        $ready=$false
        for($i=0;$i -lt 15;$i++) {
            $state=(Get-NetIPAddress -InterfaceIndex $adapterIndex -IPAddress $config.source).AddressState
            if($state -eq 'Duplicate') { throw 'Duplicate source IP detected.' }
            if($state -eq 'Preferred') { $ready=$true;break }
            Start-Sleep -Seconds 1
        }
        if(-not $ready) { throw 'Adapter address did not become ready.' }
    }
    $route=@(Find-NetRoute -RemoteIPAddress $config.host)
    if(-not $route.Count -or @($route | Where-Object InterfaceIndex -ne $adapterIndex).Count) { throw 'Panel route points to another adapter.' }
    $pinger=[System.Net.NetworkInformation.Ping]::new()
    try { $null=$pinger.Send($config.host,1500) } finally { $pinger.Dispose() }
    $neighbors=@(Get-NetNeighbor -InterfaceIndex $adapterIndex -AddressFamily IPv4 | Where-Object IPAddress -eq $config.host)
    $expected=$config.mac.Replace(':','-').ToUpperInvariant()
    if (@($neighbors | Where-Object LinkLayerAddress -eq $expected).Count -ne 1) { throw 'The panel MAC does not match the selected device.' }
    $report.State='ready';Save-Report
    while(-not (Test-Path -LiteralPath $StopFile)) {
        $current=Get-Process -Id $OwnerId -ErrorAction SilentlyContinue
        if(-not $current -or $current.StartTime -ne $ownerStarted) { break }
        Start-Sleep -Seconds 1
    }
} catch {
    $report.Error=$_.Exception.Message
} finally {
    if($changed) {
        try {
            $current=@(Get-NetAdapter | Where-Object PnPDeviceID -eq $config.adapter_id)
            if($current.Count -ne 1 -or $current[0].InterfaceGuid -ne $adapterGuid) { throw 'Adapter identity changed; reconnect it to restore DHCP.' }
            $adapterIndex=$current[0].ifIndex
            Get-NetIPAddress -InterfaceIndex $adapterIndex -IPAddress $config.source -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction Stop
            Set-NetIPInterface -InterfaceIndex $adapterIndex -AddressFamily IPv4 -Dhcp Enabled -PolicyStore ActiveStore
            $report.DhcpRestored=((Get-NetIPInterface -InterfaceIndex $adapterIndex -AddressFamily IPv4).Dhcp -eq 'Enabled')
            $report.TemporaryAddressRemoved=(-not @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceIndex -eq $adapterIndex -and $_.IPAddress -eq $config.source }).Count)
            $report.OtherNetworkUnchanged=((Other-State $adapterIndex) -eq $beforeOther)
        } catch { $report.CleanupError=$_.Exception.Message }
    }
    $report.State='closed'
    if($report.Error -or $report.CleanupError) { $report.State='error' }
    $report.FinishedAt=(Get-Date).ToString('o');Save-Report
}
