using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Management.Automation;

namespace CitrixAdminTool.Core.Connection
{
    /// <summary>
    /// Broker SDKのロードとPSObjectの値取り出しの共通処理。
    ///
    /// 検証済みの <see cref="CitrixConnectionService"/> には手を入れず、
    /// 管理操作（<see cref="BrokerOperationService"/>）から同じSDKロード手順を
    /// 使えるよう切り出したもの。モジュール／スナップイン名は
    /// CitrixConnectionService の公開定数を単一の出所として参照する。
    /// </summary>
    internal static class BrokerSdkHelpers
    {
        private const string SdkMissingHint =
            " ／ このマシンにCitrix SDKが入っていない可能性があります。"
            + "本ツールはCitrix Studioがインストールされた管理端末で実行してください。";

        /// <summary>
        /// Citrix Broker SDKをロードする。モジュール→スナップインの順に試す。
        /// 両方失敗したら <see cref="SdkLoadException"/>。
        /// </summary>
        public static string LoadBrokerSdk(PowerShell ps, List<string> diagnostics)
        {
            ps.Commands.Clear();
            ps.Streams.ClearStreams();

            var module = CitrixConnectionService.BrokerModuleName;
            var snapin = CitrixConnectionService.BrokerSnapinName;

            ps.AddScript(@"
                $ErrorActionPreference = 'Stop'
                $moduleError = $null
                try {
                    Import-Module " + module + @" -ErrorAction Stop
                    'Module'
                    return
                } catch {
                    $moduleError = $_.Exception.Message
                }
                try {
                    Add-PSSnapin " + snapin + @" -ErrorAction Stop
                    'PSSnapin'
                    return
                } catch {
                    throw ('Citrix Broker SDK のロードに失敗しました。' +
                           'モジュール(" + module + @"): ' + $moduleError +
                           ' / スナップイン(" + snapin + @"): ' + $_.Exception.Message)
                }
            ");

            Collection<PSObject> output;
            try
            {
                output = ps.Invoke();
                CollectStreams(ps, diagnostics);
                ThrowIfHadErrors(ps);
            }
            catch (Exception ex)
            {
                CollectStreams(ps, diagnostics);
                throw new SdkLoadException(ex.Message + SdkMissingHint, ex);
            }

            var method = output.Select(o => o == null ? null : o.BaseObject as string)
                               .FirstOrDefault(s => !string.IsNullOrEmpty(s));

            if (method == null)
                throw new SdkLoadException(
                    "Citrix Broker SDK のロード結果を判定できませんでした。" + SdkMissingHint);

            return method;
        }

        public static void CollectStreams(PowerShell ps, List<string> diagnostics)
        {
            foreach (var w in ps.Streams.Warning) diagnostics.Add("[警告] " + w.Message);
            foreach (var e in ps.Streams.Error) diagnostics.Add("[エラー] " + e.ToString());
        }

        public static void ThrowIfHadErrors(PowerShell ps)
        {
            if (ps.HadErrors && ps.Streams.Error.Count > 0)
                throw new InvalidOperationException(ps.Streams.Error[0].ToString());
        }

        public static string GetProp(PSObject obj, string name)
        {
            try
            {
                var p = obj.Properties[name];
                if (p == null) return null;
                var value = p.Value;
                return value == null ? null : value.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// 配列で返るプロパティ（AssociatedUserNames 等）を1つの文字列にまとめて取り出す。
        ///
        /// <see cref="GetProp"/> は value.ToString() しているため、配列に使うと
        /// "System.String[]" という文字列になってしまう。配列の場合は要素を列挙して連結する。
        /// 値が配列でなければ従来どおり ToString() の結果を返す。
        /// </summary>
        public static string GetStringsProp(PSObject obj, string name, string separator = "; ")
        {
            try
            {
                var p = obj.Properties[name];
                if (p == null) return null;

                var value = p.Value;
                if (value == null) return null;

                // string も IEnumerable なので、先に文字列として扱う。
                var single = value as string;
                if (single != null) return single;

                var items = value as System.Collections.IEnumerable;
                if (items == null) return value.ToString();

                var parts = new List<string>();
                foreach (var item in items)
                {
                    if (item == null) continue;

                    // PSObject 越しに来ることがあるので、素の値まで剥がす。
                    var ps = item as PSObject;
                    var raw = ps == null ? item : ps.BaseObject;
                    if (raw == null) continue;

                    var text = raw.ToString();
                    if (!string.IsNullOrWhiteSpace(text)) parts.Add(text.Trim());
                }

                return parts.Count == 0 ? null : string.Join(separator, parts.ToArray());
            }
            catch { return null; }
        }

        public static string FirstNonEmpty(params string[] candidates)
        {
            return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        }

        public static bool GetBoolProp(PSObject obj, string name)
        {
            var raw = GetProp(obj, name);
            bool b;
            return bool.TryParse(raw, out b) && b;
        }

        public static int GetIntProp(PSObject obj, string name)
        {
            var raw = GetProp(obj, name);
            int n;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        public static long GetLongProp(PSObject obj, string name)
        {
            var raw = GetProp(obj, name);
            long n;
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }
    }
}
