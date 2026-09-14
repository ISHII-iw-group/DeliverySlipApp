using System.Text.Json;
using ClosedXML.Excel;

namespace DeliverySlipApp.Services;

/// <summary>JustDBから取得したデータをExcelファイルへ出力する処理。</summary>
public static class ExcelExportService
{
    /// <summary>
    /// 伝票管理テーブルのデータを、JustDB側に設定済みのフィルター（配送日の期間絞り込み）を用いて取得しExcel出力する。
    /// 「使用量計」は充填数量＋検査ビン使用量の計算値、「年」「月」「日」は配送日の各部分を格納する。
    /// </summary>
    /// <returns>出力した件数。</returns>
    public static async Task<int> ExportSlipDataAsync(
        JustDbClient client,
        string slipTableName,
        string panelName,
        string filterName,
        DateTime startDate,
        DateTime endDate,
        string filePath,
        CancellationToken ct = default)
    {
        var records = await client.GetRecordFieldsByPeriodFilterAsync(
            slipTableName, panelName, filterName, SlipFields.DeliveryDate, startDate, endDate, ct);

        var rows = records
            .Select(fields => (Fields: fields, DeliveryDate: JustDbFieldParser.ExtractDate(GetOrDefault(fields, SlipFields.DeliveryDate))))
            .Where(x => x.DeliveryDate is not null)
            .OrderBy(x => x.DeliveryDate)
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("伝票データ");

        string[] headers =
        {
            "配送日", "伝票ID", "販売店", "配送先", "管理方法", "充填日", "充填所",
            "充填数量", "うち耐圧充填量", "備考", "納入数量", "残数量",
            "検査ビン残数量", "検査ビン容量計", "検査ビン使用量", "使用量計", "年", "月", "日",
        };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        var maxRemarksLineWidth = GetMaxLineWidth("備考");
        foreach (var (fields, deliveryDateValue) in rows)
        {
            var deliveryDate = deliveryDateValue!.Value;
            var fillingQuantity = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.FillingQuantity));
            var inspectionUsage = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.InspectionBinUsage));

            SetDateCell(sheet.Cell(rowIndex, 1), deliveryDate);
            sheet.Cell(rowIndex, 2).Value = JustDbFieldParser.ExtractNumberingValue(GetOrDefault(fields, SlipFields.SlipId));
            sheet.Cell(rowIndex, 3).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, SlipFields.Store));
            sheet.Cell(rowIndex, 4).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, SlipFields.Destination));
            sheet.Cell(rowIndex, 5).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, SlipFields.ManagementType));

            var fillingDate = JustDbFieldParser.ExtractDate(GetOrDefault(fields, SlipFields.FillingDate));
            if (fillingDate is not null)
            {
                SetDateCell(sheet.Cell(rowIndex, 6), fillingDate.Value);
            }

            sheet.Cell(rowIndex, 7).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, SlipFields.FillingStation));
            sheet.Cell(rowIndex, 8).Value = fillingQuantity;
            sheet.Cell(rowIndex, 9).Value = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.PressureFillingAmount));
            var remarksCell = sheet.Cell(rowIndex, 10);
            var remarksText = JustDbFieldParser.ExtractText(GetOrDefault(fields, SlipFields.Remarks));
            remarksCell.Value = remarksText;
            remarksCell.Style.Alignment.WrapText = true;
            remarksCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            maxRemarksLineWidth = Math.Max(maxRemarksLineWidth, GetMaxLineWidth(remarksText));
            sheet.Cell(rowIndex, 11).Value = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.DeliveredQuantity));
            sheet.Cell(rowIndex, 12).Value = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.RemainingQuantity));
            sheet.Cell(rowIndex, 13).Value = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.InspectionBinRemaining));
            sheet.Cell(rowIndex, 14).Value = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, SlipFields.InspectionBinCapacityTotal));
            sheet.Cell(rowIndex, 15).Value = inspectionUsage;
            sheet.Cell(rowIndex, 16).Value = fillingQuantity + inspectionUsage;
            sheet.Cell(rowIndex, 17).Value = deliveryDate.Year;
            sheet.Cell(rowIndex, 18).Value = deliveryDate.Month;
            sheet.Cell(rowIndex, 19).Value = deliveryDate.Day;

            rowIndex++;
        }

        sheet.Columns().AdjustToContents();

        // 備考列（J列）はAdjustToContentsが改行を考慮せず1行分の長さで幅を決め、かつ全角文字を
        // 半角文字と同じ幅として計算してしまうため、実際の表示幅と合わない。
        // 全角=2/半角=1でカウントした最長行の幅を基準に、独自に列幅を算出する。
        const double remarksWidthPadding = 2;
        const double minRemarksColumnWidth = 8.28;
        const double maxRemarksColumnWidth = 40;
        sheet.Column(10).Width = Math.Clamp(maxRemarksLineWidth + remarksWidthPadding, minRemarksColumnWidth, maxRemarksColumnWidth);

        workbook.SaveAs(filePath);

        return rows.Count;
    }

    /// <summary>
    /// ボンベ管理テーブルのデータを、JustDB側に設定済みのフィルター（日付の期間絞り込み）を用いて取得しExcel出力する。
    /// 「年」「月」「日」は日付の各部分、「集計用」は固定値1を格納する。
    /// </summary>
    /// <returns>出力した件数。</returns>
    public static async Task<int> ExportCylinderDataAsync(
        JustDbClient client,
        string cylinderTableName,
        string panelName,
        string filterName,
        DateTime startDate,
        DateTime endDate,
        string filePath,
        CancellationToken ct = default)
    {
        var records = await client.GetRecordFieldsByPeriodFilterAsync(
            cylinderTableName, panelName, filterName, CylinderFields.DeliveryDate, startDate, endDate, ct);

        var rows = records
            .Select(fields => (Fields: fields, Date: JustDbFieldParser.ExtractDate(GetOrDefault(fields, CylinderFields.DeliveryDate))))
            .Where(x => x.Date is not null)
            .OrderBy(x => x.Date)
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("ボンベデータ");

        string[] headers =
        {
            "ボンベID", "伝票ID", "ボンベ記号", "ボンベ番号", "ボンベ容量", "引渡・引取",
            "検査ビン", "耐圧ビン", "販売店", "配送先", "日付", "年", "月", "日", "集計用",
        };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        foreach (var (fields, dateValue) in rows)
        {
            var date = dateValue!.Value;

            sheet.Cell(rowIndex, 1).Value = JustDbFieldParser.ExtractNumberingValue(GetOrDefault(fields, CylinderFields.CylinderId));
            sheet.Cell(rowIndex, 2).Value = JustDbFieldParser.ExtractNumberingValue(GetOrDefault(fields, CylinderFields.SlipId));
            sheet.Cell(rowIndex, 3).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, CylinderFields.Symbol));
            sheet.Cell(rowIndex, 4).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, CylinderFields.Number));
            sheet.Cell(rowIndex, 5).Value = JustDbFieldParser.ExtractDecimalOrZero(GetOrDefault(fields, CylinderFields.Capacity));
            sheet.Cell(rowIndex, 6).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, CylinderFields.DeliveryType));
            sheet.Cell(rowIndex, 7).Value = JustDbFieldParser.ExtractBool(GetOrDefault(fields, CylinderFields.InspectionBin));
            sheet.Cell(rowIndex, 8).Value = JustDbFieldParser.ExtractBool(GetOrDefault(fields, CylinderFields.PressureBin));
            sheet.Cell(rowIndex, 9).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, CylinderFields.Store));
            sheet.Cell(rowIndex, 10).Value = JustDbFieldParser.ExtractText(GetOrDefault(fields, CylinderFields.Destination));
            SetDateCell(sheet.Cell(rowIndex, 11), date);
            sheet.Cell(rowIndex, 12).Value = date.Year;
            sheet.Cell(rowIndex, 13).Value = date.Month;
            sheet.Cell(rowIndex, 14).Value = date.Day;
            sheet.Cell(rowIndex, 15).Value = 1;

            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);

        return rows.Count;
    }

    private static void SetDateCell(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "yyyy/mm/dd";
    }

    private static JsonElement GetOrDefault(Dictionary<string, JsonElement> fields, string key)
        => fields.TryGetValue(key, out var value) ? value : default;

    /// <summary>改行で区切られた各行のうち、最も幅が広い行の幅（半角=1/全角=2換算）を返す。</summary>
    private static double GetMaxLineWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var maxWidth = 0.0;
        foreach (var line in text.Split('\n'))
        {
            var width = MeasureTextWidth(line.TrimEnd('\r'));
            if (width > maxWidth)
            {
                maxWidth = width;
            }
        }

        return maxWidth;
    }

    /// <summary>文字列の表示幅を、半角文字=1・全角文字=2として算出する。</summary>
    private static double MeasureTextWidth(string text)
    {
        var width = 0.0;
        foreach (var c in text)
        {
            width += IsFullWidth(c) ? 2 : 1;
        }

        return width;
    }

    /// <summary>ひらがな・カタカナ・漢字・全角記号など、表示幅が半角の2倍となる文字かどうかを判定する。</summary>
    private static bool IsFullWidth(char c)
        => (c >= 'ᄀ' && c <= 'ᅟ')
        || (c >= '⺀' && c <= '꓏')
        || (c >= '가' && c <= '힣')
        || (c >= '豈' && c <= '﫿')
        || (c >= '＀' && c <= '｠')
        || (c >= '￠' && c <= '￦');
}
