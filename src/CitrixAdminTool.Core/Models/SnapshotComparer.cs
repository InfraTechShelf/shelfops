using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CitrixAdminTool.Core.Models
{
    /// <summary>差分の種類。</summary>
    public enum SnapshotDiffKind
    {
        /// <summary>両方にあるが値が違う。</summary>
        Changed,

        /// <summary>保存ファイルにだけある（保存後に削除された、または値が無くなった）。</summary>
        OnlyInSaved,

        /// <summary>現在にだけある（保存後に追加された）。</summary>
        OnlyInCurrent,

        /// <summary>要素は同じで並び順だけが違う（ユーザー一覧など）。</summary>
        Reordered,

        /// <summary>どちらかで区分を取得できておらず、比較できない。</summary>
        NotComparable
    }

    /// <summary>差分1件。</summary>
    public class SnapshotDiffEntry
    {
        public SnapshotDiffKind Kind { get; set; }

        /// <summary>区分 ID（"desktopGroup" または <see cref="DesktopGroupSnapshot.SectionIds"/> のいずれか）。</summary>
        public string SectionId { get; set; }

        /// <summary>対象の名前（ルール名・アプリ名など）。</summary>
        public string ItemName { get; set; }

        /// <summary>プロパティ名。対象そのものが片方にしか無い場合は null。</summary>
        public string Property { get; set; }

        /// <summary>保存ファイル側の値（表示用に整形済み）。</summary>
        public string SavedValue { get; set; }

        /// <summary>現在の値（表示用に整形済み）。</summary>
        public string CurrentValue { get; set; }
    }

    /// <summary>比較結果。</summary>
    public class SnapshotDiff
    {
        public List<SnapshotDiffEntry> Entries { get; } = new List<SnapshotDiffEntry>();

        /// <summary>比較から除外したプロパティ名（稼働状況・Uid 系）。画面で「対象外」として示す。</summary>
        public SortedSet<string> ExcludedProperties { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public int Count(SnapshotDiffKind kind)
        {
            return Entries.Count(e => e.Kind == kind);
        }

        /// <summary>実質的な差分（並び順のみ・比較不可を除く）があるか。</summary>
        public bool HasChanges
        {
            get
            {
                return Entries.Any(e => e.Kind == SnapshotDiffKind.Changed
                                     || e.Kind == SnapshotDiffKind.OnlyInSaved
                                     || e.Kind == SnapshotDiffKind.OnlyInCurrent);
            }
        }
    }

    /// <summary>
    /// 2つのスナップショットを比較する（0.9 は表示のみ。1.0 の復元はこの結果を元に適用する）。
    ///
    /// 【対応付けは名前で行う】
    /// ルール・アプリ・スキームは名前で突き合わせる（大文字小文字は区別しない）。
    /// Uid はサイト固有で、DG を作り直すと変わるため使わない。
    ///
    /// 【比較から除外するもの】
    /// - 稼働状況の値（マシン数・セッション数など）。設定ではないのに毎回変わり、
    ///   差分に出すと本当の変更が埋もれる
    /// - Uid とその参照（"...Uid" / "...Uids"）。サイト固有の内部番号
    /// 除外したものはファイルには残し、比較結果では「対象外」として名前を列挙する（黙って捨てない）。
    /// </summary>
    public static class SnapshotComparer
    {
        /// <summary>
        /// 稼働状況を表すプロパティ。設定値ではないので比較しない。
        /// CVAD のバージョンで増える可能性があるため、名前の前置きでも判定する（<see cref="IsExcluded"/>）。
        /// </summary>
        private static readonly HashSet<string> VolatileProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Sessions",
            "TotalApplications",
            "TotalApplicationGroups",
            "TotalDesktops",
            "MachineCost",
            "UUID",
            "LastChangeId",
            "AssociatedApplicationGroupUids",
            "AssociatedApplicationGroupUUIDs"
        };

        public static bool IsExcluded(string property)
        {
            if (string.IsNullOrEmpty(property)) return false;
            if (VolatileProperties.Contains(property)) return true;

            // DesktopsAvailable / DesktopsInUse / DesktopsUnregistered / DesktopsDisconnected ... は稼働状況。
            // "Desktops" で始まるプロパティのうち設定値は無い（DesktopKind 等は "Desktop" 単数形）。
            if (property.StartsWith("Desktops", StringComparison.Ordinal)) return true;

            // Uid と、他オブジェクトの Uid を指す参照。
            if (property.Equals("Uid", StringComparison.OrdinalIgnoreCase)) return true;
            if (property.EndsWith("Uid", StringComparison.Ordinal) || property.EndsWith("Uids", StringComparison.Ordinal)) return true;

            return false;
        }

        /// <param name="saved">保存ファイル（比較の基準）。</param>
        /// <param name="current">現在の状態。</param>
        public static SnapshotDiff Compare(DesktopGroupSnapshot saved, DesktopGroupSnapshot current)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (current == null) throw new ArgumentNullException(nameof(current));

            var diff = new SnapshotDiff();

            // DG 本体
            if (saved.DesktopGroup != null && current.DesktopGroup != null)
                CompareItem(diff, DesktopGroupSnapshot.DesktopGroupSectionId,
                    current.DesktopGroup.Name ?? saved.DesktopGroup.Name, saved.DesktopGroup, current.DesktopGroup);

            // 関連オブジェクト
            foreach (var sectionId in DesktopGroupSnapshot.SectionIds)
            {
                string savedWarning, currentWarning;
                saved.Warnings.TryGetValue(sectionId, out savedWarning);
                current.Warnings.TryGetValue(sectionId, out currentWarning);

                // 片方でも取得に失敗している区分は、中身の有無を比べても意味が無い。
                if (savedWarning != null || currentWarning != null)
                {
                    diff.Entries.Add(new SnapshotDiffEntry
                    {
                        Kind = SnapshotDiffKind.NotComparable,
                        SectionId = sectionId,
                        SavedValue = savedWarning,
                        CurrentValue = currentWarning
                    });
                    continue;
                }

                var savedItems = Index(saved, sectionId);
                var currentItems = Index(current, sectionId);

                foreach (var name in savedItems.Keys.Union(currentItems.Keys, StringComparer.OrdinalIgnoreCase)
                                                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    SnapshotItem s, c;
                    savedItems.TryGetValue(name, out s);
                    currentItems.TryGetValue(name, out c);

                    if (s != null && c == null)
                        diff.Entries.Add(new SnapshotDiffEntry { Kind = SnapshotDiffKind.OnlyInSaved, SectionId = sectionId, ItemName = s.Name });
                    else if (s == null && c != null)
                        diff.Entries.Add(new SnapshotDiffEntry { Kind = SnapshotDiffKind.OnlyInCurrent, SectionId = sectionId, ItemName = c.Name });
                    else
                        CompareItem(diff, sectionId, c.Name, s, c);
                }
            }

            return diff;
        }

        private static void CompareItem(SnapshotDiff diff, string sectionId, string itemName, SnapshotItem saved, SnapshotItem current)
        {
            var names = saved.Properties.Keys.Union(current.Properties.Keys, StringComparer.Ordinal)
                                             .OrderBy(n => n, StringComparer.Ordinal);

            foreach (var prop in names)
            {
                if (IsExcluded(prop))
                {
                    diff.ExcludedProperties.Add(prop);
                    continue;
                }

                object sv, cv;
                var hasSaved = saved.Properties.TryGetValue(prop, out sv);
                var hasCurrent = current.Properties.TryGetValue(prop, out cv);

                // プロパティ自体が片方にしか無い = CVAD のバージョン差か、値が null で落ちたもの。
                // 値として null と同じに扱い、内容が違えば変更として出す。
                var savedText = Format(hasSaved ? sv : null);
                var currentText = Format(hasCurrent ? cv : null);

                if (savedText == currentText) continue;

                var kind = SnapshotDiffKind.Changed;
                if (IsSameMultiset(sv, cv)) kind = SnapshotDiffKind.Reordered;

                diff.Entries.Add(new SnapshotDiffEntry
                {
                    Kind = kind,
                    SectionId = sectionId,
                    ItemName = itemName,
                    Property = prop,
                    SavedValue = hasSaved ? savedText : null,
                    CurrentValue = hasCurrent ? currentText : null
                });
            }
        }

        /// <summary>
        /// 区分内の項目を名前で引けるようにする。名前が重複した場合は「名前 #2」のように番号を振る
        /// （通常は起きないが、黙って片方を捨てると差分を見落とすため）。
        /// </summary>
        private static Dictionary<string, SnapshotItem> Index(DesktopGroupSnapshot snap, string sectionId)
        {
            var result = new Dictionary<string, SnapshotItem>(StringComparer.OrdinalIgnoreCase);

            List<SnapshotItem> items;
            if (!snap.Sections.TryGetValue(sectionId, out items) || items == null) return result;

            foreach (var item in items)
            {
                var key = string.IsNullOrEmpty(item.Name) ? "(Uid " + item.Uid + ")" : item.Name;
                var unique = key;
                for (var n = 2; result.ContainsKey(unique); n++) unique = key + " #" + n;
                result[unique] = item;
            }

            return result;
        }

        /// <summary>並びが違うだけで、要素の集まりとしては同じか。</summary>
        private static bool IsSameMultiset(object a, object b)
        {
            var la = Unwrap(a) as IList;
            var lb = Unwrap(b) as IList;
            if (la == null || lb == null || la.Count != lb.Count) return false;

            var sa = la.Cast<object>().Select(Format).OrderBy(x => x, StringComparer.Ordinal);
            var sb = lb.Cast<object>().Select(Format).OrderBy(x => x, StringComparer.Ordinal);
            return sa.SequenceEqual(sb, StringComparer.Ordinal);
        }

        /// <summary>
        /// 値を、比較と表示の両方に使う決定的な文字列にする。
        /// 辞書はキー順に並べるので、取得順の揺れで差分が出ない。
        /// </summary>
        public static string Format(object value)
        {
            var sb = new StringBuilder();
            Append(sb, value);
            return sb.ToString();
        }

        /// <summary>
        /// PowerShell の包み（PSObject）を外す。通常の経路（変換処理・JSON 読み込み）の値は
        /// 包まれていないが、呼び出し元がスクリプトの場合に備える。
        /// </summary>
        private static object Unwrap(object value)
        {
            var ps = value as System.Management.Automation.PSObject;
            return ps == null ? value : ps.BaseObject;
        }

        private static void Append(StringBuilder sb, object value)
        {
            value = Unwrap(value);
            if (value == null) { sb.Append("(null)"); return; }

            var s = value as string;
            if (s != null) { sb.Append(s); return; }

            var dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                var first = true;
                foreach (var key in dict.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!first) sb.Append("; ");
                    first = false;
                    sb.Append(key).Append('=');
                    Append(sb, dict[key]);
                }
                sb.Append('}');
                return;
            }

            var list = value as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                var first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    Append(sb, item);
                }
                sb.Append(']');
                return;
            }

            sb.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
