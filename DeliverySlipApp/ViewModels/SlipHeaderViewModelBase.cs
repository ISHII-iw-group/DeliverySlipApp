using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeliverySlipApp.Models;
using DeliverySlipApp.Services;

namespace DeliverySlipApp.ViewModels;

/// <summary>
/// 伝票ヘッダー＋ボンベ情報明細の入力フォームが持つ共通のプロパティ・バリデーション・
/// JustDB送信用フィールド組み立てロジック。新規登録（<see cref="MainViewModel"/>）と
/// 参照・編集（<see cref="SlipReferenceEditViewModel"/>）の両方がこれを継承する。
/// </summary>
public abstract partial class SlipHeaderViewModelBase : ObservableObject
{
    protected readonly AppConfig Config;
    protected readonly MasterDataService MasterData;

    protected SlipHeaderViewModelBase()
    {
        Config = AppConfig.Load();

        MasterData = new MasterDataService();
        MasterData.Load();

        Stores = new ObservableCollection<string>(MasterData.Stores);
        FillingStations = new ObservableCollection<string>(MasterData.FillingStations);
        Destinations = new ObservableCollection<string>();
        CylinderRows = new ObservableCollection<CylinderRow>();
    }

    public ObservableCollection<string> Stores { get; }

    public ObservableCollection<string> Destinations { get; }

    public ObservableCollection<string> FillingStations { get; }

    public ObservableCollection<CylinderRow> CylinderRows { get; }

    [ObservableProperty]
    private DateTime? deliveryDate = DateTime.Today;

    [ObservableProperty]
    private string? selectedStore;

    [ObservableProperty]
    private string? selectedDestination;

    [ObservableProperty]
    private string managementType = "ボンベ";

    [ObservableProperty]
    private DateTime? fillingDate = DateTime.Today;

    [ObservableProperty]
    private string? selectedFillingStation;

    [ObservableProperty]
    private string deliveredQuantity = string.Empty;

    [ObservableProperty]
    private string remainingQuantity = string.Empty;

    [ObservableProperty]
    private string? inspectionBinCapacity1;

    [ObservableProperty]
    private string inspectionBinCount1 = string.Empty;

    [ObservableProperty]
    private string? inspectionBinCapacity2;

    [ObservableProperty]
    private string inspectionBinCount2 = string.Empty;

    [ObservableProperty]
    private string? inspectionBinCapacity3;

    [ObservableProperty]
    private string inspectionBinCount3 = string.Empty;

    [ObservableProperty]
    private string inspectionBinRemaining = string.Empty;

    [ObservableProperty]
    private string pressureFillingAmount = string.Empty;

    [ObservableProperty]
    private string remarks = string.Empty;

    [ObservableProperty]
    private string inspectionBinCapacityTotalDisplay = "0";

    [ObservableProperty]
    private string inspectionBinUsageDisplay = "0";

    [ObservableProperty]
    private string fillingQuantityDisplay = "0";

    [ObservableProperty]
    private bool isBusy;

    partial void OnIsBusyChanged(bool value) => OnIsBusyChangedCore(value);

    /// <summary>IsBusyの変更をサブクラス側のコマンドのCanExecute再評価に伝えるためのフック。</summary>
    protected virtual void OnIsBusyChangedCore(bool value)
    {
    }

    partial void OnSelectedStoreChanged(string? value)
    {
        Destinations.Clear();
        if (value is not null && MasterData.DestinationsByStore.TryGetValue(value, out var list))
        {
            foreach (var destination in list)
            {
                Destinations.Add(destination);
            }
        }

        if (SelectedDestination is not null && !Destinations.Contains(SelectedDestination))
        {
            SelectedDestination = null;
        }
    }

    partial void OnInspectionBinCapacity1Changed(string? value) => RecalculateDerivedValues();

    partial void OnInspectionBinCount1Changed(string value) => RecalculateDerivedValues();

    partial void OnInspectionBinCapacity2Changed(string? value) => RecalculateDerivedValues();

