using System.IO;

namespace DeliverySlipApp.Services;

/// <summary>
/// 操作履歴・エラー内容をファイルに記録するロガー。
/// ログは Logs フォルダに日付単位で保存し、保存期間は無期限（自動削除は行わない）。
/// </summary>
public static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(folder);

            var filePath = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{level}\t{message.Replace("\r", " ").Replace("\n", " / ")}";

            lock (SyncRoot)
            {
                File.AppendAllLines(filePath, new[] { line });
            }
        }
        catch
        {
            // ログ出力自体の失敗で業務処理を止めないようにする
        }
    }
}
