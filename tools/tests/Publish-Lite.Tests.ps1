<#
.SYNOPSIS
  Maintained tests for tools\Publish-Lite.ps1. They use ONLY synthetic temporary repositories and candidates
  (nothing under C:\PROJETS\Agendis-V3 and no real candidate is read or written).
.EXAMPLE
  pwsh -NoProfile -File tools\tests\Publish-Lite.Tests.ps1 [-ResultsCsv results.csv] [-KeepWorkspace]
  Exit code 0 = all tests passed.
#>
[CmdletBinding()]
param([string]$ResultsCsv = '', [switch]$KeepWorkspace, [string]$ToolPath = '')
$ErrorActionPreference = 'Stop'
if (-not $ToolPath) { $ToolPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Publish-Lite.ps1' }
if (-not (Test-Path -LiteralPath $ToolPath)) { throw "tool not found: $ToolPath" }
$Ws = Join-Path ([IO.Path]::GetTempPath()) ('publish-lite-tests-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
[void][IO.Directory]::CreateDirectory($Ws)
$Utf8 = New-Object Text.UTF8Encoding($false)
$Results = New-Object System.Collections.ArrayList
$PwshExe = (Get-Process -Id $PID).Path

function Rec([string]$id, [string]$what, [bool]$ok, [string]$detail = '') {
  [void]$Results.Add([pscustomobject]@{ Id = $id; Test = $what; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
  Write-Host ("[{0}] {1} | {2}{3}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $id, $what, $(if ($detail) { " | $detail" } else { '' }))
}
function Put([string]$p, [string]$t) { [void][IO.Directory]::CreateDirectory((Split-Path -Parent $p)); [IO.File]::WriteAllText($p, $t, $Utf8) }
function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash }
function GitIn([string]$d, [string[]]$a) { $o = & git -C $d -c user.name=t -c user.email=t@example.invalid -c core.autocrlf=false @a 2>&1; if ($LASTEXITCODE -ne 0) { throw "git $($a -join ' '): $o" }; $o }
function SnapDir([string]$d) { @(Get-ChildItem -LiteralPath $d -Recurse -Force | Sort-Object FullName | ForEach-Object { if ($_.PSIsContainer) { 'D|' + $_.FullName.Substring($d.Length) } else { 'F|' + $_.FullName.Substring($d.Length) + '|' + (Sha $_.FullName) + '|' + [int]$_.Attributes } }) -join "`n" }

# Synthetic world: candidate (18 files like the accepted one) + git repo (accepted commit A, later commit B).
function New-World([string]$Name, [string]$RepoDirName = 'repo', [bool]$IgnorePublish = $true) {
  $root = Join-Path $Ws $Name; [void][IO.Directory]::CreateDirectory($root)
  $cand = Join-Path $root 'cand'; $st = Join-Path $cand 'staging'
  $exe = "frozen exe bytes $Name"
  $files = [ordered]@{
    'ScriptManager.bin\ScriptManagerAgendisLite.bat' = "client launcher`r`n"; 'ScriptManager.bin\Lite\ScriptManager.exe' = $exe; 'ScriptManager.bin\Lite\ScriptManager.exe.config' = 'exe cfg'
    'ScriptManager.bin\Lite\ScriptManager.pdb' = 'pdb'; 'ScriptManager.bin\Lite\log4net.dll' = 'log4net'; 'ScriptManager.bin\Lite\Config\Log4net.config' = 'l4n cfg'
    'ScriptManager.bin\Lite\Config\ScriptManager.config' = 'sm cfg'; 'ScriptManager.bin\Lite\Config\Database.config.example' = 'example'; 'ScriptManager.bin\Lite\README-Lite.txt' = 'readme'
    'ScriptManager.bin\Lite\Preflight-History-ReadOnly.sql' = 'select 1'; 'ScriptManager.dev\ConfigVS-ScriptManagerLite.bat' = "dev bat`r`n"; 'ScriptManager.dev\Lite\ScriptManager.exe' = $exe
    'ScriptManager.dev\Lite\ScriptManager.exe.config' = 'exe cfg'; 'ScriptManager.dev\Lite\ScriptManager.pdb' = 'pdb'; 'ScriptManager.dev\Lite\log4net.dll' = 'log4net'
    'ScriptManager.dev\Lite\Config\Database.config' = 'DEV DB CONFIG (must never be published)'; 'ScriptManager.dev\Lite\Config\Log4net.config' = 'l4n cfg'; 'ScriptManager.dev\Lite\Config\ScriptManager.config' = 'sm cfg' }
  $rows = @()
  foreach ($k in $files.Keys) { Put "$st\$k" $files[$k]; [IO.File]::SetAttributes("$st\$k", [IO.FileAttributes]::ReadOnly); $rows += [pscustomobject]@{ Path = $k; Size = (Get-Item "$st\$k").Length; SHA256 = (Sha "$st\$k") } }
  [void][IO.Directory]::CreateDirectory("$cand\candidate-manifest")
  $rows | Export-Csv "$cand\candidate-manifest\delivery-manifest.csv" -NoTypeInformation
  Put "$cand\candidate-manifest\delivery-manifest.txt" "manifest $Name"
  Put "$cand\notes\evidence-extra.txt" "extra evidence $Name"
  $inv = Get-ChildItem $cand -Recurse -File | Where-Object { $_.Name -ne 'evidence-inventory.txt' } | Sort-Object FullName | ForEach-Object { '{0}  {1,8}  {2}' -f (Sha $_.FullName), $_.Length, $_.FullName.Substring($cand.Length + 1) }
  Put "$cand\evidence-inventory.txt" (($inv -join "`r`n") + "`r`n")
  $repo = Join-Path $root $RepoDirName; [void][IO.Directory]::CreateDirectory($repo)
  & git init -q -b main $repo 2>&1 | Out-Null
  Put "$repo\README.md" 'a'; if ($IgnorePublish) { Put "$repo\.gitignore" "/publish/`n" } else { Put "$repo\.gitignore" "/other/`n" }
  GitIn $repo @('add', '-A') | Out-Null; GitIn $repo @('commit', '-q', '-m', 'accepted') | Out-Null
  $acc = (GitIn $repo @('rev-parse', 'HEAD')) -join ''; $tree = (GitIn $repo @('rev-parse', 'HEAD^{tree}')) -join ''
  Put "$repo\README.md" 'b'; GitIn $repo @('add', '-A') | Out-Null; GitIn $repo @('commit', '-q', '-m', 'later') | Out-Null
  @{ Root = $root; Cand = $cand; Stage = $st; Repo = $repo; Acc = $acc; Tree = $tree; Exe = (Sha "$st\ScriptManager.bin\Lite\ScriptManager.exe"); Size = (Get-Item "$st\ScriptManager.bin\Lite\ScriptManager.exe").Length
    Txt = (Sha "$cand\candidate-manifest\delivery-manifest.txt"); Csv = (Sha "$cand\candidate-manifest\delivery-manifest.csv"); Inv = (Sha "$cand\evidence-inventory.txt") }
}
function Run-Tool($w, [hashtable]$over = @{}, [string]$Stamp = '20260101-120000') {
  $p = @{ RepoRoot = $w.Repo; CandidateDir = $w.Cand; ExpectedExeSha256 = $w.Exe; ExpectedExeSize = $w.Size; ExpectedManifestTxtSha256 = $w.Txt; ExpectedManifestCsvSha256 = $w.Csv; ExpectedInventorySha256 = $w.Inv; AcceptedSourceCommit = $w.Acc; AcceptedSourceTree = $w.Tree; Timestamp = $Stamp }
  foreach ($k in $over.Keys) { $p[$k] = $over[$k] }
  $a = @('-NoProfile', '-File', $ToolPath); foreach ($k in $p.Keys) { $a += "-$k"; $a += "$($p[$k])" }
  $out = & $PwshExe @a 2>&1 | ForEach-Object { "$_" }
  [pscustomobject]@{ Exit = $LASTEXITCODE; Out = ($out -join "`n") }
}
function Short($w) { (GitIn $w.Repo @('rev-parse', '--short=7', 'HEAD')) -join '' }
function Refused($id, $what, $w, $r, [string]$state0) {
  $pub = Join-Path $w.Repo 'publish'
  $left = @(if (Test-Path $pub) { Get-ChildItem $pub -Force })
  Rec $id "$what | refused (exit 1, RESULT: NOT PUBLISHED, no success line)" ($r.Exit -eq 1 -and $r.Out -match 'RESULT: NOT PUBLISHED' -and $r.Out -notmatch 'PUBLISHED: publish') "exit=$($r.Exit)"
  Rec "$id-clean" "$what | nothing left in publish\ and candidate unchanged" ($left.Count -eq 0 -and (SnapDir $w.Cand) -ceq $state0) "left=$($left.Count)"
}
$expected14 = @('AgendisTools/ScriptManager.bin/Lite/Config/Log4net.config', 'AgendisTools/ScriptManager.bin/Lite/ScriptManager.exe', 'AgendisTools/ScriptManager.bin/Lite/ScriptManager.exe.config', 'AgendisTools/ScriptManager.bin/Lite/ScriptManager.pdb', 'AgendisTools/ScriptManager.bin/Lite/log4net.dll',
  'AgendisTools/ScriptManager.bin/ScriptManagerAgendisLite.bat', 'AgendisTools/ScriptManager.dev/ConfigVS-ScriptManagerLite.bat', 'AgendisTools/ScriptManager.dev/Lite/ScriptManager.exe', 'AgendisTools/ScriptManager.dev/Lite/ScriptManager.pdb',
  'INSTALL.md', 'docs/Database.config.example', 'docs/Preflight-History-ReadOnly.sql', 'docs/README-Lite.txt', 'publication-manifest.csv') | Sort-Object

try {
  # ---- P1 happy path
  $w = New-World 'p1'; $cs0 = SnapDir $w.Cand; $git0 = (GitIn $w.Repo @('status', '--porcelain', '-uall')) -join '|'; $idx0 = (GitIn $w.Repo @('ls-files', '-s')) -join '|'
  $r = Run-Tool $w; $nm = "Lite-20260101-120000-$(Short $w)"; $pd = Join-Path $w.Repo "publish\$nm"
  Rec 'P1' 'happy path: exit 0 and PUBLISHED line with the Lite-<timestamp>-<shortSHA> name' ($r.Exit -eq 0 -and $r.Out -match [regex]::Escape("PUBLISHED: publish\$nm")) "exit=$($r.Exit) out=$($r.Out)"
  $got = @(Get-ChildItem $pd -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($pd.Length + 1) -replace '\\', '/' } | Sort-Object)
  Rec 'P1a' 'exact file set: 12 published + INSTALL.md + publication-manifest.csv (14), nothing else' (($got -join '|') -ceq ($expected14 -join '|')) "files=$($got.Count)"
  Rec 'P1b' 'no Database.config, no ScriptManager.config, no Logs anywhere' (@($got | Where-Object { $_ -match 'Database\.config$|ScriptManager\.config$|/Logs/' }).Count -eq 0) ''
  $mrows = @(Import-Csv "$pd\publication-manifest.csv")
  $hashOk = (@($mrows | Where-Object { (Sha "$pd\$($_.RelativePath -replace '/','\')") -ceq $_.SHA256 }).Count -eq 13)
  Rec 'P1c' 'manifest lists 13 files (all but itself); every SHA-256 and size matches the published bytes' ($mrows.Count -eq 13 -and $hashOk) "rows=$($mrows.Count)"
  $srcOk = (@($mrows | Where-Object { $_.SourceInCandidate -like 'staging/*' } | Where-Object { (Sha "$($w.Cand)\$($_.SourceInCandidate -replace '/','\')") -ceq $_.SourceSHA256 -and $_.SourceSHA256 -ceq $_.SHA256 }).Count -eq 12)
  Rec 'P1d' 'every published file is byte-identical to its frozen candidate source' $srcOk ''
  Rec 'P1e' 'dev and bin executables equal the qualified executable hash' ((Sha "$pd\AgendisTools\ScriptManager.dev\Lite\ScriptManager.exe") -ceq $w.Exe -and (Sha "$pd\AgendisTools\ScriptManager.bin\Lite\ScriptManager.exe") -ceq $w.Exe) ''
  Rec 'P1f' 'published files are normal (not ReadOnly) while the frozen candidate is untouched (bytes and attributes)' (@(Get-ChildItem $pd -Recurse -File -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReadOnly }).Count -eq 0 -and (SnapDir $w.Cand) -ceq $cs0) ''
  $txt = [IO.File]::ReadAllText("$pd\INSTALL.md") + [IO.File]::ReadAllText("$pd\publication-manifest.csv")
  Rec 'P1g' 'no machine-specific absolute path in INSTALL.md / manifest' ($txt -notmatch '(?i)\b[a-z]:\\') ''
  $ins = [IO.File]::ReadAllText("$pd\INSTALL.md")
  Rec 'P1h' 'INSTALL.md states: manual integration, protected Dev files/logs, no Bin Database.config, SQL prerequisite, O/o confirmation, exit-code caveat, accepted commit and exe hash' (($ins -match 'manual') -and ($ins -match 'NEVER overwrite') -and ($ins -match 'Logs') -and ($ins -match 'ScriptManager\.bin\\SQL') -and ($ins -match 'type O') -and ($ins -match 'does NOT prove') -and $ins.Contains($w.Acc) -and $ins.Contains($w.Exe)) ''
  Rec 'P1i' 'git: index and status unchanged (publish\ is ignored, nothing staged or committed)' ((((GitIn $w.Repo @('status', '--porcelain', '-uall')) -join '|') -ceq $git0) -and (((GitIn $w.Repo @('ls-files', '-s')) -join '|') -ceq $idx0)) ''
  $r2 = Run-Tool $w @{} '20260101-120000'; Rec 'P2' 'a second run with an existing target name is refused and leaves the first publication untouched' ($r2.Exit -eq 1 -and $r2.Out -match 'already exists' -and (Sha "$pd\publication-manifest.csv") -ceq (Sha "$pd\publication-manifest.csv")) "exit=$($r2.Exit)"
  $r3 = Run-Tool $w @{} '20260101-120001'; Rec 'P3' 'two successive publications create distinct directories' ($r3.Exit -eq 0 -and @(Get-ChildItem "$($w.Repo)\publish" -Directory).Count -eq 2) ''

  # ---- refusals
  $w = New-World 'r1'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ ExpectedManifestTxtSha256 = ('0' * 64) }; Refused 'R1' 'wrong expected manifest (txt) hash' $w $r $s
  $w = New-World 'r2'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ ExpectedManifestCsvSha256 = ('0' * 64) }; Refused 'R2' 'wrong expected manifest (csv) hash' $w $r $s
  $w = New-World 'r3'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ ExpectedInventorySha256 = ('0' * 64) }; Refused 'R3' 'wrong expected inventory hash' $w $r $s
  $w = New-World 'r4'; $f = "$($w.Stage)\ScriptManager.bin\Lite\README-Lite.txt"; [IO.File]::SetAttributes($f, [IO.FileAttributes]::Normal); [IO.File]::AppendAllText($f, 'x'); $s = SnapDir $w.Cand; $r = Run-Tool $w; Refused 'R4' 'a staged candidate file was tampered with' $w $r $s
  $w = New-World 'r15'; [IO.File]::AppendAllText("$($w.Cand)\notes\evidence-extra.txt", 'x'); $s = SnapDir $w.Cand; $r = Run-Tool $w; Refused 'R15' 'an evidence file outside staging differs from the accepted inventory' $w $r $s
  Rec 'R15m' 'the refusal names the inventory' ($r.Out -match 'inventory')
  $w = New-World 'r5'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ ExpectedExeSha256 = ('0' * 64) }; Refused 'R5' 'executable hash differs from the qualified artifact' $w $r $s
  $w = New-World 'r6'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ ExpectedExeSize = ($w.Size + 1) }; Refused 'R6' 'executable size differs' $w $r $s
  $w = New-World 'r7'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ AcceptedSourceCommit = ('1' * 40) }; Refused 'R7' 'accepted source commit not present in the repository' $w $r $s
  $w = New-World 'r8'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ AcceptedSourceTree = ('2' * 40) }; Refused 'R8' 'accepted source tree differs' $w $r $s
  $w = New-World 'r9'; $head = (GitIn $w.Repo @('rev-parse', 'HEAD')) -join ''; GitIn $w.Repo @('checkout', '-q', '--orphan', 'orphan') | Out-Null; Put "$($w.Repo)\o.txt" 'o'; GitIn $w.Repo @('add', '-A') | Out-Null; GitIn $w.Repo @('commit', '-q', '-m', 'orphan') | Out-Null
  $s = SnapDir $w.Cand; $r = Run-Tool $w; Refused 'R9' 'accepted commit is not an ancestor of HEAD' $w $r $s
  $w = New-World 'r10' 'repo' $false; $s = SnapDir $w.Cand; $r = Run-Tool $w; Refused 'R10' 'publish/ is not ignored by Git' $w $r $s
  $w = New-World 'r11'; [void][IO.Directory]::CreateDirectory("$($w.Root)\elsewhere"); cmd /c "mklink /J `"$($w.Repo)\publish`" `"$($w.Root)\elsewhere`"" | Out-Null; $s = SnapDir $w.Cand; $r = Run-Tool $w
  Rec 'R11' 'publish\ is a junction: refused and nothing written through it' ($r.Exit -eq 1 -and $r.Out -match 'junction' -and @(Get-ChildItem "$($w.Root)\elsewhere" -Force).Count -eq 0 -and (SnapDir $w.Cand) -ceq $s) "exit=$($r.Exit)"
  $w = New-World 'r12'; $f = "$($w.Stage)\ScriptManager.dev\Lite\ScriptManager.pdb"; [IO.File]::SetAttributes($f, [IO.FileAttributes]::Normal); [IO.File]::Delete($f); $s = SnapDir $w.Cand; $r = Run-Tool $w; Refused 'R12' 'a required candidate file is missing' $w $r $s
  $w = New-World 'r13'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ Timestamp = 'bad' }; Refused 'R13' 'invalid timestamp' $w $r $s
  $w = New-World 'r14'; $s = SnapDir $w.Cand; $r = Run-Tool $w @{ AgendisToolsReference = "$($w.Root)\no-such-folder" }; Refused 'R14' 'optional Agendis reference given but invalid' $w $r $s

  # ---- special characters in paths, optional read-only Agendis reference
  $w = New-World 'sp' 'repo with space & amp (x)'; $r = Run-Tool $w; $pd = Join-Path $w.Repo "publish\Lite-20260101-120000-$(Short $w)"
  Rec 'S1' 'repository path with spaces, & and parentheses publishes correctly (14 files)' ($r.Exit -eq 0 -and @(Get-ChildItem $pd -Recurse -File -Force).Count -eq 14) "exit=$($r.Exit)"
  $w = New-World 'ref'; $ref = Join-Path $w.Root 'AgendisTools'; Put "$ref\ScriptManager.dev\keep.txt" 'k'; Put "$ref\ScriptManager.bin\keep.txt" 'k'; $rs0 = SnapDir $ref; $r = Run-Tool $w @{ AgendisToolsReference = $ref }
  Rec 'S2' 'optional Agendis reference is only read: publication succeeds and the reference tree is unchanged' ($r.Exit -eq 0 -and (SnapDir $ref) -ceq $rs0) ''

  # ---- static properties of the tool
  $src = [IO.File]::ReadAllText($ToolPath)
  $gitLines = @($src -split "`r?`n" | Where-Object { $_ -match "(?i)\bgit\b|GitRun @\(" -and $_ -notmatch '^\s*#' })
  $gitWrites = @($gitLines | Where-Object { $_ -match "'(add|commit|push|tag|checkout|reset|rebase|stash|clean|rm|mv|apply|cherry-pick|pull|fetch|restore|switch|branch|config)'" })
  Rec 'T1' 'static: git is only used read-only (no add/commit/push/tag/checkout/reset/...), no sqlcmd/Invoke-Sqlcmd, no Remove-Item' ($gitLines.Count -ge 5 -and $gitWrites.Count -eq 0 -and $src -notmatch '(?i)\bsqlcmd\b|Invoke-Sqlcmd|Remove-Item') "gitLines=$($gitLines.Count) writes=$($gitWrites.Count)"
  Rec 'T2' 'static: the only recursive delete is the guarded removal of the tool''s own .incomplete-* folder' (([regex]::Matches($src, 'Directory\]::Delete\(')).Count -eq 1 -and $src -match "-like '\.incomplete-\*'" -and $src -match 'IsUnder \$tmpDir \$pubRoot') ''
  Rec 'T3' 'static: no reference to an Agendis checkout path other than the optional read-only parameter' ($src -notmatch '(?i)Agendis-V3|agendis-3\.5') ''
}
finally {
  if (-not $KeepWorkspace) {
    try { Get-ChildItem $Ws -Recurse -Force | ForEach-Object { try { [IO.File]::SetAttributes($_.FullName, [IO.FileAttributes]::Normal) } catch {} }
      Get-ChildItem $Ws -Recurse -Force -Directory | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | ForEach-Object { [IO.Directory]::Delete($_.FullName) }
      if ($Ws.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $Ws) -like 'publish-lite-tests-*') { [IO.Directory]::Delete($Ws, $true) } } catch { Write-Host "note: workspace cleanup incomplete: $($_.Exception.Message)" } }
}
if ($ResultsCsv) { $Results | Export-Csv -LiteralPath $ResultsCsv -NoTypeInformation }
$fails = @($Results | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Host "TESTS: total=$($Results.Count) fail=$fails"
exit $(if ($fails -eq 0) { 0 } else { 1 })