    partial void OnInspectionBinCount2Changed(string value) => RecalculateDerivedValues();

    partial void OnInspectionBinCapacity3Changed(string? value) => RecalculateDerivedValues();

    partial void OnInspectionBinCount3Changed(string value) => RecalculateDerivedValues();

    partial void OnInspectionBinRemainingChanged(string value) => RecalculateDerivedValues();

    partial void OnDeliveredQuantityChanged(string value) => RecalculateDerivedValues();

    partial void OnRemainingQuantityChanged(string value) => RecalculateDerivedValues();

    protected void RecalculateDerivedValues()
    {
        var c1 = ParseOrZero(InspectionBinCapacity1);
        var n1 = ParseOrZero(InspectionBinCount1);
        var c2 = ParseOrZero(InspectionBinCapacity2);
        var n2 = ParseOrZero(InspectionBinCount2);
        var c3 = ParseOrZero(InspectionBinCapacity3);
        var n3 = ParseOrZero(InspectionBinCount3);
        var binRemaining = ParseOrZero(InspectionBinRemaining);
        var delivered = ParseOrZero(DeliveredQuantity);
        var remaining = ParseOrZero(RemainingQuantity);

        var capacityTotal = (c1 * n1) + (c2 * n2) + (c3 * n3);
        var usage = capacityTotal - binRemaining;
        var fillingQuantity = delivered - remaining - usage;

        InspectionBinCapacityTotalDisplay = FormatNumber(capacityTotal);
        InspectionBinUsageDisplay = FormatNumber(usage);
        FillingQuantityDisplay = FormatNumber(fillingQuantity);
    }

    [RelayCommand]
    private void AddRow() => CylinderRows.Add(new CylinderRow());

    [RelayCommand]
    private void RemoveRow(CylinderRow? row)
    {
        if (row is not null)
        {
            CylinderRows.Remove(row);
        }
    }

    protected List<string> ValidateHeader()
    {
        var missing = new List<string>();

        if (DeliveryDate is null) missing.Add("配送日");
        if (string.IsNullOrWhiteSpace(SelectedStore)) missing.Add("販売店");
        if (string.IsNullOrWhiteSpace(SelectedDestination)) missing.Add("配送先");
        if (string.IsNullOrWhiteSpace(ManagementType)) missing.Add("管理方法");
        if (FillingDate is null) missing.Add("充填日");
        if (string.IsNullOrWhiteSpace(SelectedFillingStation)) missing.Add("充填所");
        if (!IsValidNumber(DeliveredQuantity)) missing.Add("納入数量");
        if (!IsValidNumber(RemainingQuantity)) missing.Add("残数量");

        return missing;
    }

    protected static List<string> ValidateRows(List<CylinderRow> rows)
    {
        var errors = new List<string>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowLabel = $"{i + 1}行目";

            if (string.IsNullOrWhiteSpace(row.Symbol))
            {
                errors.Add($"{rowLabel}：ボンベ記号を入力してください。");
            }
            else if (!Regex.IsMatch(row.Symbol, "^[A-Z]+$"))
            {
                errors.Add($"{rowLabel}：ボンベ記号は大文字英字のみで入力してください。");
            }

            if (string.IsNullOrWhiteSpace(row.Number))
            {
                errors.Add($"{rowLabel}：ボンベ番号を入力してください。");
            }
            else if (!Regex.IsMatch(row.Number, "^[0-9]{1,6}$"))
            {
                errors.Add($"{rowLabel}：ボンベ番号は数字6桁以内で入力してください。");
            }

            if (string.IsNullOrWhiteSpace(row.Capacity))
            {
                errors.Add($"{rowLabel}：ボンベ容量を選択してください。");
            }

            if (string.IsNullOrWhiteSpace(row.DeliveryType))
            {
                errors.Add($"{rowLabel}：引渡／引取を選択してください。");
            }
        }

