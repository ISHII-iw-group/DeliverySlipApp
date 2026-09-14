# ガスボンベ充填伝票管理システム

設計書（`ガスボンベ充填伝票管理システム_設計書.md`）に基づいて実装した、伝票情報・ボンベ情報の入力とJustDBへの登録を行うWindowsデスクトップアプリケーション（C# / WPF / .NET 8）です。

## フォルダ構成

```
src/DeliverySlipApp/        アプリ本体（Visual Studio / dotnet CLI で開けます）
  appsettings.json          JustDB接続設定（APIキーなど）
  MasterData/                配送先マスタ・充填所マスタ（Excel）
  Models/                    画面上のデータモデル
  Services/                  JustDB APIクライアント・マスタ読込・設定読込・ログ出力
  ViewModels/                画面の入力チェック・自動計算・登録処理ロジック
  MainWindow.xaml            画面レイアウト
```

## セットアップ

### 1. APIキーの設定

`src/DeliverySlipApp/appsettings.json` を開き、`ApiKey` にJustDBの運用管理画面で発行したAPI-Keyを設定してください。

```json
{
  "JustDb": {
    "BaseUrl": "https://iw-group.just-db.com",
    "ApiKey": "発行したAPI-Keyをここに設定",
    "SlipTableName": "table_1785400222",
    "CylinderTableName": "table_1788918808"
  }
}
```

API-Keyには、伝票管理・ボンベ管理の両テーブルに対する「閲覧・追加・削除」権限が必要です（編集は行わないため「編集」権限は不要です）。

配布後は `appsettings.json` を実行フォルダ（exeと同じ場所）に置いてください。ソース管理にAPIキーを含めないよう注意してください。

### 2. マスタファイルの配置・更新

`src/DeliverySlipApp/MasterData/` フォルダに以下のファイルを配置します（ビルド時に実行フォルダへ自動コピーされます）。

- `配送先マスタ*.xlsx`（1列目：販売店、2列目：配送先。1行目はヘッダー）
- `充填所マスタ*.xlsx`（1列目：充填所名。1行目はヘッダー）

年1回程度の更新時は、同じフォルダに新しいファイル（例：`配送先マスタ_20270901.xlsx`）を追加するだけで構いません。ファイル名が「配送先マスタ」「充填所マスタ」で始まる最新（更新日時が最も新しい）ファイルを自動で読み込みます。

## ビルド・実行

```
cd src/DeliverySlipApp
dotnet build
dotnet run
```

## 配布用に発行する

担当者のPCにインストール不要の単一exeとして配布する場合：

```
cd src/DeliverySlipApp
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`bin/Release/net8.0-windows/win-x64/publish/` に生成される `DeliverySlipApp.exe` と、同フォルダに `appsettings.json`・`MasterData` フォルダを一緒に配布してください。

## ログ

操作履歴・エラー内容は実行フォルダ配下の `Logs/yyyy-MM-dd.log` に自動保存されます（自動削除は行いません）。障害発生時はこのログを確認のうえ、必要に応じてシステム課へ連絡してください。

## 実装した業務ロジックの要点（設計書7章対応）

- 検査ビン容量計 = 検査ビン容量1×本数1 + 容量2×本数2 + 容量3×本数3
- 検査ビン使用量 = 検査ビン容量計 − 検査ビン残数量
- 充填数量 = 納入数量 − 残数量 − 検査ビン使用量
- 上記の値がマイナスの場合は警告を表示し登録を中断
- ボンベ情報明細のうち全項目空欄の行は登録対象から除外
- 伝票ヘッダー登録 → GETで伝票ID取得 → ボンベ明細を1件ずつ登録という順で処理し、ボンベ明細登録中にエラーが発生した場合は登録済みの明細・伝票ヘッダーをDELETEでロールバック（ロールバック失敗時はシステム課へ連絡するメッセージを表示）
- 登録が正常に完了した場合のみ、入力フォームをクリアして次の伝票をすぐに入力できる状態にする
