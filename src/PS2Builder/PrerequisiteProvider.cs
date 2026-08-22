namespace PS2Builder;

public static class PrerequisiteProvider
{
    const string VcUrl = "https://aka.ms/vc14/vc_redist.x64.exe";
    public static async Task<string> EnsureVcRedistAsync()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS2Builder", "cache", "prerequisites");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "vc_redist.x64.exe");
        if (File.Exists(file) && new FileInfo(file).Length > 10_000_000) return file;
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PS2Builder/0.3");
        await using var output = File.Create(file);
        await (await http.GetStreamAsync(VcUrl)).CopyToAsync(output);
        return file;
    }
}
