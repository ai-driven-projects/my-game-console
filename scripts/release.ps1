<#
.SYNOPSIS
Publica uma nova versão do My Game Console como release no GitHub.

.DESCRIPTION
Fluxo completo de uma versão:
  1. Define a nova versão: explícita (-Version 1.2.0) ou incrementando a atual (-Bump patch|minor|major; padrão patch).
  2. Grava a versão em <Version> do csproj (fonte da versão do app, do MSI e da tag).
  3. Gera o instalador com installer\build.ps1 (MSI + .sha256).
  4. Faz o commit "Versão X.Y.Z", cria a tag anotada vX.Y.Z e envia branch e tag para o origin.
  5. Cria a release vX.Y.Z no GitHub com o MSI e o .sha256 anexados: é exatamente o que o app
     procura ao verificar atualizações (UpdateService).

Pré-requisitos: árvore de trabalho limpa (commite ou faça stash antes), GitHub CLI autenticado (gh auth login)
e .NET SDK 10.

.PARAMETER Version
Versão exata a publicar (Major.Minor.Patch). Se omitida, incrementa a atual conforme -Bump.

.PARAMETER Bump
Qual parte incrementar quando -Version não é informada: patch (1.0.0 -> 1.0.1), minor (1.0.3 -> 1.1.0) ou major (1.4.0 -> 2.0.0).

.PARAMETER Notes
Notas da release (markdown). Alternativas: -NotesFile ou, se nenhum for dado, a lista de commits desde a tag anterior.

.PARAMETER NotesFile
Arquivo com as notas da release.

.PARAMETER Draft
Cria a release como rascunho (o app só vê releases publicadas).

.PARAMETER Prerelease
Marca como pré-lançamento (o app ignora pré-lançamentos: ele consulta apenas a "latest").

.PARAMETER SkipBuild
Não gera o instalador; usa o MSI já existente em installer\bin\Release para a versão.

.PARAMETER NoPush
Faz tudo localmente (csproj, build, commit, tag) e para antes do push e da release.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release.ps1
Publica a próxima versão de patch (ex.: 1.0.0 -> 1.0.1).

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 1.0.0 -Notes "Primeira versão."

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Bump minor -NotesFile notas.md
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [string]$Notes,
    [string]$NotesFile,
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$SkipBuild,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\MyGameConsole\MyGameConsole.csproj'
$repo = 'ai-driven-projects/my-game-console'

function Invoke-Git {
    # Roda o git na raiz do repositório e falha se o comando falhar.
    $output = & git -C $root @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') falhou ($LASTEXITCODE)." }
    return $output
}

# --- Ferramentas -------------------------------------------------------------
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    $gh = 'C:\Program Files\GitHub CLI\gh.exe'
    if (-not (Test-Path $gh)) { throw 'GitHub CLI (gh) não encontrado. Instale com: winget install --id GitHub.cli' }
} else {
    $gh = $gh.Source
}

if (-not $NoPush) {
    & $gh auth status | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI não está autenticado. Rode: gh auth login' }
}

# --- Estado do repositório ---------------------------------------------------
$dirty = Invoke-Git status --porcelain
if ($dirty) {
    Write-Host $dirty
    throw 'Há alterações não commitadas. Faça commit (ou stash) antes de publicar uma versão.'
}

$branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') {
    Write-Warning "Você está na branch '$branch', não em 'main'. A release será criada a partir dela."
}

# --- Versão ------------------------------------------------------------------
$content = Get-Content -Raw $csproj
$match = [regex]::Match($content, '<Version>([^<]+)</Version>')
if (-not $match.Success) { throw "Não encontrei <Version> em $csproj." }
$current = $match.Groups[1].Value.Trim()
if ($current -notmatch '^\d+\.\d+\.\d+$') { throw "A versão atual no csproj ($current) não está no formato Major.Minor.Patch." }

if ([string]::IsNullOrWhiteSpace($Version)) {
    $parts = $current.Split('.') | ForEach-Object { [int]$_ }
    switch ($Bump) {
        'major' { $parts = @($parts[0] + 1, 0, 0) }
        'minor' { $parts = @($parts[0], $parts[1] + 1, 0) }
        default { $parts = @($parts[0], $parts[1], $parts[2] + 1) }
    }
    $Version = $parts -join '.'
} elseif ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versão inválida: '$Version'. Use o formato Major.Minor.Patch (ex.: 1.0.1)."
}

