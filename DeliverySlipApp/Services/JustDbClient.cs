using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DeliverySlipApp.Services;

/// <summary>JustDB APIがエラーレスポンスを返したときにスローされる例外。</summary>
public sealed class JustDbApiException : Exception
{
    public JustDbApiException(HttpStatusCode statusCode, string? errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorCode { get; }
}

/// <summary>
/// JustDB REST API（伝票管理・ボンベ管理テーブル向け）の最小限のクライアント。
/// 仕様は「API連携利用ヘルプ」（1.4 APIエンドポイント）に基づく。
/// </summary>
public sealed class JustDbClient : IDisposable
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public JustDbClient(string baseUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("JustDBのBaseUrlが設定されていません。", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("JustDBのApiKeyが設定されていません。appsettings.json を確認してください。", nameof(apiKey));
        }

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>レコードを1件挿入し、内部レコードID（recordId）を返す。</summary>
    public async Task<int> InsertRecordAsync(string tableName, IDictionary<string, object?> fields, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["records"] = new[]
            {
                new Dictionary<string, object?> { ["record"] = fields },
            },
        };

        using var response = await SendAsync(HttpMethod.Post, $"sites/api/services/v1/tables/{tableName}/records/", body, ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<InsertResponse>(json, ResponseJsonOptions);
        if (result is null || result.RecordIds.Length == 0)
        {
            throw new InvalidOperationException("レコード登録のレスポンスが不正です（recordIdsが空です）。");
        }

        return result.RecordIds[0];
    }

    /// <summary>レコードを1件取得し、record内のフィールド値を返す。</summary>
    public async Task<Dictionary<string, JsonElement>> GetRecordFieldsAsync(string tableName, int recordId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"sites/api/services/v1/tables/{tableName}/records/{recordId}", ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);

        // レコード1件取得APIのレスポンスは配列でラップされている
        var list = JsonSerializer.Deserialize<List<RecordGetResponse>>(json, ResponseJsonOptions);
        var record = list?.FirstOrDefault()
            ?? throw new InvalidOperationException("レコード取得のレスポンスが不正です。");

        return record.Record;
    }

    /// <summary>
    /// 「フィールド値検索」（`_{fieldId}=値` クエリパラメータ）を用いて、指定フィールドが値と完全一致するレコードを取得する。
    /// 文字列（1行）・数値・採番（全体）・枝番・メール型フィールドでのみ利用可能（日付時刻フィールドは対象外）。
    /// 伝票ID（採番型）・ボンベ管理側の伝票ID（枝番型）はこの方式で検索できることを確認済み。
    /// </summary>
    public async Task<List<(int RecordId, Dictionary<string, JsonElement> Fields)>> GetRecordsByFieldValueAsync(
        string tableName, string fieldId, string value, CancellationToken ct = default)
    {
        var url = $"sites/api/services/v1/tables/{tableName}/records/?_{fieldId}={Uri.EscapeDataString(value)}";

        using var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<RecordGetResponse>>(json, ResponseJsonOptions)
            ?? throw new InvalidOperationException("フィールド値検索のレスポンスが不正です。");

        return list.Select(r => (r.RecordId, r.Record)).ToList();
    }

    /// <summary>
    /// レコードを1件更新する。
    /// 注意：JustDBの公式API仕様書（api_help.pdf）で確認できていないため、
    /// レコード挿入（POST）・削除（DELETE）と同じ `{"records":[{...}]}` 形式に倣った未検証の実装。
    /// 実際に呼び出してエラーになる場合はレスポンスのエラーメッセージ（JustDbApiException.Message）を確認すること。
    /// </summary>
    public async Task UpdateRecordAsync(string tableName, int recordId, IDictionary<string, object?> fields, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["records"] = new[]
            {
                new Dictionary<string, object?> { ["recordId"] = recordId, ["record"] = fields },
            },
        };

        using var response = await SendAsync(HttpMethod.Put, $"sites/api/services/v1/tables/{tableName}/records/", body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>
    /// JustDB管理画面側にあらかじめ設定済みのフィルター（配送日／日付に「以後」「以前」の2条件をAND設定したもの）を用いて、
    /// 指定期間に該当するレコードのみをサーバーサイドで絞り込んで取得する（動的指定可能検索）。
    /// レコード複数件取得APIは1回あたり最大100件（未指定時は10件）しか返さないため、
    /// offset/limitを使ってページングしながら該当レコードを全件取得する。
    /// </summary>
    public async Task<List<Dictionary<string, JsonElement>>> GetRecordFieldsByPeriodFilterAsync(
        string tableName,
        string panelName,
        string filterName,
        string dateFieldName,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(panelName) || string.IsNullOrWhiteSpace(filterName))
        {
            throw new InvalidOperationException(
                "Excel出力用のパネル／フィルター設定が appsettings.json にありません。設定を確認してください。");
        }

        const int pageSize = 100;
        var result = new List<Dictionary<string, JsonElement>>();
        var offset = 0;

        var startParam = Uri.EscapeDataString(ToApiDate(startDate));
        var endParam = Uri.EscapeDataString(ToApiDate(endDate));

        while (true)
        {
            var url = $"sites/api/services/v1/tables/{tableName}/records/" +
                $"?panelName={panelName}&filterName={filterName}" +
                $"&_{dateFieldName}-0={startParam}&_{dateFieldName}-1={endParam}" +
                $"&offset={offset}&limit={pageSize}";

            using var response = await _http.GetAsync(url, ct);
            await EnsureSuccessAsync(response, ct);

            var json = await response.Content.ReadAsStringAsync(ct);
            var list = JsonSerializer.Deserialize<List<RecordGetResponse>>(json, ResponseJsonOptions)
                ?? throw new InvalidOperationException("レコード一覧取得のレスポンスが不正です。");

            result.AddRange(list.Select(r => r.Record));

            if (list.Count < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        return result;
    }

    private static string ToApiDate(DateTime date)
        => date.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) + "+09:00";

    /// <summary>レコードを1件削除する。</summary>
    public async Task DeleteRecordAsync(string tableName, int recordId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["records"] = new[]
            {
                new Dictionary<string, object?> { ["recordId"] = recordId },
            },
        };

        using var response = await SendAsync(HttpMethod.Delete, $"sites/api/services/v1/tables/{tableName}/records/", body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await _http.SendAsync(request, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        var message = $"JustDB APIとの通信でエラーが発生しました。(HTTP {(int)response.StatusCode})";

        try
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            var error = JsonSerializer.Deserialize<ErrorResponse>(json, ResponseJsonOptions);
            if (error is not null && !string.IsNullOrEmpty(error.Message))
            {
                code = error.Code;
                message = error.Message;
            }
        }
        catch
        {
            // エラーレスポンスがJSON形式でない場合はデフォルトメッセージを使用する
        }

        throw new JustDbApiException(response.StatusCode, code, message);
    }

    public void Dispose() => _http.Dispose();

    private sealed class InsertResponse
    {
        public int[] RecordIds { get; set; } = Array.Empty<int>();
    }

    private sealed class ErrorResponse
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }

    private sealed class RecordGetResponse
    {
        public int RecordId { get; set; }
        public Dictionary<string, JsonElement> Record { get; set; } = new();
    }
}
