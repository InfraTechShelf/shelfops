# CVAD GUI管理ツール 仕様書（フェーズ1: コア接続層 + コンソール検証版）

## 0. このドキュメントの目的

本ドキュメントは、Citrix Virtual Apps and Desktops (CVAD) オンプレミス環境向けの
GUI管理ツールを開発するための仕様書である。このフェーズ1では、最もリスクの高い
**PowerShell/Citrix SDK 接続層**を実装し、**複数DDCへの接続可否をコンソールアプリで検証する**
ことをゴールとする。WPF GUIはフェーズ2以降で接続層の上に構築する。

Claude Codeはこのドキュメントを唯一の文脈として実装に着手してよい。

---

## 1. プロジェクトの最終目標（背景情報）

- Citrix標準のCitrix Studioが網羅していないBrokerレベルの設定も操作できる、
  `Citrix.Broker.Admin` 系コマンドレットの機能を広くカバーするGUI管理ツールを作る。
- 管理対象のCVADサイトは**複数**存在する。サイトごとの接続設定を保存し、切り替えて使う。
- 運用形態は「**管理端末からのリモート実行**」。各コマンドレットに `-AdminAddress` で
  対象DDC（Delivery Controller）を指定して操作する。

このフェーズ1はその土台となる接続層の確立に限定する。

---

## 2. 確定済みの技術選定（変更不可）

| 項目 | 決定 | 理由 |
|------|------|------|
| ランタイム | **.NET Framework 4.8** | CVAD SDKはWindows PowerShell 5.1 (.NET Framework) 前提。PS7/.NET 8からのスナップイン利用は躓きやすいため避ける。 |
| 言語 | C# | |
| GUIフレームワーク（フェーズ2） | WPF (MVVM) | データバインディングが管理ダッシュボードに適する。 |
| PowerShellホスティング | `System.Management.Automation`（Windows PowerShell 5.1） | |
| Citrix SDKロード方式 | **実行時に動的ロード**（モジュール優先、失敗時スナップイン） | ビルド時にCitrixアセンブリ参照を不要にするため。 |
| 接続認証（フェーズ1） | 統合Windows認証のみ | |
| 接続認証（フェーズ2） | 統合Windows認証 + 別資格情報（都度入力・非永続） | |

---

## 3. 【最重要】環境分離の制約

開発・ビルド環境と、実際の動作検証環境は**完全に分離している**。

### 3.1 Claude Code 実行環境（ビルド環境）
- **Citrix Studioは無い → Citrix SDKは無い**。
- ドメインに参加していない可能性がある。
- ここで可能なこと: **コンパイル / ビルド**、JSON読み書きやアプリ起動の確認。
- ここで**不可能**なこと: 実DDCへの接続検証（SDKもドメインも無いため）。

### 3.2 CVAD管理環境（検証環境）
- Citrix Studioインストール済み → **Citrix SDK利用可能**。
- ドメイン参加済み → 統合Windows認証が機能する。
- ここで初めて「実DDCへの接続可否」を検証できる。
- **この環境にはインストール作業を一切させない**（後述の必須要件）。

### 3.3 ビルドが分離環境で成立する根拠
`System.Management.Automation` は NuGetパッケージ
**`Microsoft.PowerShell.5.1.ReferenceAssemblies`** で参照アセンブリを取得できる。
Citrixのモジュール/スナップインは実行時に動的ロードするため、
**コンパイル時にCitrixアセンブリは不要**。したがってビルドはClaude Code環境で完結する。

### 3.4 検証フロー（必ずこの順序）
1. Claude Code環境でビルド → 自己完結フォルダを生成。
2. そのフォルダを**CVAD管理環境へコピー**して実行。
3. 接続結果とログファイルを取得し、必要なら開発環境へ持ち帰って分析。

---

## 4. 【必須要件】配布形態

- **単一フォルダで動く自己完結型**であること。フォルダごとコピーすれば動く。
- **CVAD管理環境でインストーラ・セットアップ・管理者権限を要求しない**こと。
  - .NET Framework 4.8 はWindows Server/10/11に標準搭載のため追加ランタイム不要。
  - 依存DLLはすべて出力フォルダ内に同梱（コピーローカル）。
- 標準ユーザー権限で起動・実行できること（UAC昇格マニフェストを付けない）。

---

## 5. フェーズ1のスコープ

