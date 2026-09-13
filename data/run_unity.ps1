param([string]$Method = "-", [string]$Name = "run", [string]$Extra = "")
$log = "D:\Tashkent city\data\logs\$Name.log"
New-Item -ItemType Directory -Force "D:\Tashkent city\data\logs" | Out-Null
if (Test-Path $log) { Remove-Item $log -Force }
$ua = @('-batchmode','-quit','-projectPath','"D:\Tashkent city\AmirTemurSquare"','-logFile',"`"$log`"")
if ($Method -ne "-") { $ua += @('-executeMethod', $Method) }
if ($Extra -ne "") { $ua += $Extra.Split(' ') }
Write-Output "ARGS: $ua"
$p = Start-Process -FilePath "D:\Unity downloads\6000.3.9f1\Editor\Unity.exe" -ArgumentList $ua -PassThru -Wait
Write-Output "Unity exit code: $($p.ExitCode)"
Write-Output "=== compiler errors ==="
Select-String -Path $log -Pattern "error CS\d+" | ForEach-Object { $_.Line -replace '^.*Assets','Assets' } | Sort-Object -Unique | Select-Object -First 80
Write-Output "=== build log / exceptions ==="
Select-String -Path $log -Pattern "^\[BuildAll\]|^\[AmirTemur|Exception|Error:|error:" | Where-Object { $_.Line -notmatch "error CS" } | ForEach-Object { $_.Line } | Select-Object -First 80
Write-Output "FINISHED"
