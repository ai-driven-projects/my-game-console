using static MyGameConsole.Native.NativeMethods;

namespace MyGameConsole.Services.Recording;

/// <summary>
/// Arquivo MP4 (H.264 + AAC) pelo Sink Writer do Media Foundation, que escolhe sozinho o encoder da placa de
/// vídeo quando existe (NVENC, Quick Sync, AMF) e cai no encoder por software do Windows quando não.
///
/// Dois caminhos para a imagem, escolhidos na criação:
/// - na placa (<see cref="OnGpu"/>): a textura vai direto para o encoder, que converte as cores na GPU;
/// - pela memória: a textura é lida para a RAM e convertida pelo Media Foundation. Mais lento, mas funciona em
///   qualquer placa — é a reserva quando o driver não aceita o primeiro caminho.
///
/// Tempos em unidades de 100 ns, contados do início da gravação. Uma thread só: não é seguro chamar de duas.
/// </summary>
internal sealed unsafe class Mp4Writer : IDisposable
{
    public const int AudioSampleRate = 48000;
    public const int AudioChannels = 2;

    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private readonly int _width;
    private readonly int _height;
    private IntPtr _writer;
    private IntPtr _manager;
    private IntPtr _staging;
    private readonly uint _videoStream;
    private readonly uint _audioStream = uint.MaxValue;
    private bool _finished;

    public bool OnGpu { get; }
    public bool HasAudio => _audioStream != uint.MaxValue;

    public Mp4Writer(string path, IntPtr device, IntPtr context, int width, int height, int fps, bool audio, bool onGpu)
    {
        _device = device;
        _context = context;
        _width = width;
        _height = height;
        OnGpu = onGpu;

        IntPtr attributes = IntPtr.Zero;
        try
        {
            Com.Check(MFCreateAttributes(out attributes, 4), "Criar os atributos do gravador");
            Mf.SetUINT32(attributes, MfGuid.ReadwriteEnableHardwareTransforms, 1);
            Mf.SetGUID(attributes, MfGuid.TranscodeContainerType, MfGuid.ContainerTypeMpeg4);
            if (onGpu)
            {
                Com.Check(MFCreateDXGIDeviceManager(out uint token, out _manager), "Criar o gerenciador de vídeo");
                Com.Check(Mf.ResetDevice(_manager, device, token), "Ligar o Direct3D ao Media Foundation");
                Mf.SetUnknown(attributes, MfGuid.SinkWriterD3DManager, _manager);
            }

            Com.Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out _writer), "Criar o arquivo de vídeo");

            // Vídeo: H.264 High, taxa de bits pelo tamanho e quadros por segundo (≈ 15 Mb/s em 1080p60).
            long bitrate = Math.Clamp((long)(width * (long)height * fps * 0.12), 4_000_000, 60_000_000);
            _videoStream = AddStream(
                t =>
                {
                    Mf.SetGUID(t, MfGuid.MajorType, MfGuid.MediaTypeVideo);
                    Mf.SetGUID(t, MfGuid.Subtype, MfGuid.VideoFormatH264);
                    Mf.SetUINT32(t, MfGuid.AvgBitrate, (uint)bitrate);
                    Mf.SetUINT32(t, MfGuid.Mpeg2Profile, 100); // eAVEncH264VProfile_High
                    SetVideoFormat(t, fps);
                },
                t =>
                {
                    Mf.SetGUID(t, MfGuid.MajorType, MfGuid.MediaTypeVideo);
                    Mf.SetGUID(t, MfGuid.Subtype, MfGuid.VideoFormatRgb32);
                    Mf.SetUINT32(t, MfGuid.DefaultStride, (uint)(width * 4)); // positivo: de cima para baixo
                    Mf.SetUINT32(t, MfGuid.AllSamplesIndependent, 1);
                    SetVideoFormat(t, fps);
                },
                "vídeo");

            if (audio)
            {
                // Áudio: AAC 48 kHz estéreo a 192 kb/s, a partir de PCM de 16 bits.
                _audioStream = AddStream(
                    t =>
                    {
                        Mf.SetGUID(t, MfGuid.MajorType, MfGuid.MediaTypeAudio);
                        Mf.SetGUID(t, MfGuid.Subtype, MfGuid.AudioFormatAac);
                        SetAudioFormat(t);
                        Mf.SetUINT32(t, MfGuid.AudioAvgBytesPerSecond, 24000);
                    },
                    t =>
                    {
                        Mf.SetGUID(t, MfGuid.MajorType, MfGuid.MediaTypeAudio);
                        Mf.SetGUID(t, MfGuid.Subtype, MfGuid.AudioFormatPcm);
                        SetAudioFormat(t);
                        Mf.SetUINT32(t, MfGuid.AudioBlockAlignment, AudioChannels * 2);
                        Mf.SetUINT32(t, MfGuid.AudioAvgBytesPerSecond, AudioSampleRate * AudioChannels * 2);
                    },
                    "áudio");
            }

            if (!onGpu)
            {
                var desc = DesktopDuplication.TextureDesc(width, height, D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_READ);
                Com.Check(D3D11.CreateTexture2D(device, desc, null, out _staging), "Criar a textura de leitura");
            }

