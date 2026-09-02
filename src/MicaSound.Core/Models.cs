namespace MicaSound.Core.Models;

/// <summary>歌曲最小信息,来自 /song/detail 或搜索结果的精简字段。</summary>
public sealed class SongInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string[] Artists { get; set; } = Array.Empty<string>();
    public string Album { get; set; } = "";
    public string AlbumPicUrl { get; set; } = "";
    public int DurationMs { get; set; }

    public string ArtistText => string.Join(" / ", Artists);

    public string SongUrl { get; set; } = "";
    public int Br { get; set; }
}

/// <summary>歌单一览信息,来自 /playlist/detail 或用户歌单列表。</summary>
public sealed class PlaylistInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string CoverPicUrl { get; set; } = "";
    public int TrackCount { get; set; }
    public long CreatorUid { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>LRC 歌词中的一行。</summary>
public sealed class LyricLine
{
    public TimeSpan Time { get; set; }
    public string Text { get; set; } = "";
    public string? Translation { get; set; }
}

/// <summary>用户登录态概要。</summary>
public sealed class UserProfile
{
    public long UserId { get; set; }
    public string Nickname { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public bool LoggedIn { get; set; }
}