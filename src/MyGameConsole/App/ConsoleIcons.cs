using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MyGameConsole.App;

/// <summary>Ícones dos cartões de baixo da tela do console.</summary>
public enum ConsoleIcon
{
    GameMode,
    Controller,
    Desktop,
    Settings,
    Record,
}

/// <summary>
/// Ícones no estilo do logo da Steam, para os cartões de baixo combinarem com ele: um badge redondo com
/// o degradê azul-marinho → azul-petróleo do logo (reto, chapado) e um símbolo branco sólido por cima, com os recortes (botões,
/// porta, furo da engrenagem) mostrando o degradê por baixo. Tudo vetorial, desenhado com GraphicsPath
/// numa caixa de 100×100 e renderizado no tamanho pedido; sem depender do formulário, dá para gerar um PNG
/// por um projeto de teste para conferir a arte.
/// </summary>
public static class ConsoleIcons
{
    // Degradê do logo da Steam, medido no ícone do steam.exe: azul-marinho no alto, azul-petróleo embaixo. Só o
    // degradê reto, sem brilho nem sombra: um ícone chapado, como o da Steam, e não uma esfera.
    private static readonly Color[] BadgeColors =
    [
        ColorTranslator.FromHtml("#1A1D3C"),
        ColorTranslator.FromHtml("#171E40"),
        ColorTranslator.FromHtml("#123362"),
        ColorTranslator.FromHtml("#075A8D"),
        ColorTranslator.FromHtml("#0079B8"),
    ];
    private static readonly float[] BadgePositions = [0f, 0.24f, 0.5f, 0.76f, 1f];

