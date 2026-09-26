<#
.SYNOPSIS
    Repairs a save damaged by the original Aerocraft Framework (an aircraft that followed itself).

.DESCRIPTION
    The original mod deep-saved the thing an aircraft follows. An aircraft ordered to follow itself wrote a full
    copy of itself into itself at every save, so saves grew by megabytes per save until writing them failed half
    way: the file then ends in the middle of hundreds of nested <FollowTargetThing> elements and cannot be loaded.

    This script copies the save while dropping every deep-saved <FollowTargetThing Class="..."> element. If the
    file is truncated, all open elements are closed at the point where it ends; things that were saved after the
    damaged aircraft are then lost, everything before it is kept. The original file is never modified.

.EXAMPLE
    .\tools\Repair-Save.ps1 -Path "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves\testreload.rws" -Output "...\Saves\testreload_repaired.rws"
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = "Stop"
if ((Resolve-Path $Path).Path -eq [System.IO.Path]::GetFullPath($Output)) {
    throw "Output must be a different file."
}

$readerSettings = New-Object System.Xml.XmlReaderSettings
$readerSettings.IgnoreWhitespace = $true
$readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Indent = $true
$writerSettings.IndentChars = "`t"
$writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)

$reader = [System.Xml.XmlReader]::Create($Path, $readerSettings)
$writer = [System.Xml.XmlWriter]::Create($Output, $writerSettings)
$dropped = 0
$truncated = $false
try {
    $writer.WriteStartDocument()
    $read = $reader.Read()
    while ($read) {
        switch ($reader.NodeType) {
            ([System.Xml.XmlNodeType]::Element) {
                if ($reader.LocalName -eq "FollowTargetThing" -and $reader.GetAttribute("Class")) {
                    $dropped++
                    $reader.Skip()
                    $read = -not $reader.EOF
                    continue
                }
                $writer.WriteStartElement($reader.Prefix, $reader.LocalName, $reader.NamespaceURI)
                $writer.WriteAttributes($reader, $true)
                if ($reader.IsEmptyElement) {
                    $writer.WriteEndElement()
                }
            }
            ([System.Xml.XmlNodeType]::EndElement) { $writer.WriteFullEndElement() }
            ([System.Xml.XmlNodeType]::Text) { $writer.WriteString($reader.Value) }
            ([System.Xml.XmlNodeType]::CDATA) { $writer.WriteCData($reader.Value) }
            ([System.Xml.XmlNodeType]::Comment) { $writer.WriteComment($reader.Value) }
        }
        $read = $reader.Read()
    }
}
catch [System.Xml.XmlException] {
    $truncated = $true
    Write-Warning "The save is truncated ($($_.Exception.Message.Split('.')[0])); closing it at that point."
}
finally {
    $reader.Close()
}
$writer.WriteEndDocument()
$writer.Close()

Write-Host "Dropped $dropped deep-saved follow targets. Truncated: $truncated."
Write-Host ("Wrote {0} ({1:N1} MB, was {2:N1} MB)." -f $Output, ((Get-Item $Output).Length / 1MB), ((Get-Item $Path).Length / 1MB))
