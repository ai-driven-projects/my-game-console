using System.Collections.Concurrent;
using static MyGameConsole.Native.NativeMethods;

namespace MyGameConsole.Services.Recording;

/// <summary>Um trecho de som: PCM 16 bits estéreo 48 kHz e o instante (100 ns, relógio QPC) da primeira amostra.</summary>
internal readonly record struct AudioChunk(long Time, short[] Samples);

/// <summary>
/// Som que está saindo no PC (o que se ouve nos alto-falantes ou no fone), pelo modo loopback do WASAPI.
/// Roda numa thread própria e entrega trechos já em PCM 16 bits estéreo 48 kHz em <see cref="Chunks"/>,
/// cada um com o horário em que foi tocado — é por ele que a gravação alinha o som com a imagem.
///
/// Sem nada tocando, o Windows não entrega nada (nem silêncio): quem grava completa os buracos.
/// Se a saída de som padrão mudar (fone conectado, por exemplo), a captura passa para a nova sozinha.
/// </summary>
internal sealed unsafe class LoopbackAudio : IDisposable
{
    private static readonly TimeSpan DeviceCheckInterval = TimeSpan.FromSeconds(2);

    private readonly Thread _thread;
    private volatile bool _stop;

    private IntPtr _enumerator;
    private IntPtr _client;
    private IntPtr _capture;
    private string? _deviceId;

    // Formato do Windows (normalmente float 32 bits, 48 kHz) e o estado da conversão para 48 kHz.
    private int _channels;
    private int _rate;
    private int _bits;
    private bool _float;
    private double _resamplePos;
    private float _prevL, _prevR;

    public ConcurrentQueue<AudioChunk> Chunks { get; } = new();

    public LoopbackAudio()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Gravação: áudio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Run()
    {
        int coHr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
        try
        {
            if (CoCreateInstance(Iid.MMDeviceEnumeratorClass, IntPtr.Zero, CLSCTX_ALL, Iid.MMDeviceEnumerator, out _enumerator) < 0) return;

            var nextDeviceCheck = DateTime.UtcNow;
            while (!_stop)
            {
                if (DateTime.UtcNow >= nextDeviceCheck)
                {
                    nextDeviceCheck = DateTime.UtcNow + DeviceCheckInterval;
                    if (_client == IntPtr.Zero || CurrentDefaultId() != _deviceId) Open();
                }

                if (_capture != IntPtr.Zero && !Drain()) Close(); // dispositivo sumiu: reabre na próxima checagem

                Thread.Sleep(10);
            }
        }
        catch
        {
            // sem áudio: a gravação segue com silêncio
        }
        finally
        {
            Close();
            Com.Release(ref _enumerator);
            if (coHr >= 0) CoUninitialize();
        }
    }

    private string? CurrentDefaultId()
    {
        if (Wasapi.GetDefaultRenderEndpoint(_enumerator, out var device) < 0) return null;
        var id = Wasapi.GetId(device);
        Com.Release(ref device);
        return id;
    }

    private void Open()
    {
        Close();
        IntPtr device = IntPtr.Zero, format = IntPtr.Zero;
        try
        {
            if (Wasapi.GetDefaultRenderEndpoint(_enumerator, out device) < 0) return;
            _deviceId = Wasapi.GetId(device);
            if (Wasapi.Activate(device, Iid.AudioClient, out _client) < 0) return;
            if (Wasapi.GetMixFormat(_client, out format) < 0) return;

            byte* f = (byte*)format;
            ushort tag = *(ushort*)f;
            _channels = *(ushort*)(f + 2);
            _rate = (int)*(uint*)(f + 4);
            _bits = *(ushort*)(f + 14);
            // WAVE_FORMAT_EXTENSIBLE: o tipo real está no SubFormat (1 = PCM, 3 = float).
            uint kind = tag == 0xFFFE ? *(uint*)(f + 24) : tag;
            _float = kind == 3;
            if (kind is not (1 or 3) || _channels < 1 || _rate < 8000) { Close(); return; }

            const long bufferDuration = 2_000_000; // 200 ms
            if (Wasapi.Initialize(_client, AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK, bufferDuration, format) < 0
                || Wasapi.GetService(_client, Iid.AudioCaptureClient, out _capture) < 0
                || Wasapi.Start(_client) < 0)
            {
                Close();
                return;
            }

            _resamplePos = 0;
            _prevL = _prevR = 0;
        }
        finally
        {
            if (format != IntPtr.Zero) CoTaskMemFree(format);
            Com.Release(ref device);
        }
    }

