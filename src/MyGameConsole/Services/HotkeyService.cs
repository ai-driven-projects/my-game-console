using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Tecla de atalho global (funciona com qualquer janela em foco). Usa uma janela invisível
/// para receber WM_HOTKEY. O atalho é descrito como texto, ex.: "Ctrl+Alt+G".
/// </summary>
public sealed class HotkeyService : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xC0DE;

    public event EventHandler? Pressed;

    /// <summary>Atalho registrado no momento, ou null.</summary>
    public string? RegisteredHotkey { get; private set; }

    public HotkeyService()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>Registra o atalho. Retorna false se o texto for inválido ou se o atalho estiver em uso.</summary>
    public bool Register(string? hotkey)
    {
        Unregister();

        if (!TryParse(hotkey, out var modifiers, out var key)) return false;

        if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key))
        {
            return false;
        }

        RegisteredHotkey = hotkey;
        return true;
    }

    public void Unregister()
    {
        if (RegisteredHotkey is null) return;
        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        RegisteredHotkey = null;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)NativeMethods.WM_HOTKEY && (int)m.WParam == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    // ------------------------------------------------------------------
    // Conversão texto <-> teclas
    // ------------------------------------------------------------------

    public static bool TryParse(string? text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    if (key != Keys.None || !Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed))
                    {
                        return false;
                    }
                    key = parsed;
                    break;
            }
        }

        return key != Keys.None && !IsModifierKey(key);
    }

    /// <summary>Monta o texto do atalho a partir de um KeyDown. Retorna null se só houver modificadores.</summary>
    public static string? Format(KeyEventArgs e)
    {
        if (IsModifierKey(e.KeyCode)) return null;

        var parts = new List<string>(4);
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        parts.Add(e.KeyCode.ToString());
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.LWin or Keys.RWin;
}
