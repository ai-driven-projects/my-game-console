using System.Text.RegularExpressions;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Bateria de um controle HID que chega por Bluetooth (ex.: 8BitDo), a mesma que aparece em Configurações >
/// Bluetooth. O HID não tem um campo padrão de bateria, mas o Windows guarda a porcentagem no nó do aparelho
/// Bluetooth (<c>BTHLE\DEV_&lt;endereço&gt;</c> no Bluetooth LE, <c>BTHENUM\DEV_&lt;endereço&gt;</c> no clássico),
/// e o endereço do aparelho vem no próprio caminho do dispositivo HID.
/// </summary>
internal static partial class BluetoothBattery
{
    /// <summary>Endereço Bluetooth (12 dígitos hexadecimais) no fim do identificador do HID: "..._REV&amp;0001_E417D8B4A7E8#...".</summary>
    [GeneratedRegex(@"_([0-9A-F]{12})#", RegexOptions.IgnoreCase)]
    private static partial Regex AddressInPath();

    /// <summary>Porcentagem de 0 a 100, ou nulo se o controle não for Bluetooth ou o Windows não souber a bateria.</summary>
    public static int? Read(string hidPath)
    {
        var match = AddressInPath().Match(hidPath);
        if (!match.Success) return null;
        var address = match.Groups[1].Value;

        foreach (var enumerator in new[] { "BTHLE", "BTHENUM" })
        {
            foreach (var id in DeviceIds(enumerator))
            {
                if (!id.Contains($@"\DEV_{address}", StringComparison.OrdinalIgnoreCase)) continue;
                if (ReadBatteryProperty(id) is { } percent) return percent;
            }
        }

        return null;
    }

    private static unsafe int? ReadBatteryProperty(string deviceId)
    {
        if (NativeMethods.CM_Locate_DevNode(out var devInst, deviceId, NativeMethods.CM_LOCATE_DEVNODE_NORMAL) != NativeMethods.CR_SUCCESS)
        {
            return null;
        }

        byte value;
        uint size = 1;
        int cr = NativeMethods.CM_Get_DevNode_Property(devInst, NativeMethods.DEVPKEY_Bluetooth_Battery, out var type, &value, ref size, 0);
        return cr == NativeMethods.CR_SUCCESS && type == NativeMethods.DEVPROP_TYPE_BYTE && value <= 100 ? value : null;
    }

    private static unsafe List<string> DeviceIds(string enumerator)
    {
        var result = new List<string>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.CM_Get_Device_ID_List_Size(out var length, enumerator, NativeMethods.CM_GETIDLIST_FILTER_ENUMERATOR) != NativeMethods.CR_SUCCESS
                || length == 0)
            {
                return result;
            }

            var buffer = new char[length];
            int cr;
            fixed (char* p = buffer)
            {
                cr = NativeMethods.CM_Get_Device_ID_List(enumerator, p, length, NativeMethods.CM_GETIDLIST_FILTER_ENUMERATOR);
            }

            if (cr == NativeMethods.CR_BUFFER_SMALL) continue;
            if (cr != NativeMethods.CR_SUCCESS) return result;

            // Multi-string: identificadores separados por NUL.
            int start = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '\0') continue;
                if (i > start) result.Add(new string(buffer, start, i - start));
                start = i + 1;
            }

            return result;
        }

        return result;
    }
}
