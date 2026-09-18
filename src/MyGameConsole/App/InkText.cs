using System.Drawing.Drawing2D;

namespace MyGameConsole.App;

/// <summary>
/// Texto (ou glifo da fonte de ícones) centrado pelo desenho, não pela caixa da fonte. O <c>DrawString</c> centra a
/// caixa da linha, que reserva espaço para acentos e descendentes, e cada glifo da fonte de ícones fica numa posição
/// diferente dentro dela: centrados assim, ícones e números saíam tortos dentro dos círculos.
/// </summary>
public static class InkText
{
    /// <summary>Desenha o texto com o centro da tinta no centro de <paramref name="rect"/>, nos dois eixos.</summary>
    public static void DrawCentered(Graphics g, string text, string family, FontStyle style, float emPixels, Brush brush, RectangleF rect)
    {
        using var path = new GraphicsPath();
        using (var ff = new FontFamily(family))
        {
            path.AddString(text, ff, (int)style, emPixels, PointF.Empty, StringFormat.GenericTypographic);
        }

        var ink = path.GetBounds();
        if (ink.IsEmpty) return;

        using (var m = new Matrix())
        {
            m.Translate(rect.X + rect.Width / 2f - (ink.X + ink.Width / 2f), rect.Y + rect.Height / 2f - (ink.Y + ink.Height / 2f));
            path.Transform(m);
        }

        g.FillPath(brush, path);
    }
}
