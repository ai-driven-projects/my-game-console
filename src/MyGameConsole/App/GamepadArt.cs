using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MyGameConsole.App;

/// <summary>Partes do controle que o desenho sabe acender.</summary>
[Flags]
public enum PadPart
{
    None = 0,
    A = 1,
    B = 2,
    X = 4,
    Y = 8,
    Back = 16,
    Start = 32,
    Dpad = 64,
    LeftStick = 128,
    RightStick = 256,
}

/// <summary>
/// Desenho de um controle, usado na página "Controle" da tela do console: as partes passadas em
/// <see cref="PadPart"/> ficam acesas, para mostrar que botões cada atalho usa. Tudo é desenhado em
/// uma escala própria (100 x 66) e depois ajustado à área disponível, então o desenho não depende
/// do tamanho da tela.
/// </summary>
public static class GamepadArt
{
    private const float Width = 100f;
    private const float Height = 66f;

    /// <summary>Folga no topo, onde ficam os botões superiores (LB/RB), acima do corpo.</summary>
    private const float TopMargin = 4f;

    /// <summary>Contorno do controle: pontos de uma curva fechada, no sentido horário a partir do topo.</summary>
    private static readonly PointF[] Outline =
    [
        new(50f, 5f), new(64f, 5f), new(78f, 4f), new(88f, 10f), new(94f, 22f), new(95f, 36f),
        new(90f, 50f), new(82f, 58f), new(74f, 55f), new(68f, 46f), new(60f, 43f),
        new(50f, 42f),
        new(40f, 43f), new(32f, 46f), new(26f, 55f), new(18f, 58f), new(10f, 50f),
        new(5f, 36f), new(6f, 22f), new(12f, 10f), new(22f, 4f), new(36f, 5f),
    ];

    private static readonly Color Plastic = Color.FromArgb(255, 30, 44, 60);
    /// <summary>Botões superiores: um tom mais claro que o corpo, senão somem no fundo escuro.</summary>
    private static readonly Color Bumper = Color.FromArgb(255, 44, 62, 82);
    private static readonly Color Button = Color.FromArgb(255, 52, 71, 92);
    private static readonly Color StickCap = Color.FromArgb(255, 58, 78, 99);

