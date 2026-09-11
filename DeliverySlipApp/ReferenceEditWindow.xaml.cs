using System.Windows;
using DeliverySlipApp.ViewModels;

namespace DeliverySlipApp;

public partial class ReferenceEditWindow : Window
{
    public ReferenceEditWindow()
    {
        InitializeComponent();
        DataContext = new SlipReferenceEditViewModel();
    }
}
