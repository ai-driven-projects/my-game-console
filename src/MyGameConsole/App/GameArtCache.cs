using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MyGameConsole.Services;

namespace MyGameConsole.App;

/// <summary>
/// Artes da biblioteca já no tamanho em que são desenhadas: capas redimensionadas para o tile e o fundo do
/// jogo selecionado composto na resolução da tela (arte, escurecimento e degradê). Redimensionar a cada
/// repintura custaria dezenas de milissegundos por quadro; assim cada arte é decodificada uma vez só.
/// </summary>
public sealed class GameArtCache : IDisposable
{
    private const int MaxBackdrops = 3;

    private readonly Dictionary<int, Bitmap?> _capsules = [];
    private Size _capsuleSize;
    private readonly List<(int AppId, Size Size, Bitmap Image)> _backdrops = [];

    /// <summary>Capa vertical do jogo no tamanho pedido (recortada para preencher), ou null sem arte no cache.</summary>
    public Bitmap? Capsule(SteamGame game, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return null;

        if (size != _capsuleSize)
        {
            ClearCapsules();
            _capsuleSize = size;
        }

        if (!_capsules.TryGetValue(game.AppId, out var bitmap))
        {
            bitmap = Load(game.CapsulePath) is { } source ? Cover(source, size) : null;
            _capsules[game.AppId] = bitmap;
        }

        return bitmap;
    }

    /// <summary>
    /// Proporção da arte horizontal do Steam (header.jpg / library_header, 460×215): é a que o Big Picture mostra no
    /// jogo selecionado, na mesma altura das capas verticais dos outros.
    /// </summary>
    public const float HeaderAspect = 460f / 215f;

    /// <summary>
    /// Arte horizontal do jogo selecionado no tamanho pedido, ou null sem header no cache. Só uma fica guardada:
    /// o selecionado muda a todo momento.
    /// </summary>
    public Bitmap? Header(SteamGame game, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return null;
        if (_header is { } hit && hit.AppId == game.AppId && hit.Size == size) return hit.Image;

        _header?.Image?.Dispose();
        var image = Load(game.HeaderPath) is { } source ? Cover(source, size) : null;
        _header = (game.AppId, size, image);
        return image;
    }

    private (int AppId, Size Size, Bitmap? Image)? _header;

    /// <summary>Fundo em tela cheia com a arte panorâmica do jogo, ou null se ele não tiver uma.</summary>
    public Bitmap? Backdrop(SteamGame game, Size screen)
    {
        if (screen.Width <= 0 || screen.Height <= 0 || game.HeroPath is null) return null;

        var hit = _backdrops.FindIndex(b => b.AppId == game.AppId && b.Size == screen);
        if (hit >= 0)
        {
            // mais recente no fim
            var entry = _backdrops[hit];
            _backdrops.RemoveAt(hit);
            _backdrops.Add(entry);
            return entry.Image;
        }

        if (Load(game.HeroPath) is not { } hero) return null;
        var composed = ComposeBackdrop(hero, screen);
        hero.Dispose();

        _backdrops.Add((game.AppId, screen, composed));
        if (_backdrops.Count > MaxBackdrops)
        {
            _backdrops[0].Image.Dispose();
            _backdrops.RemoveAt(0);
        }

        return composed;
    }

    public void Dispose()
    {
        ClearCapsules();
        _header?.Image?.Dispose();
        _header = null;
        foreach (var b in _backdrops) b.Image.Dispose();
        _backdrops.Clear();
    }

    private void ClearCapsules()
    {
        foreach (var b in _capsules.Values) b?.Dispose();
        _capsules.Clear();
    }

    /// <summary>Lê a imagem sem manter o arquivo aberto (o Steam pode querer atualizá-lo).</summary>
    private static Bitmap? Load(string? path)
    {
        if (path is null) return null;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return null; // arquivo sumiu, está sendo gravado ou não é uma imagem válida
        }
    }

    /// <summary>Redimensiona preenchendo o tamanho pedido, recortando o que sobrar (centralizado). Descarta a origem.</summary>
    private static Bitmap Cover(Bitmap source, Size size)
    {
        using (source)
        {
            var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(Point.Empty, size), CoverSource(source.Size, size), GraphicsUnit.Pixel);
            return result;
        }
    }

    private static RectangleF CoverSource(Size source, Size target)
    {
        float scale = Math.Max((float)target.Width / source.Width, (float)target.Height / source.Height);
        float w = target.Width / scale;
        float h = target.Height / scale;
        return new RectangleF((source.Width - w) / 2f, (source.Height - h) / 2f, w, h);
    }

    /// <summary>
    /// Fundo do console com a arte do jogo por cima: a arte aparece inteira no alto e se dissolve no
    /// azul-escuro do tema em direção às fileiras, para o texto continuar legível.
    /// </summary>
    private static Bitmap ComposeBackdrop(Bitmap hero, Size screen)
    {
        var result = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(result);
        var all = new Rectangle(Point.Empty, screen);

        using (var bg = new LinearGradientBrush(all, Theme.BgTop, Theme.BgBottom, 90f))
        {
            g.FillRectangle(bg, all);
        }

        // A arte cobre a largura toda e mais ou menos 3/4 da altura; o resto é o degradê do tema.
        var heroRect = new Rectangle(0, 0, screen.Width, (int)(screen.Height * 0.78f));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = 0.62f });
            var src = CoverSource(hero.Size, heroRect.Size);
            g.DrawImage(hero, heroRect, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }

        using var fade = new LinearGradientBrush(all, Color.Black, Color.Black, 90f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors =
                [
                    Color.FromArgb(150, Theme.BgTop),     // topo: cabeçalho legível
                    Color.FromArgb(20, Theme.BgTop),
                    Color.FromArgb(150, Theme.BgBottom),
                    Color.FromArgb(245, Theme.BgBottom),  // fileiras
                    Theme.BgBottom,
                ],
                Positions = [0f, 0.24f, 0.5f, 0.74f, 1f],
            },
        };
        g.FillRectangle(fade, all);

        // escurece um pouco a lateral esquerda, onde ficam os títulos das fileiras
        var left = new Rectangle(0, 0, Math.Max(1, screen.Width / 2), screen.Height);
        using var side = new LinearGradientBrush(left, Color.FromArgb(110, Theme.BgTop), Color.FromArgb(0, Theme.BgTop), 0f);
        g.FillRectangle(side, left);

        return result;
    }
}