            Com.Check(Mf.BeginWriting(_writer), "Começar a gravar o arquivo");
        }
        catch
        {
            Com.Release(ref attributes);
            Dispose();
            throw;
        }

        Com.Release(ref attributes);

        void SetVideoFormat(IntPtr t, int rate)
        {
            Mf.SetUINT32(t, MfGuid.InterlaceMode, 2); // MFVideoInterlace_Progressive
            Mf.SetUINT64(t, MfGuid.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
            Mf.SetUINT64(t, MfGuid.FrameRate, ((ulong)(uint)rate << 32) | 1);
            Mf.SetUINT64(t, MfGuid.PixelAspectRatio, (1UL << 32) | 1);
        }

        static void SetAudioFormat(IntPtr t)
        {
            Mf.SetUINT32(t, MfGuid.AudioNumChannels, AudioChannels);
            Mf.SetUINT32(t, MfGuid.AudioSamplesPerSecond, AudioSampleRate);
            Mf.SetUINT32(t, MfGuid.AudioBitsPerSample, 16);
        }
    }

    private uint AddStream(Action<IntPtr> output, Action<IntPtr> input, string what)
    {
        IntPtr outType = IntPtr.Zero, inType = IntPtr.Zero;
        try
        {
            Com.Check(MFCreateMediaType(out outType), "Criar o formato de " + what);
            output(outType);
            Com.Check(Mf.AddStream(_writer, outType, out uint stream), "Configurar o encoder de " + what);

            Com.Check(MFCreateMediaType(out inType), "Criar o formato de " + what);
            input(inType);
            Com.Check(Mf.SetInputMediaType(_writer, stream, inType), "Configurar a entrada de " + what);
            return stream;
        }
        finally
        {
            Com.Release(ref outType);
            Com.Release(ref inType);
        }
    }

    /// <summary>Grava a textura (BGRA, do tamanho da gravação) como um quadro.</summary>
    public void WriteVideo(IntPtr frame, long time, long duration)
    {
        IntPtr buffer = IntPtr.Zero, texture = IntPtr.Zero;
        try
        {
            if (OnGpu)
            {
                // Cópia própria para cada quadro: o encoder trabalha em paralelo e a textura da tela muda.
                var desc = DesktopDuplication.TextureDesc(_width, _height, D3D11_USAGE_DEFAULT,
                    D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE, 0);
                Com.Check(D3D11.CreateTexture2D(_device, desc, null, out texture), "Criar a textura do quadro");
                D3D11.CopyResource(_context, texture, frame);
                Com.Check(MFCreateDXGISurfaceBuffer(Iid.D3D11Texture2D, texture, 0, false, out buffer), "Preparar o quadro");

                if (Com.QueryInterface(buffer, Iid.Mf2DBuffer, out var buffer2D) >= 0)
                {
                    if (Mf.GetContiguousLength(buffer2D, out uint length) >= 0) Mf.SetCurrentLength(buffer, length);
                    Com.Release(ref buffer2D);
                }
            }
            else
            {
                D3D11.CopyResource(_context, _staging, frame);
                Com.Check(D3D11.Map(_context, _staging, D3D11_MAP_READ, out var mapped), "Ler o quadro");
                try
                {
                    int rowBytes = _width * 4;
                    uint size = (uint)(rowBytes * _height);
                    Com.Check(MFCreateMemoryBuffer(size, out buffer), "Criar o quadro");
                    Com.Check(Mf.Lock(buffer, out byte* dst), "Preparar o quadro");
                    byte* src = (byte*)mapped.Data;
                    for (int y = 0; y < _height; y++)
                    {
                        Buffer.MemoryCopy(src + y * mapped.RowPitch, dst + y * rowBytes, rowBytes, rowBytes);
                    }
                    Mf.Unlock(buffer);
                    Mf.SetCurrentLength(buffer, size);
                }
                finally
                {
                    D3D11.Unmap(_context, _staging);
                }
            }

            WriteSample(_videoStream, buffer, time, duration, "Gravar o quadro");
        }
        finally
        {
            Com.Release(ref buffer);
            Com.Release(ref texture);
        }
    }

    /// <summary>Grava PCM 16 bits estéreo 48 kHz, com os dois canais intercalados (E, D, E, D...).</summary>
    public void WriteAudio(ReadOnlySpan<short> samples, long time)
    {
        if (!HasAudio || samples.IsEmpty) return;

        uint size = (uint)(samples.Length * 2);
        IntPtr buffer = IntPtr.Zero;
        try
        {
            Com.Check(MFCreateMemoryBuffer(size, out buffer), "Criar o bloco de áudio");
            Com.Check(Mf.Lock(buffer, out byte* dst), "Preparar o áudio");
            samples.CopyTo(new Span<short>(dst, samples.Length));
            Mf.Unlock(buffer);
            Mf.SetCurrentLength(buffer, size);

            long duration = samples.Length / AudioChannels * 10_000_000L / AudioSampleRate;
            WriteSample(_audioStream, buffer, time, duration, "Gravar o áudio");
        }
        finally
        {
            Com.Release(ref buffer);
        }
    }

    private void WriteSample(uint stream, IntPtr buffer, long time, long duration, string what)
    {
        IntPtr sample = IntPtr.Zero;
        try
        {
            Com.Check(MFCreateSample(out sample), what);
            Com.Check(Mf.AddBuffer(sample, buffer), what);
            Mf.SetSampleTime(sample, time);
            Mf.SetSampleDuration(sample, duration);
            Com.Check(Mf.WriteSample(_writer, stream, sample), what);
        }
        finally
        {
            Com.Release(ref sample);
        }
    }

    /// <summary>Fecha o MP4 (grava o índice). Sem isso o arquivo não abre; por isso a gravação sempre termina por aqui.</summary>
    public void Finish()
    {
        if (_finished || _writer == IntPtr.Zero) return;
        _finished = true;
        Com.Check(Mf.FinalizeWriter(_writer), "Finalizar o arquivo de vídeo");
    }

    public void Dispose()
    {
        Com.Release(ref _writer);
        Com.Release(ref _manager);
        Com.Release(ref _staging);
    }
}
