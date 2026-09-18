using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MyGameConsole.App;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Ajustes visuais da área de trabalho usados pelo Modo Game: barra de tarefas em auto-ocultar,
/// ícones da área de trabalho escondidos, papel de parede e telas de "concluir a configuração do Windows".
/// Nenhum deles exige reiniciar o explorer nem administrador.
/// </summary>
public sealed class DesktopTweaksService
{
    private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string WallpapersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";

    // ------------------------------------------------------------------
    // Barra de tarefas
    // ------------------------------------------------------------------

    public bool IsTaskbarAutoHide
    {
        get
        {
            var data = NewAppBarData();
            var state = (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
            return (state & NativeMethods.ABS_AUTOHIDE) != 0;
        }
    }

    public void SetTaskbarAutoHide(bool enabled)
    {
        var data = NewAppBarData();
        var state = (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
        var newState = enabled
            ? state | NativeMethods.ABS_AUTOHIDE
            : state & ~NativeMethods.ABS_AUTOHIDE;

        if (newState == state) return;

        data = NewAppBarData();
        data.lParam = (IntPtr)newState;
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
    }

    private static NativeMethods.AppBarData NewAppBarData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
    };

    // ------------------------------------------------------------------
    // Ícones da área de trabalho
    // ------------------------------------------------------------------

    private const string DesktopBagKey = @"Software\Microsoft\Windows\Shell\Bags\1\Desktop";

    /// <summary>
    /// Os ícones estão escondidos de fato: com o Explorer aberto, pela lista de ícones da área de trabalho;
    /// sem ele, pelo que o Explorer vai ler ao abrir (FFlags e, na falta dele, HideIcons).
    /// </summary>
    public bool AreDesktopIconsHidden
    {
        get
        {
            if (LiveIconsHidden(FindDesktopDefView()) is { } live) return live;
            if (ReadDesktopFlags() is { } flags) return (flags & NativeMethods.FWF_NOICONS) != 0;

            using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey);
            return key?.GetValue("HideIcons") is int v && v != 0;
        }
    }

    public void SetDesktopIconsHidden(bool hidden)
    {
        // O Explorer só reflete a mudança na hora pelo comando "Mostrar ícones da área de trabalho", que alterna:
        // só é enviado quando o estado na tela é o contrário do pedido. Decidir pelo registro invertia o comando
        // quando ele e a tela divergiam (o Explorer não atualiza o HideIcons), e os ícones não voltavam.
        var defView = FindDesktopDefView();
        if (LiveIconsHidden(defView) is { } live && live != hidden)
        {
            NativeMethods.SendMessage(defView, NativeMethods.WM_COMMAND,
                (IntPtr)NativeMethods.CMD_TOGGLE_DESKTOP_ICONS, IntPtr.Zero);
        }

        // Grava os dois lugares que o Explorer pode ler ao abrir, para valer também sem ele rodando agora
        // (Modo Console) e não deixar um dos dois contradizendo o outro.
        using (var key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKey))
        {
            if (key.GetValue("HideIcons") is not int current || current != (hidden ? 1 : 0))
            {
                key.SetValue("HideIcons", hidden ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        if (ReadDesktopFlags() is { } flags)
        {
            int wanted = hidden ? flags | NativeMethods.FWF_NOICONS : flags & ~NativeMethods.FWF_NOICONS;
            if (wanted != flags)
            {
                using var key = Registry.CurrentUser.CreateSubKey(DesktopBagKey);
                key.SetValue("FFlags", wanted, RegistryValueKind.DWord);
            }
        }
    }

    /// <summary>Ícones escondidos na tela agora, ou nulo sem o Explorer (sem a área de trabalho dele).</summary>
    private static bool? LiveIconsHidden(IntPtr defView)
    {
        if (defView == IntPtr.Zero) return null;
        var list = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return list == IntPtr.Zero ? null : !NativeMethods.IsWindowVisible(list);
    }

    private static int? ReadDesktopFlags()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopBagKey);
        return key?.GetValue("FFlags") is int v ? v : null;
    }