### 5.1 やること
- コア接続層プロジェクト（クラスライブラリ）。
- コンソール検証アプリ（接続テスト専用）。
- 複数DDCに対する接続テスト（プライマリ→代替の順にフェイルオーバー）。
- `Get-BrokerSite -AdminAddress <ddc>` による疎通確認とサイト情報取得。
- 詳細な診断ログのファイル出力（検証環境にClaude Codeが無いため持ち帰り分析用）。
- サイト設定のJSON永続化（読み込み・保存）。

### 5.2 やらないこと（フェーズ2以降）
- WPF GUI。
- 別資格情報モードの実装（**構造だけ用意し、実装は後回し**）。
- Brokerの各種管理操作（マシン/セッション/メンテナンスモード等）。
- 常駐Runspace管理（フェーズ1は試行ごとに使い捨て）。

---

## 6. ソリューション構成

```
CitrixAdminTool.sln
├── src/
│   ├── CitrixAdminTool.Core/            (クラスライブラリ, .NET Framework 4.8)
│   │   ├── Models/
│   │   │   ├── SiteConnection.cs        サイト接続設定（JSON永続化対象）
│   │   │   └── ConnectionResult.cs      接続結果モデル
│   │   ├── Connection/
│   │   │   └── CitrixConnectionService.cs  PowerShellホスト・Get-BrokerSite実行
│   │   └── Configuration/
│   │       └── SiteConfigStore.cs       JSON読み書き
│   └── CitrixAdminTool.ConsoleTest/     (コンソールアプリ, .NET Framework 4.8)
│       └── Program.cs                   検証エントリポイント
```

将来、`CitrixAdminTool.Wpf`（WPFアプリ）を同階層に追加し、同じCoreを参照する。

---

## 7. 認証方式の技術的背景（実装者向け重要事項）

公式ドキュメントで確認済みの事実:

- **オンプレCVADはドメイン内で認証境界が実質無い**。ドメイン参加済みマシンから
  コマンドを実行すれば、現在のユーザーコンテキスト（Kerberos/NTLM）でDDCに認証される。
  → 統合Windows認証モードでは**特別な接続処理は不要**。`-AdminAddress` 指定のみ。
- 権限の主体は**Citrix Delegated Administrationで付与されたCitrix管理者ロール**。
  ローカル端末が標準ユーザー権限でも、そのADアカウントにCitrix管理者ロールが
  あれば動作する。ローカルの管理者昇格は不要。
- **別資格情報モード（フェーズ2）の実装方針**:
  Brokerコマンドレットは汎用的な `-Credential` を持たないものが多い。
  そのため別ADアカウントで接続する場合は、Win32 API `LogonUser` で当該アカウントの
  トークンを取得し、その**偽装（impersonation）コンテキスト内でRunspaceを実行**する。
  WinRMリモーティングは使わない（DDC側の追加構成を不要にするため）。
  → フェーズ1では `IAuthenticationContext` のような抽象だけ用意し、
    統合認証の実装（何もしないパススルー）のみ提供する。

---

## 8. SDKロードの実装詳細

CVADのバージョンによりSDK提供形態が異なる（新しい版はモジュール、従来はスナップイン）。
両対応するため、接続時に以下の順でロードを試みる:

1. `Import-Module Citrix.Broker.Commands`（モジュール版）
2. 失敗時 `Add-PSSnapin Citrix.Broker.Admin.V2`（スナップイン版）
3. 両方失敗ならエラーとして扱う。

実行ポリシーに左右されないよう、InitialSessionStateで ExecutionPolicy=Bypass とする。

---

## 9. データモデル仕様

### 9.1 SiteConnection（JSON永続化対象）
- `Id` (string): サイト一意ID（例 "site-tokyo-prod"）
- `DisplayName` (string): 表示名（例 "東京本番サイト"）
- `PrimaryDdc` (string): 優先接続DDCのFQDN
- `AlternateDdcs` (List<string>): 代替DDC群（フェイルオーバー用）
- `AuthMode` (enum): IntegratedWindows | PromptForCredential
- メソッド `GetDdcsInTryOrder()`: プライマリ→代替の順でDDCを列挙

**資格情報そのものは絶対に保存しない**。AuthModeのみ保存する。

