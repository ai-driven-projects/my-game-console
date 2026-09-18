using System.Diagnostics;
using MyGameConsole.Native;
using MyGameConsole.Services.Recording;
using static MyGameConsole.Native.NativeMethods;

namespace MyGameConsole.Services;

/// <summary>
/// Gravação da tela com o som do PC em MP4 (H.264 + AAC), para registrar uma partida sem programa extra.
/// Grava o monitor em que está a janela da frente (o jogo), na resolução dele, com o encoder da placa de vídeo
/// quando houver. Os arquivos vão para <see cref="Folder"/> (padrão: Vídeos\My Game Console).
///
/// Tudo roda numa thread própria: a imagem vem da Desktop Duplication (<see cref="DesktopDuplication"/>), o
/// som do loopback do WASAPI (<see cref="LoopbackAudio"/>) e os dois vão para o <see cref="Mp4Writer"/> na
/// mesma thread, intercalados pelo relógio (QPC) — é isso que mantém o som em sincronia com a imagem.
/// Os quadros saem em ritmo constante (repetindo a imagem quando a tela está parada), o que todo player aceita.
///
/// <see cref="StateChanged"/> chega sempre na thread da interface.
/// </summary>
public sealed class ScreenRecorderService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly SynchronizationContext? _ui;
    private readonly object _gate = new();

    private Thread? _thread;
    private volatile bool _stopRequested;
    private long _startedAt; // Stopwatch.GetTimestamp() do primeiro quadro

    /// <summary>Gravando agora.</summary>
    public bool IsRecording => _thread is not null;

    /// <summary>Arquivo da gravação em andamento, ou da última que terminou.</summary>
    public string? CurrentFile { get; private set; }

    /// <summary>Por que a última gravação parou sozinha (erro), ou nulo.</summary>
    public string? LastError { get; private set; }

    /// <summary>Tempo gravado até agora.</summary>
    public TimeSpan Elapsed => IsRecording && _startedAt != 0
        ? Stopwatch.GetElapsedTime(Interlocked.Read(ref _startedAt))
        : TimeSpan.Zero;

    /// <summary>Começou, terminou ou falhou (ver <see cref="IsRecording"/> e <see cref="LastError"/>).</summary>
    public event EventHandler? StateChanged;

    public ScreenRecorderService(SettingsService settings)
    {
        _settings = settings;
        _ui = SynchronizationContext.Current;
    }

    /// <summary>Pasta das gravações (a escolhida nas configurações, ou Vídeos\My Game Console).</summary>
    public string Folder
    {
        get
        {
            var custom = _settings.Current.RecordingFolder;
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "My Game Console");
        }
    }

    public void Toggle()
    {
        if (IsRecording) Stop();
        else Start();
    }

    /// <summary>
    /// Começa a gravar. Volta só depois que a captura e o arquivo estão prontos, e lança a exceção com o motivo
    /// se não der para gravar (o chamador mostra o aviso).
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_thread is not null) return;

            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"Gravação {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
            var options = new Options(
                path,
                Math.Clamp(_settings.Current.RecordingFramerate, 15, 60),
                _settings.Current.RecordingCaptureAudio,
                NativeMethods.GetForegroundWindow());

            Exception? startError = null;
            using var ready = new ManualResetEventSlim();
            _stopRequested = false;
            _startedAt = 0;
            LastError = null;
            CurrentFile = path;

            var thread = new Thread(() => Record(options, ready, e => startError = e))
            {
                IsBackground = true,
                Name = "Gravação: vídeo",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread = thread;
            thread.Start();
            ready.Wait();

            if (startError is not null)
            {
                thread.Join();
                _thread = null;
                throw new InvalidOperationException($"Não foi possível começar a gravar: {startError.Message}", startError);
            }
        }

        RaiseStateChanged();
    }

    /// <summary>Para e fecha o arquivo (espera o encoder terminar). Pode ser chamado de qualquer thread.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null) return;
            _stopRequested = true;
        }

        thread.Join(TimeSpan.FromSeconds(15));
    }

    private sealed record Options(string Path, int Fps, bool Audio, IntPtr Window);

    private void Record(Options options, ManualResetEventSlim ready, Action<Exception> startFailed)
    {
        int coHr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
        bool mfStarted = false;
        DesktopDuplication? screen = null;
        LoopbackAudio? audio = null;
        Mp4Writer? writer = null;
        bool started = false;

        try
        {
            Com.Check(MFStartup(MF_VERSION, 0), "Iniciar o Media Foundation");
            mfStarted = true;

            screen = new DesktopDuplication(options.Window);
            if (options.Audio) audio = new LoopbackAudio();
            writer = CreateWriter(options, screen, onGpu: screen.VideoSupport);

            var session = new Session(options.Fps, audio, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _startedAt, session.StartTimestamp);

            // O primeiro quadro ainda faz parte do "começar": se o driver recusar a textura direto no encoder,
            // o arquivo é refeito pelo caminho da memória, que funciona em qualquer placa.
            try
            {
                writer.WriteVideo(screen.Frame, 0, session.FrameDuration);
            }
            catch when (writer.OnGpu)
            {
                writer.Dispose();
                TryDelete(options.Path);
                writer = CreateWriter(options, screen, onGpu: false);
                writer.WriteVideo(screen.Frame, 0, session.FrameDuration);
            }

            started = true;
            ready.Set();

            RecordLoop(session, screen, writer);
            writer.Finish();
        }
        catch (Exception ex)
        {
            if (!started)
            {
                startFailed(ex);
            }
            else
            {
                LastError = ex.Message;
                try { writer?.Finish(); } catch { /* o arquivo pode ficar incompleto */ }
            }
        }
        finally
        {
            audio?.Dispose();
            writer?.Dispose();
            screen?.Dispose();
            if (mfStarted) MFShutdown();
            if (coHr >= 0) CoUninitialize();

            if (!started)
            {
                TryDelete(options.Path);
                ready.Set();
            }
            else
            {
                lock (_gate) _thread = null;
                RaiseStateChanged();
            }
        }
    }

    private static Mp4Writer CreateWriter(Options options, DesktopDuplication screen, bool onGpu) =>
        new(options.Path, screen.Device, screen.Context, screen.Width, screen.Height, options.Fps, options.Audio, onGpu);

    private void RecordLoop(Session session, DesktopDuplication screen, Mp4Writer writer)
    {
        long frame = 1;
        while (!_stopRequested)
        {
            long due = session.StartTime + frame * session.FrameDuration;
            long now = Session.Now();

            if (now < due)
            {
                // Enquanto espera a hora do próximo quadro, pega as imagens novas da tela.
                screen.TryUpdate((int)Math.Max(1, (due - now) / 10_000));
                continue;
            }

            // Atrasou demais (máquina ocupada): pula os quadros perdidos em vez de acumular atraso.
            if (now - due > session.FrameDuration * 4)
            {
                frame = (now - session.StartTime) / session.FrameDuration;
                due = session.StartTime + frame * session.FrameDuration;
            }

            session.PumpAudio(writer, now);
            writer.WriteVideo(screen.Frame, due - session.StartTime, session.FrameDuration);
            frame++;
        }

        session.PumpAudio(writer, Session.Now());
    }

    /// <summary>Relógio da gravação e o áudio já gravado, para alinhar os trechos de som com a imagem.</summary>
    private sealed class Session(int fps, LoopbackAudio? audio, long startTimestamp)
    {
        private const int Rate = Mp4Writer.AudioSampleRate;
        private const int Channels = Mp4Writer.AudioChannels;
        /// <summary>O loopback entrega o som com uns poucos ms de atraso: o silêncio só é preenchido até essa margem.</summary>
        private const long SilenceMarginFrames = Rate / 10;     // 100 ms
        private const long GapToleranceFrames = Rate / 50;      // 20 ms
        private const long OverlapToleranceFrames = Rate / 10;  // 100 ms

        private static readonly short[] Silence = new short[Rate / 10 * Channels];

        private long _audioFrames; // amostras (por canal) já gravadas

        public long StartTimestamp { get; } = startTimestamp;
        public long StartTime { get; } = ToHundredNs(startTimestamp);
        public long FrameDuration { get; } = 10_000_000L / fps;

        public static long Now() => ToHundredNs(Stopwatch.GetTimestamp());

        private static long ToHundredNs(long timestamp) =>
            (long)(timestamp * (10_000_000.0 / Stopwatch.Frequency));

        private long FramesAt(long time) => (time - StartTime) * Rate / 10_000_000;

        /// <summary>
        /// Grava o som que chegou até agora. Os trechos entram um atrás do outro; buracos (nada tocando) viram
        /// silêncio e sobreposições grandes são cortadas, então o som nunca se afasta da imagem.
        /// </summary>
        public void PumpAudio(Mp4Writer writer, long now)
        {
            if (!writer.HasAudio) return;

            while (audio is not null && audio.Chunks.TryDequeue(out var chunk))
            {
                long start = FramesAt(chunk.Time);
                ReadOnlySpan<short> samples = chunk.Samples;

                if (start > _audioFrames + GapToleranceFrames)
                {
                    WriteSilence(writer, start - _audioFrames);
                }
                else if (start < _audioFrames - OverlapToleranceFrames)
                {
                    long skip = _audioFrames - start;
                    if (skip * Channels >= samples.Length) continue;
                    samples = samples[(int)(skip * Channels)..];
                }

                Write(writer, samples);
            }

            // Nada tocando (ou sem captura de áudio): silêncio até perto de agora.
            long expected = FramesAt(now) - SilenceMarginFrames;
            if (expected > _audioFrames) WriteSilence(writer, expected - _audioFrames);
        }

        private void WriteSilence(Mp4Writer writer, long frames)
        {
            while (frames > 0)
            {
                int n = (int)Math.Min(frames, Silence.Length / Channels);
                Write(writer, Silence.AsSpan(0, n * Channels));
                frames -= n;
            }
        }

        private void Write(Mp4Writer writer, ReadOnlySpan<short> samples)
        {
            writer.WriteAudio(samples, _audioFrames * 10_000_000 / Rate);
            _audioFrames += samples.Length / Channels;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // arquivo preso: fica para trás, sem prejuízo
        }
    }

    private void RaiseStateChanged()
    {
        if (_ui is null) StateChanged?.Invoke(this, EventArgs.Empty);
        else _ui.Post(_ => StateChanged?.Invoke(this, EventArgs.Empty), null);
    }

    public void Dispose() => Stop();
}
