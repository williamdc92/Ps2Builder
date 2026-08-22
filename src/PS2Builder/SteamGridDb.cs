using System.Net.Http.Headers;
using System.Text.Json;

namespace PS2Builder;

public static class SteamGridDb
{
    public static async Task<List<string>> FindIconUrlsAsync(string title, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return [];
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PS2Builder/0.1");
        var searchJson = await http.GetStringAsync("https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(title));
        using var search = JsonDocument.Parse(searchJson);
        var first = search.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) return [];
        var id = first.GetProperty("id").GetInt32();
        var iconsJson = await http.GetStringAsync($"https://www.steamgriddb.com/api/v2/icons/game/{id}");
        using var icons = JsonDocument.Parse(iconsJson);
        return icons.RootElement.GetProperty("data").EnumerateArray().Take(12).Select(x => x.GetProperty("url").GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
    }
}
