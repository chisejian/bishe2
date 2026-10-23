$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$dest = Join-Path $root 'Assets'
$bad = Join-Path $dest 'bishe'
$source = Join-Path $bad 'Assets'
if ($root -ne 'C:\Users\Y7000P\Documents\Codex\bishe2' -or !(Test-Path -LiteralPath $source)) { throw 'Unexpected project path' }
$map = [System.Collections.Generic.List[object]]::new()
function Copy-VerifiedAsset($item) {
 $name = $item.Name
 $target = Join-Path $dest $name
 $n = 0
 while ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath ($target + '.meta'))) {
  $n++
  if ($item.PSIsContainer) { $newName = $name + '_from_bishe' + $(if ($n -gt 1) { "_$n" }) }
  else { $newName = [IO.Path]::GetFileNameWithoutExtension($name) + '_from_bishe' + $(if ($n -gt 1) { "_$n" }) + $item.Extension }
  $target = Join-Path $dest $newName
 }
 Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse
 if ($item.PSIsContainer) { $files = @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Force) } else { $files = @($item) }
 foreach ($file in $files) {
  $out = if ($item.PSIsContainer) { $target + $file.FullName.Substring($item.FullName.Length) } else { $target }
  if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $out).Hash) { throw "Verification failed: $out" }
 }
 $meta = $item.FullName + '.meta'
 if (Test-Path -LiteralPath $meta) {
  Copy-Item -LiteralPath $meta -Destination ($target + '.meta')
  if ((Get-FileHash -LiteralPath $meta).Hash -ne (Get-FileHash -LiteralPath ($target + '.meta')).Hash) { throw 'Meta verification failed' }
 }
 $map.Add([pscustomobject]@{Source=$item.FullName;Destination=$target;VerifiedFiles=$files.Count;MetaPreserved=(Test-Path -LiteralPath $meta)})
}
Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Extension -ne '.meta' } | ForEach-Object { Copy-VerifiedAsset $_ }
Get-ChildItem -LiteralPath $bad -File -Force | Where-Object { $_.Extension -ne '.meta' } | ForEach-Object { Copy-VerifiedAsset $_ }
# Preserve any orphan metadata from the asset source as well.
Get-ChildItem -LiteralPath $source -File -Filter '*.meta' | Where-Object { !(Test-Path -LiteralPath $_.FullName.Substring(0,$_.FullName.Length-5)) } | ForEach-Object { Copy-VerifiedAsset $_ }
$map | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'bishe-migration-report.json') -Encoding utf8
if ([IO.Path]::GetFullPath($bad) -ne ($root + '\Assets\bishe')) { throw 'Unsafe deletion target' }
Remove-Item -LiteralPath $bad -Recurse -Force
Remove-Item -LiteralPath ($bad + '.meta') -Force
$map | Format-Table -AutoSize
'Nested project removed: ' + !(Test-Path -LiteralPath $bad)
'Verified asset files: ' + (($map | Measure-Object VerifiedFiles -Sum).Sum)
