using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Enumera e mantém abertos os controles HID genéricos (DirectInput) do sistema.
/// Dispositivos XInput são ignorados aqui (o caminho HID deles contém "IG_"), pois o
/// <see cref="ControllerService"/> já os lê pela API do XInput.
/// </summary>
public sealed class HidGamepadService : IDisposable
{
    private readonly Dictionary<string, HidGamepadDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _devices.Count;

    internal IReadOnlyCollection<HidGamepadDevice> Devices => _devices.Values;

    /// <summary>Abre controles novos e descarta os que sumiram ou pararam de responder.</summary>
    public void Refresh()
    {
        var present = EnumerateHidPaths();

        foreach (var (path, device) in _devices.ToList())
        {
            if (!device.IsAlive || !present.Contains(path))
            {
                device.Dispose();
                _devices.Remove(path);
            }
        }

        foreach (var path in present)
        {
            if (_devices.ContainsKey(path)) continue;
            if (path.Contains("IG_", StringComparison.OrdinalIgnoreCase)) continue; // XInput

            try
            {
                var device = HidGamepadDevice.TryOpen(path);
                if (device is not null) _devices[path] = device;
            }
            catch
            {
                // Dispositivo estranho ou sem permissão: ignora e segue.
            }
        }
    }

    private static unsafe HashSet<string> EnumerateHidPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NativeMethods.HidD_GetHidGuid(out var hidGuid);

        // A lista pode crescer entre as duas chamadas; nesse caso tenta de novo.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.CM_Get_Device_Interface_List_Size(out var length, in hidGuid, IntPtr.Zero,
                    NativeMethods.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != NativeMethods.CR_SUCCESS || length == 0)
            {
                return result;
            }

            var buffer = new char[length];
            int cr;
            fixed (char* p = buffer)
            {
                cr = NativeMethods.CM_Get_Device_Interface_List(in hidGuid, IntPtr.Zero, p, length,
                    NativeMethods.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            }

            if (cr == NativeMethods.CR_BUFFER_SMALL) continue;
            if (cr != NativeMethods.CR_SUCCESS) return result;

            // Multi-string: caminhos separados por NUL, terminada por NUL duplo.
            int start = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '\0') continue;
                if (i > start) result.Add(new string(buffer, start, i - start));
                start = i + 1;
            }
            break;
        }

        return result;
    }

    public void Dispose()
    {
        foreach (var device in _devices.Values) device.Dispose();
        _devices.Clear();
    }
}
