using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Estado bruto lido de um controle HID: bit (n-1) ligado = botão HID n pressionado,
/// direcional (hat switch) já convertido em direções e eixos normalizados do analógico esquerdo
/// (X, Y) e do direito (RX, RY; zero quando o controle não publica esses eixos).
/// </summary>
internal readonly record struct HidRawState(
    uint ButtonMask, bool Up, bool Down, bool Left, bool Right, short X, short Y, short RX = 0, short RY = 0);

/// <summary>
/// Um controle HID genérico (o que o Windows chama de "Controlador de jogo compatível com HID",
/// lido por DirectInput). Lê os relatórios de entrada em uma thread própria e guarda o último estado.
/// É assim que o 8BitDo se apresenta por Bluetooth, sem passar pelo XInput.
/// </summary>
internal sealed class HidGamepadDevice : IDisposable
{
    private const ushort UsagePageGenericDesktop = 0x01;
    private const ushort UsagePageButton = 0x09;
    private const ushort UsageJoystick = 0x04;
    private const ushort UsageGamepad = 0x05;
    private const ushort UsageMultiAxis = 0x08;
    private const ushort UsageX = 0x30;
    private const ushort UsageY = 0x31;
    private const ushort UsageZ = 0x32;
    private const ushort UsageRx = 0x33;
    private const ushort UsageRy = 0x34;
    private const ushort UsageRz = 0x35;
    private const ushort UsageHat = 0x39;

    /// <summary>Um eixo publicado pelo controle: o usage HID e o intervalo lógico dos valores.</summary>
    private readonly record struct Axis(ushort Usage, int Min, int Max, ushort BitSize);

    private readonly SafeFileHandle _handle;
    private readonly IntPtr _preparsed;
    private readonly ushort _reportLength;
    private readonly Axis? _x;
    private readonly Axis? _y;
    private readonly Axis? _rx;
    private readonly Axis? _ry;
    private readonly Axis? _hat;
    private readonly uint _maxButtonUsages;

    private readonly object _lock = new();
    private HidRawState _latest;
    private volatile bool _dead;
    private volatile bool _disposed;
    private IntPtr _readerThread;

    public string Path { get; }
    public string Name { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }

    /// <summary>Falso depois que a leitura falhou (controle desligado ou desconectado).</summary>
    public bool IsAlive => !_dead;

    private HidGamepadDevice(string path, SafeFileHandle handle, IntPtr preparsed, ushort reportLength,
        Axis? x, Axis? y, Axis? rx, Axis? ry, Axis? hat, ushort vid, ushort pid, string name)
    {
        Path = path;
        _handle = handle;
        _preparsed = preparsed;
        _reportLength = reportLength;
        _x = x;
        _y = y;
        _rx = rx;
        _ry = ry;
        _hat = hat;
        VendorId = vid;
        ProductId = pid;
        Name = name;
        _maxButtonUsages = NativeMethods.HidP_MaxUsageListLength(NativeMethods.HidP_Input, UsagePageButton, preparsed);

        var thread = new Thread(ReadLoop) { IsBackground = true, Name = "HidGamepad " + name };
        thread.Start();
    }

    /// <summary>Abre o caminho HID se ele for um controle de jogo legível. Null para qualquer outro dispositivo.</summary>
    public static HidGamepadDevice? TryOpen(string path)
    {
        // Primeiro sem acesso de leitura: basta para consultar as capacidades e evita tocar em teclado/mouse.
        using (var probe = NativeMethods.CreateFile(path, 0, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                   IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero))
        {
            if (probe.IsInvalid || !IsGamepad(probe)) return null;
        }

        var handle = NativeMethods.CreateFile(path, NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed))
        {
            handle.Dispose();
            return null;
        }

