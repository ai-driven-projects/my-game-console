using System.Diagnostics;
using System.Runtime.InteropServices;
using MyGameConsole.App;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Teclado virtual do Windows, para digitar com o controle (junto com o mouse pelo analógico).
/// Duas opções, escolhidas em <see cref="Models.AppSettings.VirtualKeyboardClassic"/>:
///
/// - teclado de toque (TabTip.exe), o mesmo do Windows 11: mostrado e escondido pela interface COM
///   ITipInvocation, a mesma que o botão da barra de tarefas usa;
/// - teclado clássico (osk.exe): uma janela comum, que se abre e se fecha como qualquer processo.
///
/// O estado é lido do próprio Windows (janela do teclado de toque visível, ou processo do osk vivo),
/// e não de um sinalizador interno: assim o app acerta mesmo se o usuário fechar o teclado na mão.
/// </summary>
public sealed class VirtualKeyboardService
{
    private const string TouchKeyboardClass = "IPTip_Main_Window";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";
    private static readonly string[] TouchKeyboardProcesses = ["TextInputHost", "TabTip", "InputApp"];

    /// <summary>A leitura do estado percorre as janelas do Windows; um cache curto evita repeti-la a cada quadro desenhado.</summary>
    private static readonly TimeSpan StateCache = TimeSpan.FromMilliseconds(400);

    private readonly SettingsService _settings;
    private bool _visible;
    private DateTime _visibleAt;

    public VirtualKeyboardService(SettingsService settings)
    {
        _settings = settings;
    }

    private bool UseClassic => _settings.Current.VirtualKeyboardClassic;

    /// <summary>O teclado virtual está aparecendo agora.</summary>
    public bool IsVisible
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now - _visibleAt < StateCache) return _visible;

            _visible = UseClassic ? IsClassicRunning() : IsTouchKeyboardVisible();
            _visibleAt = now;
            return _visible;
        }
    }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    public void Show()
    {
        if (UseClassic) ShowClassic();
        else ShowTouch();
        _visibleAt = default; // o estado mudou: a próxima leitura vai ao Windows de novo
    }

    public void Hide()
    {
        if (UseClassic) HideClassic();
        else HideTouch();
        _visibleAt = default;
    }

    // ------------------------------------------------------------------
    // Teclado clássico (osk.exe)
    // ------------------------------------------------------------------

    private static bool IsClassicRunning()
    {
        try
        {
            var running = Process.GetProcessesByName("osk");
            foreach (var p in running) p.Dispose();
            return running.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ShowClassic()
    {
        if (IsClassicRunning()) return;

        // Elevado, o lançamento passa pelo Explorer (o osk.exe não deve herdar privilégios).
        var osk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe");
        ProcessLauncher.Start(File.Exists(osk) ? osk : "osk.exe");
    }

    private static void HideClassic()
    {
        foreach (var p in Process.GetProcessesByName("osk"))
        {
            using (p)
            {
                try { p.CloseMainWindow(); if (!p.WaitForExit(1500)) p.Kill(); }
                catch { /* já saiu ou sem permissão: nada a fazer */ }
            }
        }
    }

    // ------------------------------------------------------------------
    // Teclado de toque (TabTip.exe)
    // ------------------------------------------------------------------

    private static bool IsTouchKeyboardVisible()
    {
        foreach (var w in ForegroundWindow.ListVisible())
        {
            if (w.ClassName is not (TouchKeyboardClass or CoreWindowClass)) continue;
            if (w.ClassName == CoreWindowClass && !IsTouchKeyboardProcess(w.ProcessId)) continue;
            if (IsCloaked(w.Handle)) continue; // no Windows 11 a janela existe sempre; escondida, fica "cloaked"

            return true;
        }

        return false;
    }

    private static bool IsTouchKeyboardProcess(uint processId)
    {
        try
        {
            using var p = Process.GetProcessById((int)processId);
            return TouchKeyboardProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                   && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private void ShowTouch()
    {
        EnsureTabTipRunning();
        if (!IsTouchKeyboardVisible()) InvokeTouchKeyboard(show: true);
    }

    private void HideTouch()
    {
        if (IsTouchKeyboardVisible()) InvokeTouchKeyboard(show: false);
    }

    /// <summary>TabTip.exe hospeda o teclado de toque; sem ele no ar, a chamada COM não tem o que mostrar.</summary>
    private static void EnsureTabTipRunning()
    {
        try
        {
            var running = Process.GetProcessesByName("TabTip");
            foreach (var p in running) p.Dispose();
            if (running.Length > 0) return;
        }
        catch
        {
            // sem leitura da lista de processos, tenta abrir assim mesmo
        }

        var tabTip = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "microsoft shared", "ink", "TabTip.exe");
        if (!File.Exists(tabTip)) return;

        ProcessLauncher.Start(tabTip);
        Thread.Sleep(300); // o serviço COM só responde depois que o processo sobe
    }

    /// <summary>
    /// Mostra ou esconde o teclado de toque pela interface COM do Windows (ITipInvocation.Toggle),
    /// que é o mesmo caminho do botão do teclado na barra de tarefas. Se ela falhar, cai no osk.exe.
    /// </summary>
    private void InvokeTouchKeyboard(bool show)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(TipInvocationClsid)
                       ?? throw new InvalidOperationException("Teclado de toque indisponível neste Windows.");
            var instance = Activator.CreateInstance(type)
                           ?? throw new InvalidOperationException("Não foi possível falar com o teclado de toque.");
            try
            {
                ((ITipInvocation)instance).Toggle(NativeMethods.GetDesktopWindow());
            }
            finally
            {
                Marshal.ReleaseComObject(instance);
            }
        }
        catch
        {
            // Alguns ambientes (ou o app elevado) recusam a chamada COM: o teclado clássico sempre funciona.
            _settings.Update(s => s.VirtualKeyboardClassic = true);
            if (show) ShowClassic();
            else HideClassic();
        }
    }

    private static readonly Guid TipInvocationClsid = new("4ce576fa-83dc-4f88-951c-9d0782b4e376");

    [ComImport]
    [Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr hwnd);
    }
}
