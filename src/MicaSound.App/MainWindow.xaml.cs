using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MicaSound.ApiHost.Client;
using MicaSound.ApiHost.ProcessHost;
using MicaSound.Core.Models;

namespace MicaSound.App;

/// <summary>列表项展示模型。</summary>
public sealed class SongItem
{
    public int Index { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public SongInfo Song { get; init; } = null!;
}

public sealed partial class MainWindow : Window
{
    private NcmApiProcessHost _host = null!;
    private readonly ObservableCollection<SongItem> _songs = new();
    private readonly MicaPlayerService _player = new();
    private NcmApiClient? _api;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private bool _syncSeek;

    public MainWindow()
    {
        InitializeComponent();
        ApplyMica();
        SongList.ItemsSource = _songs;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += OnProgressTick;
        _timer.Start();

        Closed += (_, _) => { _player.Dispose(); };
    }

    /// <summary>启动自托管 API 进程并补拉一次推荐。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var work = TryLocateApiDir() ?? "";
            if (string.IsNullOrEmpty(work) || !File.Exists(Path.Combine(work, "boot.js")))
            {
                await PromptEnsureApiAsync(work);
                return;
            }

            var opts = NcmApiHostOptions.CreateDefault();
            opts.ApiWorkDir = work;
            opts.NodeExe = ResolveNode();
            _host = new NcmApiProcessHost(opts);

            await _host.StartAsync();
            _api = new NcmApiClient(_host.BaseAddress!);
            EmptyHint.Text = "输入关键词开始搜索";
        }
        catch (Exception ex)
        {
            EmptyHint.Text = $"API 启动失败: {ex.Message}\n请先运行 scripts/ensure-api.ps1";
        }
    }

    private static string ResolveNode()
    {
        var node = "node";
        try { var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node, "--version") { RedirectStandardOutput = true, UseShellExecute = false }); p?.Kill(); }
        catch { node = "node.exe"; }
        return node;
    }

    private static string? TryLocateApiDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "mica-sound-api");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private async Task PromptEnsureApiAsync(string work)
    {
        EmptyHint.Text = "缺少自托管 API(withmico-sound-api 未找到)。\n请先执行: scripts/ensure-api.ps1,然后重新打开应用。";
        var dlg = new ContentDialog
        {
            Title = "需要先准备 API 服务",
            Content = "微声需要本地部署的网易云 API。请在项目根目录运行:\n\npowershell -ExecutionPolicy Bypass -File scripts/ensure-api.ps1\n\n完成后重启本应用。",
            CloseButtonText = "知道了",
            XamlRoot = Content.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private void ApplyMica()
    {
        try { SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop(); }
        catch { /* 旧系统无 Mica,沿用默认背景 */ }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await DoSearchAsync();

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await DoSearchAsync();
    }

    private async Task DoSearchAsync()
    {
        if (_api is null) return;
        var kw = SearchBox.Text.Trim();
        if (kw.Length == 0) return;

        EmptyHint.Text = "搜索中…";
        try
        {
            var songs = await _api.SearchSongsAsync(kw, 60);
            _songs.Clear();
            int index = 1;
            foreach (var s in songs)
            {
                _songs.Add(new SongItem
                {
                    Index = index++,
                    Title = s.Name,
                    Subtitle = string.Join(" / ", s.Artists) + (string.IsNullOrEmpty(s.Album) ? "" : " · " + s.Album),
                    Song = s,
                });
            }
            EmptyHint.Text = _songs.Count == 0 ? "没有找到相关结果" : $"{_songs.Count} 首结果";
        }
        catch (Exception ex)
        {
            EmptyHint.Text = $"搜索失败: {ex.Message}";
        }
    }

    private async void OnSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongItem item) await PlayAsync(item);
    }

    private async Task PlayAsync(SongItem item)
    {
        if (_api is null) return;
        try
        {
            var url = await _api.GetSongUrlAsync(item.Song.Id);
            if (string.IsNullOrEmpty(url))
            {
                NowTitle.Text = item.Title;
                NowSub.Text = "无法获取播放地址(可能为 VIP 或需登录)";
                return;
            }
            NowTitle.Text = item.Title;
            NowSub.Text = item.Subtitle;
            _player.Load(new Uri(url));
            PlayBtn.Content = "⏸";
        }
        catch (Exception ex)
        {
            NowSub.Text = "播放失败: " + ex.Message;
        }
    }

    private void OnTogglePlay(object sender, RoutedEventArgs e)
    {
        if (!_player.HasSource) return;
        if (_player.Toggle()) PlayBtn.Content = "⏸"; else PlayBtn.Content = "▶";
    }

    private void OnProgressTick(object sender, object e)
    {
        var session = _player.Session;
        if (session is null) return;
        double dur = Math.Max(1, session.NaturalDuration.TotalSeconds);
        _syncSeek = true;
        SeekBar.Minimum = 0;
        SeekBar.Maximum = dur;
        SeekBar.Value = session.Position.TotalSeconds;
        _syncSeek = false;
    }
}

/// <summary>基于系统 MediaPlayer 的音频播放封装。</summary>
public sealed class MicaPlayerService : IDisposable
{
    private readonly Windows.Media.Playback.MediaPlayer _mp = new();

    public bool HasSource => _mp.Source is not null;
    public Windows.Media.Playback.MediaPlaybackSession? Session => _mp.PlaybackSession;

    public bool Toggle()
    {
        if (_mp.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
        { _mp.Pause(); return false; }
        _mp.Play(); return true;
    }

    public void Load(Uri source)
    {
        _mp.Source = Windows.Media.Core.MediaSource.CreateFromUri(source);
        _mp.Play();
    }

    public void Dispose() => _mp.Dispose();
}