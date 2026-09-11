using System.Globalization;
using System.Text.Json;

namespace DeliverySlipApp.Services;

/// <summary>
/// JustDB APIのレスポンスに含まれるフィールド値（文字列・数値・真偽値・配列など、フィールド種別によって形式が異なる）を
/// 統一的に取り出すためのヘルパー。
/// 選択肢系フィールドは [値, 表示ラベル] の2要素配列、採番フィールドは [接頭語, 番号, 接尾語, 全体表記] の4要素配列で返却される。
/// </summary>
public static class JustDbFieldParser
{
    /// <summary>選択肢系フィールド・文字列フィールドの値を取り出す。配列形式の場合は先頭要素を使用する。</summary>
    public static string ExtractText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean().ToString(),
            JsonValueKind.Array => ExtractFromArray(element, 0),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// 採番フィールド（伝票ID・ボンベID等）から「番号」部分を取り出す。
    /// レスポンスは [接頭語, 番号, 接尾語, 全体の表記] の配列形式。
    /// </summary>
    public static string ExtractNumberingValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => ExtractFromArray(element, 1),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            _ => string.Empty,
        };
    }

    public static decimal ExtractDecimalOrZero(JsonElement element)
    {
        var text = ExtractText(element);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    public static DateTime? ExtractDate(JsonElement element)
    {
        var text = ExtractText(element);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value.DateTime
            : null;
    }

    public static bool ExtractBool(JsonElement element)
        => element.ValueKind is JsonValueKind.True or JsonValueKind.False && element.GetBoolean();

    private static string ExtractFromArray(JsonElement array, int preferredIndex)
    {
        var items = array.EnumerateArray().ToList();
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var element = items.Count > preferredIndex ? items[preferredIndex] : items[0];
        return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
    }
}
