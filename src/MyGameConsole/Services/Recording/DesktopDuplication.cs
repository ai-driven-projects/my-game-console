using System.Diagnostics;
using MyGameConsole.Native;
using static MyGameConsole.Native.NativeMethods;

namespace MyGameConsole.Services.Recording;

/// <summary>
/// Imagem de um monitor pela Desktop Duplication do DXGI: a mesma que o Windows usa para a Área de Trabalho
/// Remota, feita na placa de vídeo e que pega jogos em tela cheia sem bordas e em tela cheia comum (com flip).
/// A última imagem fica sempre em <see cref="Frame"/>, uma textura do tamanho da gravação: quando a tela não
/// muda, o Windows não entrega quadro novo e a gravação repete o que já tem.
///
/// Perder a duplicação é normal (troca de resolução, jogo entrando em tela cheia exclusiva, tela do UAC ou de
/// bloqueio): a textura mantém a última imagem e a duplicação é refeita sozinha assim que possível.
/// </summary>
internal sealed unsafe class DesktopDuplication : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);

    private IntPtr _device;
    private IntPtr _context;
    private IntPtr _output;
    private IntPtr _duplication;
    private IntPtr _frame;
    private long _retryAt;

    /// <summary>Dispositivo Direct3D 11 do adaptador do monitor (o encoder usa o mesmo).</summary>
    public IntPtr Device => _device;
    public IntPtr Context => _context;

    /// <summary>Textura BGRA com a última imagem da tela, em <see cref="Width"/> × <see cref="Height"/>.</summary>
    public IntPtr Frame => _frame;

    /// <summary>Tamanho da gravação: o do monitor, arredondado para par (exigência do H.264).</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>O dispositivo aceita vídeo na placa (encoder por hardware recebendo a textura direto).</summary>
    public bool VideoSupport { get; }

    /// <summary>Duplica o monitor em que está a janela dada (o jogo em primeiro plano), ou o principal.</summary>
    public DesktopDuplication(IntPtr window)
    {
        try
        {
            var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTOPRIMARY);
            IntPtr adapter = FindOutput(monitor, out _output);
            try
            {
                // O dispositivo tem de ser do adaptador que exibe o monitor (em notebooks híbridos, a placa integrada).
                const int D3D_DRIVER_TYPE_UNKNOWN = 0;
                int hr = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT, IntPtr.Zero, 0,
                    D3D11_SDK_VERSION, out _device, out _, out _context);
                VideoSupport = hr >= 0;
                if (hr < 0)
                {
                    hr = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                        IntPtr.Zero, 0, D3D11_SDK_VERSION, out _device, out _, out _context);
                }
                Com.Check(hr, "Criar o dispositivo Direct3D 11");
            }
            finally
            {
                Com.Release(ref adapter);
            }

            if (Com.QueryInterface(_device, Iid.D3D11Multithread, out var multithread) >= 0)
            {
                D3D11.SetMultithreadProtected(multithread, true);
                Com.Release(ref multithread);
            }

            int dupHr = Duplicate();
            if (dupHr >= 0)
            {
                var desc = Dxgi.GetDuplicationDesc(_duplication);
                Width = (int)desc.Width & ~1;
                Height = (int)desc.Height & ~1;
            }
            else if (dupHr is E_ACCESSDENIED or DXGI_ERROR_ACCESS_LOST)
            {
                // Tela bloqueada ou do UAC agora: começa preto e passa a gravar quando a área de trabalho voltar.
                Com.Check(Dxgi.GetOutputDesc(_output, out var outputDesc), "Ler o tamanho do monitor");
                Width = (outputDesc.Right - outputDesc.Left) & ~1;
                Height = (outputDesc.Bottom - outputDesc.Top) & ~1;
            }
            else
            {
                throw new InvalidOperationException(dupHr == DXGI_ERROR_UNSUPPORTED
                    ? "O Windows não permite capturar esta tela (Desktop Duplication indisponível neste adaptador de vídeo)."
                    : $"Não foi possível capturar a tela (0x{dupHr:X8}).");
            }

            // Textura da última imagem, começando preta (o conteúdo inicial de uma textura é indefinido).
            var zeros = new byte[Width * Height * 4];
            fixed (byte* p = zeros)
            {
                var init = new D3D11SubresourceData { SysMem = (IntPtr)p, SysMemPitch = (uint)Width * 4 };
                var texDesc = TextureDesc(Width, Height, D3D11_USAGE_DEFAULT, D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE, 0);
                Com.Check(D3D11.CreateTexture2D(_device, texDesc, &init, out _frame), "Criar a textura da imagem");
            }

            TryUpdate(200); // já começa com a tela atual, não com preto
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static D3D11Texture2DDesc TextureDesc(int width, int height, uint usage, uint bind, uint cpuAccess) => new()
    {
        Width = (uint)width,
        Height = (uint)height,
        MipLevels = 1,
        ArraySize = 1,
        Format = DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleCount = 1,
        Usage = usage,
        BindFlags = bind,
        CPUAccessFlags = cpuAccess,
    };

    /// <summary>Acha a saída (monitor) no DXGI e devolve o adaptador dela; na falta, o primeiro monitor ligado.</summary>
    private static IntPtr FindOutput(IntPtr monitor, out IntPtr output)
    {
        Com.Check(CreateDXGIFactory1(Iid.DxgiFactory1, out var factory), "Abrir o DXGI");
        IntPtr fallbackAdapter = IntPtr.Zero, fallbackOutput = IntPtr.Zero;
        try
        {
            for (uint a = 0; Dxgi.EnumAdapters1(factory, a, out var adapter) >= 0; a++)
            {
                bool keepAdapter = false;
                for (uint o = 0; Dxgi.EnumOutputs(adapter, o, out var candidate) >= 0; o++)
                {
                    if (Dxgi.GetOutputDesc(candidate, out var desc) >= 0 && desc.AttachedToDesktop != 0)
                    {
                        if (desc.Monitor == monitor)
                        {
                            if (fallbackAdapter == adapter) fallbackAdapter = IntPtr.Zero; // é o mesmo: fica com quem chama
                            Com.Release(ref fallbackAdapter);
                            Com.Release(ref fallbackOutput);
                            output = candidate;
                            return adapter;
                        }

                        if (fallbackOutput == IntPtr.Zero)
                        {
                            fallbackOutput = candidate;
                            fallbackAdapter = adapter;
                            keepAdapter = true;
                            continue;
                        }
                    }

                    Com.Release(ref candidate);
                }

                if (!keepAdapter) Com.Release(ref adapter);
            }
        }
        finally
        {
            Com.Release(ref factory);
        }

        if (fallbackOutput == IntPtr.Zero) throw new InvalidOperationException("Nenhum monitor encontrado para gravar.");
        output = fallbackOutput;
        return fallbackAdapter;
    }

    /// <summary>(Re)cria a duplicação da saída. Devolve o HRESULT; em erro, <see cref="_duplication"/> fica vazio.</summary>
    private int Duplicate()
    {
        Com.Release(ref _duplication);

        // IDXGIOutput5 (Windows 10 1703+) entrega em BGRA 8 bits mesmo com o HDR ligado; senão, IDXGIOutput1.
        if (Com.QueryInterface(_output, Iid.DxgiOutput5, out var output5) >= 0)
        {
            int hr5 = Dxgi.DuplicateOutput1(output5, _device, DXGI_FORMAT_B8G8R8A8_UNORM, out _duplication);
            Com.Release(ref output5);
            if (hr5 >= 0) return hr5;
            _duplication = IntPtr.Zero;
        }

        Com.Check(Com.QueryInterface(_output, Iid.DxgiOutput1, out var output1), "Abrir o monitor (IDXGIOutput1)");
        int hr = Dxgi.DuplicateOutput(output1, _device, out _duplication);
        Com.Release(ref output1);
        if (hr < 0) _duplication = IntPtr.Zero;
        return hr;
    }

    /// <summary>
    /// Espera até <paramref name="timeoutMs"/> por uma imagem nova e a copia para <see cref="Frame"/>.
    /// Devolve se chegou imagem nova. Sem duplicação (perdida), só espera e tenta refazê-la de tempos em tempos.
    /// </summary>
    public bool TryUpdate(int timeoutMs)
    {
        if (_duplication == IntPtr.Zero)
        {
            if (Stopwatch.GetTimestamp() < _retryAt)
            {
                Thread.Sleep(Math.Max(1, timeoutMs));
                return false;
            }

            if (Duplicate() < 0)
            {
                _retryAt = Stopwatch.GetTimestamp() + (long)(RetryInterval.TotalSeconds * Stopwatch.Frequency);
                Thread.Sleep(Math.Max(1, timeoutMs));
                return false;
            }
        }

        int hr = Dxgi.AcquireNextFrame(_duplication, (uint)Math.Max(0, timeoutMs), out var info, out var resource);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) return false;
        if (hr < 0)
        {
            // Perdeu a duplicação (resolução, tela cheia exclusiva, UAC, bloqueio): refaz na próxima volta.
            Com.Release(ref _duplication);
            return false;
        }

        bool updated = false;
        try
        {
            // LastPresentTime zero: só o mouse mexeu, a imagem é a mesma.
            if (info.LastPresentTime != 0 && Com.QueryInterface(resource, Iid.D3D11Texture2D, out var texture) >= 0)
            {
                CopyToFrame(texture);
                Com.Release(ref texture);
                updated = true;
            }
        }
        finally
        {
            Com.Release(ref resource);
            Dxgi.ReleaseFrame(_duplication);
        }

        return updated;
    }

    private void CopyToFrame(IntPtr texture)
    {
        var desc = Dxgi.GetDuplicationDesc(_duplication);
        // Se a resolução mudou no meio (jogo trocando de modo), grava a parte que cabe; o resto fica como estava.
        var box = new D3D11Box
        {
            Right = Math.Min(desc.Width, (uint)Width),
            Bottom = Math.Min(desc.Height, (uint)Height),
            Back = 1,
        };
        D3D11.CopySubresourceRegion(_context, _frame, texture, box);
    }

    public void Dispose()
    {
        Com.Release(ref _duplication);
        Com.Release(ref _frame);
        Com.Release(ref _output);
        Com.Release(ref _context);
        Com.Release(ref _device);
    }
}
