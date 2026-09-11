namespace DeliverySlipApp.Services;

/// <summary>伝票管理テーブル（table_1785400222）のフィールドID。</summary>
public static class SlipFields
{
    public const string SlipId = "field_20002";
    public const string DeliveryDate = "field_20003";
    public const string Store = "field_20004";
    public const string Destination = "field_20005";
    public const string ManagementType = "field_20006";
    public const string FillingDate = "field_20007";
    public const string FillingStation = "field_20008";
    public const string DeliveredQuantity = "field_20009";
    public const string RemainingQuantity = "field_20010";
    public const string InspectionBinCapacity1 = "field_20011";
    public const string InspectionBinCount1 = "field_20012";
    public const string InspectionBinCapacity2 = "field_20013";
    public const string InspectionBinCount2 = "field_20014";
    public const string InspectionBinCapacity3 = "field_20015";
    public const string InspectionBinCount3 = "field_20016";
    public const string InspectionBinRemaining = "field_20018";
    public const string PressureFillingAmount = "field_20021";
    public const string Remarks = "field_20022";
    public const string InspectionBinCapacityTotal = "field_1785401257";
    public const string InspectionBinUsage = "field_1785401362";
    public const string FillingQuantity = "field_1785401460";
}

/// <summary>ボンベ管理テーブル（table_1788918808）のフィールドID。</summary>
public static class CylinderFields
{
    public const string CylinderId = "field_1788918843";
    public const string SlipId = "field_1788918872";
    public const string Symbol = "field_1788918913";
    public const string Number = "field_1788918978";
    public const string Capacity = "field_1788919100";
    public const string DeliveryType = "field_1788919183";
    public const string InspectionBin = "field_1788919234";
    public const string PressureBin = "field_1788919262";
    public const string Store = "field_1788920501";
    public const string Destination = "field_1788920533";
    public const string DeliveryDate = "field_1788920544";
}