        try
        {
            if (NativeMethods.HidP_GetCaps(preparsed, out var caps) != NativeMethods.HIDP_STATUS_SUCCESS
                || caps.InputReportByteLength == 0)
            {
                throw new InvalidOperationException("Dispositivo HID sem relatório de entrada.");
            }

            ReadAxes(preparsed, caps.NumberInputValueCaps, out var x, out var y, out var rx, out var ry, out var hat);

            var attrs = new NativeMethods.HiddAttributes { Size = (uint)Marshal.SizeOf<NativeMethods.HiddAttributes>() };
            NativeMethods.HidD_GetAttributes(handle, ref attrs);

            // Por Bluetooth LE o Windows costuma não devolver a string do produto; usa o fabricante quando conhecido.
            var name = ReadProductName(handle)
                ?? (attrs.VendorId == 0x2DC8 ? "8BitDo (Bluetooth)" : $"Controle HID {attrs.VendorId:X4}:{attrs.ProductId:X4}");
            return new HidGamepadDevice(path, handle, preparsed, caps.InputReportByteLength, x, y, rx, ry, hat,
                attrs.VendorId, attrs.ProductId, name);
        }
        catch
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
            handle.Dispose();
            return null;
        }
    }

    private static bool IsGamepad(SafeFileHandle handle)
    {
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed)) return false;
        try
        {
            if (NativeMethods.HidP_GetCaps(preparsed, out var caps) != NativeMethods.HIDP_STATUS_SUCCESS) return false;
            return caps.UsagePage == UsagePageGenericDesktop
                && caps.Usage is UsageJoystick or UsageGamepad or UsageMultiAxis;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>
    /// Eixos publicados pelo controle. O analógico esquerdo é sempre X/Y; o direito não tem um padrão:
    /// a maioria dos controles (inclusive o 8BitDo em D-input) usa Z/Rz, outros usam Rx/Ry — o primeiro
    /// par presente vale. Controles que não publicam nenhum dos dois ficam sem analógico direito.
    /// </summary>
    private static void ReadAxes(IntPtr preparsed, ushort valueCapsCount,
        out Axis? x, out Axis? y, out Axis? rx, out Axis? ry, out Axis? hat)
    {
        x = y = rx = ry = hat = null;
        if (valueCapsCount == 0) return;

        Axis? z = null, rz = null, rxRaw = null, ryRaw = null;

        var list = new NativeMethods.HidpValueCaps[valueCapsCount];
        ushort count = valueCapsCount;
        if (NativeMethods.HidP_GetValueCaps(NativeMethods.HidP_Input, list, ref count, preparsed) != NativeMethods.HIDP_STATUS_SUCCESS)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            ref var vc = ref list[i];
            if (vc.UsagePage != UsagePageGenericDesktop) continue;

            ushort first = vc.UsageMin;
            ushort last = vc.IsRange != 0 ? vc.UsageMax : vc.UsageMin;

            ushort bits = vc.BitSize;
            int min = vc.LogicalMin;
            int max = vc.LogicalMax;
            if (max < min)
            {
                // hid.dll estende o sinal de campos sem sinal (ex.: 0..65535 vira 0..-1).
                max = (int)((1L << bits) - 1);
            }

            Axis Range(ushort usage) => new(usage, min, max, bits);
            bool Has(ushort usage) => first <= usage && usage <= last;

            if (Has(UsageX)) x ??= Range(UsageX);
            if (Has(UsageY)) y ??= Range(UsageY);
            if (Has(UsageZ)) z ??= Range(UsageZ);
            if (Has(UsageRz)) rz ??= Range(UsageRz);
            if (Has(UsageRx)) rxRaw ??= Range(UsageRx);
            if (Has(UsageRy)) ryRaw ??= Range(UsageRy);
            if (Has(UsageHat)) hat ??= Range(UsageHat);
        }

        // Z/Rz primeiro (o mais comum); Rx/Ry como alternativa. Só vale com os dois eixos do par.
        if (z is not null && rz is not null) (rx, ry) = (z, rz);
        else if (rxRaw is not null && ryRaw is not null) (rx, ry) = (rxRaw, ryRaw);
    }

    private static unsafe string? ReadProductName(SafeFileHandle handle)
    {
        const int Chars = 126;
        char* buffer = stackalloc char[Chars];
        if (!NativeMethods.HidD_GetProductString(handle, buffer, Chars * sizeof(char))) return null;
        var name = new string(buffer).Trim();
        return name.Length == 0 ? null : name;
    }

    // ------------------------------------------------------------------
    // Leitura
    // ------------------------------------------------------------------

    public HidRawState Read()
    {
        lock (_lock) return _latest;
    }

    private void ReadLoop()
    {
        _readerThread = NativeMethods.OpenThread(NativeMethods.THREAD_TERMINATE, false, NativeMethods.GetCurrentThreadId());

        var report = new byte[_reportLength];
        var usages = new ushort[Math.Max(1, _maxButtonUsages)];

        while (!_disposed)
        {
            if (!NativeMethods.ReadFile(_handle, report, _reportLength, out var read, IntPtr.Zero) || read == 0)
            {
                break;
            }

            var state = Parse(report, read, usages);
            lock (_lock) _latest = state;
        }

        _dead = true;
        lock (_lock) _latest = default;
    }

    private HidRawState Parse(byte[] report, uint length, ushort[] usages)
    {
        uint mask = 0;
        uint count = (uint)usages.Length;
        if (NativeMethods.HidP_GetUsages(NativeMethods.HidP_Input, UsagePageButton, 0, usages, ref count, _preparsed, report, length)
            == NativeMethods.HIDP_STATUS_SUCCESS)
        {
            for (int i = 0; i < count; i++)
            {
                int n = usages[i];
                if (n is >= 1 and <= 32) mask |= 1u << (n - 1);
            }
        }

        bool up = false, down = false, left = false, right = false;
        if (_hat is { } hatAxis && TryGetValue(hatAxis, report, length, out var hatRaw))
        {
            // 0 = cima, sentido horário até 7 = cima-esquerda; fora do intervalo lógico = centro.
            int dir = hatRaw - hatAxis.Min;
            if (hatRaw >= hatAxis.Min && hatRaw <= hatAxis.Max && dir <= 7)
            {
                up = dir is 7 or 0 or 1;
                right = dir is 1 or 2 or 3;
                down = dir is 3 or 4 or 5;
                left = dir is 5 or 6 or 7;
            }
        }

        var (x, y) = ReadStick(_x, _y, report, length);
        var (rx, ry) = ReadStick(_rx, _ry, report, length);

        return new HidRawState(mask, up, down, left, right, x, y, rx, ry);
    }

    /// <summary>Par de eixos de um analógico, normalizado como no XInput (Y positivo para cima).</summary>
    private (short X, short Y) ReadStick(Axis? xAxis, Axis? yAxis, byte[] report, uint length)
    {
        short x = 0, y = 0;
        if (xAxis is { } xa && TryGetValue(xa, report, length, out var xv)) x = Normalize(xv, xa);
        if (yAxis is { } ya && TryGetValue(ya, report, length, out var yv)) y = (short)-Normalize(yv, ya);
        return (x, y);
    }

    private bool TryGetValue(Axis axis, byte[] report, uint length, out int value)
    {
        value = 0;
        if (NativeMethods.HidP_GetUsageValue(NativeMethods.HidP_Input, UsagePageGenericDesktop, 0, axis.Usage, out var raw, _preparsed, report, length)
            != NativeMethods.HIDP_STATUS_SUCCESS)
        {
            return false;
        }

        value = (int)raw;
        if (axis.Min < 0 && axis.BitSize is > 0 and < 32)
        {
            // Campo com sinal: estende o bit de sinal.
            int shift = 32 - axis.BitSize;
            value = (int)(raw << shift) >> shift;
        }

        return true;
    }

    private static short Normalize(int value, Axis axis)
    {
        long span = (long)axis.Max - axis.Min;
        if (span <= 0) return 0;
        double norm = ((value - axis.Min) * 2.0 / span) - 1.0;
        norm = Math.Clamp(norm, -1.0, 1.0);
        return (short)Math.Round(norm * short.MaxValue);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Acorda o ReadFile bloqueado na thread de leitura; sem isso o handle só fecharia no próximo relatório.
        if (_readerThread != IntPtr.Zero)
        {
            NativeMethods.CancelSynchronousIo(_readerThread);
            NativeMethods.CloseHandle(_readerThread);
            _readerThread = IntPtr.Zero;
        }

        _handle.Dispose();
        NativeMethods.HidD_FreePreparsedData(_preparsed);
    }
}
