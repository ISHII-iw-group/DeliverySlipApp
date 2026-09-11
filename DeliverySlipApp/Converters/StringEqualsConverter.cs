using System.Globalization;
using System.Windows.Data;

namespace DeliverySlipApp.Converters;

/// <summary>
/// ラジオボタンのIsCheckedと文字列プロパティを相互変換するコンバータ。
/// ConverterParameterと値が一致していればtrue。チェック時はConverterParameterの値を書き戻す。
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value as string, parameter as string, StringComparison.Ordinal);
    }

    public object? ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? parameter : Binding.DoNothing;
    }
}
