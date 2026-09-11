using System.IO;
using System.Text.Json;

namespace DeliverySlipApp.Services;

public sealed class JustDbConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SlipTableName { get; set; } = string.Empty;
    public string CylinderTableName { get; set; } = string.Empty;

    /// <summary>伝票管理テーブルの配送日で期間絞り込みを行うための、パネル・フィルター識別名。</summary>
    public string SlipPanelName { get; set; } = string.Empty;
    public string SlipFilterName { get; set; } = string.Empty;

    /// <summary>ボンベ管理テーブルの日付で期間絞り込みを行うための、パネル・フィルター識別名。</summary>
    public string CylinderPanelName { get; set; } = string.Empty;
    public string CylinderFilterName { get; set; } = string.Empty;
}

public sealed class AppConfig
{
    public JustDbConfig JustDb { get; set; } = new();

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"設定ファイルが見つかりません。実行フォルダに appsettings.json を配置してください。({path})");
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return config ?? throw new InvalidOperationException("appsettings.json の読み込みに失敗しました。");
    }
}