    private static IntPtr FindDesktopDefView()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var dv = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
        }

        // Em alguns cenários (ex.: papel de parede animado) a DefView fica dentro de um WorkerW.
        var worker = IntPtr.Zero;
        while ((worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            var dv = NativeMethods.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
        }

        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------
    // Telas de "Vamos concluir a configuração do seu dispositivo"
    // ------------------------------------------------------------------

    private const string CurrentVersionKey = @"Software\Microsoft\Windows\CurrentVersion";

    /// <summary>
    /// Valores DWORD (em HKCU\...\CurrentVersion) que fazem o Windows abrir, no logon ou após atualizações,
    /// a tela cheia "Vamos concluir a configuração do seu dispositivo" (SCOOBE) e a de boas-vindas/novidades.
    /// São as mesmas caixas de Configurações > Sistema > Notificações > Configurações adicionais.
    /// Formato: subchave relativa a CurrentVersion + "\" + nome do valor.
    /// </summary>
    private static readonly string[] SetupPromptValues =
    [
        @"UserProfileEngagement\ScoobeSystemSettingEnabled",
        @"ContentDeliveryManager\SubscribedContent-310093Enabled",
    ];

    /// <summary>Verdadeiro se todas as telas de configuração/boas-vindas estão desligadas (valor 0).</summary>
    public bool AreSetupPromptsDisabled => GetSetupPrompts().Values.All(v => v == 0);

    /// <summary>Valor atual de cada ajuste. Nulo = valor ausente (o Windows usa o padrão, que é mostrar a tela).</summary>
    public Dictionary<string, int?> GetSetupPrompts() => GetDwords(SetupPromptValues);

    public void SetSetupPromptsDisabled() => SetDwords(SetupPromptValues, 0);

    /// <summary>Devolve os valores originais; os que não existiam antes são apagados.</summary>
    public void RestoreSetupPrompts(Dictionary<string, int?> original) => RestoreDwords(SetupPromptValues, original);

    // ------------------------------------------------------------------
    // Atraso dos apps de inicialização
    // ------------------------------------------------------------------

    /// <summary>
    /// O Windows segura de propósito os apps da chave Run por alguns segundos depois do logon (e, no Windows 11,
    /// espera o sistema ficar ocioso). Com os dois valores em 0, o app (e o que mais iniciar com o Windows) abre
    /// assim que a área de trabalho carrega. Ajuste do usuário, sem administrador.
    /// </summary>
    private static readonly string[] StartupDelayValues =
    [
        @"Explorer\Serialize\StartupDelayInMSec",
        @"Explorer\Serialize\WaitForIdleState",
    ];

    /// <summary>Verdadeiro se o Windows não atrasa mais os apps de inicialização.</summary>
    public bool IsStartupDelayDisabled => GetStartupDelay().Values.All(v => v == 0);

    public Dictionary<string, int?> GetStartupDelay() => GetDwords(StartupDelayValues);

    public void SetStartupDelayDisabled() => SetDwords(StartupDelayValues, 0);

    public void RestoreStartupDelay(Dictionary<string, int?> original) => RestoreDwords(StartupDelayValues, original);

    // ------------------------------------------------------------------
    // Valores DWORD em HKCU\...\CurrentVersion (ler, gravar e devolver o original)
    // ------------------------------------------------------------------

    private static Dictionary<string, int?> GetDwords(string[] entries)
    {
        var result = new Dictionary<string, int?>();
        foreach (var entry in entries)
        {
            var (subKey, name) = SplitEntry(entry);
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            result[entry] = key?.GetValue(name) is int v ? v : null;
        }
        return result;
    }

    private static void SetDwords(string[] entries, int value)
    {
        foreach (var entry in entries)
        {
            var (subKey, name) = SplitEntry(entry);
            using var key = Registry.CurrentUser.CreateSubKey(subKey);
            if (key.GetValue(name) is not int current || current != value) key.SetValue(name, value, RegistryValueKind.DWord);
        }
    }

    /// <summary>Devolve os valores originais; os que não existiam antes são apagados.</summary>
    private static void RestoreDwords(string[] entries, Dictionary<string, int?> original)
    {
        foreach (var (entry, value) in original)
        {
            if (!entries.Contains(entry)) continue;
            var (subKey, name) = SplitEntry(entry);
            using var key = Registry.CurrentUser.CreateSubKey(subKey);
            if (value is { } v) key.SetValue(name, v, RegistryValueKind.DWord);
            else key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private static (string SubKey, string Name) SplitEntry(string entry)
    {
        int i = entry.LastIndexOf('\\');
        return (CurrentVersionKey + @"\" + entry[..i], entry[(i + 1)..]);
    }

    // ------------------------------------------------------------------
    // Papel de parede
    // ------------------------------------------------------------------

    public const int BackgroundPicture = 0;
    public const int BackgroundSolidColor = 1;

    public (string Path, string Style, string Tile) GetWallpaper()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
        return (
            key?.GetValue("WallPaper") as string ?? string.Empty,
            key?.GetValue("WallpaperStyle") as string ?? "10",
            key?.GetValue("TileWallpaper") as string ?? "0");
    }

    /// <summary>
    /// Tipo de plano de fundo escolhido em Personalização. É o que o Explorer consulta no logon:
    /// se for cor sólida ou apresentação, a imagem gravada em WallPaper é ignorada ao reiniciar.
    /// </summary>
    public int BackgroundType
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(WallpapersKey);
            if (key?.GetValue("BackgroundType") is int v) return v;
            return string.IsNullOrEmpty(GetWallpaper().Path) ? BackgroundSolidColor : BackgroundPicture;
        }
    }

    /// <param name="style">"10" = Preencher, "6" = Ajustar, "2" = Esticar, "0" = Centralizar.</param>
    /// <param name="backgroundType">Tipo de plano de fundo a gravar; nulo = imagem (ou cor sólida se o caminho for vazio).</param>
    public void SetWallpaper(string path, string style = "10", string tile = "0", int? backgroundType = null)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey))
        {
            key.SetValue("WallpaperStyle", style, RegistryValueKind.String);
            key.SetValue("TileWallpaper", tile, RegistryValueKind.String);
        }

        if (!NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETDESKWALLPAPER, 0, path,
                NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE))
        {
            throw new InvalidOperationException("O Windows recusou a troca do papel de parede.");
        }

        // SystemParametersInfo troca a imagem na hora, mas não atualiza a Personalização. Sem isso,
        // um desktop que era "cor sólida" volta a ficar sem imagem no próximo logon.
        var type = backgroundType ?? (string.IsNullOrEmpty(path) ? BackgroundSolidColor : BackgroundPicture);
        using (var key = Registry.CurrentUser.CreateSubKey(WallpapersKey))
        {
            key.SetValue("BackgroundType", type, RegistryValueKind.DWord);
            if (!string.IsNullOrEmpty(path))
            {
                key.SetValue("CurrentWallpaperPath", path, RegistryValueKind.String);
            }
        }
    }

    /// <summary>
    /// Papel de parede padrão do Modo Game: a imagem <c>wallpaper.webp</c> instalada junto com o app
    /// (copiada de <c>img/wallpaper.webp</c> na publicação). Nulo se o arquivo não estiver ao lado do executável.
    /// </summary>
    public string? DefaultWallpaperPath
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wallpaper.webp");
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Versão da arte gerada. Aumente ao mudar o desenho para o arquivo em cache ser regenerado.</summary>
    private const int WallpaperVersion = 2;

    /// <summary>
    /// Reserva usada só quando <see cref="DefaultWallpaperPath"/> não existe ou o Windows recusa o WebP
    /// (sem o decodificador instalado). Gera (uma única vez por resolução e versão) um fundo escuro
    /// azul-acinzentado com um emblema de "console" inspirado no logo da Steam, no tamanho do monitor principal.
    /// </summary>
    public string EnsureFallbackWallpaper(string directory)
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        int w = Math.Max(800, screen.Bounds.Width);
        int h = Math.Max(600, screen.Bounds.Height);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"wallpaper_v{WallpaperVersion}_{w}x{h}.png");
        if (File.Exists(path)) return path;

        // Limpa a arte antiga (versão 1) desta resolução.
        try { File.Delete(Path.Combine(directory, $"wallpaper_{w}x{h}.png")); } catch { /* opcional */ }

        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawWallpaperBackground(g, w, h);

            float cx = w / 2f;
            float cy = h * 0.46f;
            float r = Math.Min(w, h) * 0.2f;
            DrawConsoleEmblem(g, cx, cy, r);

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            using (var captionFont = new Font("Segoe UI", h * 0.034f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var captionBrush = new SolidBrush(Color.FromArgb(235, Color.White)))
            {
                g.DrawString("MY GAME CONSOLE", captionFont, captionBrush,
                    new RectangleF(0, cy + r * 1.45f, w, h * 0.06f), center);
            }

            using (var subFont = new Font("Segoe UI", h * 0.016f, GraphicsUnit.Pixel))
            using (var subBrush = new SolidBrush(Color.FromArgb(150, Theme.Muted)))
            {
                g.DrawString("M O D O   G A M E", subFont, subBrush,
                    new RectangleF(0, cy + r * 1.45f + h * 0.058f, w, h * 0.04f), center);
            }
        }

        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static void DrawWallpaperBackground(Graphics g, int w, int h)
    {
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Theme.BgTop, Theme.BgBottom, 90f))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        // brilho radial azul atrás do emblema
        using (var glowPath = new GraphicsPath())
        {
            var glowRect = new RectangleF(w * 0.1f, -h * 0.1f, w * 0.8f, h * 1.15f);
            glowPath.AddEllipse(glowRect);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(95, Theme.Accent),
                SurroundColors = [Color.FromArgb(0, Theme.Accent)],
                CenterPoint = new PointF(w / 2f, h * 0.44f),
            };
            g.FillPath(glow, glowPath);
        }

        // faixa diagonal clara bem sutil, para dar profundidade
        using (var sweep = new LinearGradientBrush(
                   new PointF(0, h), new PointF(w, 0),
                   Color.FromArgb(0, Color.White), Color.FromArgb(14, Color.White)))
        {
            g.FillRectangle(sweep, 0, 0, w, h);
        }

        // vinheta nas bordas
        using (var vignettePath = new GraphicsPath())
        {
            vignettePath.AddEllipse(new RectangleF(-w * 0.2f, -h * 0.35f, w * 1.4f, h * 1.7f));
            using var vignette = new PathGradientBrush(vignettePath)
            {
                CenterColor = Color.FromArgb(0, Theme.BgTop),
                SurroundColors = [Color.FromArgb(170, Theme.BgTop)],
                CenterPoint = new PointF(w / 2f, h * 0.45f),
            };
            g.FillPath(vignette, vignettePath);
        }
    }

    /// <summary>
    /// Emblema inspirado no logo da Steam: um anel grande, um anel pequeno ("rolamento") dentro dele,
    /// e um braço de pistão que sai do rolamento e atravessa o anel para fora, em direção ao canto inferior esquerdo.
    /// </summary>
    private static void DrawConsoleEmblem(Graphics g, float cx, float cy, float r)
    {
        var ink = Color.FromArgb(240, 245, 250);
        var dark = Theme.BgTop;
        float ringWidth = r * 0.15f;

        // halo suave atrás do emblema
        using (var haloPath = new GraphicsPath())
        {
            haloPath.AddEllipse(cx - r * 1.9f, cy - r * 1.9f, r * 3.8f, r * 3.8f);
            using var halo = new PathGradientBrush(haloPath)
            {
                CenterColor = Color.FromArgb(70, Theme.Accent),
                SurroundColors = [Color.FromArgb(0, Theme.Accent)],
            };
            g.FillPath(halo, haloPath);
        }

        // anel grande
        using (var ringPen = new Pen(ink, ringWidth))
        {
            g.DrawEllipse(ringPen, cx - r, cy - r, 2 * r, 2 * r);
        }

        // rolamento (anel pequeno) no quadrante superior direito
        var bearing = new PointF(cx + r * 0.30f, cy - r * 0.30f);
        float bearingR = r * 0.30f;

        // braço do pistão: do rolamento até fora do anel, no canto inferior esquerdo, afinando na ponta
        var armEnd = new PointF(cx - r * 1.28f, cy + r * 0.95f);
        DrawTaperedArm(g, bearing, armEnd, r * 0.50f, r * 0.30f, ink, dark, ringWidth * 0.45f);

        // rolamento por cima do braço: miolo vazado (escuro) e contorno escuro para se destacar
        using (var holeBrush = new SolidBrush(dark))
        {
            g.FillEllipse(holeBrush, bearing.X - bearingR, bearing.Y - bearingR, 2 * bearingR, 2 * bearingR);
        }
        using (var bearingOutline = new Pen(dark, bearingR * 0.55f + ringWidth * 0.9f))
        {
            g.DrawEllipse(bearingOutline, bearing.X - bearingR, bearing.Y - bearingR, 2 * bearingR, 2 * bearingR);
        }
        using (var bearingPen = new Pen(ink, bearingR * 0.55f))
        {
            g.DrawEllipse(bearingPen, bearing.X - bearingR, bearing.Y - bearingR, 2 * bearingR, 2 * bearingR);
        }

        // detalhe de "console": quatro botões (losango) dentro do anel, no quadrante inferior direito
        var pad = new PointF(cx + r * 0.42f, cy + r * 0.50f);
        float dot = r * 0.075f;
        float spread = r * 0.19f;
        using (var accentBrush = new SolidBrush(Theme.Accent))
        using (var dotBrush = new SolidBrush(Color.FromArgb(200, ink)))
        {
            g.FillEllipse(dotBrush, pad.X - dot, pad.Y - spread - dot, 2 * dot, 2 * dot);
            g.FillEllipse(dotBrush, pad.X - spread - dot, pad.Y - dot, 2 * dot, 2 * dot);
            g.FillEllipse(dotBrush, pad.X + spread - dot, pad.Y - dot, 2 * dot, 2 * dot);
            g.FillEllipse(accentBrush, pad.X - dot, pad.Y + spread - dot, 2 * dot, 2 * dot);
        }
    }

    /// <summary>Braço com largura variável entre as pontas, extremidades arredondadas e contorno escuro.</summary>
    private static void DrawTaperedArm(Graphics g, PointF from, PointF to, float widthFrom, float widthTo,
        Color fill, Color outline, float outlineWidth)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        if (MathF.Sqrt(dx * dx + dy * dy) < 1f) return;

        // Contorno único: lateral, semicírculo na ponta, lateral de volta e semicírculo na base.
        // Ângulos do GDI+ são em graus, no sentido horário na tela (eixo Y para baixo).
        float theta = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        using var path = new GraphicsPath();
        path.AddArc(to.X - widthTo / 2f, to.Y - widthTo / 2f, widthTo, widthTo, theta + 90f, -180f);
        path.AddArc(from.X - widthFrom / 2f, from.Y - widthFrom / 2f, widthFrom, widthFrom, theta - 90f, -180f);
        path.CloseFigure();

        using (var outlinePen = new Pen(outline, outlineWidth * 2f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(outlinePen, path);
        }

        // O gradiente precisa cobrir também os semicírculos das pontas, senão o GDI+ repete a cor inicial na ponta.
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float ux = dx / len, uy = dy / len;
        var gradStart = new PointF(from.X - ux * widthFrom / 2f, from.Y - uy * widthFrom / 2f);
        var gradEnd = new PointF(to.X + ux * widthTo / 2f, to.Y + uy * widthTo / 2f);
        using var brush = new LinearGradientBrush(gradStart, gradEnd, fill, Color.FromArgb(255, Theme.Accent));
        g.FillPath(brush, path);
    }
}
