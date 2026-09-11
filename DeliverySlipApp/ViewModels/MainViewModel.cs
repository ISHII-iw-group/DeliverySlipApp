using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using DeliverySlipApp.Models;
using DeliverySlipApp.Services;

namespace DeliverySlipApp.ViewModels;

public partial class MainViewModel : SlipHeaderViewModelBase
{
    public MainViewModel()
    {
        InitializeNewSlip();
    }

    protected override void OnIsBusyChangedCore(bool value) => RegisterCommand.NotifyCanExecuteChanged();

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

            using var client = new JustDbClient(Config.JustDb.BaseUrl, Config.JustDb.ApiKey);

            int slipRecordId;
            try
            {
                var slipFields = BuildSlipFields();
                slipRecordId = await client.InsertRecordAsync(Config.JustDb.SlipTableName, slipFields);
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
                var record = await client.GetRecordFieldsAsync(Config.JustDb.SlipTableName, slipRecordId);
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
                    var id = await client.InsertRecordAsync(Config.JustDb.CylinderTableName, fields);
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
                await client.DeleteRecordAsync(Config.JustDb.CylinderTableName, id);
            }
            catch
            {
                success = false;
            }
        }

        try
        {
            await client.DeleteRecordAsync(Config.JustDb.SlipTableName, slipRecordId);
        }
        catch
        {
            success = false;
        }

        return success;
    }

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
