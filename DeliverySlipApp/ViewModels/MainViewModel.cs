using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeliverySlipApp.Models;
using DeliverySlipApp.Services;

namespace DeliverySlipApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly MasterDataService _masterData;

    public MainViewModel()
    {
        _config = AppConfig.Load();

        _masterData = new MasterDataService();
        _masterData.Load();

        Stores = new ObservableCollection<string>(_masterData.Stores);
        FillingStations = new ObservableCollection<string>(_masterData.FillingStations);
        Destinations = new ObservableCollection<string>();
        CylinderRows = new ObservableCollection<CylinderRow>();

        InitializeNewSlip();
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
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private bool isBusy;

    partial void OnSelectedStoreChanged(string? value)
    {
        Destinations.Clear();
        if (value is not null && _masterData.DestinationsByStore.TryGetValue(value, out var list))
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

    private void RecalculateDerivedValues()
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

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        IsBusy = true;
        try
        {
            AppLogger.Info("登録操作を開始しました。");

            var headerErrors = ValidateHeader();
            if (headerErrors.Count > 0)
            {
                AppLogger.Warn("未入力項目のため登録を中断しました。" + string.Join("、", headerErrors));
                ShowError("次の項目が未入力です。\n\n" + string.Join("、", headerErrors));
                return;
            }

            var usedRows = CylinderRows.Where(r => !r.IsBlank).ToList();
            var rowErrors = ValidateRows(usedRows);
            if (rowErrors.Count > 0)
            {
                AppLogger.Warn("ボンベ情報明細の入力エラーのため登録を中断しました。" + string.Join(" / ", rowErrors));
                ShowError("ボンベ情報明細を確認してください。\n\n" + string.Join("\n", rowErrors));
                return;
            }

            RecalculateDerivedValues();
            var capacityTotal = decimal.Parse(InspectionBinCapacityTotalDisplay, CultureInfo.InvariantCulture);
            var usage = decimal.Parse(InspectionBinUsageDisplay, CultureInfo.InvariantCulture);
            var fillingQuantity = decimal.Parse(FillingQuantityDisplay, CultureInfo.InvariantCulture);
            var remaining = ParseOrZero(RemainingQuantity);

            var negatives = new (string Name, decimal Value)[]
            {
                ("残数量", remaining),
                ("検査ビン容量計", capacityTotal),
                ("検査ビン使用量", usage),
                ("充填数量", fillingQuantity),
            }.Where(x => x.Value < 0).Select(x => x.Name).ToList();

            if (negatives.Count > 0)
            {
                AppLogger.Warn("計算結果がマイナスのため登録を中断しました。" + string.Join("、", negatives));
                ShowWarning("次の項目がマイナスの値になっています。入力内容を見直してください。\n\n" + string.Join("、", negatives));
                return;
            }

            if (!ConfirmRegistration(usedRows.Count))
            {
                AppLogger.Info("登録確認でキャンセルされました。");
                return;
            }

            using var client = new JustDbClient(_config.JustDb.BaseUrl, _config.JustDb.ApiKey);

            int slipRecordId;
            try
            {
                var slipFields = BuildSlipFields();
                slipRecordId = await client.InsertRecordAsync(_config.JustDb.SlipTableName, slipFields);
                AppLogger.Info($"伝票ヘッダーを登録しました。recordId={slipRecordId}");
            }
            catch (JustDbApiException ex)
            {
                AppLogger.Error($"伝票ヘッダーの登録に失敗しました。code={ex.ErrorCode} message={ex.Message}");
                ShowError($"伝票情報の登録に失敗しました。\n{ex.Message}\n\n再度登録操作を行ってください。");
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"伝票ヘッダーの登録に失敗しました（通信エラー）。message={ex.Message}");
                ShowError($"JustDBとの通信でエラーが発生しました。\n{ex.Message}\n\n再度登録操作を行ってください。");
                return;
            }

            string slipIdText;
            try
            {
                var record = await client.GetRecordFieldsAsync(_config.JustDb.SlipTableName, slipRecordId);
                slipIdText = JustDbFieldParser.ExtractNumberingValue(record[SlipFields.SlipId]);
                AppLogger.Info($"伝票IDを取得しました。slipId={slipIdText}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"伝票IDの取得に失敗しました。recordId={slipRecordId} message={ex.Message}");
                var rollbackOk = await RollbackAsync(client, slipRecordId, new List<int>());
                AppLogger.Info($"ロールバック結果：{(rollbackOk ? "成功" : "失敗")}");
                ShowError($"登録した伝票の情報取得に失敗したため、登録内容を取り消しました。\n{ex.Message}\n\n再度登録操作を行ってください。");
                return;
            }

            var insertedCylinderIds = new List<int>();
            try
            {
                foreach (var row in usedRows)
                {
                    var fields = BuildCylinderFields(row, slipIdText);
                    var id = await client.InsertRecordAsync(_config.JustDb.CylinderTableName, fields);
                    insertedCylinderIds.Add(id);
                }

                AppLogger.Info($"ボンベ情報明細を{insertedCylinderIds.Count}件登録しました。slipId={slipIdText}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"ボンベ情報明細の登録に失敗しました。slipId={slipIdText} message={ex.Message}");
                var rollbackOk = await RollbackAsync(client, slipRecordId, insertedCylinderIds);
                AppLogger.Info($"ロールバック結果：{(rollbackOk ? "成功" : "失敗")}");
                if (rollbackOk)
                {
                    ShowError($"ボンベ情報の登録中にエラーが発生したため、登録内容を取り消しました。\n{ex.Message}\n\n再度登録操作を行ってください。");
                }
                else
                {
                    ShowError("ボンベ情報の登録中にエラーが発生し、かつ登録内容の取り消し処理にも失敗しました。\nJustDB上にデータの不整合が生じている可能性があります。システム課へご連絡ください。");
                }

                return;
            }

            AppLogger.Info($"伝票の登録が完了しました。slipId={slipIdText}");
            MessageBox.Show("登録が完了しました。", "登録完了", MessageBoxButton.OK, MessageBoxImage.Information);
            InitializeNewSlip();
        }
        catch (Exception ex)
        {
            // async Task の登録コマンドはfire-and-forgetで実行されるため、
            // 内側のtry/catchで捕捉されなかった例外はここで捕捉しないと利用者に一切通知されない。
            AppLogger.Error($"登録処理中に予期しないエラーが発生しました。message={ex.Message}");
            ShowError($"登録処理中に予期しないエラーが発生しました。\n{ex.Message}\n\nシステム課へご連絡ください。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRegister() => !IsBusy;

    private async Task<bool> RollbackAsync(JustDbClient client, int slipRecordId, List<int> cylinderRecordIds)
    {
        var success = true;

        foreach (var id in cylinderRecordIds)
        {
            try
            {
                await client.DeleteRecordAsync(_config.JustDb.CylinderTableName, id);
            }
            catch
            {
                success = false;
            }
        }

        try
        {
            await client.DeleteRecordAsync(_config.JustDb.SlipTableName, slipRecordId);
        }
        catch
        {
            success = false;
        }

        return success;
    }

    private List<string> ValidateHeader()
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

    private static List<string> ValidateRows(List<CylinderRow> rows)
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
    private Dictionary<string, object?> BuildSlipFields()
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

    private Dictionary<string, object?> BuildCylinderFields(CylinderRow row, string slipId)
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

    private static string ToApiDate(DateTime date)
        => date.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) + "+09:00";

    private static bool IsValidNumber(string? s)
        => !string.IsNullOrWhiteSpace(s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    private static decimal ParseOrZero(string? s)
        => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static string NormalizeNumber(string s)
        => FormatNumber(decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture));

    private static string NormalizeNumberOrZero(string? s) => FormatNumber(ParseOrZero(s));

    private static string FormatNumber(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// WPFの複数行TextBoxはEnter入力時に\r\n（CRLF）を挿入するが、
    /// JustDB APIは複数行文字列の改行記号として\nのみを受け付け、CRを含む値はエラーになるため、送信前に\nへ統一する。
    /// </summary>
    private static string NormalizeLineBreaks(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>登録実行前の最終確認ダイアログを表示する。ボンベ情報明細が0件の場合はその旨を明記する。</summary>
    private bool ConfirmRegistration(int cylinderRowCount)
    {
        var lines = new List<string>
        {
            $"配送日：{DeliveryDate:yyyy/MM/dd}",
            $"販売店：{SelectedStore}",
            $"配送先：{SelectedDestination}",
            $"充填日：{FillingDate:yyyy/MM/dd}",
            $"充填所：{SelectedFillingStation}",
        };

        if (cylinderRowCount == 0)
        {
            lines.Add(string.Empty);
            lines.Add("※ ボンベ情報明細は0件（未入力）のまま登録します。");
        }
        else
        {
            lines.Add($"ボンベ情報明細：{cylinderRowCount}件");
        }

        var message = "以下の内容で登録します。よろしいですか？\n\n" + string.Join("\n", lines);
        var icon = cylinderRowCount == 0 ? MessageBoxImage.Warning : MessageBoxImage.Question;

        var result = MessageBox.Show(message, "登録確認", MessageBoxButton.YesNo, icon, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static void ShowError(string message)
        => MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

    private static void ShowWarning(string message)
        => MessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void InitializeNewSlip()
    {
        DeliveryDate = DateTime.Today;
        SelectedStore = null;
        SelectedDestination = null;
        ManagementType = "ボンベ";
        FillingDate = DateTime.Today;
        SelectedFillingStation = null;
        DeliveredQuantity = string.Empty;
        RemainingQuantity = string.Empty;
        InspectionBinCapacity1 = null;
        InspectionBinCount1 = string.Empty;
        InspectionBinCapacity2 = null;
        InspectionBinCount2 = string.Empty;
        InspectionBinCapacity3 = null;
        InspectionBinCount3 = string.Empty;
        InspectionBinRemaining = string.Empty;
        PressureFillingAmount = string.Empty;
        Remarks = string.Empty;

        CylinderRows.Clear();
        CylinderRows.Add(new CylinderRow());

        RecalculateDerivedValues();
    }
}
