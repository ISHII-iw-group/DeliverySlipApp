namespace DeliverySlipApp.Models;

/// <summary>
/// 検査ビン容量／ボンベ容量のプルダウン選択肢（表示は「50kg」、計算・送信時は「50」）。
/// </summary>
public sealed class CapacityOption
{
    public CapacityOption(string value)
    {
        Value = value;
        Display = value + "kg";
    }

    public string Value { get; }
    public string Display { get; }

    public static readonly CapacityOption[] All =
    {
        new("5"),
        new("10"),
        new("20"),
        new("30"),
        new("50"),
    };
}
