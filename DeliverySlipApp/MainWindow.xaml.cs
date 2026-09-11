using System.Windows;
using DeliverySlipApp.ViewModels;

namespace DeliverySlipApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        try
        {
            DataContext = new MainViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"アプリケーションの起動に失敗しました。\n{ex.Message}\n\n設定ファイル（appsettings.json）とマスタファイル（MasterDataフォルダ）を確認してください。",
                "起動エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void ExcelExportButton_Click(object sender, RoutedEventArgs e)
    {
        var exportWindow = new ExportWindow
        {
            Owner = this,
        };
        exportWindow.ShowDialog();
    }

    private void ReferenceEditButton_Click(object sender, RoutedEventArgs e)
    {
        var referenceEditWindow = new ReferenceEditWindow
        {
            Owner = this,
        };
        referenceEditWindow.ShowDialog();
    }
}
