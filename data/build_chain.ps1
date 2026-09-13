$u = "D:\Unity downloads\6000.3.9f1\Editor\Unity.exe"
$l1 = "D:\Tashkent city\data\logs\buildall5.log"; $l2 = "D:\Tashkent city\data\logs\player1.log"
Remove-Item $l1,$l2 -Force -ErrorAction SilentlyContinue
$p = Start-Process -FilePath $u -ArgumentList @('-batchmode','-quit','-projectPath','"D:\Tashkent city\AmirTemurSquare"','-logFile',"`"$l1`"",'-executeMethod','AmirTemur.Editor.BuildAll.Run') -PassThru -Wait
"BuildAll exit: $($p.ExitCode)"
$p = Start-Process -FilePath $u -ArgumentList @('-batchmode','-quit','-nographics','-projectPath','"D:\Tashkent city\AmirTemurSquare"','-logFile',"`"$l2`"",'-executeMethod','AmirTemur.Editor.BuildAll.BuildPlayer') -PassThru -Wait
"BuildPlayer exit: $($p.ExitCode)"
Select-String -Path $l2 -Pattern "error CS\d+|Build succeeded|Build Finished|Result:|BuildPlayer|Exception" | Select-Object -First 30 | ForEach-Object { $_.Line }
"CHAIN DONE"