        return errors;
    }

    /// <summary>
    /// 伝票ヘッダーの送信用フィールドを組み立てる。
    /// 検査ビン容量計・検査ビン使用量・充填数量はJustDB側の「数値計算」フィールドであり
    /// API経由での書き込みを受け付けないため、画面表示・計算のみ行い送信対象には含めない。
    /// </summary>
    protected Dictionary<string, object?> BuildSlipFields()
    {
        return new Dictionary<string, object?>
        {
            [SlipFields.DeliveryDate] = ToApiDate(DeliveryDate!.Value),
            [SlipFields.Store] = SelectedStore,
            [SlipFields.Destination] = SelectedDestination,
            [SlipFields.ManagementType] = ManagementType,
            [SlipFields.FillingDate] = ToApiDate(FillingDate!.Value),
            [SlipFields.FillingStation] = SelectedFillingStation,
            [SlipFields.DeliveredQuantity] = NormalizeNumber(DeliveredQuantity),
            [SlipFields.RemainingQuantity] = NormalizeNumber(RemainingQuantity),
            [SlipFields.InspectionBinCapacity1] = NormalizeNumberOrZero(InspectionBinCapacity1),
            [SlipFields.InspectionBinCount1] = NormalizeNumberOrZero(InspectionBinCount1),
            [SlipFields.InspectionBinCapacity2] = NormalizeNumberOrZero(InspectionBinCapacity2),
            [SlipFields.InspectionBinCount2] = NormalizeNumberOrZero(InspectionBinCount2),
            [SlipFields.InspectionBinCapacity3] = NormalizeNumberOrZero(InspectionBinCapacity3),
            [SlipFields.InspectionBinCount3] = NormalizeNumberOrZero(InspectionBinCount3),
            [SlipFields.InspectionBinRemaining] = NormalizeNumberOrZero(InspectionBinRemaining),
            [SlipFields.PressureFillingAmount] = NormalizeNumberOrZero(PressureFillingAmount),
            [SlipFields.Remarks] = NormalizeLineBreaks(Remarks),
        };
    }

    protected Dictionary<string, object?> BuildCylinderFields(CylinderRow row, string slipId)
    {
        return new Dictionary<string, object?>
        {
            // 伝票ID（ボンベ管理側）は手動採番フィールド（接頭語・接尾語なし）のため、
            // [接頭語, 番号, 接尾語] の配列形式で送信する。
            [CylinderFields.SlipId] = new[] { string.Empty, slipId, string.Empty },
            [CylinderFields.Symbol] = row.Symbol.Trim(),
            [CylinderFields.Number] = row.Number.Trim(),
            [CylinderFields.Capacity] = NormalizeNumberOrZero(row.Capacity),
            [CylinderFields.DeliveryType] = row.DeliveryType,
            [CylinderFields.InspectionBin] = row.InspectionBin,
            [CylinderFields.PressureBin] = row.PressureBin,
            [CylinderFields.Store] = SelectedStore,
            [CylinderFields.Destination] = SelectedDestination,
            [CylinderFields.DeliveryDate] = ToApiDate(DeliveryDate!.Value),
        };
    }

    protected static string ToApiDate(DateTime date)
        => date.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) + "+09:00";

    protected static bool IsValidNumber(string? s)
        => !string.IsNullOrWhiteSpace(s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    protected static decimal ParseOrZero(string? s)
        => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    protected static string NormalizeNumber(string s)
        => FormatNumber(decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture));

    protected static string NormalizeNumberOrZero(string? s) => FormatNumber(ParseOrZero(s));

    protected static string FormatNumber(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// WPFの複数行TextBoxはEnter入力時に\r\n（CRLF）を挿入するが、
    /// JustDB APIは複数行文字列の改行記号として\nのみを受け付け、CRを含む値はエラーになるため、送信前に\nへ統一する。
    /// </summary>
    protected static string NormalizeLineBreaks(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    protected static void ShowError(string message)
        => MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

    protected static void ShowWarning(string message)
        => MessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
}
