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
  `DesktopTweaksService` e são ligados/desligados em `GameModeService.Apply`/`Restore`. Todo ajuste novo
  também entra no checklist da tela do console (`Forms/ConsoleForm.GameMode.cs`, via `GameModeService.Inspect`).
- Ajustes que exigem administrador (ex.: senha ao acordar, `PowerService`) só pedem UAC em ação do usuário
  (`ReapplyIfEnabled(interactive: true)`); no início do app (`interactive: false`) ficam pendentes no checklist.
  O UAC é obtido relançando o próprio exe elevado com `--wake-password` (tratado em `Program.cs`).
- O que o app não consegue fazer sozinho (login automático, que exige a senha) vira item "para fazer à mão"
  no checklist, com o estado lido do Windows e a tela certa aberta com A.
- A tela do console (`Forms/ConsoleForm.cs`) é 100% desenhada em `OnPaint` e precisa continuar navegável
  só com controle: qualquer confirmação deve usar o overlay Sim/Não da própria tela, nunca `MessageBox`.
- Atalhos do controle: o catálogo é único, em `Models/ControllerCombo.cs` (botões, texto e título). Um atalho
  novo entra lá, ganha o seu campo em `AppSettings` (via `IsEnabledIn`/`SetEnabledIn`) e um caso em
  `TrayApplicationContext.OnControllerComboTriggered`; a página "Controle" o lista sozinha.
- "Mouse pelo analógico" (`ControllerMouseService`) e teclado virtual (`VirtualKeyboardService`) guardam o
  estado no `AppSettings`; o `TrayApplicationContext` sincroniza em `SyncControllerFeatures` e pausa o mouse
  (`Suspended`) enquanto a tela do console está visível, onde o controle navega a própria tela.
- O cursor é movido pelo **analógico direito** de propósito: o esquerdo é o que navega o app em primeiro plano
  (Big Picture, jogos) e usá-lo criaria conflito. Por isso a rolagem ficou no direcional ▲▼. Controles HID
  publicam o analógico direito como Z/Rz ou Rx/Ry — ver `HidGamepadDevice.ReadAxes`; os que não publicam
  nenhum dos pares precisam da opção "Analógico que move o cursor" em Esquerdo.
- O desenho do controle fica em `App/GamepadArt.cs`, sem depender do formulário: dá para renderizá-lo em um
  PNG por um projeto de teste separado para conferir a arte sem abrir o app.

## Versões e releases

- Versão única em `<Version>` do csproj (Major.Minor.Patch, a partir de 1.0.0). Não edite à mão: use
  `scripts/release.ps1` (`-Bump patch|minor|major` ou `-Version X.Y.Z`), que grava a versão, gera o MSI
  + `.sha256` (`installer/build.ps1`), commita, cria a tag `vX.Y.Z` e publica a release com `gh`.
- `UpdateService` depende desse contrato: tag `vX.Y.Z` e assets `*.msi` e `*.msi.sha256` na release.
  Só releases publicadas (não draft/prerelease) e só em repositório público são vistas pelo app.
- Publicar uma release é ação externa: só rode o script quando o usuário pedir.
