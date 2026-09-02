using System.Text.RegularExpressions;
using MicaSound.Core.Models;

namespace MicaSound.Core;

/// <summary>
/// 解析网易云 /lyric 返回的 LRC 歌词文本为标准行列表。
/// 每行形如 [mm:ss.xx]文本；可选双语 tlyric 作为翻译一并接入。
/// </summary>
public static class LrcParser
{
    // 兼容 [00:12.34] [00:12.345] [1:02.5] 等多种时间戳写法
    private static readonly Regex TimeTag = new(@"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    /// <summary>解析主歌词。</summary>
    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return result;

        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var match = TimeTag.Match(line);
            if (!match.Success) continue;

            var text = TimeTag.Replace(line, "").Trim();
            if (text.Length == 0) text = "♪"; // 纯音乐占位
            var time = ToTimeSpan(match);
            result.Add(new LyricLine { Time = time, Text = text });
        }

        return SequenceTime(result);
    }

    /// <summary>把翻译歌词按时间戳并入主歌词的 Translation 字段。</summary>
    public static void MergeTranslation(IReadOnlyList<LyricLine> lyrics, string? tlyric)
    {
        if (lyrics.Count == 0 || string.IsNullOrWhiteSpace(tlyric)) return;
        var map = new Dictionary<int, string>();
        foreach (var raw in tlyric.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var match = TimeTag.Match(line);
            if (!match.Success) continue;
            var text = TimeTag.Replace(line, "").Trim();
            if (text.Length == 0) continue;
            map[TimestampKey(match)] = text;
        }
        for (var i = 0; i < lyrics.Count; i++)
        {
            if (map.TryGetValue(TimestampKeyFrom(lyrics[i].Time), out var t) && !string.IsNullOrEmpty(t))
                lyrics[i].Translation = t;
        }
    }

    /// <summary>按出现时间升序排序并确保首行为起点。</summary>
    private static List<LyricLine> SequenceTime(List<LyricLine> lines)
    {
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static TimeSpan ToTimeSpan(Match m)
    {
        var min = int.Parse(m.Groups[1].Value);
        var sec = int.Parse(m.Groups[2].Value);
        var ms = m.Groups[3].Success
            ? int.Parse(m.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
            : 0;
        return new TimeSpan(0, 0, min, sec, ms);
    }

    private static int TimestampKey(Match m)
    {
        var t = ToTimeSpan(m);
        return (int)Math.Round(t.TotalMilliseconds);
    }
    private static int TimestampKeyFrom(TimeSpan t) => (int)Math.Round(t.TotalMilliseconds);
}