    /// <summary>Desenha o ícone num bitmap quadrado com transparência. Quem chama descarta.</summary>
    public static Bitmap Render(ConsoleIcon icon, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 100f, size / 100f);
        Draw(g, icon);
        return bitmap;
    }

    /// <summary>Desenha na caixa 0..100 do <paramref name="g"/> atual.</summary>
    public static void Draw(Graphics g, ConsoleIcon icon)
    {
        // Um pouco menor que a caixa: o ícone do steam.exe também tem margem, e os dois ficam do mesmo tamanho.
        var circle = new RectangleF(3.5f, 3.5f, 93, 93);
        using var badge = new LinearGradientBrush(circle, BadgeColors[0], BadgeColors[^1], 90f)
        {
            InterpolationColors = new ColorBlend { Colors = BadgeColors, Positions = BadgePositions },
        };
        g.FillEllipse(badge, circle);

        // O símbolo é desenhado na caixa 0..100 e reduzido em volta do centro, para sobrar respiro dentro do
        // círculo. Os recortes usam o degradê do badge: o pincel leva a redução ao contrário, para o degradê
        // dos recortes continuar batendo com o do fundo.
        var symbol = new Matrix();
        symbol.Translate(50, 50);
        symbol.Scale(SymbolScale, SymbolScale);
        symbol.Translate(-50, -50);
        using var hole = (LinearGradientBrush)badge.Clone();
        using (var inverse = symbol.Clone())
        {
            inverse.Invert();
            hole.MultiplyTransform(inverse);
        }

        var state = g.Save();
        g.MultiplyTransform(symbol);
        symbol.Dispose();

        using var white = new SolidBrush(Color.White);
        switch (icon)
        {
            case ConsoleIcon.GameMode: DrawGamepad(g, white, hole); break;
            case ConsoleIcon.Controller: DrawKeyboardAndMouse(g, white, hole); break;
            case ConsoleIcon.Desktop: DrawHouse(g, white, hole); break;
            case ConsoleIcon.Settings: DrawGear(g, white, hole); break;
            case ConsoleIcon.Record: DrawCamera(g, white, hole); break;
        }

        g.Restore(state);
    }

    /// <summary>Tamanho do símbolo branco em relação ao desenho original: 75%, com folga até a borda do círculo.</summary>
    private const float SymbolScale = 0.75f;

    /// <summary>Controle de videogame: corpo com as duas empunhaduras, direcional e botões vazados.</summary>
    private static void DrawGamepad(Graphics g, Brush fill, Brush hole)
    {
        using var body = new GraphicsPath();
        // metade de cima (reta com cantos) e empunhaduras arredondadas embaixo
        body.AddArc(20, 34, 14, 14, 180, 90);
        body.AddLine(27, 34, 73, 34);
        body.AddArc(66, 34, 14, 14, 270, 90);
        body.AddBezier(80, 41, 84, 52, 86, 62, 84, 69);
        body.AddBezier(84, 69, 82, 76, 73, 76, 69, 70);
        body.AddLine(69, 70, 63, 62);
        body.AddLine(63, 62, 37, 62);
        body.AddLine(37, 62, 31, 70);
        body.AddBezier(31, 70, 27, 76, 18, 76, 16, 69);
        body.AddBezier(16, 69, 14, 62, 16, 52, 20, 41);
        body.CloseFigure();
        g.FillPath(fill, body);

        // direcional em cruz
        using var dpad = new GraphicsPath();
        dpad.AddRectangle(new RectangleF(27.5f, 45.5f, 12, 4));
        dpad.AddRectangle(new RectangleF(31.5f, 41.5f, 4, 12));
        dpad.FillMode = FillMode.Winding;
        g.FillPath(hole, dpad);

        // quatro botões em losango
        foreach (var (x, y) in new[] { (66f, 41.5f), (71f, 46.5f), (66f, 51.5f), (61f, 46.5f) })
        {
            g.FillEllipse(hole, x - 2.6f, y - 2.6f, 5.2f, 5.2f);
        }
    }

    /// <summary>Teclado com teclas vazadas e, na frente, um mouse separado por um contorno do degradê.</summary>
    private static void DrawKeyboardAndMouse(Graphics g, Brush fill, Brush hole)
    {
        var keyboard = new RectangleF(17, 30, 54, 34);
        using (var kb = Shapes.RoundedRect(keyboard, 6))
        {
            g.FillPath(fill, kb);
        }

        // três fileiras de teclas e a barra de espaço
        for (int row = 0; row < 3; row++)
        {
            int keys = row == 2 ? 5 : 6;
            float y = 36 + row * 7.5f;
            float step = 7.4f;
            float x0 = 23 + (row == 2 ? step / 2f : 0);
            for (int k = 0; k < keys; k++)
            {
                using var key = Shapes.RoundedRect(new RectangleF(x0 + k * step, y, 4.6f, 4.6f), 1.2f);
                g.FillPath(hole, key);
            }
        }
        using (var space = Shapes.RoundedRect(new RectangleF(30, 57, 28, 3.6f), 1.2f))
        {
            g.FillPath(hole, space);
        }

        // mouse na frente, com uma borda do degradê separando do teclado
        var mouse = new RectangleF(60, 44, 23, 33);
        using var mousePath = new GraphicsPath();
        mousePath.AddArc(mouse.X, mouse.Y, mouse.Width, mouse.Width, 180, 180);
        mousePath.AddArc(mouse.X, mouse.Bottom - mouse.Width, mouse.Width, mouse.Width, 0, 180);
        mousePath.CloseFigure();
        using (var outline = new Pen(hole, 5.5f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(outline, mousePath);
        }
        g.FillPath(fill, mousePath);

        // divisão dos botões e rodinha
        using var line = new Pen(hole, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, mouse.X + 1.5f, 57, mouse.Right - 1.5f, 57);
        g.DrawLine(line, mouse.X + mouse.Width / 2f, mouse.Y + 1.5f, mouse.X + mouse.Width / 2f, 57);
        using var wheel = new Pen(hole, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(wheel, mouse.X + mouse.Width / 2f, 48.5f, mouse.X + mouse.Width / 2f, 52.5f);
    }

    /// <summary>Casa: telhado com beiral, chaminé e porta vazada.</summary>
    private static void DrawHouse(Graphics g, Brush fill, Brush hole)
    {
        using var roof = new GraphicsPath();
        roof.AddLines(new PointF[] { new(50, 20), new(84, 49), new(78, 55), new(50, 31), new(22, 55), new(16, 49) });
        roof.CloseFigure();
        g.FillPath(fill, roof);

        // chaminé
        g.FillRectangle(fill, 64, 25, 8, 14);

        // corpo da casa (abaixo do telhado, com uma fresta do degradê entre os dois)
        using var body = new GraphicsPath();
        body.AddLines(new PointF[] { new(50, 37), new(74, 57), new(74, 76), new(26, 76), new(26, 57) });
        body.CloseFigure();
        g.FillPath(fill, body);

        using var door = Shapes.RoundedRect(new RectangleF(44, 59, 12, 17.5f), 2.5f);
        g.FillPath(hole, door);
    }

    /// <summary>Engrenagem de oito dentes com furo no centro.</summary>
    private static void DrawGear(Graphics g, Brush fill, Brush hole)
    {
        const int teeth = 8;
        const float cx = 50, cy = 50;
        const float outer = 31, inner = 24;
        using var gear = new GraphicsPath();
        var points = new List<PointF>();
        for (int i = 0; i < teeth; i++)
        {
            // cada dente: base larga, topo mais estreito (trapézio)
            float a = i * 360f / teeth;
            points.Add(Polar(cx, cy, inner, a - 17));
            points.Add(Polar(cx, cy, outer, a - 10));
            points.Add(Polar(cx, cy, outer, a + 10));
            points.Add(Polar(cx, cy, inner, a + 17));
        }
        gear.AddPolygon(points.ToArray());
        g.FillPath(fill, gear);
        g.FillEllipse(fill, cx - inner - 0.5f, cy - inner - 0.5f, (inner + 0.5f) * 2, (inner + 0.5f) * 2);

        g.FillEllipse(hole, cx - 10, cy - 10, 20, 20);
    }

    /// <summary>Filmadora: corpo arredondado com a lente em trapézio ao lado e a luz de gravação vazada.</summary>
    private static void DrawCamera(Graphics g, Brush fill, Brush hole)
    {
        using (var body = Shapes.RoundedRect(new RectangleF(16, 33, 46, 34), 7))
        {
            g.FillPath(fill, body);
        }

        using var lens = new GraphicsPath();
        lens.AddLines(new PointF[] { new(66, 44), new(84, 34.5f), new(84, 65.5f), new(66, 56) });
        lens.CloseFigure();
        g.FillPath(fill, lens);

        g.FillEllipse(hole, 23, 40, 9, 9);
    }

    private static PointF Polar(float cx, float cy, float r, float degrees)
    {
        double rad = degrees * Math.PI / 180d;
        return new PointF(cx + r * (float)Math.Cos(rad), cy + r * (float)Math.Sin(rad));
    }
}
