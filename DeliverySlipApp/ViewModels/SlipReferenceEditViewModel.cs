using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeliverySlipApp.Models;
using DeliverySlipApp.Services;

namespace DeliverySlipApp.ViewModels;

/// <summary>
/// 伝票ID検索による伝票参照・編集・削除画面（<see cref="ReferenceEditWindow"/>）のViewModel。
/// 検索で読み込んだ内容は「変更」ボタンを押すまでJustDBへは反映しない
/// （ボンベ情報明細の追加・削除も含め、変更操作はすべて「変更」押下時にまとめて差分適用する）。
/// 「伝票削除」のみ、確認ダイアログの後に即時実行する独立した操作。
/// </summary>
public partial class SlipReferenceEditViewModel : SlipHeaderViewModelBase
{
    private int? _slipRecordId;
    private string? _loadedSlipId;
    private HashSet<int> _originalCylinderRecordIds = new();

    [ObservableProperty]
    private string? slipIdInput;

    [ObservableProperty]
    private string? displayedSlipId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSlipCommand))]
    private bool isLoaded;

    protected override void OnIsBusyChangedCore(bool value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        DeleteSlipCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var slipId = SlipIdInput?.Trim();
        if (string.IsNullOrWhiteSpace(slipId))
        {
            ShowWarning("伝票IDを入力してください。");
            return;
        }

        IsBusy = true;
        try
        {
            using var client = new JustDbClient(Config.JustDb.BaseUrl, Config.JustDb.ApiKey);
            await LoadBySlipIdAsync(client, slipId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task UpdateAsync()
    {
        if (_slipRecordId is null || _loadedSlipId is null)
        {
            return;
        }

        var slipRecordId = _slipRecordId.Value;
        var loadedSlipId = _loadedSlipId;

        IsBusy = true;
        try
        {
            AppLogger.Info($"伝票の更新操作を開始しました。slipId={loadedSlipId}");

            var headerErrors = ValidateHeader();
            if (headerErrors.Count > 0)
            {
                AppLogger.Warn("未入力項目のため更新を中断しました。" + string.Join("、", headerErrors));
                ShowError("次の項目が未入力です。\n\n" + string.Join("、", headerErrors));
                return;
            }

            var usedRows = CylinderRows.Where(r => !r.IsBlank).ToList();
            var rowErrors = ValidateRows(usedRows);
            if (rowErrors.Count > 0)
            {
                AppLogger.Warn("ボンベ情報明細の入力エラーのため更新を中断しました。" + string.Join(" / ", rowErrors));
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
                AppLogger.Warn("計算結果がマイナスのため更新を中断しました。" + string.Join("、", negatives));
                ShowWarning("次の項目がマイナスの値になっています。入力内容を見直してください。\n\n" + string.Join("、", negatives));
                return;
            }

            var currentRecordIds = usedRows.Where(r => r.RecordId is not null).Select(r => r.RecordId!.Value).ToHashSet();
            var toDelete = _originalCylinderRecordIds.Where(id => !currentRecordIds.Contains(id)).ToList();
            var toUpdate = usedRows.Where(r => r.RecordId is not null).ToList();
            var toInsert = usedRows.Where(r => r.RecordId is null).ToList();

            if (!ConfirmUpdate(toUpdate.Count, toInsert.Count, toDelete.Count))
            {
                AppLogger.Info("更新確認でキャンセルされました。");
                return;
            }

            using var client = new JustDbClient(Config.JustDb.BaseUrl, Config.JustDb.ApiKey);

            try
            {
                var slipFields = BuildSlipFields();
                await client.UpdateRecordAsync(Config.JustDb.SlipTableName, slipRecordId, slipFields);
                AppLogger.Info($"伝票ヘッダーを更新しました。recordId={slipRecordId}");
            }
            catch (JustDbApiException ex)
            {
                AppLogger.Error($"伝票ヘッダーの更新に失敗しました。code={ex.ErrorCode} message={ex.Message}");
                ShowError($"伝票情報の更新に失敗しました。\n{ex.Message}\n\nボンベ情報明細は更新していません。内容を確認のうえ、再度操作してください。");
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"伝票ヘッダーの更新に失敗しました（通信エラー）。message={ex.Message}");
                ShowError($"JustDBとの通信でエラーが発生しました。\n{ex.Message}\n\nボンベ情報明細は更新していません。");
                return;
            }

            var failures = new List<string>();

            foreach (var row in toUpdate)
            {
                try
                {
                    var fields = BuildCylinderFields(row, loadedSlipId);
                    await client.UpdateRecordAsync(Config.JustDb.CylinderTableName, row.RecordId!.Value, fields);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ボンベ情報明細の更新に失敗しました。recordId={row.RecordId} message={ex.Message}");
                    failures.Add($"更新失敗（記号:{row.Symbol} 番号:{row.Number}）：{ex.Message}");
                }
            }

            foreach (var row in toInsert)
            {
                try
                {
                    var fields = BuildCylinderFields(row, loadedSlipId);
                    row.RecordId = await client.InsertRecordAsync(Config.JustDb.CylinderTableName, fields);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ボンベ情報明細の追加に失敗しました。message={ex.Message}");
                    failures.Add($"追加失敗（記号:{row.Symbol} 番号:{row.Number}）：{ex.Message}");
                }
            }

            foreach (var recordId in toDelete)
            {
                try
                {
                    await client.DeleteRecordAsync(Config.JustDb.CylinderTableName, recordId);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ボンベ情報明細の削除に失敗しました。recordId={recordId} message={ex.Message}");
                    failures.Add($"削除失敗（recordId:{recordId}）：{ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                AppLogger.Warn($"伝票の更新は一部失敗しました。slipId={loadedSlipId} " + string.Join(" / ", failures));
                ShowError("伝票ヘッダーは更新されましたが、一部のボンベ情報明細の反映に失敗しました。\n\n" +
                    string.Join("\n", failures) +
                    "\n\n内容を確認し、必要であれば再度操作してください。");

                // 一部失敗時は現在のサーバー側の状態を再表示し、内容を確認・修正できるようにする。
                await LoadBySlipIdAsync(client, loadedSlipId);
            }
            else
            {
                AppLogger.Info($"伝票の更新が完了しました。slipId={loadedSlipId}");
                MessageBox.Show("更新が完了しました。", "更新完了", MessageBoxButton.OK, MessageBoxImage.Information);

                // エラーなく完了した場合は画面をクリアし、続けて次の伝票を検索・修正できるようにする。
                SlipIdInput = string.Empty;
                ClearLoadedSlip();
            }
        }
        catch (Exception ex)
        {
            // async Task のコマンドはfire-and-forgetで実行されるため、
            // 内側のtry/catchで捕捉されなかった例外はここで捕捉しないと利用者に一切通知されない。
            AppLogger.Error($"更新処理中に予期しないエラーが発生しました。message={ex.Message}");
            ShowError($"更新処理中に予期しないエラーが発生しました。\n{ex.Message}\n\nシステム課へご連絡ください。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeleteSlipAsync()
    {
        if (_slipRecordId is null || _loadedSlipId is null)
        {
            return;
        }

        var slipId = _loadedSlipId;
        var slipRecordId = _slipRecordId.Value;

        var confirmMessage = $"伝票ID「{slipId}」を削除します。\nこの伝票に紐づくボンベ情報明細もすべて削除されます。\nこの操作は取り消せません。よろしいですか？";
        var confirmResult = MessageBox.Show(confirmMessage, "伝票削除の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmResult != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AppLogger.Info($"伝票の削除操作を開始しました。slipId={slipId}");

            using var client = new JustDbClient(Config.JustDb.BaseUrl, Config.JustDb.ApiKey);

            List<(int RecordId, Dictionary<string, JsonElement> Fields)> cylinderResults;
            try
            {
                cylinderResults = await client.GetRecordsByFieldValueAsync(Config.JustDb.CylinderTableName, CylinderFields.SlipId, slipId);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"削除対象のボンベ情報明細の検索に失敗しました。slipId={slipId} message={ex.Message}");
                ShowError($"ボンベ情報明細の検索に失敗したため、削除を中断しました。\n{ex.Message}");
                return;
            }

            var failures = new List<string>();

            foreach (var (recordId, _) in cylinderResults)
            {
                try
                {
                    await client.DeleteRecordAsync(Config.JustDb.CylinderTableName, recordId);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ボンベ情報明細の削除に失敗しました。recordId={recordId} message={ex.Message}");
                    failures.Add($"recordId={recordId}：{ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                AppLogger.Warn($"伝票削除が一部失敗しました。slipId={slipId} " + string.Join(" / ", failures));
                ShowError("一部のボンベ情報明細の削除に失敗しました。伝票ヘッダーは削除していません。\n\n" +
                    string.Join("\n", failures) +
                    "\n\nシステム課へご連絡ください。");
                await LoadBySlipIdAsync(client, slipId);
                return;
            }

            try
            {
                await client.DeleteRecordAsync(Config.JustDb.SlipTableName, slipRecordId);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"伝票ヘッダーの削除に失敗しました。recordId={slipRecordId} message={ex.Message}");
                ShowError($"ボンベ情報明細は削除しましたが、伝票ヘッダーの削除に失敗しました。\n{ex.Message}\n\nシステム課へご連絡ください。");
                return;
            }

            AppLogger.Info($"伝票の削除が完了しました。slipId={slipId}");
            MessageBox.Show("削除が完了しました。", "削除完了", MessageBoxButton.OK, MessageBoxImage.Information);
            SlipIdInput = string.Empty;
            ClearLoadedSlip();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"削除処理中に予期しないエラーが発生しました。message={ex.Message}");
            ShowError($"削除処理中に予期しないエラーが発生しました。\n{ex.Message}\n\nシステム課へご連絡ください。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanModify() => IsLoaded && !IsBusy;

    /// <summary>指定の伝票IDで伝票ヘッダー・ボンベ情報明細を検索し、フォームへ読み込む。</summary>
    private async Task<bool> LoadBySlipIdAsync(JustDbClient client, string slipId)
    {
        List<(int RecordId, Dictionary<string, JsonElement> Fields)> slipResults;
        try
        {
            slipResults = await client.GetRecordsByFieldValueAsync(Config.JustDb.SlipTableName, SlipFields.SlipId, slipId);
        }
        catch (JustDbApiException ex)
        {
            AppLogger.Error($"伝票検索に失敗しました。slipId={slipId} code={ex.ErrorCode} message={ex.Message}");
            ShowError($"伝票の検索に失敗しました。\n{ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"伝票検索に失敗しました（通信エラー）。slipId={slipId} message={ex.Message}");
            ShowError($"JustDBとの通信でエラーが発生しました。\n{ex.Message}");
            return false;
        }

        if (slipResults.Count == 0)
        {
            ClearLoadedSlip();
            ShowWarning($"伝票ID「{slipId}」に該当する伝票が見つかりませんでした。");
            return false;
        }

        if (slipResults.Count > 1)
        {
            AppLogger.Warn($"伝票IDが重複しています。slipId={slipId} 件数={slipResults.Count}");
            ShowWarning($"伝票ID「{slipId}」に該当する伝票が複数件見つかりました。データが重複している可能性があります。先頭の1件を表示します。システム課へご連絡ください。");
        }

        var (slipRecordId, slipFields) = slipResults[0];

        List<(int RecordId, Dictionary<string, JsonElement> Fields)> cylinderResults;
        try
        {
            cylinderResults = await client.GetRecordsByFieldValueAsync(Config.JustDb.CylinderTableName, CylinderFields.SlipId, slipId);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ボンベ情報明細の検索に失敗しました。slipId={slipId} message={ex.Message}");
            ShowError($"ボンベ情報明細の検索に失敗しました。\n{ex.Message}");
            return false;
        }

        LoadSlip(slipId, slipRecordId, slipFields, cylinderResults);
        AppLogger.Info($"伝票を読み込みました。slipId={slipId} recordId={slipRecordId} 明細件数={cylinderResults.Count}");
        return true;
    }

    private void LoadSlip(
        string slipId,
        int slipRecordId,
        Dictionary<string, JsonElement> fields,
        List<(int RecordId, Dictionary<string, JsonElement> Fields)> cylinderResults)
    {
        _slipRecordId = slipRecordId;
        _loadedSlipId = slipId;
        DisplayedSlipId = slipId;

        var store = JustDbFieldParser.ExtractText(fields[SlipFields.Store]);
        EnsureOption(Stores, store);
        SelectedStore = store; // Destinationsを現在のマスタデータから再構築する

        var destination = JustDbFieldParser.ExtractText(fields[SlipFields.Destination]);
        EnsureOption(Destinations, destination);
        SelectedDestination = destination;

        DeliveryDate = JustDbFieldParser.ExtractDate(fields[SlipFields.DeliveryDate]);
        ManagementType = JustDbFieldParser.ExtractText(fields[SlipFields.ManagementType]);
        FillingDate = JustDbFieldParser.ExtractDate(fields[SlipFields.FillingDate]);

        var fillingStation = JustDbFieldParser.ExtractText(fields[SlipFields.FillingStation]);
        EnsureOption(FillingStations, fillingStation);
        SelectedFillingStation = fillingStation;

        DeliveredQuantity = JustDbFieldParser.ExtractText(fields[SlipFields.DeliveredQuantity]);
        RemainingQuantity = JustDbFieldParser.ExtractText(fields[SlipFields.RemainingQuantity]);

        InspectionBinCapacity1 = ExtractCapacityOrNull(fields[SlipFields.InspectionBinCapacity1]);
        InspectionBinCount1 = InspectionBinCapacity1 is null ? string.Empty : JustDbFieldParser.ExtractText(fields[SlipFields.InspectionBinCount1]);
        InspectionBinCapacity2 = ExtractCapacityOrNull(fields[SlipFields.InspectionBinCapacity2]);
        InspectionBinCount2 = InspectionBinCapacity2 is null ? string.Empty : JustDbFieldParser.ExtractText(fields[SlipFields.InspectionBinCount2]);
        InspectionBinCapacity3 = ExtractCapacityOrNull(fields[SlipFields.InspectionBinCapacity3]);
        InspectionBinCount3 = InspectionBinCapacity3 is null ? string.Empty : JustDbFieldParser.ExtractText(fields[SlipFields.InspectionBinCount3]);

        InspectionBinRemaining = JustDbFieldParser.ExtractText(fields[SlipFields.InspectionBinRemaining]);
        PressureFillingAmount = JustDbFieldParser.ExtractText(fields[SlipFields.PressureFillingAmount]);
        Remarks = JustDbFieldParser.ExtractText(fields[SlipFields.Remarks]);

        CylinderRows.Clear();
        foreach (var (recordId, cylinderFields) in cylinderResults)
        {
            CylinderRows.Add(new CylinderRow
            {
                RecordId = recordId,
                Symbol = JustDbFieldParser.ExtractText(cylinderFields[CylinderFields.Symbol]),
                Number = JustDbFieldParser.ExtractText(cylinderFields[CylinderFields.Number]),
                Capacity = ExtractCapacityOrNull(cylinderFields[CylinderFields.Capacity]),
                DeliveryType = JustDbFieldParser.ExtractText(cylinderFields[CylinderFields.DeliveryType]),
                InspectionBin = JustDbFieldParser.ExtractBool(cylinderFields[CylinderFields.InspectionBin]),
                PressureBin = JustDbFieldParser.ExtractBool(cylinderFields[CylinderFields.PressureBin]),
            });
        }

        _originalCylinderRecordIds = cylinderResults.Select(r => r.RecordId).ToHashSet();

        RecalculateDerivedValues();
        IsLoaded = true;
    }

    private void ClearLoadedSlip()
    {
        _slipRecordId = null;
        _loadedSlipId = null;
        DisplayedSlipId = null;
        IsLoaded = false;

        SelectedStore = null;
        SelectedDestination = null;
        ManagementType = "ボンベ";
        DeliveryDate = null;
        FillingDate = null;
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
        _originalCylinderRecordIds = new HashSet<int>();

        RecalculateDerivedValues();
    }

    private bool ConfirmUpdate(int updateCount, int insertCount, int deleteCount)
    {
        var lines = new List<string>
        {
            $"伝票ID：{_loadedSlipId}",
            $"配送日：{DeliveryDate:yyyy/MM/dd}",
            $"販売店：{SelectedStore}",
            $"配送先：{SelectedDestination}",
            string.Empty,
            $"ボンベ情報明細：更新{updateCount}件／追加{insertCount}件／削除{deleteCount}件",
        };

        var message = "以下の内容で更新します。よろしいですか？\n\n" + string.Join("\n", lines);
        var result = MessageBox.Show(message, "更新確認", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>選択肢系フィールドの値が「0」（未選択）の場合はnullとして扱う（検査ビン容量・ボンベ容量）。</summary>
    private static string? ExtractCapacityOrNull(JsonElement element)
    {
        var text = JustDbFieldParser.ExtractText(element);
        return string.IsNullOrEmpty(text) || text == "0" ? null : text;
    }

    /// <summary>マスタデータ変更後の履歴データ等で選択肢一覧に値が存在しない場合、表示が消えないよう一時的に追加する。</summary>
    private static void EnsureOption(ObservableCollection<string> options, string value)
    {
        if (!string.IsNullOrEmpty(value) && !options.Contains(value))
        {
            options.Add(value);
        }
    }
}
