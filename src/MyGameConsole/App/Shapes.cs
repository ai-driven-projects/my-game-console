using System.Drawing.Drawing2D;

namespace MyGameConsole.App;

/// <summary>Formas geométricas compartilhadas pelo que é desenhado à mão (tela do console e desenho do controle).</summary>
public static class Shapes
{
    /// <summary>Retângulo de cantos arredondados. Quem chama é dono do caminho (descartar depois).</summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
