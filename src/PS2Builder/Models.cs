using System.Text.Json.Serialization;

namespace PS2Builder;

public enum ResolutionProfile { Automatic, Native, X2, X3, X4, X6 }
public enum AspectProfile { Automatic, Original4x3, Widescreen16x9 }

public sealed class BuildSettings
{
    public string GamePath { get; set; } = "";
    public string BiosPath { get; set; } = "";
    public string OutputIso { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? CustomIconPath { get; set; }
    public ResolutionProfile Resolution { get; set; } = ResolutionProfile.Automatic;
    public AspectProfile Aspect { get; set; } = AspectProfile.Automatic;
    public bool UseRecommendedFixes { get; set; } = true;
    public List<string> EnabledPatchGroups { get; set; } = [];
}

public sealed class DiscManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = "PlayStation 2 Game";
    public string Serial { get; set; } = "UNKNOWN";
    public string Region { get; set; } = "Unknown";
    public string GameRelativePath { get; set; } = @"content\game.iso";
    public string BiosFileName { get; set; } = "bios.bin";
    public string Pcsx2ExeRelativePath { get; set; } = @"runtime\pcsx2-qt.exe";
    [JsonConverter(typeof(JsonStringEnumConverter))] public ResolutionProfile Resolution { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public AspectProfile Aspect { get; set; }
    public List<string> EnabledPatchGroups { get; set; } = [];
    public string RuntimeSource { get; set; } = "PCSX2";
    public string? RuntimeVersion { get; set; }
}

public sealed class GameInfo
{
    public string Serial { get; set; } = "UNKNOWN";
    public string Title { get; set; } = "PlayStation 2 Game";
    public string Region { get; set; } = "Unknown";
    public string? Crc { get; set; }
    public List<PatchGroupInfo> Patches { get; set; } = [];
}

public sealed class PatchGroupInfo
{
    public string Name { get; set; } = "Patch";
    public string SourceFile { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Recommended { get; set; }
    public override string ToString() => Recommended ? $"{Name}  ★ recommended" : Name;
}
