using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MyGameConsole.App;
using MyGameConsole.Native;

namespace MyGameConsole.Forms;

/// <summary>
/// Aviso flutuante que aparece sobre qualquer janela, na parte de baixo da tela, e some sozinho.
/// É como o app confirma o que mudou quando a ação veio do controle e não há tela nenhuma na frente
/// (ex.: ligar e desligar o mouse pelo analógico). Nunca rouba o foco nem bloqueia o clique: o jogo ou
/// o app que estiver na frente continua recebendo tudo.
///
/// A janela é uma só, reaproveitada: avisos em sequência (ligar e desligar seguidos) só trocam o
/// conteúdo e reiniciam a contagem, em vez de destruir e recriar a janela.
/// </summary>
public sealed class ToastForm : Form
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(2);
    private const double StartOpacity = 0.97;
    private const double FadeStep = 0.09;

    private static ToastForm? _instance;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private string _text = string.Empty;
    private string _glyph = string.Empty;
    private Color _accent = Theme.Accent;
    private bool _crossed;
    private bool _glyphIsText;
    private float _u = 10f;
    private DateTime _fadeAt;

    /// <summary>
    /// Mostra o aviso (trocando o que estiver na tela). <paramref name="crossed"/> risca o ícone,
    /// para "desligado" ficar diferente de "ligado" sem depender só da cor. Com <paramref name="glyphIsText"/>, o
    /// ícone é um texto curto em negrito (ex.: o número da contagem antes de gravar).
    /// </summary>
    public static void Show(string text, string glyph, Color accent, bool crossed = false, bool glyphIsText = false)
    {
        if (_instance is null || _instance.IsDisposed) _instance = new ToastForm();
        _instance.Present(text, glyph, accent, crossed, glyphIsText);
    }

    /// <summary>Tira o aviso da tela e descarta a janela (usado ao sair do app).</summary>
    public static void CloseCurrent()
    {
        var toast = _instance;
        _instance = null;
        if (toast is { IsDisposed: false }) toast.Close();
    }

    private ToastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Theme.Tile;
        Opacity = StartOpacity;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _timer.Tick += (_, _) => Fade();
    }

    private void Present(string text, string glyph, Color accent, bool crossed, bool glyphIsText)
    {
        _text = text;
        _glyphIsText = glyphIsText;
        _glyph = glyph;
        _accent = accent;
        _crossed = crossed;

        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        _u = screen.Bounds.Height / 100f;
        Bounds = MeasureBounds(screen.Bounds);

        var previous = Region;
        using (var path = Shapes.RoundedRect(new RectangleF(0, 0, Width, Height), Height / 2f))
        {
            Region = new Region(path);
        }
        previous?.Dispose();

        Opacity = StartOpacity;
        _fadeAt = DateTime.UtcNow + HoldDuration;
        _timer.Start();

        if (Visible) Invalidate();
        else Show();
        TopMost = true; // volta ao topo se outra janela de sempre-visível subiu no meio tempo
    }

    /// <summary>Tamanho pelo texto e posição centralizada na parte de baixo da tela, acima da barra de tarefas.</summary>
    private Rectangle MeasureBounds(Rectangle screen)
    {
        using var font = TextFont();
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        float textWidth = g.MeasureString(_text, font).Width;

        int height = (int)(_u * 8f);
        int width = (int)(_u * 3f + IconSize + _u * 2f + textWidth + _u * 3.5f);
        int x = screen.X + (screen.Width - width) / 2;
        int y = screen.Bottom - (int)(_u * 12f) - height;
        return new Rectangle(x, y, width, height);
    }

    private float IconSize => _u * 5f;

    private Font TextFont() => new("Segoe UI", _u * 2.2f, FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>O aviso aparece na tela, mas não em capturas: não entra nas gravações do app nem em prints.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>Não ativar a janela ao mostrá-la: o foco continua onde estava.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    private void Fade()
    {
        if (DateTime.UtcNow < _fadeAt) return;

        Opacity -= FadeStep;
        if (Opacity > 0.01) return;

        _timer.Stop();
        Hide(); // a janela fica pronta para o próximo aviso
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // tudo é pintado em OnPaint
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new RectangleF(0, 0, Width, Height);
        using (var path = Shapes.RoundedRect(rect, Height / 2f))
        using (var fill = new SolidBrush(Theme.Tile))
        using (var border = new Pen(Color.FromArgb(120, _accent), _u * 0.2f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        float d = IconSize;
        var icon = new RectangleF(_u * 3f - d * 0.15f, (Height - d) / 2f, d, d);
        using (var iconBrush = new SolidBrush(_accent))
        using (var glyphBrush = new SolidBrush(Theme.BgTop))
        {
            g.FillEllipse(iconBrush, icon);
            // Centrado pelo desenho: centrado pela caixa da fonte, o glifo saía deslocado dentro do círculo.
            if (_glyphIsText) InkText.DrawCentered(g, _glyph, "Segoe UI", FontStyle.Bold, d * 0.62f, glyphBrush, icon);
            else InkText.DrawCentered(g, _glyph, Theme.IconFontName, FontStyle.Regular, d * 0.5f, glyphBrush, icon);

            if (_crossed)
            {
                // Risco sobre o ícone: "desligado" se reconhece mesmo sem distinguir a cor.
                using var slash = new Pen(Theme.BgTop, _u * 0.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float inset = d * 0.22f;
                g.DrawLine(slash, icon.Left + inset, icon.Bottom - inset, icon.Right - inset, icon.Top + inset);
            }
        }

        using var font = TextFont();
        using var textBrush = new SolidBrush(Theme.Text);
        var textRect = new RectangleF(icon.Right + _u * 2f, 0, Width - icon.Right - _u * 4f, Height);
        g.DrawString(_text, font, textBrush, textRect,
            new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
    }
}
