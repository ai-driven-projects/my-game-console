# Gera o instalador MSI do My Game Console (e o .sha256 que a atualização automática confere).
# Uso (na raiz do repositório ou nesta pasta):
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
# Saída: installer\bin\Release\MyGameConsole-<versão>-Setup.msi (+ .sha256)
# A versão vem do <Version> em src\MyGameConsole\MyGameConsole.csproj.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' } else { $dotnet = $dotnet.Source }

$csproj = Join-Path $root 'src\MyGameConsole\MyGameConsole.csproj'
$version = ([regex]::Match((Get-Content -Raw $csproj), '<Version>([^<]+)</Version>')).Groups[1].Value
if ([string]::IsNullOrWhiteSpace($version)) { throw "Não encontrei <Version> em $csproj." }

$publish = Join-Path $PSScriptRoot 'publish'
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

Write-Host "== Publicando o app $version (self-contained, win-x64) ==" -ForegroundColor Cyan
& $dotnet publish (Join-Path $root 'src\MyGameConsole') -c Release -r win-x64 --self-contained true `
    -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)." }

Write-Host '== Compilando o MSI ==' -ForegroundColor Cyan
& $dotnet build (Join-Path $PSScriptRoot 'MyGameConsole.wixproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build do instalador falhou ($LASTEXITCODE)." }

$msiName = "MyGameConsole-$version-Setup.msi"
$msi = Get-ChildItem (Join-Path $PSScriptRoot 'bin\Release') -Recurse -Filter $msiName | Select-Object -First 1
if ($null -eq $msi) { throw "O MSI $msiName não foi gerado." }

# Hash no formato do sha256sum ("<hash>  <arquivo>"); o app confere antes de instalar uma atualização.
$hash = (Get-FileHash $msi.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$($msi.FullName).sha256"
[System.IO.File]::WriteAllText($shaPath, "$hash  $($msi.Name)`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host "== Pronto: $($msi.FullName) ($([math]::Round($msi.Length / 1MB, 1)) MB) ==" -ForegroundColor Green
Write-Host "   SHA-256: $hash"
