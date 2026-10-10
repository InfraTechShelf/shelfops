# ShelfOps スナップショット（Core）の検証
#
# ビルド済みの dist\Release\ShelfOps.Core.dll に対して、PowerShell オブジェクトの
# 平坦化（ToPlainProperties）、JSON の往復、比較（SnapshotComparer）を確かめる。DDC は不要。
# Citrix の型は PSCustomObject で模している。実際の CVAD での確認の代わりにはならない。
#
# 使い方（先に Release ビルドしておくこと）:
#   powershell -NoProfile -File .\tools\SnapshotCheck\verify-snapshot.ps1
#
# 終了コード 0 = すべて OK、それ以外 = 問題の件数。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dist = Join-Path $root 'dist\Release'
$asm = [System.Reflection.Assembly]::LoadFrom("$dist\ShelfOps.Core.dll")
$helpers = $asm.GetType('CitrixAdminTool.Core.Connection.BrokerSdkHelpers')
$toPlain = $helpers.GetMethod('ToPlainProperties', [System.Reflection.BindingFlags]'NonPublic,Public,Static')

$fail = 0
function Check($name, $cond, $detail) {
    if ($cond) { "  OK   : $name" } else { "  NG   : $name -> $detail"; $script:fail++ }
}

"=== 1. PowerShell オブジェクトの変換 ==="
# Citrix の User 型を模す（Name と SID を持つ入れ子オブジェクト）
$u1 = [pscustomobject]@{ Name = 'CORP\VDI-Users'; SID = 'S-1-5-21-1-2-3-1001'; FullName = 'VDI Users' }
$u2 = [pscustomobject]@{ Name = 'CORP\taro';      SID = 'S-1-5-21-1-2-3-1002'; FullName = 'Taro' }
$dg = [psobject]::AsPSObject([pscustomobject]@{
    Name                 = 'VDI-Standard'
    Uid                  = 12
    Enabled              = $true
    SessionSupport       = [System.DayOfWeek]::Monday      # 列挙値の代わり
    ShutdownDesktopsAfterUse = $false
    PeakBufferSizePercent = 10
    Tags                 = [string[]]@('Tier1','Prod')
    TimeZone             = 'Tokyo Standard Time'
    LastUpdated          = [datetime]'2026-10-01T09:30:00'
    DesktopsInUse        = 87
    IncludedUsers        = @($u1, $u2)
    Nothing              = $null
})
$props = $toPlain.Invoke($null, @($dg, 2))
Check 'string はそのまま'          ($props['Name'] -eq 'VDI-Standard') $props['Name']
Check '真偽値は文字列 "True"'      ($props['Enabled'] -eq 'True') $props['Enabled']
Check '数値は文字列（カルチャ非依存）' ($props['PeakBufferSizePercent'] -eq '10') $props['PeakBufferSizePercent']
Check '列挙値は名前の文字列'        ($props['SessionSupport'] -eq 'Monday') $props['SessionSupport']
Check '日時は ISO 8601'            ($props['LastUpdated'] -like '2026-10-01T09:30:00*') $props['LastUpdated']
Check '配列はリスト'               ($props['Tags'].Count -eq 2 -and $props['Tags'][1] -eq 'Prod') ($props['Tags'] -join ',')
Check 'null は null'              ($props.ContainsKey('Nothing') -and $null -eq $props['Nothing']) 'missing'
$users = $props['IncludedUsers']
Check '入れ子ユーザーは辞書（SIDを失わない）' ($users[0]['SID'] -eq 'S-1-5-21-1-2-3-1001' -and $users[0]['Name'] -eq 'CORP\VDI-Users') ($users[0] | Out-String)

"=== 2. スナップショットのファイル往復 ==="
$snap = New-Object CitrixAdminTool.Core.Models.DesktopGroupSnapshot
$snap.CapturedAt = '2026-10-10T10:00:00+09:00'; $snap.CapturedBy = 'CORP\admin01'; $snap.Tool = 'ShelfOps test'
$snap.SiteName = 'TokyoSite'; $snap.Ddc = 'ddc01.corp.example.com'; $snap.SiteVersion = '7.2507'
$item = New-Object CitrixAdminTool.Core.Models.SnapshotItem
$item.Name = 'VDI-Standard'; $item.Uid = '12'; $item.Properties = $props
$snap.DesktopGroup = $item

