# ShelfOps 公開パッケージ作成スクリプト
#
# dist\Release のビルド成果物から、一般公開する実行ファイル群だけを取り出して ZIP にまとめる。
#   含める : ShelfOps.exe / .Worker.exe / .Core.dll ＋ 各 .config ＋ クイックスタート（日英）＋ LICENSE / NOTICE
#   除外   : ConsoleTest（内部検証ツール）、pdb（デバッグシンボル・ビルドパス漏れ防止）
#
# 使い方:
#   .\pack-release.ps1                        … 署名なしでパッケージ作成
#   .\pack-release.ps1 -CertThumbprint <拇印>  … コード署名してからパッケージ作成
#
# 署名について:
#   証明書は「証明書ストアに入っている証明書の拇印」で指定する。
#   pfx とそのパスワードをスクリプトやリポジトリに置かないため。拇印は次で調べられる:
#     Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject, Thumbprint
#   タイムスタンプ（/tr）は必須。付けないと証明書の有効期限切れと同時に署名も無効になる。
#
#   -SelfSigned : 自己署名証明書で署名するときに付ける。
#     自己署名は証明書チェーンが信頼されないため、署名後の検証（signtool verify /pa）が
#     必ず失敗する。これは想定内なので、検証失敗を「エラー」ではなく「警告」として扱う。
#     ※ 自己署名は署名手順のリハーサルと社内配布用。一般配布では SmartScreen 対策にならない。
param(
    [string]$CertThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$SelfSigned
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist\Release"
$version = "0.7.0-alpha"
$appName = "ShelfOps"

# 公開する実行ファイル群（これ以外は含めない）
$runtimeFiles = @(
    "ShelfOps.exe",
    "ShelfOps.exe.config",
    "ShelfOps.Worker.exe",
    "ShelfOps.Worker.exe.config",
    "ShelfOps.Core.dll"
)
# 同梱するドキュメント（ソースパス → パッケージ内の名前）
$docs = @{
    "docs\クイックスタート.md"  = "クイックスタート.md"
    "docs\QuickStart_en.md"     = "QuickStart.md"
    "LICENSE"                    = "LICENSE"
    "NOTICE"                     = "NOTICE"
}

# 出力先を準備
$releaseDir = Join-Path $root "release"
$stageRoot  = Join-Path $releaseDir "stage"
$stageApp   = Join-Path $stageRoot $appName
$zipPath    = Join-Path $releaseDir ("{0}-{1}.zip" -f $appName, $version)

if (Test-Path $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force $stageApp | Out-Null

# 実行ファイルをコピー（無ければエラーで停止）
foreach ($f in $runtimeFiles) {
    $src = Join-Path $dist $f
    if (-not (Test-Path $src)) { throw "必要なファイルがありません: $src（先に Release ビルドしてください）" }
    Copy-Item $src (Join-Path $stageApp $f) -Force
}
# ドキュメントをコピー
foreach ($srcRel in $docs.Keys) {
    $src = Join-Path $root $srcRel
    if (Test-Path $src) { Copy-Item $src (Join-Path $stageApp $docs[$srcRel]) -Force }
    else { Write-Warning "ドキュメントが見つかりません（スキップ）: $src" }
}

# コード署名（-CertThumbprint を指定したときだけ実行）。
# ZIP に固めた後では個々の exe/dll に署名できないため、必ずステージング後・圧縮前に行う。
if ($CertThumbprint) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $signtool = Get-ChildItem $sdkBin -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.DirectoryName -like "*\x64" } |
                Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe が見つかりません（Windows SDK を入れてください）: $sdkBin" }

    # 署名対象は自前のバイナリのみ（.config やドキュメントは署名できない）。
    $targets = @(Get-ChildItem $stageApp -Recurse -Include *.exe, *.dll | ForEach-Object { $_.FullName })
    Write-Output ("署名します: {0} ファイル / {1}" -f $targets.Count, $signtool.FullName)

    & $signtool.FullName sign /sha1 $CertThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /v @targets
    if ($LASTEXITCODE -ne 0) { throw "署名に失敗しました（signtool 終了コード $LASTEXITCODE）。" }

    & $signtool.FullName verify /pa /v @targets
    if ($LASTEXITCODE -ne 0) {
        if ($SelfSigned) {
            # 自己署名では信頼チェーンを辿れないため、この失敗は想定内。
            # 署名そのものが載っているかは Get-AuthenticodeSignature の署名者名で確認する。
            Write-Warning "署名の検証に失敗しました（自己署名のため想定内・終了コード $LASTEXITCODE）。"
            Write-Warning "この署名は一般配布では信頼されません。SmartScreen の警告も消えません。"
        }
        else {
            throw "署名の検証に失敗しました（signtool 終了コード $LASTEXITCODE）。"
        }
    }

    # 署名者名とタイムスタンプを目視確認できるように出す。
    Get-ChildItem $stageApp -Recurse -Include *.exe, *.dll |
        Get-AuthenticodeSignature |
        Format-Table @{ n = 'ファイル'; e = { Split-Path $_.Path -Leaf } },
                     Status,
                     @{ n = '署名者'; e = { if ($_.SignerCertificate) { $_.SignerCertificate.Subject } } },
                     @{ n = 'タイムスタンプ'; e = { if ($_.TimeStamperCertificate) { 'あり' } else { 'なし' } } } |
        Out-String | Write-Output
}

# ZIP を作成（トップに ShelfOps フォルダが来る形）
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path $stageApp -DestinationPath $zipPath -CompressionLevel Optimal

# 後片付けと結果表示
Remove-Item -LiteralPath $stageRoot -Recurse -Force
Write-Output ("作成: {0}  ({1:N0} バイト)" -f $zipPath, (Get-Item $zipPath).Length)
Write-Output "--- ZIP の内容 ---"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$zip.Entries | ForEach-Object { Write-Output ("  {0,10:N0}  {1}" -f $_.Length, $_.FullName) }
$zip.Dispose()

# ここまで来ていれば成功。-SelfSigned のとき signtool verify が残す終了コード 1 を
# スクリプト自体の失敗と誤解されないよう、明示的に 0 を返す。
exit 0
