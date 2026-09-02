using System.Diagnostics;
using System.Net.Sockets;
using Process = System.Diagnostics.Process;

namespace MicaSound.ApiHost.ProcessHost;

/// <summary>进程生命周期状态。</summary>
public enum ApiProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

/// <summary>
/// 内嵌网易云音乐 API 进程的受管子进程。
/// 负责：探测可执行文件 → 选空闲端口 → 拉起 node app.js → 健康检查 → 崩溃自动重启 → 优雅退出。
/// </summary>
public sealed class NcmApiProcessHost : IAsyncDisposable
{
    public event Action<ApiProcessState>? StateChanged;

    private readonly NcmApiHostOptions _options;
    private Process? _process;
    private CancellationTokenSource? _lifeCts;
    private Task? _supervisorTask;
    private ApiProcessState _state = ApiProcessState.Stopped;
    private readonly object _gate = new();

    public ApiProcessState State { get { lock (_gate) return _state; } }
    public Uri? BaseAddress { get; private set; }

    public NcmApiProcessHost(NcmApiHostOptions? options = null)
    {
        _options = options ?? NcmApiHostOptions.CreateDefault();
    }

    /// <summary>启动子进程并进入监督循环。</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        SetState(ApiProcessState.Starting);

        BaseAddress = new Uri($"http://127.0.0.1:{GetFreePort()}");
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _lifeCts = cts;

        // 首次拉起
        Launch();

        // 监督循环：健康检查 + 崩溃重启
        _supervisorTask = Task.Run(() => SuperviseAsync(cts.Token), ct);
        await WaitUntilRunningAsync(_options.StartTimeoutMs, ct).ConfigureAwait(false);
    }

    /// <summary>构造启动命令。端口与回环绑定通过环境变量注入(server.js 读取 process.env.PORT/HOST)。</summary>
    private (string exe, string workDir, string entryJs) ResolveCommand()
    {
        return (_options.NodeExe, _options.ApiWorkDir, _options.ApiEntryJs);
    }

    private void Launch()
    {
        var (exe, workDir, entryJs) = ResolveCommand();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(entryJs);
        // 通过环境变量注入端口与回环主机,确保 Windows 下生效
        psi.Environment["PORT"] = BaseAddress!.Port.ToString();
        psi.Environment["HOST"] = BaseAddress.Host;
        _process = Process.Start(psi);
        if (_process is null) throw new InvalidOperationException("无法启动 API 子进程");
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        var failCount = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_process is null || _process.HasExited)
                {
                    if (failCount >= _options.MaxRestartAttempts)
                    {
                        SetState(ApiProcessState.Failed);
                        return;
                    }
                    failCount++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, failCount), 30)), ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return;
                    if (State is ApiProcessState.Running or ApiProcessState.Starting)
                    {
                        Launch();
                        continue;
                    }
                }

                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    var resp = await client.GetAsync(new Uri(BaseAddress!, "banner?type=2"), ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        if (State != ApiProcessState.Running) SetState(ApiProcessState.Running);
                        failCount = 0;
                    }
                }
                catch
                {
                    // 尚未就绪或健康检查失败,不立即判死,交给计数
                }

                await Task.Delay(_options.HealthCheckIntervalMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* 正常停止 */ }
    }

    private async Task WaitUntilRunningAsync(int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (State != ApiProcessState.Running && DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        if (State != ApiProcessState.Running)
            throw new TimeoutException($"API 服务在 {timeoutMs}ms 内未就绪(最后状态:{State})");
    }

    /// <summary>优雅关闭子进程并回收资源。</summary>
    public async Task StopAsync()
    {
        if (State is ApiProcessState.Stopped or ApiProcessState.Failed) return;
        SetState(ApiProcessState.Stopping);

        _lifeCts?.Cancel();
        try { if (_supervisorTask is not null) await _supervisorTask.ConfigureAwait(false); }
        catch { /* ignore */ }

        if (_process is { HasExited: false })
        {
            // 先发 Ctrl-C 之外的友好退出：直接尝试结束
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        _process?.Dispose();
        _process = null;
        SetState(ApiProcessState.Stopped);
    }

    private void SetState(ApiProcessState s)
    {
        lock (_gate) _state = s;
        StateChanged?.Invoke(s);
    }

    private static int GetFreePort()
    {
        using var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

/// <summary>进程托管配置。</summary>
public sealed class NcmApiHostOptions
{
    public string NodeExe { get; set; } = "node";
    public string ApiWorkDir { get; set; } = "";
    public string ApiEntryJs { get; set; } = "boot.js";
    public int StartTimeoutMs { get; set; } = 30_000;
    public int HealthCheckIntervalMs { get; set; } = 2_000;
    public int MaxRestartAttempts { get; set; } = 5;

    public static NcmApiHostOptions CreateDefault()
    {
        var apiDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "mica-sound-api");
        return new NcmApiHostOptions { ApiWorkDir = Path.GetFullPath(apiDir) };
    }
}