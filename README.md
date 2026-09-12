# My Game Console

Aplicação desktop para Windows (C# / .NET 10 / WinForms) que roda na **bandeja do sistema** e ajuda a
transformar o PC em algo parecido com um console (estilo SteamOS): abrir o Steam Big Picture,
esconder a área de trabalho, reagir à conexão de controles, atalhos rápidos e ações de energia.

## Funcionalidades

- **Ícone na bandeja** com menu de contexto e status (Steam, controles conectados).
- **Tela do console** (`Ctrl+Alt+G` por padrão, configurável, ou segurando **− e +** / Back + Start no
  controle por meio segundo): launcher em tela cheia estilo PlayStation / Big Picture, navegável só com o
  controle (D-pad/analógico, A seleciona, B volta), teclado ou mouse.
  Traz Big Picture, atalhos, Modo Game, configurações, suspender, reiniciar e desligar (com confirmação na tela).
- **Configurações dentro da tela do console**: página com lista grande e navegável só com o controle
  (A alterna/seleciona, ◀ ▶ ajusta, B volta), painel de descrição ao lado e salvamento imediato. Captura a
  tecla de atalho pelo teclado e o mapeamento dos botões HID pressionando o botão no próprio controle.
  A janela clássica (teclado/mouse) continua no menu da bandeja e no item "Avançado".
- **Modo Game** (persistente): barra de tarefas em auto-ocultar, ícones da área de trabalho escondidos,
  papel de parede do console: a imagem `img/wallpaper.webp`, que vai junto com o app e com o instalador, ou
  uma imagem sua (se o Windows não tiver o decodificador WebP, o app gera uma arte própria como reserva),
  e entrada sem senha ao acordar da suspensão (o mesmo que "Nunca" em Contas > Opções de entrada, aplicado
  em todos os planos de energia; Win+L continua pedindo senha; gravar pede o UAC uma vez). O estado anterior
  é guardado e restaurado ao desativar; ao ligar o PC, o app reaplica tudo (ativar o Modo Game também liga
  "Iniciar com o Windows"). O tile "Modo Game" da tela do console abre um checklist: o estado real de cada
  ajuste no Windows (aplicado, pendente, desligado) e os passos que só você pode fazer, como o login
  automático ao ligar o PC (netplwiz) e a instalação do Steam, com A abrindo a tela certa do Windows.
- **Modo Console**: esconde a barra de tarefas/área de trabalho (`explorer.exe`) e abre o Steam Big Picture.
  Ao desmarcar, ou ao sair do app, o shell do Windows é restaurado.
- **Steam Big Picture**: abrir/fechar pelo menu ou com duplo clique no ícone. Detecção automática da
  pasta do Steam pelo registro (ou caminho manual nas configurações).
- **Controles XInput e HID/DirectInput**: até 4 controles XInput (Xbox e compatíveis, 8BitDo por dongle
  2.4G ou cabo) mais controles HID genéricos (ex.: 8BitDo por Bluetooth, que o Windows expõe só como
  "Controlador de jogo compatível com HID"). Opção de abrir o Big Picture automaticamente quando o primeiro
  controle conectar; conectar ou desconectar um controle não mostra aviso. O mapeamento dos botões HID fica em `HidButtons`
  no `settings.json` (padrão no layout D-input do 8BitDo); a tela de configurações mostra os números
  dos botões pressionados para conferir.
- **Atalhos personalizados**: lista de jogos/launchers/apps no menu (ex.: Playnite, Epic, emuladores).
- **Energia**: suspender, hibernar, reiniciar e desligar (com confirmação). Na tela do console, "Suspender"
  oferece duas opções: suspender o PC (padrão) ou o **repouso de console**: apaga só a tela e, em notebooks
  Lenovo com o app elevado, a luz do teclado; o PC continua ligado sem entrar em suspensão por inatividade,
  e qualquer botão do controle (inclusive por Bluetooth), tecla ou mouse reacende tudo.
- **Iniciar com o Windows**: registro em `HKCU\...\Run` ou, com "Iniciar como administrador", uma tarefa
  agendada no logon com privilégios mais altos (criada com um aval do UAC; sem limite de tempo e sem
  restrições de bateria). Elevado, o app controla a luz do teclado em notebooks Lenovo; o Steam e os
  atalhos continuam sendo abertos sem privilégios, via Explorer. Há também "Reiniciar o app agora como
  administrador" nas configurações do console.
- **Atualizações pelo GitHub**: o app consulta as [releases do repositório](https://github.com/ai-driven-projects/my-game-console/releases)
  ao iniciar (opcional) e pelo menu da bandeja ou pela tela do console ("Verificar atualizações agora").
  Havendo versão nova, baixa o MSI, confere o SHA-256 publicado e executa o instalador, que fecha o app,
  atualiza e o reabre. Nada é baixado nem instalado sem confirmação.
- **Configurações** persistidas em `%LocalAppData%\MyGameConsole\settings.json`.
- Início silencioso: o app vai direto para a bandeja, sem balão de boas-vindas. Instância única (uma segunda
  execução encerra em silêncio).

## Requisitos

- Windows 10/11
- [.NET SDK 10](https://dotnet.microsoft.com/download) para compilar

## Como compilar e executar

```bash
dotnet build MyGameConsole.slnx -c Release
```

```bash
dotnet run --project src/MyGameConsole
```

O app não abre janela: procure o ícone do controle na bandeja (ao lado do relógio).

## Instalador (MSI)

O instalador fica em `installer/` e usa o [WiX Toolset](https://wixtoolset.org/) via NuGet (nada a instalar
além do .NET SDK). O app é publicado **self-contained** (win-x64), então o PC de destino não precisa do
runtime do .NET.

```bash
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

Saída: `installer/bin/Release/MyGameConsole-<versão>-Setup.msi` e um `.sha256` ao lado (a versão vem do
`<Version>` do csproj).

O que o instalador faz:

- Instala em `Arquivos de Programas\My Game Console` (para todos os usuários, pede UAC) e cria atalho no
  Menu Iniciar; ao concluir, oferece abrir o app.
- Fecha o app em execução antes de instalar, atualizar ou desinstalar (ele não tem janela, então o
  Windows não consegue pedir para fechá-lo).
- Ao desinstalar, remove a entrada em `HKCU\...\Run` e a tarefa agendada `MyGameConsole` criadas por
  "Iniciar com o Windows". As configurações em `%LocalAppData%\MyGameConsole` são mantidas, e os
  ajustes do Modo Game (barra, ícones, papel de parede, senha ao acordar) devem ser desativados no app antes de desinstalar.
- Atualizações: instalar um MSI de versão maior substitui a anterior mantendo inicialização e configurações.

O MSI e o executável não são assinados. Com o **Smart App Control** ligado (Windows 11), o Windows pode
bloquear tanto o instalador quanto o app; nesse caso é preciso desativá-lo em Segurança do Windows >
Controle de aplicativos e navegador.

## Versões e publicação de releases

A versão do app segue `Major.Minor.Patch` e tem uma única fonte: o `<Version>` em
`src/MyGameConsole/MyGameConsole.csproj`. Dela saem a versão do MSI, a tag da release (`v1.0.1`) e a
comparação feita pela verificação de atualização. A primeira versão publicada é a `1.0.0`; correções
incrementam o patch (`1.0.1`, `1.0.2`...), funcionalidades novas o minor e mudanças grandes o major.

Para publicar uma versão nova (requer o [GitHub CLI](https://cli.github.com/) autenticado com `gh auth login`
e a árvore de trabalho sem alterações pendentes):

```bash
powershell -ExecutionPolicy Bypass -File scripts/release.ps1
```

Sem parâmetros, o script incrementa o patch. Opções: `-Version 1.2.0`, `-Bump minor|major`,
`-Notes "texto"` ou `-NotesFile notas.md` (sem notas, usa a lista de commits desde a tag anterior),
`-Draft`, `-Prerelease`, `-SkipBuild` (reaproveita o MSI já gerado) e `-NoPush` (só commit e tag locais).

O que o script faz, em ordem: grava a versão no csproj, gera o MSI e o `.sha256` (`installer/build.ps1`),
faz o commit `Versão X.Y.Z`, cria a tag `vX.Y.Z`, envia branch e tag, e cria a release no GitHub com os dois
arquivos anexados.

Como o app encontra a atualização (`Services/UpdateService.cs`): consulta
`https://api.github.com/repos/ai-driven-projects/my-game-console/releases/latest`, lê a versão da tag, e
se for maior que a instalada baixa o asset `*.msi` para `%LocalAppData%\MyGameConsole\updates`, confere o
`*.sha256` (se publicado) e roda `msiexec /i`. Rascunhos e pré-lançamentos não contam como "latest", então
não são oferecidos. A API só expõe releases de repositórios **públicos** sem token; enquanto o repositório
estiver privado, o app responde "já está na versão mais recente".

## Gerar executável único (publish)

```bash
dotnet publish src/MyGameConsole -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

O resultado fica em `publish/MyGameConsole.exe` (requer o .NET 10 Runtime instalado na máquina).
Para não depender do runtime, use `--self-contained true`.

## Estrutura

```
src/MyGameConsole/
├── Program.cs                    # ponto de entrada, instância única
├── App/TrayApplicationContext.cs # ícone da bandeja, menu, timer de monitoramento
├── App/Theme.cs                  # cores, glifos e fonte de ícones
├── Forms/ConsoleForm.cs          # tela do console (launcher em tela cheia, controle/teclado/mouse)
├── Forms/ConsoleForm.Settings.cs # página de configurações dentro da tela do console
├── Forms/ConsoleForm.GameMode.cs # página "Modo Game": checklist do que o app aplica e do que fazer à mão
├── Forms/SettingsForm.cs         # janela de configurações clássica (teclado/mouse)
├── Forms/ShortcutEditorForm.cs   # diálogo de atalho
├── Forms/UpdateForm.cs           # janela de atualização (notas da release, download, instalar)
├── Models/AppSettings.cs         # modelo das configurações (JSON)
├── Models/GamepadState.cs        # estado normalizado de controle (botões/analógicos)
├── Native/NativeMethods.cs       # P/Invoke (user32, powrprof, XInput, hid, cfgmgr32)
├── Services/
│   ├── SettingsService.cs        # carregar/salvar settings.json
│   ├── SteamService.cs           # localizar Steam, Big Picture
│   ├── ControllerService.cs      # controles: XInput + HID, estado unificado
│   ├── HidGamepadService.cs      # enumeração de controles HID (DirectInput)
│   ├── HidGamepadDevice.cs       # leitura/parsing de um controle HID
│   ├── ControllerComboService.cs # gesto Back + Start (− e +) que abre a tela do console
│   ├── ShellService.cs           # parar/iniciar explorer.exe
│   ├── ConsoleModeService.cs     # orquestra o Modo Console
│   ├── DesktopTweaksService.cs   # barra auto-ocultar, ícones, papel de parede
│   ├── GameModeService.cs        # Modo Game persistente (backup/restauração)
│   ├── HotkeyService.cs          # tecla de atalho global
│   ├── LogonService.cs           # lê se o login automático (netplwiz) está ligado
│   ├── PowerService.cs           # suspender/hibernar/reiniciar/desligar e senha ao acordar (por plano de energia)
│   ├── StartupService.cs         # iniciar com o Windows
│   └── UpdateService.cs          # verificar/baixar/instalar releases do GitHub
└── Resources/app.ico
installer/                        # MSI (WiX) e build.ps1
scripts/release.ps1               # publica uma nova versão como release no GitHub
```

## Ideias para próximos passos

- Botão Guide/Home do controle abrindo a tela do console (exige XInputGetStateEx; hoje o gesto é − e +).
- Splash em tela cheia no boot até o Big Picture aparecer.
- Trocar resolução/taxa de atualização ao entrar no Modo Console.
- Integração com outros launchers (Playnite, Epic, GOG) e emuladores.
- Perfis de energia e controle de volume pelo menu.
- Assinar o MSI e o executável (Smart App Control).
