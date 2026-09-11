using System.IO;
using ClosedXML.Excel;

namespace DeliverySlipApp.Services;

public sealed class MasterDataService
{
    private const string MasterDataFolderName = "MasterData";

    /// <summary>販売店 → 配送先一覧。</summary>
    public IReadOnlyDictionary<string, List<string>> DestinationsByStore { get; private set; }
        = new Dictionary<string, List<string>>();

    /// <summary>販売店一覧（配送先マスタの出現順）。</summary>
    public IReadOnlyList<string> Stores { get; private set; } = Array.Empty<string>();

    /// <summary>充填所一覧。</summary>
    public IReadOnlyList<string> FillingStations { get; private set; } = Array.Empty<string>();

    public void Load()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, MasterDataFolderName);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"マスタファイル用フォルダが見つかりません。({folder})\n配送先マスタ・充填所マスタのExcelファイルを配置してください。");
        }

        var destinationFile = FindLatestMasterFile(folder, "配送先マスタ");
        var stationFile = FindLatestMasterFile(folder, "充填所マスタ");

        LoadDestinations(destinationFile);
        LoadFillingStations(stationFile);
    }

    private static string FindLatestMasterFile(string folder, string prefix)
    {
        var file = Directory.EnumerateFiles(folder, prefix + "*.xlsx")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (file is null)
        {
            throw new FileNotFoundException(
                $"「{prefix}」で始まるExcelファイルが {folder} に見つかりません。");
        }

        return file;
    }

    private void LoadDestinations(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();

        var stores = new List<string>();
        var map = new Dictionary<string, List<string>>();

        var firstRow = true;
        foreach (var row in sheet.RowsUsed())
        {
            if (firstRow)
            {
                // 1行目はヘッダー（販売店／配送先）のためスキップ
                firstRow = false;
                continue;
            }

            var store = row.Cell(1).GetString().Trim();
            var destination = row.Cell(2).GetString().Trim();

            if (string.IsNullOrEmpty(store) || string.IsNullOrEmpty(destination))
            {
                continue;
            }

            if (!map.TryGetValue(store, out var list))
            {
                list = new List<string>();
                map[store] = list;
                stores.Add(store);
            }

            list.Add(destination);
        }

        Stores = stores;
        DestinationsByStore = map;
    }

    private void LoadFillingStations(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();

        var stations = new List<string>();
        var firstRow = true;
        foreach (var row in sheet.RowsUsed())
        {
            if (firstRow)
            {
                firstRow = false;
                continue;
            }

            var value = row.Cell(1).GetString().Trim();
            if (!string.IsNullOrEmpty(value))
            {
                stations.Add(value);
            }
        }

        FillingStations = stations;
    }
}