    private void Close()
    {
        if (_client != IntPtr.Zero) Wasapi.Stop(_client);
        Com.Release(ref _capture);
        Com.Release(ref _client);
    }

    /// <summary>Lê todos os pacotes prontos. Falso se o dispositivo deixou de funcionar.</summary>
    private bool Drain()
    {
        while (true)
        {
            if (Wasapi.GetNextPacketSize(_capture, out uint packet) < 0) return false;
            if (packet == 0) return true;

            if (Wasapi.GetBuffer(_capture, out byte* data, out uint frames, out uint flags, out ulong qpc) < 0) return false;
            try
            {
                if (frames > 0)
                {
                    bool silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                    var samples = Convert(silent ? null : data, (int)frames);
                    if (samples.Length > 0) Chunks.Enqueue(new AudioChunk((long)qpc, samples));
                }
            }
            finally
            {
                Wasapi.ReleaseBuffer(_capture, frames);
            }
        }
    }

    /// <summary>Converte um pacote do formato do Windows para PCM 16 bits estéreo 48 kHz.</summary>
    private short[] Convert(byte* data, int frames)
    {
        int bytesPerSample = _bits / 8;
        int stride = bytesPerSample * _channels;

        float Sample(int frame, int channel)
        {
            if (data is null) return 0f;
            if (channel >= _channels) channel = 0; // mono: o mesmo nos dois lados
            byte* p = data + frame * stride + channel * bytesPerSample;
            return _float
                ? (_bits == 64 ? (float)*(double*)p : *(float*)p)
                : _bits switch
                {
                    16 => *(short*)p / 32768f,
                    24 => ((p[0] << 8) | (p[1] << 16) | (p[2] << 24)) / 2147483648f,
                    32 => *(int*)p / 2147483648f,
                    _ => 0f,
                };
        }

        static short ToPcm(float v) => (short)Math.Clamp((int)MathF.Round(v * 32767f), short.MinValue, short.MaxValue);

        if (_rate == Mp4Writer.AudioSampleRate)
        {
            var direct = new short[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                direct[i * 2] = ToPcm(Sample(i, 0));
                direct[i * 2 + 1] = ToPcm(Sample(i, 1));
            }
            return direct;
        }

        // Outra taxa (44,1 kHz, 96 kHz...): interpolação linear, continuando de um pacote para o outro.
        double step = _rate / (double)Mp4Writer.AudioSampleRate;
        var output = new List<short>((int)(frames / step) * 2 + 4);
        while (_resamplePos < frames - 1)
        {
            int i = (int)Math.Floor(_resamplePos);
            float t = (float)(_resamplePos - i);
            float l0 = i < 0 ? _prevL : Sample(i, 0), r0 = i < 0 ? _prevR : Sample(i, 1);
            float l1 = Sample(i + 1, 0), r1 = Sample(i + 1, 1);
            output.Add(ToPcm(l0 + (l1 - l0) * t));
            output.Add(ToPcm(r0 + (r1 - r0) * t));
            _resamplePos += step;
        }

        _resamplePos -= frames;
        _prevL = Sample(frames - 1, 0);
        _prevR = Sample(frames - 1, 1);
        return output.ToArray();
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(2000);
    }
}
