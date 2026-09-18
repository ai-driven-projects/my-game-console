using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MyGameConsole.Native;

internal static partial class NativeMethods
{
    // ---------- user32 ----------

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Localizar janelas de outros processos e trazê-las para a frente (ver App/ForegroundWindow.cs).

    [LibraryImport("user32.dll", EntryPoint = "EnumWindows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static unsafe partial int GetWindowText(IntPtr hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    internal static unsafe partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetDesktopWindow")]
    internal static partial IntPtr GetDesktopWindow();

    [LibraryImport("user32.dll", EntryPoint = "BringWindowToTop")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "AttachThreadInput")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll", EntryPoint = "AllowSetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    internal const uint ASFW_ANY = 0xFFFFFFFF;

    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    // Estilos estendidos de janela usados pelo aviso flutuante (Forms/ToastForm.cs): ele aparece sobre tudo,
    // nunca rouba o foco (WS_EX_NOACTIVATE), deixa o clique passar (WS_EX_TRANSPARENT) e não entra no Alt+Tab.
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_RESTORE = 9;
    internal const int SW_SHOWMINNOACTIVE = 7;

    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_HOTKEY = 0x0312;

    /// <summary>Comando do menu de contexto da área de trabalho: "Mostrar ícones da área de trabalho".</summary>
    internal const int CMD_TOGGLE_DESKTOP_ICONS = 0x7402;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const uint SPI_SETDESKWALLPAPER = 0x0014;
    internal const uint SPIF_UPDATEINIFILE = 0x0001;
    internal const uint SPIF_SENDCHANGE = 0x0002;

    // ---------- dwm (janelas "cloaked": existem mas não aparecem, como o teclado de toque) ----------

    /// <summary>DWMWA_CLOAKED: diferente de zero quando a janela está escondida pelo compositor.</summary>
    internal const uint DWMWA_CLOAKED = 14;

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttribute(IntPtr hWnd, uint attribute, out int value, int size);

    // ---------- shell32 (barra de tarefas) ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    internal const uint ABM_GETSTATE = 0x0004;
    internal const uint ABM_SETSTATE = 0x000A;
    internal const uint ABS_AUTOHIDE = 0x0001;

