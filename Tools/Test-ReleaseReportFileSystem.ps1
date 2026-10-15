param([Parameter(Mandatory=$true)][string]$ReportDirectory, [string]$ResultPath='')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ReportDirectory)
if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Existing report directory required.' }
$fixture=Join-Path $root ('write-probe-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReportWriteProbe {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    public static extern bool MoveFileEx(string source,string destination,uint flags);
    public static void ReplaceNull(string source,string destination) { System.IO.File.Replace(source,destination,null); }
}
'@
$results=@()
foreach ($method in @('FileReplaceNull','FileReplaceBackup','MoveFileEx','PublishFresh','RotateTwoSlots')) {
    $source=Join-Path $fixture ($method+'-source.txt')
    $destination=Join-Path $fixture ($method+'-destination.txt')
    [IO.File]::WriteAllText($destination,'complete-old')
    [IO.File]::WriteAllText($source,'complete-new')
    $errorText=''
    try {
        switch ($method) {
            'FileReplaceNull' { [ReportWriteProbe]::ReplaceNull($source,$destination) }
            'FileReplaceBackup' { [IO.File]::Replace($source,$destination,$destination+'.backup') }
            'MoveFileEx' { if (![ReportWriteProbe]::MoveFileEx($source,$destination,9)) { throw ('Win32 error '+[Runtime.InteropServices.Marshal]::GetLastWin32Error()) } }
            'PublishFresh' { $destination=$destination+'.published'; [IO.File]::Move($source,$destination) }
            'RotateTwoSlots' {
                $otherSlot=$destination+'.other'
                [IO.File]::Move($source,$otherSlot)
                [IO.File]::WriteAllText($source,'complete-newest')
                [IO.File]::Delete($destination)
                [IO.File]::Move($source,$destination)
                if ([IO.File]::ReadAllText($otherSlot) -ne 'complete-new') { throw 'Previous complete slot changed.' }
            }
        }
    } catch { $errorText=$_.Exception.ToString() }
    $results += [pscustomobject]@{method=$method; error=$errorText; destination=[IO.File]::ReadAllText($destination);
        destination_attributes=(Get-Item -LiteralPath $destination).Attributes.ToString(); source_exists=(Test-Path -LiteralPath $source)}
}
$json=$results | ConvertTo-Json -Depth 4
if ($ResultPath.Length -gt 0) { [IO.File]::WriteAllText($ResultPath,$json,[Text.UTF8Encoding]::new($false)) }
$json
Write-Output "Retained isolated fixture: $fixture"
