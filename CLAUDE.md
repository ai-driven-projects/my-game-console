# My Game Console — notas para o Claude Code

App de bandeja do sistema (WinForms, .NET 10, C#) para deixar o Windows com cara de console (estilo SteamOS).
Idioma da interface e da documentação: português (pt-BR). Identificadores de código em inglês.

## Comandos

```bash
dotnet build MyGameConsole.slnx -c Release
dotnet run --project src/MyGameConsole
```

Se `dotnet` não estiver no PATH da sessão, use `C:\Program Files\dotnet\dotnet.exe`.

## Convenções

- Sem Windows Forms Designer: formulários são construídos em código (`Forms/*.cs`).
- P/Invoke centralizado em `Native/NativeMethods.cs` usando `LibraryImport` (exige `AllowUnsafeBlocks`).
- Cada funcionalidade do sistema vive em um serviço em `Services/`; o `TrayApplicationContext` só orquestra.
- Configurações: `Models/AppSettings.cs` + `SettingsService` (JSON em `%LocalAppData%\MyGameConsole`).
- Ações destrutivas (desligar, reiniciar) sempre pedem confirmação.
- Ao mexer no Modo Console, garanta que `explorer.exe` seja restaurado em qualquer caminho de saída.
- "Modo Game" (`GameModeService`) é persistente por design: guarda o estado original em `AppSettings.GameModeBackup`
  ao ativar, reaplica no início do app e só restaura ao desativar. Novos ajustes de desktop entram em
  `DesktopTweaksService` e são ligados/desligados em `GameModeService.Apply`/`Restore`.
- A tela do console (`Forms/ConsoleForm.cs`) é 100% desenhada em `OnPaint` e precisa continuar navegável
  só com controle: qualquer confirmação deve usar o overlay Sim/Não da própria tela, nunca `MessageBox`.

## Versões e releases

- Versão única em `<Version>` do csproj (Major.Minor.Patch, a partir de 1.0.0). Não edite à mão: use
  `scripts/release.ps1` (`-Bump patch|minor|major` ou `-Version X.Y.Z`), que grava a versão, gera o MSI
  + `.sha256` (`installer/build.ps1`), commita, cria a tag `vX.Y.Z` e publica a release com `gh`.
- `UpdateService` depende desse contrato: tag `vX.Y.Z` e assets `*.msi` e `*.msi.sha256` na release.
  Só releases publicadas (não draft/prerelease) e só em repositório público são vistas pelo app.
- Publicar uma release é ação externa: só rode o script quando o usuário pedir.