$rule = New-Object CitrixAdminTool.Core.Models.SnapshotItem
$rule.Name = 'VDI-Standard_Direct'; $rule.Uid = '31'
$rule.Properties['AllowedConnections'] = 'NotViaAG'
$rule.Properties['IncludedUsers'] = $props['IncludedUsers']
$rule.Properties['DesktopGroupUid'] = '12'
$list = New-Object 'System.Collections.Generic.List[CitrixAdminTool.Core.Models.SnapshotItem]'
$list.Add($rule)
$snap.Sections['accessPolicyRules'] = $list
$snap.Warnings['rebootSchedules'] = 'Get-BrokerRebootScheduleV2: The term is not recognized'

$json = [CitrixAdminTool.Core.Configuration.Json.JsonWriter]::Write($snap.ToDict())
$path = Join-Path $env:TEMP 'shelfops-snapshot-test.json'
[System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
$back = [CitrixAdminTool.Core.Models.DesktopGroupSnapshot]::FromDict([CitrixAdminTool.Core.Configuration.Json.JsonParser]::Parse([System.IO.File]::ReadAllText($path)))
Check 'DG名'           ($back.DesktopGroupName -eq 'VDI-Standard') $back.DesktopGroupName
Check 'サイト情報'      ($back.SiteName -eq 'TokyoSite' -and $back.SiteVersion -eq '7.2507') "$($back.SiteName) $($back.SiteVersion)"
Check '関連オブジェクト' ($back.Sections['accessPolicyRules'].Count -eq 1) $back.RelatedItemCount
Check '取得不可の記録'   ($back.Warnings['rebootSchedules'] -like '*not recognized*') ($back.Warnings | Out-String)
Check '入れ子ユーザーのSIDが往復で残る' ($back.Sections['accessPolicyRules'][0].Properties['IncludedUsers'][1]['SID'] -eq 'S-1-5-21-1-2-3-1002') ''

$d0 = [CitrixAdminTool.Core.Models.SnapshotComparer]::Compare($back, $snap)
Check '往復後のファイルと元は差分なし（取得不可区分を除く）' (-not $d0.HasChanges) (($d0.Entries | ForEach-Object { "$($_.Kind) $($_.SectionId) $($_.ItemName) $($_.Property)" }) -join '; ')

"=== 3. 誤ったファイルは拒否される ==="
$rejected = $false
try { [CitrixAdminTool.Core.Models.DesktopGroupSnapshot]::FromDict([CitrixAdminTool.Core.Configuration.Json.JsonParser]::Parse('{ "sites": [] }')) } catch { $rejected = $true }
Check 'sites.json などの別形式' $rejected ''
$rejected = $false
try { [CitrixAdminTool.Core.Models.DesktopGroupSnapshot]::FromDict([CitrixAdminTool.Core.Configuration.Json.JsonParser]::Parse('{ "format": "shelfops-desktopgroup", "formatVersion": 99 }')) } catch { $rejected = $true }
Check '新しすぎる形式' $rejected ''

"=== 4. 比較 ==="
function NewSnap([hashtable]$dgProps, [hashtable[]]$rules, [string]$warn) {
    $s = New-Object CitrixAdminTool.Core.Models.DesktopGroupSnapshot
    $i = New-Object CitrixAdminTool.Core.Models.SnapshotItem; $i.Name = 'VDI-Standard'
    foreach ($k in $dgProps.Keys) { $i.Properties[$k] = $dgProps[$k] }
    $s.DesktopGroup = $i
    $l = New-Object 'System.Collections.Generic.List[CitrixAdminTool.Core.Models.SnapshotItem]'
    foreach ($r in $rules) {
        $ri = New-Object CitrixAdminTool.Core.Models.SnapshotItem; $ri.Name = $r.Name
        foreach ($k in $r.Keys) { if ($k -ne 'Name') { $ri.Properties[$k] = $r[$k] } }
        $l.Add($ri)
    }
    $s.Sections['accessPolicyRules'] = $l
    if ($warn) { $s.Warnings['accessPolicyRules'] = $warn }
    return $s
}
function L([object[]]$a) { $x = New-Object 'System.Collections.Generic.List[object]'; foreach ($v in $a) { $x.Add($v) }; return ,$x }

$saved = NewSnap @{ Enabled='True'; PeakBufferSizePercent='10'; DesktopsInUse='80'; Uid='12'; Tags=(L @('A','B')) } `
                 @(@{ Name='Direct'; AllowedConnections='NotViaAG'; DesktopGroupUid='12' }, @{ Name='OldRule'; AllowedConnections='ViaAG' }) $null
$current = NewSnap @{ Enabled='False'; PeakBufferSizePercent='10'; DesktopsInUse='95'; Uid='99'; Tags=(L @('B','A')) } `
                 @(@{ Name='direct'; AllowedConnections='AnyViaAG'; DesktopGroupUid='99' }, @{ Name='NewRule'; AllowedConnections='ViaAG' }) $null
$diff = [CitrixAdminTool.Core.Models.SnapshotComparer]::Compare($saved, $current)
$e = $diff.Entries
$dump = ($e | ForEach-Object { "$($_.Kind)|$($_.SectionId)|$($_.ItemName)|$($_.Property)|$($_.SavedValue)|$($_.CurrentValue)" }) -join "`n         "
Check '設定値の変更を検出（Enabled True→False）' (@($e | ? { $_.Property -eq 'Enabled' -and $_.Kind -eq 'Changed' -and $_.SavedValue -eq 'True' -and $_.CurrentValue -eq 'False' }).Count -eq 1) $dump
Check '稼働状況（DesktopsInUse）は比較しない' (@($e | ? { $_.Property -eq 'DesktopsInUse' }).Count -eq 0 -and $diff.ExcludedProperties.Contains('DesktopsInUse')) $dump
Check 'Uid・Uid参照は比較しない'            (@($e | ? { $_.Property -eq 'Uid' -or $_.Property -eq 'DesktopGroupUid' }).Count -eq 0) $dump
Check '並び順だけの違いは「順序のみ」'      (@($e | ? { $_.Property -eq 'Tags' -and $_.Kind -eq 'Reordered' }).Count -eq 1) $dump
Check '名前は大文字小文字を区別せず対応付け' (@($e | ? { $_.ItemName -eq 'direct' -and $_.Property -eq 'AllowedConnections' -and $_.Kind -eq 'Changed' }).Count -eq 1) $dump
Check '保存時のみのルール'                 (@($e | ? { $_.ItemName -eq 'OldRule' -and $_.Kind -eq 'OnlyInSaved' }).Count -eq 1) $dump
Check '現在のみのルール'                   (@($e | ? { $_.ItemName -eq 'NewRule' -and $_.Kind -eq 'OnlyInCurrent' }).Count -eq 1) $dump
Check '変化の無い値は出ない（PeakBufferSizePercent）' (@($e | ? { $_.Property -eq 'PeakBufferSizePercent' }).Count -eq 0) $dump

$withWarn = NewSnap @{ Enabled='True' } @() 'Get-BrokerAccessPolicyRule: Access denied'
$d2 = [CitrixAdminTool.Core.Models.SnapshotComparer]::Compare($saved, $withWarn)
Check '取得不可の区分は「比較不可」で、削除扱いにしない' (@($d2.Entries | ? { $_.SectionId -eq 'accessPolicyRules' -and $_.Kind -eq 'NotComparable' }).Count -eq 1 -and @($d2.Entries | ? { $_.Kind -eq 'OnlyInSaved' }).Count -eq 0) (($d2.Entries | % { "$($_.Kind) $($_.ItemName)" }) -join '; ')

""
if ($fail -eq 0) { "All snapshot checks passed." } else { "$fail problem(s) found." }
Remove-Item $path -ErrorAction SilentlyContinue
exit $fail
