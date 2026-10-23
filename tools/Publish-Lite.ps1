<#
.SYNOPSIS
  Publish the QUALIFIED ScriptManager Lite artifacts into <repo>\publish\Lite-<yyyyMMdd-HHmmss>-<shortSHA>\ .

.DESCRIPTION
  Publication only. The script
    * reads a frozen, hash-verified delivery candidate (never an arbitrary build output),
    * writes ONLY inside <RepoRoot>\publish\ (which must be ignored by Git),
    * never touches an Agendis checkout, never runs SQL, never commits/pushes/tags,
    * builds the output in a hidden '.incomplete-*' folder and renames it only after complete verification.
  The published AgendisTools\ subtree mirrors the relative layout of the Agendis source tree and is meant to be
  integrated MANUALLY (see the generated INSTALL.md). Compatible with Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER RepoRoot
  ScriptManager repository root (default: parent of this script's folder).
.PARAMETER CandidateDir
  Frozen delivery candidate (contains staging\, candidate-manifest\, evidence-inventory.txt).
.PARAMETER AgendisToolsReference
  Optional READ-ONLY structural check against an Agendis AgendisTools folder.
.PARAMETER Timestamp
  yyyyMMdd-HHmmss override (tests only); default = current local time.
#>
[CmdletBinding()]
param(
  [string]$RepoRoot = '',
  [string]$CandidateDir = 'C:\ScriptManagerLite-Q2\Q2-L1-C1_20261023T172001Z',
  [string]$ExpectedExeSha256 = 'B2FF8FAE1110DC51767F2C0B41F2D09B088F9F1A86A608DB11436D7056FBF79F',
  [int64]$ExpectedExeSize = 27136,
  [string]$ExpectedManifestTxtSha256 = 'A802CCC277B4E76092D8023B1446CB44F88536D48350978DFCB09CB5B62ED08A',
  [string]$ExpectedManifestCsvSha256 = '925AF45819393B1B17B2A97FA91C9CFB99DD4F7F8AD37942A08ABE71E090B724',
  [string]$ExpectedInventorySha256 = 'B87335785A5F95473102E927D7E93163065DEF45C34B67FF24AEA9E4BF407B51',
  [string]$AcceptedSourceCommit = '8c84da375a62bd5c337e70d853f769be7ceae73d',
  [string]$AcceptedSourceTree = '7c1c568175c54e778175a1b04263cbe6a6cf1b85',
  [string]$AgendisToolsReference = '',
  [string]$Timestamp = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# ---------------------------------------------------------------- helpers
function Fail([string]$m) { throw "PUBLISH REFUSED: $m" }
function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash }
function FullPath([string]$p) { [IO.Path]::GetFullPath($p) }
function IsUnder([string]$child, [string]$parent) { (FullPath $child).StartsWith(((FullPath $parent).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase) }
function IsReparse([string]$p) { (Test-Path -LiteralPath $p) -and ((([int](Get-Item -LiteralPath $p -Force).Attributes) -band [int][IO.FileAttributes]::ReparsePoint) -ne 0) }
function GitRun([string[]]$a) { $o = & git -C $script:Repo @a 2>&1; if ($LASTEXITCODE -ne 0) { Fail "git $($a -join ' ') failed: $($o -join ' ')" }; ($o | ForEach-Object { "$_" }) }
function ClearReadOnly([string]$p) { $a = [int][IO.File]::GetAttributes($p); if ($a -band [int][IO.FileAttributes]::ReadOnly) { [IO.File]::SetAttributes($p, [IO.FileAttributes]($a -band (-bnot [int][IO.FileAttributes]::ReadOnly))) } }

# ---------------------------------------------------------------- 1. repository identity
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$script:Repo = FullPath $RepoRoot
$created = $false
$tmpDir = ''
$pubRoot = ''
$name = ''
try {
  if (-not (Test-Path -LiteralPath "$script:Repo\.git")) { Fail "not a Git repository root: $script:Repo" }
  $top = (GitRun @('rev-parse', '--show-toplevel')) -join ''
  if ((FullPath $top) -ine $script:Repo) { Fail "RepoRoot is not the repository top-level ($top)" }
  $head = (GitRun @('rev-parse', 'HEAD')) -join ''
  $null = & git -C $script:Repo cat-file -e "$AcceptedSourceCommit^{commit}" 2>&1
  if ($LASTEXITCODE -ne 0) { Fail "accepted source commit $AcceptedSourceCommit not found in this repository" }
  $accTree = (GitRun @('rev-parse', "$AcceptedSourceCommit^{tree}")) -join ''
  if ($accTree -ne $AcceptedSourceTree) { Fail "tree of $AcceptedSourceCommit is $accTree, expected $AcceptedSourceTree" }
  $null = & git -C $script:Repo merge-base --is-ancestor $AcceptedSourceCommit $head 2>&1
  if ($LASTEXITCODE -ne 0) { Fail "accepted source commit is not an ancestor of HEAD ($head)" }
  $short = (GitRun @('rev-parse', '--short=7', 'HEAD')) -join ''
  $dirty = @(GitRun @('status', '--porcelain')).Count

  # -------------------------------------------------------------- 2. publish root (ignored, no redirection)
  $pubRoot = Join-Path $script:Repo 'publish'
  if (IsReparse $pubRoot) { Fail 'publish\ is a junction/symlink; refusing to write through it' }
  $null = & git -C $script:Repo check-ignore -q -- 'publish/__probe__' 2>&1
  if ($LASTEXITCODE -ne 0) { Fail "publish/ is not ignored by Git (add '/publish/' to .gitignore)" }
  if (-not $Timestamp) { $Timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss') }
  if ($Timestamp -notmatch '^\d{8}-\d{6}$') { Fail "invalid timestamp '$Timestamp'" }
  $name = "Lite-$Timestamp-$short"
  $finalDir = Join-Path $pubRoot $name
  $tmpDir = Join-Path $pubRoot ".incomplete-$name"
  if (Test-Path -LiteralPath $finalDir) { Fail "publication directory already exists: publish\$name" }
  if (Test-Path -LiteralPath $tmpDir) { Fail "an incomplete publication directory already exists: publish\.incomplete-$name" }
  if (-not (IsUnder $finalDir $pubRoot) -or -not (IsUnder $tmpDir $pubRoot)) { Fail 'output path escapes publish\' }

  # -------------------------------------------------------------- 3. qualified candidate verification (read-only)
  $cand = FullPath $CandidateDir
  foreach ($f in @('evidence-inventory.txt', 'candidate-manifest\delivery-manifest.txt', 'candidate-manifest\delivery-manifest.csv')) {
    if (-not (Test-Path -LiteralPath (Join-Path $cand $f))) { Fail "candidate file missing: $f" } }
  if ((Sha "$cand\evidence-inventory.txt") -ne $ExpectedInventorySha256) { Fail 'candidate evidence inventory hash differs from the accepted value' }
  if ((Sha "$cand\candidate-manifest\delivery-manifest.txt") -ne $ExpectedManifestTxtSha256) { Fail 'candidate manifest (txt) hash differs from the accepted value' }
  if ((Sha "$cand\candidate-manifest\delivery-manifest.csv") -ne $ExpectedManifestCsvSha256) { Fail 'candidate manifest (csv) hash differs from the accepted value' }
  $invBad = 0
  foreach ($l in (Get-Content -LiteralPath "$cand\evidence-inventory.txt")) {
    if (-not $l.Trim()) { continue }
    $x = $l -split '\s{2,}', 3
    $fp = Join-Path $cand $x[2].Trim()
    if (-not (Test-Path -LiteralPath $fp) -or (Sha $fp) -ne $x[0]) { $invBad++ } }
  if ($invBad -gt 0) { Fail "$invBad candidate evidence file(s) differ from the accepted inventory" }
  $man = @(Import-Csv -LiteralPath "$cand\candidate-manifest\delivery-manifest.csv")
  $stage = "$cand\staging"
  if ($man.Count -ne 18 -or @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force).Count -ne 18) { Fail 'candidate must contain exactly 18 staged files' }
  foreach ($m in $man) {
    $fp = Join-Path $stage $m.Path
    if (-not (Test-Path -LiteralPath $fp) -or (Sha $fp) -ne $m.SHA256) { Fail "staged file differs from the accepted manifest: $($m.Path)" } }
  foreach ($e in @('ScriptManager.dev\Lite\ScriptManager.exe', 'ScriptManager.bin\Lite\ScriptManager.exe')) {
    $fp = Join-Path $stage $e
    if ((Sha $fp) -ne $ExpectedExeSha256 -or (Get-Item -LiteralPath $fp).Length -ne $ExpectedExeSize) { Fail "executable identity differs from the qualified artifact: $e" } }

  # -------------------------------------------------------------- 4. publication plan: exactly 9 runtime + 3 documentation files
  $plan = @(
    @('Dev',  'ScriptManager.dev\ConfigVS-ScriptManagerLite.bat',       'AgendisTools\ScriptManager.dev\ConfigVS-ScriptManagerLite.bat'),
    @('Dev',  'ScriptManager.dev\Lite\ScriptManager.exe',               'AgendisTools\ScriptManager.dev\Lite\ScriptManager.exe'),
    @('Dev',  'ScriptManager.dev\Lite\ScriptManager.pdb',               'AgendisTools\ScriptManager.dev\Lite\ScriptManager.pdb'),
    @('Bin',  'ScriptManager.bin\ScriptManagerAgendisLite.bat',         'AgendisTools\ScriptManager.bin\ScriptManagerAgendisLite.bat'),
    @('Bin',  'ScriptManager.bin\Lite\ScriptManager.exe',               'AgendisTools\ScriptManager.bin\Lite\ScriptManager.exe'),
    @('Bin',  'ScriptManager.bin\Lite\ScriptManager.exe.config',        'AgendisTools\ScriptManager.bin\Lite\ScriptManager.exe.config'),
    @('Bin',  'ScriptManager.bin\Lite\ScriptManager.pdb',               'AgendisTools\ScriptManager.bin\Lite\ScriptManager.pdb'),
    @('Bin',  'ScriptManager.bin\Lite\log4net.dll',                     'AgendisTools\ScriptManager.bin\Lite\log4net.dll'),
    @('Bin',  'ScriptManager.bin\Lite\Config\Log4net.config',           'AgendisTools\ScriptManager.bin\Lite\Config\Log4net.config'),
    @('Docs', 'ScriptManager.bin\Lite\README-Lite.txt',                 'docs\README-Lite.txt'),
    @('Docs', 'ScriptManager.bin\Lite\Config\Database.config.example',  'docs\Database.config.example'),
    @('Docs', 'ScriptManager.bin\Lite\Preflight-History-ReadOnly.sql',  'docs\Preflight-History-ReadOnly.sql')
  )
  $forbiddenLeaf = @('Database.config', 'ScriptManager.config')
  foreach ($p in $plan) {
    if ($forbiddenLeaf -contains (Split-Path -Leaf $p[2])) { Fail "plan contains a forbidden file: $($p[2])" }
    if ($p[2] -match '(^|\\)Logs(\\|$)') { Fail 'plan contains runtime logs' } }

  if ($AgendisToolsReference) {
    $ref = FullPath $AgendisToolsReference
    if (-not (Test-Path -LiteralPath "$ref\ScriptManager.dev") -or -not (Test-Path -LiteralPath "$ref\ScriptManager.bin")) { Fail 'AgendisToolsReference does not contain ScriptManager.dev and ScriptManager.bin' } }

  # -------------------------------------------------------------- 5. build in the hidden incomplete folder
  if (-not (Test-Path -LiteralPath $pubRoot)) { [void][IO.Directory]::CreateDirectory($pubRoot) }
  if (IsReparse $pubRoot) { Fail 'publish\ became a junction/symlink' }
  [void][IO.Directory]::CreateDirectory($tmpDir)
  $created = $true
  $rows = New-Object System.Collections.ArrayList
  foreach ($p in $plan) {
    $src = Join-Path $stage $p[1]
    $dst = Join-Path $tmpDir $p[2]
    if (-not (IsUnder $dst $tmpDir)) { Fail "destination escapes the publication folder: $($p[2])" }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $dst))
    [IO.File]::Copy($src, $dst, $false)
    ClearReadOnly $dst   # policy: published copies are normal files; the frozen source keeps its attributes
    $h = Sha $dst; $sh = Sha $src
    if ($h -ne $sh) { Fail "copy verification failed: $($p[2])" }
    [void]$rows.Add([pscustomobject]@{ RelativePath = ($p[2] -replace '\\', '/'); Role = $p[0]; Size = (Get-Item -LiteralPath $dst).Length; SHA256 = $h; SourceInCandidate = ('staging/' + ($p[1] -replace '\\', '/')); SourceSHA256 = $sh }) }

  $lines = @(
    "# ScriptManager Lite - manual integration (publication $name)",
    '',
    'This folder is a PUBLICATION produced from the qualified ScriptManager Lite artifacts. It is not an installer and',
    'nothing in it has been applied to any Agendis checkout. Integration is manual (or by a separately authorised',
    'MSBuild change in the Agendis repository).',
    '',
    '## Identity',
    "- ScriptManager source: accepted commit $AcceptedSourceCommit (tree $AcceptedSourceTree); publication made from HEAD $head (working tree entries at publication time: $dirty).",
    "- Qualified executable: SHA-256 $ExpectedExeSha256 ($ExpectedExeSize bytes). The same bytes are published for Dev and Bin.",
    "- Qualified delivery candidate: manifest (txt) $ExpectedManifestTxtSha256, manifest (csv) $ExpectedManifestCsvSha256, evidence inventory $ExpectedInventorySha256.",
    '- The commit establishes source provenance only; it is not claimed to reproduce the executable byte for byte. The executable identity is its SHA-256.',
    '- Every published file is listed with its SHA-256 in publication-manifest.csv (INSTALL.md is listed too; the manifest does not list itself).',
    '',
    '## What is published',
    'AgendisTools\ScriptManager.dev   (3 files - merge into the existing development folder)',
    '- ConfigVS-ScriptManagerLite.bat      -> REPLACES the existing file of the same name',
    '- Lite\ScriptManager.exe              -> REPLACES the existing executable',
    '- Lite\ScriptManager.pdb              -> REPLACES the existing symbol file',
    '',
    'AgendisTools\ScriptManager.bin   (6 files - new Lite delivery folder)',
    '- ScriptManagerAgendisLite.bat, Lite\ScriptManager.exe, Lite\ScriptManager.exe.config, Lite\ScriptManager.pdb,',
    '  Lite\log4net.dll, Lite\Config\Log4net.config',
    '',
    'docs\   (documentation, NOT part of the runtime overlay)',
    '- README-Lite.txt, Database.config.example, Preflight-History-ReadOnly.sql',
    '',
    '## Manual integration rules',
    '1. Work on a clean checkout/branch you own; do not overwrite anything else. Copy FILE BY FILE (no mirror/sync).',
    '2. Before replacing the three Dev files, keep a byte-exact backup of them.',
    '3. NEVER overwrite or delete in the Dev Lite folder: Config\Database.config, Config\ScriptManager.config, Config\Log4net.config,',
    '   log4net.dll, ScriptManager.exe.config and everything under Logs\. They are intentionally not published.',
    '4. Never touch the 2012, 2014 and 2016 distributions, ScriptManagerAgendis.bat or ScriptManager.bin\Config.',
    '5. The Bin Lite folder deliberately has no Config\Database.config and no Config\ScriptManager.config. The customer-specific',
    '   connection file is supplied by the existing customer configuration mechanism. docs\Database.config.example is only a template',
    '   (no credentials; it belongs in ScriptManager.bin\Lite\Config\ as Database.config once filled in). The launcher refuses to start',
    '   while a template token of the example remains.',
    '6. The Bin launcher expects the SQL scripts in ..\SQL\ (ScriptManager.bin\SQL). That folder is NOT created here; the official',
    '   packaging must provide it.',
    '7. The Dev launcher keeps using the established development configuration (AgendisConfig\VS); no local Database.config is introduced.',
    '',
    '## Operation reminders',
    '- The operator must type O (or o) when ScriptManager asks for confirmation. No launcher answers for the operator.',
    '- Exit code 3 = cancelled; exit code 2 = launcher guard (missing/unfinished config); exit code 0 does NOT prove that every script succeeded.',
    '  Read the FINISHED summary, dbo.HistoriqueScriptSql and Logs\ScriptManager-INFO.log.',
    '- Before running Lite on a database that the original tool already processed, run docs\Preflight-History-ReadOnly.sql (read-only).',
    '',
    '## Not qualified by this publication',
    'Real SQL-login connectivity, SQL Server 2012-2016 compatibility, concurrent execution and actual customer deployment.'
  )
  $instPath = Join-Path $tmpDir 'INSTALL.md'
  [IO.File]::WriteAllText($instPath, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding($false)))
  [void]$rows.Add([pscustomobject]@{ RelativePath = 'INSTALL.md'; Role = 'Install'; Size = (Get-Item -LiteralPath $instPath).Length; SHA256 = (Sha $instPath); SourceInCandidate = 'generated'; SourceSHA256 = '' })
  $manPath = Join-Path $tmpDir 'publication-manifest.csv'
  $rows | Sort-Object RelativePath | Export-Csv -LiteralPath $manPath -NoTypeInformation -Encoding UTF8

  # -------------------------------------------------------------- 6. full verification of the incomplete folder
  $files = @(Get-ChildItem -LiteralPath $tmpDir -Recurse -File -Force)
  if ($files.Count -ne 14) { Fail "expected 14 files (12 published + INSTALL.md + manifest), found $($files.Count)" }
  $mrows = @(Import-Csv -LiteralPath $manPath)
  if ($mrows.Count -ne 13) { Fail "manifest must list 13 files, lists $($mrows.Count)" }
  foreach ($r in $mrows) {
    $fp = Join-Path $tmpDir ($r.RelativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $fp) -or (Sha $fp) -ne $r.SHA256 -or (Get-Item -LiteralPath $fp).Length -ne [int64]$r.Size) { Fail "manifest verification failed: $($r.RelativePath)" } }
  foreach ($f in $files) {
    $rel = $f.FullName.Substring($tmpDir.Length + 1) -replace '\\', '/'
    if ($rel -ne 'publication-manifest.csv' -and @($mrows | Where-Object { $_.RelativePath -eq $rel }).Count -ne 1) { Fail "file not listed in the manifest: $rel" }
    if (($forbiddenLeaf -contains $f.Name) -or $rel -match '(^|/)Logs/') { Fail "forbidden file published: $rel" }
    if (([int]$f.Attributes) -band [int][IO.FileAttributes]::ReadOnly) { Fail "published file is ReadOnly: $rel" }
    if (([int]$f.Attributes) -band [int][IO.FileAttributes]::ReparsePoint) { Fail "reparse point in publication: $rel" } }
  foreach ($e in @('AgendisTools\ScriptManager.dev\Lite\ScriptManager.exe', 'AgendisTools\ScriptManager.bin\Lite\ScriptManager.exe')) {
    if ((Sha (Join-Path $tmpDir $e)) -ne $ExpectedExeSha256) { Fail "published executable differs from the qualified artifact: $e" } }
  foreach ($t in @($manPath, $instPath)) {
    if ([IO.File]::ReadAllText($t) -match '(?i)\b[a-z]:\\|\\\\[a-z0-9_.-]+\\') { Fail "machine-specific absolute path found in $(Split-Path -Leaf $t)" } }
  foreach ($d in @(Get-ChildItem -LiteralPath $tmpDir -Recurse -Directory -Force)) {
    if (([int]$d.Attributes) -band [int][IO.FileAttributes]::ReparsePoint) { Fail 'reparse point directory in publication' } }
  foreach ($m in $man) { if ((Sha (Join-Path $stage $m.Path)) -ne $m.SHA256) { Fail "candidate file changed during publication: $($m.Path)" } }

  # -------------------------------------------------------------- 7. only now does the publication become visible (rename)
  [IO.Directory]::Move($tmpDir, $finalDir)
  $created = $false
  Write-Output "PUBLISHED: publish\$name"
  Write-Output 'FILES: 14 (12 published + INSTALL.md + publication-manifest.csv)'
  Write-Output "EXE: $ExpectedExeSha256"
  Write-Output "SOURCE-HEAD: $head"
  exit 0
}
catch {
  $msg = $_.Exception.Message
  if ($created -and $tmpDir -and $pubRoot -and (Test-Path -LiteralPath $tmpDir) -and (IsUnder $tmpDir $pubRoot) -and ((Split-Path -Leaf $tmpDir) -like '.incomplete-*') -and -not (IsReparse $tmpDir)) {
    try { [IO.Directory]::Delete($tmpDir, $true) } catch { Write-Output "WARNING: could not remove the incomplete folder $tmpDir : $($_.Exception.Message)" } }
  Write-Output "RESULT: NOT PUBLISHED - $msg"
  exit 1
}