    [LibraryImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    internal static partial UIntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    // ---------- powrprof ----------

    [LibraryImport("powrprof.dll", EntryPoint = "SetSuspendState")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    // Configurações de energia por plano (as mesmas do powercfg / "Opções de entrada"). ERROR_SUCCESS está na seção XInput.
    internal const uint ERROR_NO_MORE_ITEMS = 259;
    internal const uint ACCESS_SCHEME = 16;

    /// <summary>Subgrupo "sem subgrupo" (SUB_NONE), onde ficam os ajustes de nível do plano.</summary>
    internal static readonly Guid GUID_NO_SUBGROUP = new("fea3413e-7e05-4911-9a71-700331f1c294");

    /// <summary>"Exigir senha ao acordar" (CONSOLELOCK): 1 = exigir, 0 = entrar direto.</summary>
    internal static readonly Guid GUID_LOCK_CONSOLE_ON_WAKE = new("0e796bdb-100d-47d6-a2d5-f7d2daa51f51");

    [LibraryImport("powrprof.dll", EntryPoint = "PowerEnumerate")]
    internal static partial uint PowerEnumerate(
        IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags, uint index, out Guid buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme")]
    internal static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme")]
    internal static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerReadACValueIndex")]
    internal static partial uint PowerReadACValueIndex(
        IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid, in Guid powerSettingGuid, out uint acValueIndex);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerReadDCValueIndex")]
    internal static partial uint PowerReadDCValueIndex(
        IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid, in Guid powerSettingGuid, out uint dcValueIndex);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerWriteACValueIndex")]
    internal static partial uint PowerWriteACValueIndex(
        IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid, in Guid powerSettingGuid, uint acValueIndex);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerWriteDCValueIndex")]
    internal static partial uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey, in Guid schemeGuid, in Guid subGroupOfPowerSettingsGuid, in Guid powerSettingGuid, uint dcValueIndex);

    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
    internal static partial IntPtr LocalFree(IntPtr hMem);

    // ---------- XInput ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    internal const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
    internal const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
    internal const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    internal const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    internal const ushort XINPUT_GAMEPAD_START = 0x0010;
    internal const ushort XINPUT_GAMEPAD_BACK = 0x0020;
    internal const ushort XINPUT_GAMEPAD_A = 0x1000;
    internal const ushort XINPUT_GAMEPAD_B = 0x2000;
    internal const ushort XINPUT_GAMEPAD_X = 0x4000;
    internal const ushort XINPUT_GAMEPAD_Y = 0x8000;

    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    internal static partial uint XInputGetState(uint dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    internal const byte BATTERY_DEVTYPE_GAMEPAD = 0;
    internal const byte BATTERY_TYPE_DISCONNECTED = 0;
    internal const byte BATTERY_TYPE_WIRED = 1;
    internal const byte BATTERY_TYPE_UNKNOWN = 0xFF;

    /// <summary>Nível da bateria de um controle XInput, de 0 (vazia) a 3 (cheia).</summary>
    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    internal static partial uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XInputBatteryInformation information);

    // ---------- energia da tela (user32 / kernel32) ----------

    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const int SC_MONITORPOWER = 0xF170;
    internal const int MONITOR_OFF = 2;
    internal const int MONITOR_ON = -1;

    internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
    internal const uint ES_DISPLAY_REQUIRED = 0x00000002;
    internal const uint ES_CONTINUOUS = 0x80000000;

    internal const uint INPUT_MOUSE = 0;
    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    internal const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;
    internal const uint MOUSEEVENTF_HWHEEL = 0x1000;

    /// <summary>Uma "casa" da roda do mouse (o mesmo WHEEL_DELTA do Windows).</summary>
    internal const int WHEEL_DELTA = 120;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>INPUT com a união reduzida a MOUSEINPUT (o maior membro), que é o único usado aqui.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint type;
        public MouseInput mi;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadExecutionState")]
    internal static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    // ---------- ícones de executáveis (shell32 / user32) ----------

    /// <summary>Extrai um ícone de um .exe/.dll/.ico no tamanho pedido (até 256 px). Retorna S_OK (0) em sucesso.</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHDefExtractIcon(string iconFile, int iconIndex, uint flags, out IntPtr iconLarge, out IntPtr iconSmall, uint iconSize);

    [LibraryImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr icon);

    // ---------- HID genérico (controles DirectInput, ex.: 8BitDo por Bluetooth) ----------

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint THREAD_TERMINATE = 0x0001;

    internal const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 1;
    internal const int CR_SUCCESS = 0;
    internal const int CR_BUFFER_SMALL = 0x1A;

    internal const int HidP_Input = 0;
    internal const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    /// <summary>HIDP_VALUE_CAPS (72 bytes). A união Range/NotRange é achatada: em NotRange, UsageMin é o Usage.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        public fixed ushort Reserved2[5];
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadFile(SafeFileHandle handle, [Out] byte[] buffer, uint bytesToRead, out uint bytesRead, IntPtr overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "CancelSynchronousIo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelSynchronousIo(IntPtr thread);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenThread", SetLastError = true)]
    internal static partial IntPtr OpenThread(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
    internal static partial int CM_Get_Device_Interface_List_Size(out uint length, in Guid interfaceClassGuid, IntPtr deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW")]
    internal static unsafe partial int CM_Get_Device_Interface_List(in Guid interfaceClassGuid, IntPtr deviceId, char* buffer, uint bufferLength, uint flags);

    // Propriedades de dispositivo (ex.: a bateria que o Windows mostra em Configurações > Bluetooth).

    [StructLayout(LayoutKind.Sequential)]
    internal struct DevPropKey
    {
        public Guid FmtId;
        public uint Pid;
    }

    /// <summary>DEVPKEY_Bluetooth_Battery: porcentagem (BYTE) de aparelhos Bluetooth que informam a bateria.</summary>
    internal static readonly DevPropKey DEVPKEY_Bluetooth_Battery = new() { FmtId = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), Pid = 2 };

    internal const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1;
    internal const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    internal const uint DEVPROP_TYPE_BYTE = 0x3;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CM_Get_Device_ID_List_Size(out uint length, string? filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial int CM_Get_Device_ID_List(string? filter, char* buffer, uint bufferLength, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    internal static unsafe partial int CM_Get_DevNode_Property(uint devInst, in DevPropKey key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetHidGuid")]
    internal static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetPreparsedData")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

    [LibraryImport("hid.dll", EntryPoint = "HidD_FreePreparsedData")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_FreePreparsedData(IntPtr preparsedData);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetAttributes")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool HidD_GetAttributes(SafeFileHandle handle, ref HiddAttributes attributes);

    [LibraryImport("hid.dll", EntryPoint = "HidD_GetProductString")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static unsafe partial bool HidD_GetProductString(SafeFileHandle handle, char* buffer, uint bufferLength);

    [LibraryImport("hid.dll", EntryPoint = "HidP_GetCaps")]
    internal static partial int HidP_GetCaps(IntPtr preparsedData, out HidpCaps caps);

    [LibraryImport("hid.dll", EntryPoint = "HidP_GetValueCaps")]
    internal static partial int HidP_GetValueCaps(int reportType, [Out] HidpValueCaps[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [LibraryImport("hid.dll", EntryPoint = "HidP_MaxUsageListLength")]
    internal static partial uint HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsedData);

    [LibraryImport("hid.dll", EntryPoint = "HidP_GetUsages")]
    internal static partial int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usageList,
        ref uint usageLength, IntPtr preparsedData, byte[] report, uint reportLength);

    [LibraryImport("hid.dll", EntryPoint = "HidP_GetUsageValue")]
    internal static partial int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        out uint usageValue, IntPtr preparsedData, byte[] report, uint reportLength);
}