### 9.2 JSON形式（sites.json）
```json
{
  "sites": [
    {
      "id": "site-tokyo-prod",
      "displayName": "東京本番サイト",
      "primaryDdc": "ddc01.corp.example.com",
      "alternateDdcs": ["ddc02.corp.example.com"],
      "authMode": "IntegratedWindows"
    }
  ]
}
```
- 保存先（WPF版）: `%APPDATA%\CitrixAdminTool\sites.json`
- コンソール版: 実行フォルダ直下の `sites.json` を既定で読む
  （自己完結フォルダ運用のため。パスは引数で上書き可能にしてよい）

### 9.3 ConnectionResult / SiteConnectionResult
- 単一DDC試行結果と、サイト単位の集約結果（全試行の記録を含む）。
- 成功時: DDCアドレス, サイト名, バージョン, 認証ユーザー名, 所要時間。
- 失敗時: DDCアドレス, エラーメッセージ（生メッセージ）, 所要時間。
- `Version` はリリースでプロパティ名が揺れるため
  `ControllerVersion` → `Version` → `ProductVersion` の順で取得を試みる。

---

## 10. コンソール検証アプリの仕様

### 10.1 DDCリストの受け取り方
- **JSON設定ファイル（sites.json）を読む**方式を基本とする（WPF版とロジック共通化）。
- 既定で実行フォルダ直下の `sites.json` を読む。
- （任意）第1引数でJSONパスを上書き可能にしてよい。

### 10.2 動作
1. sites.json を読み込む。
2. 各サイトについて TestSiteConnection を実行（プライマリ→代替の順に試行）。
3. 結果をコンソールへ見やすく出力（サイト名／試行したDDC／成否／サイト名・バージョン／
   認証ユーザー／所要時間／失敗理由）。
4. 同じ内容を**ログファイルにも出力**（タイムスタンプ付きファイル名、実行フォルダ内）。
   検証環境にClaude Codeが無いため、ログを開発環境へ持ち帰って分析できるようにする。

### 10.3 出力例（イメージ）
```
[東京本番サイト]
  → ddc01.corp.example.com ... OK (Site: TokyoSite, Ver: 2402, 0.8s)
    認証: CORPdmin01
[大阪DRサイト]
  → ddc-dr01.corp.example.com ... FAILED (2.0s)
    理由: <生のエラーメッセージ>
```

---

## 11. 受け入れ基準（フェーズ1完了条件）

1. Claude Code環境で**ビルドが通る**（Citrix SDK無しで成立すること）。
2. ビルド成果物が**単一フォルダ自己完結型**で、フォルダコピーのみで他環境に展開できる。
3. CVAD管理環境にコピーして実行すると、sites.jsonに定義した**複数サイト・複数DDCへ
   統合Windows認証で接続を試み、各DDCの成否を出力**する。
4. プライマリDDC失敗時に代替DDCへ**フェイルオーバー**する。
5. 接続結果が**ログファイルに記録**され、持ち帰り分析が可能。
6. CVAD管理環境で**インストール作業・管理者昇格が一切不要**。

---

## 12. 既存のたたき台コード

`src/CitrixAdminTool.Core/` 配下に、以下のたたき台を作成済み（このチャット環境で生成）。
Claude Codeはこれをベースに、ビルドを通し、不足分（SiteConfigStore、コンソールProgram、
csproj/sln）を実装すること。なお、これらは**未ビルド・未検証**であり、
コンパイルエラーやAPI誤りが含まれる可能性がある。実SDKに照らして修正してよい。

- Models/SiteConnection.cs
- Models/ConnectionResult.cs
- Connection/CitrixConnectionService.cs

### 実装者が実物で確認・確定すべき点
- `Get-BrokerSite` の戻りオブジェクトの**正確なプロパティ名**（特にバージョン系）。
- Citrix Brokerのモジュール名が `Citrix.Broker.Commands` で正しいか（環境で確認）。
- `Microsoft.PowerShell.5.1.ReferenceAssemblies` の参照でビルドが通るか。

---

## 13. フェーズ2以降の予告（実装不要・設計の連続性のため記載）

- WPF (MVVM) GUI。サイト一覧・追加/編集・接続テストボタン・選択中サイト保持。
- 別資格情報モード（LogonUser + impersonation）の実装。
- Broker管理操作の段階的追加（マシン/セッション、メンテナンスモード、
  デリバリーグループ、ポリシー、タグ等）をモジュール単位・プラグイン的に拡張。
- 常駐Runspace管理への移行（性能最適化）。