if ([version]$Version -le [version]$current -and $Version -ne $current) {
    throw "A versão $Version é menor que a atual ($current)."
}

$tag = "v$Version"
if (Invoke-Git tag -l $tag) { throw "A tag $tag já existe localmente." }
if (-not $NoPush) {
    $remoteTag = Invoke-Git ls-remote --tags origin "refs/tags/$tag"
    if ($remoteTag) { throw "A tag $tag já existe no origin." }
}

Write-Host "== Versão: $current -> $Version (tag $tag) ==" -ForegroundColor Cyan

# --- csproj --------------------------------------------------------------------
if ($Version -ne $current) {
    $updated = $content -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    [System.IO.File]::WriteAllText($csproj, $updated, (New-Object System.Text.UTF8Encoding($false)))
}

# --- Instalador ----------------------------------------------------------------
$msiName = "MyGameConsole-$Version-Setup.msi"
if (-not $SkipBuild) {
    & (Join-Path $root 'installer\build.ps1')
}

$msi = Get-ChildItem (Join-Path $root 'installer\bin\Release') -Recurse -Filter $msiName -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $msi) { throw "Instalador $msiName não encontrado em installer\bin\Release. Rode sem -SkipBuild." }
$sha = "$($msi.FullName).sha256"
if (-not (Test-Path $sha)) { throw "Arquivo de hash não encontrado: $sha" }

# --- Notas ---------------------------------------------------------------------
if ($NotesFile) {
    $Notes = Get-Content -Raw $NotesFile
} elseif ([string]::IsNullOrWhiteSpace($Notes)) {
    $previousTag = $null
    try { $previousTag = (& git -C $root describe --tags --abbrev=0 --match 'v*' 2>$null) } catch { }
    if ($LASTEXITCODE -ne 0) { $previousTag = $null }

    if ($previousTag) {
        $log = Invoke-Git log --pretty=format:'- %s' "$previousTag..HEAD"
        $Notes = "## Mudanças desde $previousTag`n`n" + (($log | Where-Object { $_ }) -join "`n")
    } else {
        $Notes = 'Primeira versão publicada.'
    }
}

$notesPath = Join-Path ([System.IO.Path]::GetTempPath()) "MyGameConsole-release-$Version.md"
[System.IO.File]::WriteAllText($notesPath, $Notes, (New-Object System.Text.UTF8Encoding($false)))

# --- Commit + tag --------------------------------------------------------------
if ($Version -ne $current) {
    Invoke-Git add -- $csproj | Out-Null
    Invoke-Git commit -m "Versão $Version" | Out-Null
    Write-Host "== Commit 'Versão $Version' criado ==" -ForegroundColor Cyan
}
Invoke-Git tag -a $tag -m "My Game Console $Version" | Out-Null
Write-Host "== Tag $tag criada ==" -ForegroundColor Cyan

if ($NoPush) {
    Write-Host "== -NoPush: parando aqui. Para publicar depois: git push origin $branch $tag; gh release create $tag ... ==" -ForegroundColor Yellow
    return
}

# --- Push + release ------------------------------------------------------------
Write-Host '== Enviando branch e tag ==' -ForegroundColor Cyan
Invoke-Git push origin $branch | Out-Null
Invoke-Git push origin $tag | Out-Null

Write-Host "== Criando a release $tag em $repo ==" -ForegroundColor Cyan
$ghArgs = @('release', 'create', $tag, $msi.FullName, $sha,
    '--repo', $repo,
    '--title', "My Game Console $Version",
    '--notes-file', $notesPath)
if ($Draft) { $ghArgs += '--draft' }
if ($Prerelease) { $ghArgs += '--prerelease' }

& $gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create falhou ($LASTEXITCODE). A tag já foi enviada; corrija e crie a release manualmente." }

Write-Host "== Release $tag publicada: https://github.com/$repo/releases/tag/$tag ==" -ForegroundColor Green