    private static readonly StringFormat Centered = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };

    /// <summary>Desenha o controle centralizado na área, proporcional, com as partes pedidas acesas.</summary>
    public static void Draw(Graphics g, RectangleF area, PadPart lit)
    {
        float scale = Math.Min(area.Width / Width, area.Height / Height);
        if (scale <= 0f) return;

        var state = g.Save(); // guarda também os modos de suavização e de texto, restaurados no fim
        g.TranslateTransform(
            area.X + (area.Width - Width * scale) / 2f,
            area.Y + (area.Height - Height * scale) / 2f);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(0f, TopMargin);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias; // texto muito pequeno na escala do desenho

        DrawPad(g, lit);

        g.Restore(state);
    }

    private static void DrawPad(Graphics g, PadPart lit)
    {
        using var bodyBrush = new SolidBrush(Plastic);
        using var bumperBrush = new SolidBrush(Bumper);
        using var edgePen = new Pen(Color.FromArgb(130, Theme.Muted), 0.6f);

        // Botões superiores (LB/RB): desenhados antes, para ficarem por baixo do corpo.
        using (var lb = Shapes.RoundedRect(new RectangleF(19f, -3.5f, 16f, 9f), 3f)) g.FillPath(bumperBrush, lb);
        using (var rb = Shapes.RoundedRect(new RectangleF(65f, -3.5f, 16f, 9f), 3f)) g.FillPath(bumperBrush, rb);

        using (var body = new GraphicsPath())
        {
            body.AddClosedCurve(Outline, 0.45f);
            g.FillPath(bodyBrush, body);
            g.DrawPath(edgePen, body);
        }

        DrawStick(g, new PointF(26f, 17f), lit.HasFlag(PadPart.LeftStick));
        DrawStick(g, new PointF(60f, 33f), lit.HasFlag(PadPart.RightStick));
        DrawDpad(g, new PointF(36f, 33f), lit.HasFlag(PadPart.Dpad));

        DrawFaceButton(g, new PointF(74f, 9.5f), "Y", lit.HasFlag(PadPart.Y));
        DrawFaceButton(g, new PointF(66.5f, 17f), "X", lit.HasFlag(PadPart.X));
        DrawFaceButton(g, new PointF(81.5f, 17f), "B", lit.HasFlag(PadPart.B));
        DrawFaceButton(g, new PointF(74f, 24.5f), "A", lit.HasFlag(PadPart.A));

        DrawSmallButton(g, new PointF(43f, 14f), "−", lit.HasFlag(PadPart.Back));
        DrawSmallButton(g, new PointF(57f, 14f), "+", lit.HasFlag(PadPart.Start));
    }

    private static void DrawStick(Graphics g, PointF center, bool lit)
    {
        Glow(g, center, 7f, lit);

        using var wellBrush = new SolidBrush(Color.FromArgb(255, 18, 27, 38));
        using var capBrush = new SolidBrush(lit ? Theme.Accent : StickCap);
        using var wellPen = new Pen(Color.FromArgb(lit ? 200 : 90, Theme.Muted), 0.5f);

        g.FillEllipse(wellBrush, Circle(center, 7f));
        g.DrawEllipse(wellPen, Circle(center, 7f));
        g.FillEllipse(capBrush, Circle(center, 4.6f));
    }

    private static void DrawDpad(Graphics g, PointF center, bool lit)
    {
        Glow(g, center, 6.5f, lit);

        const float arm = 5.5f;
        const float thick = 3.6f;
        using var brush = new SolidBrush(lit ? Theme.Accent : StickCap);
        using var horizontal = Shapes.RoundedRect(new RectangleF(center.X - arm, center.Y - thick / 2f, arm * 2f, thick), 1f);
        using var vertical = Shapes.RoundedRect(new RectangleF(center.X - thick / 2f, center.Y - arm, thick, arm * 2f), 1f);
        g.FillPath(brush, horizontal);
        g.FillPath(brush, vertical);
    }

    private static void DrawFaceButton(Graphics g, PointF center, string label, bool lit)
    {
        Glow(g, center, 4.3f, lit);

        var circle = Circle(center, 4.3f);
        using var brush = new SolidBrush(lit ? Theme.Accent : Button);
        using var pen = new Pen(Color.FromArgb(lit ? 220 : 80, Theme.Muted), 0.45f);
        using var font = new Font("Segoe UI", 4.6f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(lit ? Theme.BgTop : Theme.Text);

        g.FillEllipse(brush, circle);
        g.DrawEllipse(pen, circle);
        g.DrawString(label, font, textBrush, circle, Centered);
    }

    private static void DrawSmallButton(Graphics g, PointF center, string label, bool lit)
    {
        Glow(g, center, 3.4f, lit);

        var rect = new RectangleF(center.X - 3.4f, center.Y - 1.8f, 6.8f, 3.6f);
        using var brush = new SolidBrush(lit ? Theme.Accent : Button);
        using var font = new Font("Segoe UI", 3.6f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(lit ? Theme.BgTop : Theme.Text);
        using var path = Shapes.RoundedRect(rect, 1.8f);

        g.FillPath(brush, path);
        g.DrawString(label, font, textBrush, rect, Centered);
    }

    /// <summary>Halo em volta da parte acesa, para o olho achá-la no desenho.</summary>
    private static void Glow(Graphics g, PointF center, float radius, bool lit)
    {
        if (!lit) return;

        using var path = new GraphicsPath();
        path.AddEllipse(Circle(center, radius * 2f));
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(150, Theme.Accent),
            SurroundColors = [Color.FromArgb(0, Theme.Accent)],
            CenterPoint = center,
        };
        g.FillPath(brush, path);
    }

    private static RectangleF Circle(PointF center, float radius) =>
        new(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
}
