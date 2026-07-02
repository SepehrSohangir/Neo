using System.Text.Json;

namespace Neo.Mobile.Maui.Configuration;

public sealed class AppSettings
{
    public string ServerBaseUrl { get; set; } = "http://localhost:8080";
}

public static class AppSettingsLoader
{
    public static AppSettings Load()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
