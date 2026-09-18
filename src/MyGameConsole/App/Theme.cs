namespace MyGameConsole.App;

/// <summary>Cores, glifos e fontes compartilhados pela tela do console e pelo papel de parede padrão.</summary>
public static class Theme
{
    public static readonly Color BgTop = ColorTranslator.FromHtml("#0B1119");
    public static readonly Color BgBottom = ColorTranslator.FromHtml("#1B2838");
    public static readonly Color Accent = ColorTranslator.FromHtml("#66C0F4");
    public static readonly Color Tile = ColorTranslator.FromHtml("#243447");
    public static readonly Color TileSelected = ColorTranslator.FromHtml("#2E4A63");
    public static readonly Color Text = Color.White;
    public static readonly Color Muted = ColorTranslator.FromHtml("#8FA3B7");
    public static readonly Color Success = ColorTranslator.FromHtml("#4CAF50");
    public static readonly Color Danger = ColorTranslator.FromHtml("#E5484D");
    /// <summary>Selo "pendente" no checklist: algo falta fazer.</summary>
    public static readonly Color Warning = ColorTranslator.FromHtml("#E39B2B");
    /// <summary>Selo de item desligado / neutro.</summary>
    public static readonly Color PillOff = Color.FromArgb(90, 100, 110);

    /// <summary>Fonte de ícones do sistema (Fluent no Windows 11, MDL2 no Windows 10).</summary>
    public static string IconFontName { get; } = ResolveIconFont();

    public const string GlyphGame = "";
    public const string GlyphPlay = "";
    public const string GlyphApps = "";
    /// <summary>Teclado e mouse: a página "Controle" (atalhos do controle, mouse pelo analógico, teclado virtual).</summary>
    public const string GlyphController = "";
    /// <summary>Mouse: aviso de mouse ligado ou desligado no controle.</summary>
    public const string GlyphMouse = "";
    public const string GlyphSettings = "";
    public const string GlyphHome = "";
    public const string GlyphMoon = "";
    public const string GlyphRefresh = "";
    public const string GlyphPower = "";
    public const string GlyphClose = "";

    private static string ResolveIconFont()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol" })
        {
            try
            {
                using var family = new FontFamily(name);
                return name;
            }
            catch (ArgumentException)
            {
                // fonte ausente, tenta a próxima
            }
        }

        return "Segoe UI";
    }
}
