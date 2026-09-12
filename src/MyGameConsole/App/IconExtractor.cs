using MyGameConsole.Native;

namespace MyGameConsole.App;

/// <summary>Extrai o ícone de um executável instalado (ex.: steam.exe) em alta resolução, para usar nos tiles.</summary>
public static class IconExtractor
{
    /// <summary>Retorna o ícone principal do arquivo como bitmap com transparência, ou null se não der.</summary>
    public static Bitmap? Extract(string? path, int size = 256)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        size = Math.Clamp(size, 16, 256);
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            // nIconSize: LOWORD = tamanho do ícone grande, HIWORD = do pequeno.
            uint sizes = (uint)size | ((uint)16 << 16);
            if (NativeMethods.SHDefExtractIcon(path, 0, 0, out large, out small, sizes) != 0 || large == IntPtr.Zero)
            {
                return null;
            }

            using var icon = Icon.FromHandle(large);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero) NativeMethods.DestroyIcon(large);
            if (small != IntPtr.Zero) NativeMethods.DestroyIcon(small);
        }
    }
}
