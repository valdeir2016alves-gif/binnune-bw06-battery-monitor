$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path $PSScriptRoot "BW06Monitor.cs"
$out = Join-Path $PSScriptRoot "BW06Monitor.exe"

& $csc /nologo /optimize+ /target:winexe `
    /r:System.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /out:$out $src

Write-Host "Compilado com sucesso: $out"
