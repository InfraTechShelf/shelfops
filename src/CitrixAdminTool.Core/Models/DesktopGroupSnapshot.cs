using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// デリバリーグループ1つ分の構成のスナップショット（フェーズ5・0.9）。
    ///
    /// DG本体と、Studio で「デリバリーグループの設定」として見える関連オブジェクト
    /// （アクセスポリシー・権利付与ルール・割り当てルール・公開アプリ・電源スキーム・再起動スケジュール）を
    /// ひとまとまりで持つ。保存・比較に使い、将来の復元（1.0）でも同じ形式を読む。
    ///
    /// 【値の持ち方】
    /// プロパティ値は string / List&lt;object&gt; / Dictionary&lt;string, object&gt; / null のいずれか。
    /// 数値や真偽値も文字列で持つ。JSON の型の揺れ（"True" と true）で比較が狂わないようにするため。
    ///
    /// 【移行を見据えた設計（docs/SPEC_phase5_deliverygroups.md §3.2）】
    /// - 項目の対応付けは Uid ではなく名前で行う。Uid は参考情報として保存するだけ
    /// - ユーザー／グループはオブジェクトのまま（Name と SID の両方を含む形で）保存する
    /// - プロパティは取れたものをすべて保存する。設定可能かどうかは適用側で判断する
    /// </summary>
    public class DesktopGroupSnapshot
    {
        public const string FormatName = "shelfops-desktopgroup";
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// 関連オブジェクトの区分。キーはファイル上の名前で、表示名は画面側で訳す。
        /// 並び順は表示順。復元（1.0）の適用順とは別に決める。
        /// </summary>
        public static readonly string[] SectionIds =
        {
            "accessPolicyRules",
            "entitlementPolicyRules",
            "appEntitlementPolicyRules",
            "assignmentPolicyRules",
            "applications",
            "powerTimeSchemes",
            "rebootSchedules"
        };

        /// <summary>DG 本体を区分として扱うときの ID（比較結果・表示用）。</summary>
        public const string DesktopGroupSectionId = "desktopGroup";

        public int FormatVersion { get; set; } = CurrentFormatVersion;

        /// <summary>採取日時（ISO 8601・タイムゾーン付き）。</summary>
        public string CapturedAt { get; set; }

        /// <summary>採取に使ったアカウント。別資格情報なら指定アカウント。</summary>
        public string CapturedBy { get; set; }

        /// <summary>採取したツールと版（例 "ShelfOps 0.9.0-alpha"）。</summary>
        public string Tool { get; set; }

        public string SiteName { get; set; }
        public string Ddc { get; set; }
        public string SiteVersion { get; set; }

        /// <summary>DG 本体。</summary>
        public SnapshotItem DesktopGroup { get; set; }

        /// <summary>区分ごとの関連オブジェクト。キーは <see cref="SectionIds"/>。</summary>
        public Dictionary<string, List<SnapshotItem>> Sections { get; set; }
            = new Dictionary<string, List<SnapshotItem>>(StringComparer.Ordinal);

        /// <summary>
        /// 取得できなかった区分と、その理由。
        ///
        /// ここに載った区分は「空」ではなく「不明」。比較ではこれを「比較不可」として扱い、
        /// 中身が全部消えたかのような誤った差分を出さない。
        /// </summary>
        public Dictionary<string, string> Warnings { get; set; }
            = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>DG 名（ファイル名や表示に使う）。</summary>
        public string DesktopGroupName
        {
            get { return DesktopGroup == null ? null : DesktopGroup.Name; }
        }

        /// <summary>関連オブジェクトの総数（DG 本体を除く）。</summary>
        public int RelatedItemCount
        {
            get { return Sections.Values.Sum(l => l == null ? 0 : l.Count); }
        }

        // ------------------------------------------------------------------
        // 辞書（JSON）との相互変換。ファイル保存とワーカー間の受け渡しの両方で使う。
        // ------------------------------------------------------------------

        public Dictionary<string, object> ToDict()
        {
            var sections = new Dictionary<string, object>();
            foreach (var id in SectionIds)
            {
                List<SnapshotItem> items;
                if (!Sections.TryGetValue(id, out items) || items == null) continue;
                sections[id] = new List<object>(items.Select(i => (object)i.ToDict()));
            }

            var warnings = new Dictionary<string, object>();
            foreach (var w in Warnings) warnings[w.Key] = w.Value;

            return new Dictionary<string, object>
            {
                { "format", FormatName },
                { "formatVersion", FormatVersion },
                { "capturedAt", CapturedAt },
                { "capturedBy", CapturedBy },
                { "tool", Tool },
                { "site", new Dictionary<string, object>
                    {
                        { "name", SiteName },
                        { "ddc", Ddc },
                        { "version", SiteVersion }
                    } },
                { "desktopGroup", DesktopGroup == null ? null : DesktopGroup.ToDict() },
                { "sections", sections },
                { "warnings", warnings }
            };
        }

        /// <summary>
        /// 辞書から復元する。形式が違う・新しすぎる場合は <see cref="InvalidDataException"/>。
        /// 利用者が別のファイルを選んだときに、分かる言葉で止めるため。
        /// </summary>
        public static DesktopGroupSnapshot FromDict(IDictionary<string, object> d)
        {
            if (d == null) throw new InvalidDataException("not a JSON object");

            if (!string.Equals(Str(d, "format"), FormatName, StringComparison.Ordinal))
                throw new InvalidDataException("format");

            var version = (int)Num(d, "formatVersion");
            if (version > CurrentFormatVersion)
                throw new InvalidDataException("formatVersion");

            var snap = new DesktopGroupSnapshot
            {
                FormatVersion = version,
                CapturedAt = Str(d, "capturedAt"),
                CapturedBy = Str(d, "capturedBy"),
                Tool = Str(d, "tool")
            };

            var site = Obj(d, "site");
            if (site != null)
            {
                snap.SiteName = Str(site, "name");
                snap.Ddc = Str(site, "ddc");
                snap.SiteVersion = Str(site, "version");
            }

            var dg = Obj(d, "desktopGroup");
            if (dg != null) snap.DesktopGroup = SnapshotItem.FromDict(dg);

            var sections = Obj(d, "sections");
            if (sections != null)
            {
                foreach (var pair in sections)
                {
                    var list = pair.Value as List<object>;
                    if (list == null) continue;

                    snap.Sections[pair.Key] = list
                        .OfType<IDictionary<string, object>>()
                        .Select(SnapshotItem.FromDict)
                        .ToList();
                }
            }

            var warnings = Obj(d, "warnings");
            if (warnings != null)
            {
                foreach (var pair in warnings)
                    snap.Warnings[pair.Key] = pair.Value == null ? string.Empty : pair.Value.ToString();
            }

            return snap;
        }

        internal static string Str(IDictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? (v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture)) : null;
        }

        internal static double Num(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            if (v is double) return (double)v;
            double n;
            return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        internal static IDictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as IDictionary<string, object> : null;
        }
    }

    /// <summary>
    /// スナップショット内の1オブジェクト（DG本体、ルール1件、アプリ1件など）。
    /// </summary>
    public class SnapshotItem
    {
        /// <summary>名前。比較・復元での対応付けの主キー。</summary>
        public string Name { get; set; }

        /// <summary>Uid（参考情報。サイト固有なので対応付けには使わない）。</summary>
        public string Uid { get; set; }

        /// <summary>全プロパティ。キーはプロパティ名（序数順に並べて保持する）。</summary>
        public SortedDictionary<string, object> Properties { get; set; }
            = new SortedDictionary<string, object>(StringComparer.Ordinal);

        public Dictionary<string, object> ToDict()
        {
            var props = new Dictionary<string, object>();
            foreach (var p in Properties) props[p.Key] = p.Value;

            return new Dictionary<string, object>
            {
                { "name", Name },
                { "uid", Uid },
                { "properties", props }
            };
        }

        public static SnapshotItem FromDict(IDictionary<string, object> d)
        {
            var item = new SnapshotItem
            {
                Name = DesktopGroupSnapshot.Str(d, "name"),
                Uid = DesktopGroupSnapshot.Str(d, "uid")
            };

            var props = DesktopGroupSnapshot.Obj(d, "properties");
            if (props != null)
            {
                foreach (var p in props) item.Properties[p.Key] = p.Value;
            }

            return item;
        }
    }
}
