using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeliverySlipApp.Services;
using Microsoft.Win32;

namespace DeliverySlipApp.ViewModels;

public partial class ExportViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public ExportViewModel()
    {
        _config = AppConfig.Load();
    }

    [ObservableProperty]
    private DateTime? startDate = DateTime.Today;

    [ObservableProperty]
    private DateTime? endDate = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportSlipCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCylinderCommand))]
    private bool isBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportSlipAsync()
    {
        try
        {
            if (!TryValidatePeriod(out var message))
            {
                ShowError(message);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excelブック (*.xlsx)|*.xlsx",
                FileName = $"伝票データ_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.xlsx",
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            try
            {
                AppLogger.Info($"伝票データのExcel出力を開始しました。期間={StartDate:yyyy-MM-dd}〜{EndDate:yyyy-MM-dd}");

                using var client = new JustDbClient(_config.JustDb.BaseUrl, _config.JustDb.ApiKey);
                var count = await ExcelExportService.ExportSlipDataAsync(
                    client, _config.JustDb.SlipTableName, _config.JustDb.SlipPanelName, _config.JustDb.SlipFilterName,
                    StartDate!.Value, EndDate!.Value, dialog.FileName);

                AppLogger.Info($"伝票データのExcel出力が完了しました。件数={count}");
                ShowInfo($"伝票データを出力しました。（{count}件）\n\n{dialog.FileName}");
            }
            catch (JustDbApiException ex)
            {
                AppLogger.Error($"伝票データの出力に失敗しました。code={ex.ErrorCode} message={ex.Message}");
                ShowError($"JustDBとの通信でエラーが発生しました。\n{ex.Message}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"伝票データの出力に失敗しました。message={ex.Message}");
                ShowError($"出力中にエラーが発生しました。\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
        catch (Exception ex)
        {
            // async Taskの出力コマンドはfire-and-forgetで実行されるため、
            // 内側のtry/catchで捕捉されなかった例外はここで捕捉しないと利用者に一切通知されない。
            AppLogger.Error($"伝票データの出力中に予期しないエラーが発生しました。message={ex.Message}");
            ShowError($"出力処理中に予期しないエラーが発生しました。\n{ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCylinderAsync()
    {
        try
        {
            if (!TryValidatePeriod(out var message))
            {
                ShowError(message);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excelブック (*.xlsx)|*.xlsx",
                FileName = $"ボンベデータ_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.xlsx",
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            try
            {
                AppLogger.Info($"ボンベデータのExcel出力を開始しました。期間={StartDate:yyyy-MM-dd}〜{EndDate:yyyy-MM-dd}");

                using var client = new JustDbClient(_config.JustDb.BaseUrl, _config.JustDb.ApiKey);
                var count = await ExcelExportService.ExportCylinderDataAsync(
                    client, _config.JustDb.CylinderTableName, _config.JustDb.CylinderPanelName, _config.JustDb.CylinderFilterName,
                    StartDate!.Value, EndDate!.Value, dialog.FileName);

                AppLogger.Info($"ボンベデータのExcel出力が完了しました。件数={count}");
                ShowInfo($"ボンベデータを出力しました。（{count}件）\n\n{dialog.FileName}");
            }
            catch (JustDbApiException ex)
            {
                AppLogger.Error($"ボンベデータの出力に失敗しました。code={ex.ErrorCode} message={ex.Message}");
                ShowError($"JustDBとの通信でエラーが発生しました。\n{ex.Message}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"ボンベデータの出力に失敗しました。message={ex.Message}");
                ShowError($"出力中にエラーが発生しました。\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ボンベデータの出力中に予期しないエラーが発生しました。message={ex.Message}");
            ShowError($"出力処理中に予期しないエラーが発生しました。\n{ex.Message}");
        }
    }

    private bool CanExport() => !IsBusy;

    private bool TryValidatePeriod(out string message)
    {
        if (StartDate is null || EndDate is null)
        {
            message = "期間（開始日・終了日）を指定してください。";
            return false;
        }

        if (StartDate > EndDate)
        {
            message = "開始日は終了日以前の日付を指定してください。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static void ShowError(string message)
        => MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

    private static void ShowInfo(string message)
        => MessageBox.Show(message, "確認", MessageBoxButton.OK, MessageBoxImage.Information);
}
