using MicaSound.ApiHost.Client;
using MicaSound.ApiHost.ProcessHost;
using MicaSound.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("==== 微声 MicaSound · API 进程托管冒烟测试 ====");

var apiDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "mica-sound-api"));

if (!Directory.Exists(apiDir) || !File.Exists(System.IO.Path.Combine(apiDir, "app.js")))
{
    Console.WriteLine($"[✗] 未找到自托管 API 目录: {apiDir}");
    Console.WriteLine("    请先执行 scripts/ensure-api.ps1 克隆并安装依赖。");
    return 1;
}

var host = new NcmApiProcessHost(new NcmApiHostOptions { ApiWorkDir = apiDir });
host.StateChanged += s => Console.WriteLine($"[进程] 状态 -> {s}");

try
{
    await host.StartAsync();
    Console.WriteLine($"[✓] API 服务已就绪: {host.BaseAddress}");

    await using var api = new NcmApiClient(host.BaseAddress!);

    // 1. 轮播图
    using (var banner = await api.BannerAsync())
    {
        var count = banner.RootElement.TryGetProperty("banners", out var b) ? b.GetArrayLength() : 0;
        Console.WriteLine($"[✓] /banner 轮播图数量: {count}");
    }

    // 2. 搜索(中文需 URL 编码,客户端已做)
    var keyword = "晴天";
    var results = await api.SearchSongsAsync(keyword);
    Console.WriteLine($"[✓] /search 搜索「{keyword}」命中 {results.Count} 首");
    var first = results.FirstOrDefault();
    if (first is not null)
        Console.WriteLine($"      首条: {first.Name} - {first.ArtistText} ({first.Album})");

    // 3. 歌词解析能力(LRC 纯函数自测,不依赖网络)
    var sample = "[00:00.50]这是测试\n[00:03.00]第二行\n[00:05.500]第三行";
    var lines = LrcParser.Parse(sample);
    Console.WriteLine($"[✓] LrcParser 解析 {lines.Count} 行,末行时间 {lines.Last().Time}");

    // 4. 播放地址(未登录预期为空,验证降级逻辑)
    if (first is not null)
    {
        var url = await api.GetSongUrlAsync(first.Id);
        Console.WriteLine(url is null
            ? "[i] /song/url 未登录返回 null(符合预期,登录后可获取)"
            : $"[✓] /song/url 返回 {url}");
    }

    Console.WriteLine("==== 冒烟测试结束 ====");
    var auto = Environment.GetEnvironmentVariable("NCM_SMOKE_AUTO") == "1"
        || Environment.GetEnvironmentVariable("CI") == "true"
        || Console.IsInputRedirected;
    if (!auto)
    {
        Console.WriteLine("按回车键关闭 API 进程...");
        Console.ReadLine();
    }
    await host.StopAsync();
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"[✗] 冒烟测试失败: {ex.Message}");
    await host.StopAsync();
    return 1;
}

static partial class Program { }