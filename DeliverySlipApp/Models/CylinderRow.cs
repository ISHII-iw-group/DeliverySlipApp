using CommunityToolkit.Mvvm.ComponentModel;

namespace DeliverySlipApp.Models;

/// <summary>ボンベ情報明細の1行。</summary>
public partial class CylinderRow : ObservableObject
{
    /// <summary>JustDB上のrecordId。参照・編集画面で読み込んだ既存行にのみ設定される（新規追加行はnull）。</summary>
    public int? RecordId { get; set; }

    /// <summary>ボンベ記号（自由入力・大文字英字のみ）。</summary>
    [ObservableProperty]
    private string symbol = string.Empty;

    /// <summary>ボンベ番号（数字のみの文字列、最大6桁）。</summary>
    [ObservableProperty]
    private string number = string.Empty;

    /// <summary>ボンベ容量（kgを除いた数値文字列）。</summary>
    [ObservableProperty]
    private string? capacity;

    /// <summary>引渡／引取（未選択では登録不可）。</summary>
    [ObservableProperty]
    private string? deliveryType;

    [ObservableProperty]
    private bool inspectionBin;

    [ObservableProperty]
    private bool pressureBin;

    /// <summary>全項目が空欄かどうか（登録対象外の判定に使用）。</summary>
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Symbol) &&
        string.IsNullOrWhiteSpace(Number) &&
        string.IsNullOrWhiteSpace(Capacity) &&
        string.IsNullOrWhiteSpace(DeliveryType) &&
        !InspectionBin &&
        !PressureBin;
}
