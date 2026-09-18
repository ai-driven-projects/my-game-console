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
- Ícones da área de trabalho (`DesktopTweaksService.SetDesktopIconsHidden`): o comando do Explorer só alterna e
  grava em `Shell\Bags\1\Desktop\FFlags` (bit `FWF_NOICONS`), nunca em `Advanced\HideIcons`. Decida sempre pelo
  estado na tela (a `SysListView32` visível ou não), nunca pelo registro, e grave os dois valores.
- Ajustes que exigem administrador (ex.: senha ao acordar, `PowerService`) só pedem UAC em ação do usuário
  (`ReapplyIfEnabled(interactive: true)`); no início do app (`interactive: false`) ficam pendentes no checklist.
  O UAC é obtido relançando o próprio exe elevado com `--wake-password` (tratado em `Program.cs`).
- A tela de bloqueio do Modo Game (`LockScreenService`) grava `HKLM\...\PersonalizationCSP` (administrador:
  relança o exe elevado com `--lock-screen`, como a senha ao acordar) e aponta para uma cópia em JPEG em
  `%ProgramData%\MyGameConsole\lockscreen.jpg`: a tela de bloqueio é desenhada pelo sistema, que não lê o perfil.
- "Entrar direto no console" (Modo Game): o `TrayApplicationContext` abre a tela do console antes de todo o resto
  no início e só reaplica o Modo Game quando a barra de tarefas (`Shell_TrayWnd`) existe, trazendo a tela de volta
  para a frente. A tarefa agendada não tem mais atraso no logon; `StartupService.RemoveTaskDelay` só recria a
  tarefa se ela abre este mesmo exe. Cuidado ao testar pela cópia de `bin\Debug`: "Iniciar com o Windows" aponta
  a tarefa/Run para o exe que está rodando.
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
- Biblioteca do Steam: `SteamLibraryService` lê os arquivos locais do Steam (parser VDF texto em `App/Vdf.cs`) e
  `App/GameArtCache.cs` guarda as capas já redimensionadas e o fundo do jogo já composto (não redimensionar arte
  a cada repintura). As artes ficam em três formatos de pasta no `librarycache`, conforme a versão do Steam: ver
  `FindArt`. Na tela do console, a fileira 0 é o botão de atualizações (`Row.IsTopBar`), desenhado pelo cabeçalho
  junto com o relógio e as baterias (controles: XInput ou a bateria Bluetooth que o Windows guarda no nó
  `BTHLE`/`BTHENUM` do aparelho, ver `BluetoothBattery`); a última é a de energia (`Row.IsBottomBar`), no canto
  inferior direito. As capas dos jogos são o destaque: os cartões do sistema ficam sempre menores que elas.
  Com a tela do console aberta, o Big Picture fica minimizado (`SteamService.MinimizeBigPicture`): o Steam também lê
  o controle e tomava a frente sozinho. Se ele aparecer sem ter sido pedido (cartão do Big Picture ou abrir um jogo,
  que marcam `_handingOffToSteam`), volta a ser minimizado no timer do relógio.
- Os ícones dos cartões de baixo da tela do console ficam em `App/ConsoleIcons.cs`: badge redondo com o degradê
  do logo da Steam e símbolo branco sólido, vetorial (GraphicsPath). Um cartão novo ganha um ícone lá, no
  mesmo estilo, para continuar coerente com o logo da Steam ao lado.
- Gravação da tela: `ScreenRecorderService` + `Services/Recording/` (Desktop Duplication → textura, `AudioCapture`
  do WASAPI → PCM 48 kHz, `Mp4Writer` com o Sink Writer do Media Foundation), tudo numa thread só, alinhado pelo QPC.
  O som tem até duas capturas (loopback da saída e, opcional, o microfone), somadas numa faixa só no `Session`
  pelo horário de cada trecho, com 200 ms de atraso para as duas chegarem antes de ir para o arquivo. Nunca confie
  cegamente no horário do pacote: com `TIMESTAMP_ERROR`, zero ou fora de [agora − 2 s, agora + 500 ms] ele é
  recolocado logo depois do anterior — um único horário no futuro já emudeceu o microfone a gravação inteira.
  Cada gravação anota o que cada captura recebeu em `%LocalAppData%\MyGameConsole\gravação.log`, e o fim da
  gravação avisa (`LastWarning`) se o microfone estava ligado e não saiu no vídeo.
  As interfaces COM (DXGI, D3D11, MF, WASAPI) são chamadas pela vtable em `Native/NativeMethods.Media.cs`, sem RCW,
  para liberar texturas e amostras a cada quadro; os números dos slots seguem a ordem dos cabeçalhos do SDK
  (contando os 3 do IUnknown) — conferir antes de acrescentar um método. O encoder recebe a textura direto
  (`OnGpu`) e cai no caminho pela memória se o driver recusar o primeiro quadro. Perder a duplicação (tela
  bloqueada ou apagada, UAC, troca de resolução) não para a gravação: repete a última imagem até voltar. Janelas
  do app que não devem sair no vídeo usam `WDA_EXCLUDEFROMCAPTURE` (ver `ToastForm`). Começar sempre passa pela
  contagem "3, 2, 1" do `TrayApplicationContext.ToggleRecording` (na tela do console, se aberta; senão no aviso
  flutuante), que some antes do primeiro quadro; o começo em si não tem aviso. Sair do app e suspender
  chamam `Stop`, que fecha o MP4 (sem o `Finalize` o arquivo não abre).
- O desenho do controle fica em `App/GamepadArt.cs`, sem depender do formulário: dá para renderizá-lo em um
  PNG por um projeto de teste separado para conferir a arte sem abrir o app.

## Versões e releases

- Versão única em `<Version>` do csproj (Major.Minor.Patch, a partir de 1.0.0). Não edite à mão: use
  `scripts/release.ps1` (`-Bump patch|minor|major` ou `-Version X.Y.Z`), que grava a versão, gera o MSI
  + `.sha256` (`installer/build.ps1`), commita, cria a tag `vX.Y.Z` e publica a release com `gh`.
- `UpdateService` depende desse contrato: tag `vX.Y.Z` e assets `*.msi` e `*.msi.sha256` na release.
  Só releases publicadas (não draft/prerelease) e só em repositório público são vistas pelo app.
- Publicar uma release é ação externa: só rode o script quando o usuário pedir.
