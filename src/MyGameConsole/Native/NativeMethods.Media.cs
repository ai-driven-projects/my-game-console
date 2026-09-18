using System.Runtime.InteropServices;

namespace MyGameConsole.Native;

/// <summary>
/// Gravação de tela: DXGI (Desktop Duplication), Direct3D 11, Media Foundation (Sink Writer, MP4) e
/// WASAPI (áudio do PC em loopback). As interfaces COM são chamadas direto pela vtable, com ponteiros
/// crus: nada de RCW, então cada objeto é liberado na hora certa (<see cref="Com.Release"/>) — o que importa
/// aqui, porque a gravação cria texturas e amostras a cada quadro. Os números das posições (slots) seguem a
/// ordem dos métodos nos cabeçalhos do Windows SDK, contando os 3 do IUnknown.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // ---------- funções exportadas ----------

    [LibraryImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
    internal static partial int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    [LibraryImport("d3d11.dll", EntryPoint = "D3D11CreateDevice")]
    internal static partial int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelCount,
        uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [LibraryImport("mfplat.dll", EntryPoint = "MFStartup")]
    internal static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll", EntryPoint = "MFShutdown")]
    internal static partial int MFShutdown();

    [LibraryImport("mfplat.dll", EntryPoint = "MFCreateAttributes")]
    internal static partial int MFCreateAttributes(out IntPtr attributes, uint initialSize);

    [LibraryImport("mfplat.dll", EntryPoint = "MFCreateMediaType")]
    internal static partial int MFCreateMediaType(out IntPtr mediaType);

    [LibraryImport("mfplat.dll", EntryPoint = "MFCreateSample")]
    internal static partial int MFCreateSample(out IntPtr sample);

    [LibraryImport("mfplat.dll", EntryPoint = "MFCreateMemoryBuffer")]
    internal static partial int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);

    [LibraryImport("mfplat.dll", EntryPoint = "MFCreateDXGIDeviceManager")]
    internal static partial int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr manager);

    [LibraryImport("mfplat.dll", EntryPoint = "MFCreateDXGISurfaceBuffer")]
    internal static partial int MFCreateDXGISurfaceBuffer(
        in Guid riid, IntPtr surface, uint subresourceIndex, [MarshalAs(UnmanagedType.Bool)] bool bottomUpWhenLinear, out IntPtr buffer);

    [LibraryImport("mfreadwrite.dll", EntryPoint = "MFCreateSinkWriterFromURL", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr sinkWriter);

    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
    internal static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint clsContext, in Guid iid, out IntPtr instance);

    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    internal static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
    internal static partial void CoTaskMemFree(IntPtr pv);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    internal static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    /// <summary>
    /// Com <see cref="WDA_EXCLUDEFROMCAPTURE"/>, a janela aparece no monitor mas não nas capturas de tela
    /// (Desktop Duplication, Print Screen): os avisos do app não entram na gravação.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowDisplayAffinity")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 1;
    internal const uint COINIT_MULTITHREADED = 0;
    internal const uint CLSCTX_ALL = 0x17;
    internal const uint MF_VERSION = 0x00020070;

    internal const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    internal const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
    internal const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);
    internal const int E_ACCESSDENIED = unchecked((int)0x80070005);
    internal const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    internal const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    internal const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;
    internal const uint D3D11_SDK_VERSION = 7;
    internal const uint D3D11_USAGE_DEFAULT = 0;
    internal const uint D3D11_USAGE_STAGING = 3;
    internal const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    internal const uint D3D11_BIND_RENDER_TARGET = 0x20;
    internal const uint D3D11_CPU_ACCESS_READ = 0x20000;
    internal const uint D3D11_MAP_READ = 1;

    internal const uint AUDCLNT_SHAREMODE_SHARED = 0;
    internal const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    internal const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    /// <summary>O horário (QPC) do pacote não é confiável: vale o estimado a partir do pacote anterior.</summary>
    internal const uint AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4;

    // ---------- estruturas ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiOutputDesc
    {
        public fixed char DeviceName[32];
        public int Left, Top, Right, Bottom;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiOutduplDesc
    {
        public uint Width, Height, RefreshNumerator, RefreshDenominator, Format, ScanlineOrdering, Scaling;
        public uint Rotation;
        public int DesktopImageInSystemMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiOutduplFrameInfo
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public int PointerX, PointerY, PointerVisible;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11SubresourceData
    {
        public IntPtr SysMem;
        public uint SysMemPitch, SysMemSlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11MappedSubresource
    {
        public IntPtr Data;
        public uint RowPitch, DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11Box
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    // ---------- identificadores ----------

    internal static class Iid
    {
        public static readonly Guid DxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
        public static readonly Guid DxgiOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
        public static readonly Guid DxgiOutput5 = new("80a07424-ab52-42eb-833c-0c42fd282d98");
        public static readonly Guid D3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        public static readonly Guid D3D11Multithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");
        public static readonly Guid Mf2DBuffer = new("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
        public static readonly Guid MMDeviceEnumeratorClass = new("bcde0395-e52f-467c-8e3d-c4579291692e");
        public static readonly Guid MMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
        public static readonly Guid AudioClient = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
        public static readonly Guid AudioCaptureClient = new("c8adbd64-e71e-48a0-a4de-185c395cd317");
    }

    /// <summary>Atributos e formatos do Media Foundation (mfapi.h / mfreadwrite.h).</summary>
    internal static class MfGuid
    {
        public static readonly Guid ReadwriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
        public static readonly Guid SinkWriterD3DManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");
        public static readonly Guid TranscodeContainerType = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
        public static readonly Guid ContainerTypeMpeg4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");

        public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid Mpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid AllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
        public static readonly Guid AudioNumChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static readonly Guid AudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static readonly Guid AudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static readonly Guid AudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static readonly Guid AudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
        public static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
        public static readonly Guid VideoFormatRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
        public static readonly Guid AudioFormatAac = new("00001610-0000-0010-8000-00aa00389b71");
        public static readonly Guid AudioFormatPcm = new("00000001-0000-0010-8000-00aa00389b71");
    }

    // ---------- chamadas pela vtable ----------

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    /// <summary>IUnknown e tratamento de HRESULT.</summary>
    internal static class Com
    {
        public static void Release(ref IntPtr instance)
        {
            if (instance == IntPtr.Zero) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(instance, 2))(instance);
            instance = IntPtr.Zero;
        }

        public static int QueryInterface(IntPtr instance, in Guid iid, out IntPtr result)
        {
            IntPtr r;
            fixed (Guid* g = &iid)
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(instance, 0))(instance, g, &r);
                result = r;
                return hr;
            }
        }

        /// <summary>Lança uma exceção com o que estava sendo feito e o código do Windows, se o HRESULT for de erro.</summary>
        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new COMException($"{what} falhou (0x{hr:X8}).", hr);
        }
    }

    internal static class Dxgi
    {
        public static int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter)
        {
            IntPtr a;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(factory, 12))(factory, index, &a);
            adapter = a;
            return hr;
        }

        public static int EnumOutputs(IntPtr adapter, uint index, out IntPtr output)
        {
            IntPtr o;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(adapter, 7))(adapter, index, &o);
            output = o;
            return hr;
        }

        public static int GetOutputDesc(IntPtr output, out DxgiOutputDesc desc)
        {
            DxgiOutputDesc d;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, DxgiOutputDesc*, int>)Slot(output, 7))(output, &d);
            desc = d;
            return hr;
        }

        /// <summary>IDXGIOutput1::DuplicateOutput.</summary>
        public static int DuplicateOutput(IntPtr output1, IntPtr device, out IntPtr duplication)
        {
            IntPtr d;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(output1, 22))(output1, device, &d);
            duplication = d;
            return hr;
        }

        /// <summary>IDXGIOutput5::DuplicateOutput1: pede a imagem já no formato dado (converte HDR para 8 bits).</summary>
        public static int DuplicateOutput1(IntPtr output5, IntPtr device, uint format, out IntPtr duplication)
        {
            IntPtr d;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint*, IntPtr*, int>)Slot(output5, 26))(
                output5, device, 0, 1, &format, &d);
            duplication = d;
            return hr;
        }

        public static DxgiOutduplDesc GetDuplicationDesc(IntPtr duplication)
        {
            DxgiOutduplDesc d;
            ((delegate* unmanaged[Stdcall]<IntPtr, DxgiOutduplDesc*, void>)Slot(duplication, 7))(duplication, &d);
            return d;
        }

        public static int AcquireNextFrame(IntPtr duplication, uint timeoutMs, out DxgiOutduplFrameInfo info, out IntPtr resource)
        {
            DxgiOutduplFrameInfo i;
            IntPtr r;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, DxgiOutduplFrameInfo*, IntPtr*, int>)Slot(duplication, 8))(
                duplication, timeoutMs, &i, &r);
            info = i;
            resource = r;
            return hr;
        }

        public static int ReleaseFrame(IntPtr duplication) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(duplication, 14))(duplication);
    }

    internal static class D3D11
    {
        public static int CreateTexture2D(IntPtr device, in D3D11Texture2DDesc desc, D3D11SubresourceData* initialData, out IntPtr texture)
        {
            IntPtr t;
            fixed (D3D11Texture2DDesc* d = &desc)
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11Texture2DDesc*, D3D11SubresourceData*, IntPtr*, int>)Slot(device, 5))(
                    device, d, initialData, &t);
                texture = t;
                return hr;
            }
        }

        public static int Map(IntPtr context, IntPtr resource, uint mapType, out D3D11MappedSubresource mapped)
        {
            D3D11MappedSubresource m;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, D3D11MappedSubresource*, int>)Slot(context, 14))(
                context, resource, 0, mapType, 0, &m);
            mapped = m;
            return hr;
        }

        public static void Unmap(IntPtr context, IntPtr resource) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(context, 15))(context, resource, 0);

        public static void CopySubresourceRegion(IntPtr context, IntPtr destination, IntPtr source, in D3D11Box box)
        {
            fixed (D3D11Box* b = &box)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, D3D11Box*, void>)Slot(context, 46))(
                    context, destination, 0, 0, 0, 0, source, 0, b);
            }
        }

        public static void CopyResource(IntPtr context, IntPtr destination, IntPtr source) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(context, 47))(context, destination, source);

        /// <summary>ID3D11Multithread::SetMultithreadProtected: o Media Foundation usa o dispositivo em outras threads.</summary>
        public static void SetMultithreadProtected(IntPtr multithread, bool enable) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(multithread, 5))(multithread, enable ? 1 : 0);
    }

    internal static class Mf
    {
        public static int SetUINT32(IntPtr attributes, in Guid key, uint value)
        {
            fixed (Guid* k = &key)
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Slot(attributes, 21))(attributes, k, value);
        }

        public static int SetUINT64(IntPtr attributes, in Guid key, ulong value)
        {
            fixed (Guid* k = &key)
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Slot(attributes, 22))(attributes, k, value);
        }

        public static int SetGUID(IntPtr attributes, in Guid key, in Guid value)
        {
            fixed (Guid* k = &key)
            fixed (Guid* v = &value)
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(attributes, 24))(attributes, k, v);
        }

        public static int SetUnknown(IntPtr attributes, in Guid key, IntPtr value)
        {
            fixed (Guid* k = &key)
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr, int>)Slot(attributes, 27))(attributes, k, value);
        }

        public static int SetSampleTime(IntPtr sample, long time) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(sample, 36))(sample, time);

        public static int SetSampleDuration(IntPtr sample, long duration) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(sample, 38))(sample, duration);

        public static int AddBuffer(IntPtr sample, IntPtr buffer) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(sample, 42))(sample, buffer);

        public static int Lock(IntPtr buffer, out byte* data)
        {
            byte* d;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &d, null, null);
            data = d;
            return hr;
        }

        public static int Unlock(IntPtr buffer) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, 4))(buffer);

        public static int SetCurrentLength(IntPtr buffer, uint length) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Slot(buffer, 6))(buffer, length);

        /// <summary>IMF2DBuffer::GetContiguousLength (tamanho da imagem de uma textura DXGI).</summary>
        public static int GetContiguousLength(IntPtr buffer2D, out uint length)
        {
            uint l;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(buffer2D, 7))(buffer2D, &l);
            length = l;
            return hr;
        }

        /// <summary>IMFDXGIDeviceManager::ResetDevice.</summary>
        public static int ResetDevice(IntPtr manager, IntPtr device, uint resetToken) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int>)Slot(manager, 7))(manager, device, resetToken);

        // IMFSinkWriter

        public static int AddStream(IntPtr writer, IntPtr mediaType, out uint streamIndex)
        {
            uint s;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Slot(writer, 3))(writer, mediaType, &s);
            streamIndex = s;
            return hr;
        }

        public static int SetInputMediaType(IntPtr writer, uint streamIndex, IntPtr mediaType) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Slot(writer, 4))(writer, streamIndex, mediaType, IntPtr.Zero);

        public static int BeginWriting(IntPtr writer) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(writer, 5))(writer);

        public static int WriteSample(IntPtr writer, uint streamIndex, IntPtr sample) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Slot(writer, 6))(writer, streamIndex, sample);

        public static int FinalizeWriter(IntPtr writer) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(writer, 11))(writer);
    }

    internal static class Wasapi
    {
        /// <summary>
        /// IMMDeviceEnumerator::GetDefaultAudioEndpoint, papel "console": a saída de som padrão
        /// (<paramref name="capture"/> falso) ou o microfone padrão.
        /// </summary>
        public static int GetDefaultEndpoint(IntPtr enumerator, bool capture, out IntPtr device)
        {
            IntPtr d;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, int, IntPtr*, int>)Slot(enumerator, 4))(enumerator, capture ? 1 : 0, 0, &d);
            device = d;
            return hr;
        }

        /// <summary>IMMDevice::GetId, para perceber a troca do dispositivo padrão (fone conectado no meio da gravação).</summary>
        public static string? GetId(IntPtr device)
        {
            char* id;
            if (((delegate* unmanaged[Stdcall]<IntPtr, char**, int>)Slot(device, 5))(device, &id) < 0) return null;
            var text = new string(id);
            CoTaskMemFree((IntPtr)id);
            return text;
        }

        public static int Activate(IntPtr device, in Guid iid, out IntPtr instance)
        {
            IntPtr i;
            fixed (Guid* g = &iid)
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)Slot(device, 3))(
                    device, g, CLSCTX_ALL, IntPtr.Zero, &i);
                instance = i;
                return hr;
            }
        }

        // IAudioClient

        public static int Initialize(IntPtr client, uint shareMode, uint streamFlags, long bufferDuration, IntPtr format) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, long, long, IntPtr, IntPtr, int>)Slot(client, 3))(
                client, shareMode, streamFlags, bufferDuration, 0, format, IntPtr.Zero);

        public static int GetMixFormat(IntPtr client, out IntPtr format)
        {
            IntPtr f;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(client, 8))(client, &f);
            format = f;
            return hr;
        }

        public static int Start(IntPtr client) => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(client, 10))(client);

        public static int Stop(IntPtr client) => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(client, 11))(client);

        public static int GetService(IntPtr client, in Guid iid, out IntPtr service)
        {
            IntPtr s;
            fixed (Guid* g = &iid)
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(client, 14))(client, g, &s);
                service = s;
                return hr;
            }
        }

        // IAudioCaptureClient

        public static int GetBuffer(IntPtr capture, out byte* data, out uint frames, out uint flags, out ulong qpcPosition)
        {
            byte* d;
            uint n, f;
            ulong devicePosition, qpc;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, ulong*, ulong*, int>)Slot(capture, 3))(
                capture, &d, &n, &f, &devicePosition, &qpc);
            data = d;
            frames = n;
            flags = f;
            qpcPosition = qpc;
            return hr;
        }

        public static int ReleaseBuffer(IntPtr capture, uint frames) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Slot(capture, 4))(capture, frames);

        public static int GetNextPacketSize(IntPtr capture, out uint frames)
        {
            uint n;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(capture, 5))(capture, &n);
            frames = n;
            return hr;
        }
    }
}
