# ShelfOps UI スモークテスト
#
# ビルド済みの dist\Release\ShelfOps.exe に対して、XAML のパース・両言語での表示・
# 未定義文言キー・バインディング警告・言語切り替えの追従を確認する。DDC は不要。
#
# 使い方（先に Release ビルドしておくこと）:
#   .\tools\XamlSmoke\run-smoke.ps1
#
# 終了コード 0 = すべて OK、それ以外 = 問題の件数。
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dist = Join-Path $root 'dist\Release'
$src  = Join-Path $PSScriptRoot 'XamlSmoke.cs'
$exe  = Join-Path $dist 'XamlSmoke.exe'

if (-not (Test-Path (Join-Path $dist 'ShelfOps.exe'))) {
    throw "dist\Release\ShelfOps.exe がありません。先に Release ビルドしてください。"
}

# csc.exe は .NET Framework に同梱。Roslyn より古いが C# 5 までで書いてあるので足りる。
$fw  = Join-Path $env:WinDir 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$wpf = Join-Path $fw 'WPF'

& $csc /nologo /target:exe /platform:anycpu "/out:$exe" `
    "/reference:$dist\ShelfOps.exe" `
    "/reference:$dist\ShelfOps.Core.dll" `
    "/reference:$wpf\PresentationCore.dll" `
    "/reference:$wpf\PresentationFramework.dll" `
    "/reference:$wpf\WindowsBase.dll" `
    "/reference:$fw\System.Xaml.dll" `
    "/reference:$fw\System.Core.dll" `
    $src
if ($LASTEXITCODE -ne 0) { throw "スモークテストのコンパイルに失敗しました。" }

try {
    # 参照アセンブリと同じフォルダで実行しないと ShelfOps.exe を解決できない。
    & $exe
    $result = $LASTEXITCODE
}
finally {
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
}

exit $result
