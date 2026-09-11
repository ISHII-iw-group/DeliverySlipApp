using System.Windows;
using DeliverySlipApp.ViewModels;

namespace DeliverySlipApp;

public partial class ExportWindow : Window
{
    public ExportWindow()
    {
        InitializeComponent();
        DataContext = new ExportViewModel();
    }
}
