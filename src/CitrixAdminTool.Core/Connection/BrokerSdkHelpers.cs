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

        /// <summary>
        /// コマンドレットが返したオブジェクトの全プロパティを、保存・比較できる素の値に変換する。
        ///
        /// 返す値は string / List&lt;object&gt; / Dictionary&lt;string, object&gt; / null のみ。
        /// - 数値・真偽値・列挙値・日時は文字列にする（日時は ISO 8601、数値はカルチャ非依存）
        /// - 配列は要素ごとに変換したリスト
        /// - 入れ子のオブジェクト（ユーザー／グループ等）は、そのプロパティの辞書にする。
        ///   ToString() に頼ると名前しか残らず SID を失うため（別サイト移行で SID と名前の両方が要る）
        /// - 入れ子は <paramref name="maxDepth"/> 段まで。それより深いものは ToString() で打ち切る
        ///
        /// 個々のプロパティの取得で例外が出ても、そのプロパティだけ諦めて続ける
        /// （一部のゲッターが環境によって例外を投げても、スナップショット全体を失わないため）。
        /// </summary>
        public static SortedDictionary<string, object> ToPlainProperties(PSObject obj, int maxDepth = 2)
        {
            var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
            if (obj == null) return result;

            foreach (var p in obj.Properties)
            {
                if (p == null) continue;
                if (p.MemberType != PSMemberTypes.Property && p.MemberType != PSMemberTypes.NoteProperty) continue;

                try
                {
                    result[p.Name] = ToPlain(p.Value, 1, maxDepth);
                }
                catch
                {
                    // このプロパティだけ取れなかった。全体は続ける。
                }
            }

            return result;
        }

        private static object ToPlain(object value, int depth, int maxDepth)
        {
            if (value == null) return null;

            // 包みは外して型を判定するが、入れ子オブジェクトとしてプロパティを読むときは
            // 包み（PSObject）の方を使う。包みにだけ付いたプロパティ（NoteProperty）は、
            // 外した中身からは見えなくなるため。
            var wrapper = value as PSObject;
            if (wrapper != null) value = wrapper.BaseObject;
            if (value == null) return null;

            var s = value as string;
            if (s != null) return s;

            if (value is DateTime)
                return ((DateTime)value).ToString("o", CultureInfo.InvariantCulture);

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal || value is Guid || value is TimeSpan)
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            var dict = value as System.Collections.IDictionary;
            if (dict != null)
            {
                var d = new Dictionary<string, object>();
                foreach (System.Collections.DictionaryEntry e in dict)
                    d[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = depth >= maxDepth ? Flat(e.Value) : ToPlain(e.Value, depth + 1, maxDepth);
                return d;
            }

            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                var l = new List<object>();
                foreach (var item in list) l.Add(ToPlain(item, depth, maxDepth));
                return l;
            }

            // 入れ子のオブジェクト（例: Citrix のユーザー型）
            if (depth >= maxDepth) return Flat(value);

            var props = ToPlainProperties(wrapper ?? PSObject.AsPSObject(value), maxDepth - depth);
            var nested = new Dictionary<string, object>();
            foreach (var pair in props) nested[pair.Key] = pair.Value;
            return nested;
        }

        private static object Flat(object value)
        {
            if (value == null) return null;
            var ps = value as PSObject;
            var raw = ps == null ? value : ps.BaseObject;
            return raw == null ? null : Convert.ToString(raw, CultureInfo.InvariantCulture);
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
