using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Mvvm;
using CitrixAdminTool.Wpf.Services;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.ViewModels
{
    /// <summary>区分 ID の表示名。画面・書き出しの両方で使う。</summary>
    public static class SnapshotLabels
    {
        public static string Section(string sectionId)
        {
            return string.IsNullOrEmpty(sectionId) ? string.Empty : Loc.T("Snap_Sec_" + sectionId);
        }

        public static string Kind(SnapshotDiffKind kind)
        {
            return Loc.T("Diff_Kind_" + kind);
        }

        /// <summary>差分の並び順。実質的な変更を上に、参考情報を下に。</summary>
        public static int Order(SnapshotDiffKind kind)
        {
            switch (kind)
            {
                case SnapshotDiffKind.Changed: return 0;
                case SnapshotDiffKind.OnlyInSaved: return 1;
                case SnapshotDiffKind.OnlyInCurrent: return 2;
                case SnapshotDiffKind.NotComparable: return 3;
                default: return 4;
            }
        }
    }

    // ======================================================================
    // 採取結果の表示
    // ======================================================================

    /// <summary>採取結果の1行（区分・対象・プロパティ・値）。WPF のバインドのため public。</summary>
    public class SnapshotRow
    {
        public string SectionId { get; set; }
        public string SectionLabel { get; set; }
        public string ItemName { get; set; }
        public string Property { get; set; }
        public string Value { get; set; }

        /// <summary>取得できなかった区分の行か（赤字で示す）。</summary>
        public bool IsWarning { get; set; }

        /// <summary>稼働状況など、比較の対象外となるプロパティか（灰色で示す）。</summary>
        public bool IsExcluded { get; set; }
    }

    /// <summary>
    /// 1つのデリバリーグループの構成スナップショットを表示する ViewModel。
    ///
    /// プロパティが DG 本体だけで約60、関連オブジェクトを含めると数百行になるため、
    /// 「区分」と「検索」で絞れるようにする。目で探す前提にしない。
    /// </summary>
    public class SnapshotViewModel : ViewModelBase, IDisposable
    {
        private readonly List<SnapshotRow> _all = new List<SnapshotRow>();
        private string _sectionFilter;
        private string _searchText;
        private string _statusMessage;

        public SnapshotViewModel(DesktopGroupSnapshot snapshot, string siteId, string siteLabel)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            Snapshot = snapshot;
            SiteId = siteId;
            SiteLabel = siteLabel;

            Rows = new ObservableCollection<SnapshotRow>();
            SectionFilters = new ObservableCollection<string>();

            BuildRows();
            BuildSectionFilters();
            _sectionFilter = SectionFilters.FirstOrDefault();
            ApplyFilter();

            Loc.LanguageChanged += OnLanguageChanged;
        }

        public DesktopGroupSnapshot Snapshot { get; private set; }
        public string SiteId { get; private set; }
        public string SiteLabel { get; private set; }

        public ObservableCollection<SnapshotRow> Rows { get; private set; }
        public ObservableCollection<string> SectionFilters { get; private set; }

        public string Title
        {
            get { return Loc.F("Snap_Title", Snapshot.DesktopGroupName, SiteLabel); }
        }

        /// <summary>見出しの要約（採取日時・件数・取得できなかった区分）。</summary>
        public string Summary
        {
            get
            {
                var text = Loc.F("Snap_Summary",
                    Snapshot.DesktopGroupName,
                    Snapshot.CapturedAt,
                    Snapshot.DesktopGroup == null ? 0 : Snapshot.DesktopGroup.Properties.Count,
                    Snapshot.RelatedItemCount);

                if (Snapshot.Warnings.Count > 0)
                    text += "  " + Loc.F("Snap_SummaryWarnings", Snapshot.Warnings.Count);

                return text;
            }
        }

        public bool HasWarnings
        {
            get { return Snapshot.Warnings.Count > 0; }
        }

        public string SectionFilter
        {
            get { return _sectionFilter; }
            set { if (SetProperty(ref _sectionFilter, value)) ApplyFilter(); }
        }

        /// <summary>対象名・プロパティ名・値の部分一致（大文字小文字を区別しない）。</summary>
        public string SearchText
        {
            get { return _searchText; }
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        public string FilterSummary
        {
            get { return Loc.F("Snap_RowCount", Rows.Count, _all.Count); }
        }

        private void BuildRows()
        {
            _all.Clear();

            if (Snapshot.DesktopGroup != null)
                AddItemRows(DesktopGroupSnapshot.DesktopGroupSectionId, Snapshot.DesktopGroup);

            foreach (var sectionId in DesktopGroupSnapshot.SectionIds)
            {
                string warning;
                if (Snapshot.Warnings.TryGetValue(sectionId, out warning))
                {
                    _all.Add(new SnapshotRow
                    {
                        SectionId = sectionId,
                        SectionLabel = SnapshotLabels.Section(sectionId),
                        ItemName = Loc.T("Snap_NotCaptured"),
                        Value = warning,
                        IsWarning = true
                    });
                    continue;
                }

                List<SnapshotItem> items;
                Snapshot.Sections.TryGetValue(sectionId, out items);

                if (items == null || items.Count == 0)
                {
                    // 「取れて 0 件」と「取れなかった」を区別して見せる。
                    _all.Add(new SnapshotRow
                    {
                        SectionId = sectionId,
                        SectionLabel = SnapshotLabels.Section(sectionId),
                        ItemName = Loc.T("Snap_NoItems")
                    });
                    continue;
                }

                foreach (var item in items) AddItemRows(sectionId, item);
            }
        }

        private void AddItemRows(string sectionId, SnapshotItem item)
        {
            foreach (var p in item.Properties)
            {
                _all.Add(new SnapshotRow
                {
                    SectionId = sectionId,
                    SectionLabel = SnapshotLabels.Section(sectionId),
                    ItemName = item.Name,
                    Property = p.Key,
                    Value = SnapshotComparer.Format(p.Value),
                    IsExcluded = SnapshotComparer.IsExcluded(p.Key)
                });
            }
        }

        private void BuildSectionFilters()
        {
            SectionFilters.Clear();
            SectionFilters.Add(Loc.T("Common_FilterAll"));
            SectionFilters.Add(SnapshotLabels.Section(DesktopGroupSnapshot.DesktopGroupSectionId));
            foreach (var id in DesktopGroupSnapshot.SectionIds) SectionFilters.Add(SnapshotLabels.Section(id));
        }

        private void ApplyFilter()
        {
            IEnumerable<SnapshotRow> query = _all;

            var index = SectionFilters.IndexOf(_sectionFilter);
            if (index == 1)
                query = query.Where(r => r.SectionId == DesktopGroupSnapshot.DesktopGroupSectionId);
            else if (index > 1)
            {
                var id = DesktopGroupSnapshot.SectionIds[index - 2];
                query = query.Where(r => r.SectionId == id);
            }

            var keyword = (_searchText ?? string.Empty).Trim();
            if (keyword.Length > 0)
                query = query.Where(r => Contains(r.ItemName, keyword) || Contains(r.Property, keyword) || Contains(r.Value, keyword));

            Rows.Clear();
            foreach (var r in query) Rows.Add(r);
            RaisePropertyChanged(nameof(FilterSummary));
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>言語切り替え時は区分名が変わるため、行と選択肢を作り直す（選択は位置で戻す）。</summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            var index = SectionFilters.IndexOf(_sectionFilter);
            BuildRows();
            BuildSectionFilters();
            _sectionFilter = index >= 0 && index < SectionFilters.Count ? SectionFilters[index] : SectionFilters.FirstOrDefault();
            RaisePropertyChanged(nameof(SectionFilter));
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(Summary));
            ApplyFilter();
        }

        public static IList<ExportColumn<SnapshotRow>> ExportColumns()
        {
            return new List<ExportColumn<SnapshotRow>>
            {
                new ExportColumn<SnapshotRow>(Loc.T("Snap_ColSection"), r => r.SectionLabel),
                new ExportColumn<SnapshotRow>(Loc.T("Snap_ColItem"), r => r.ItemName),
                new ExportColumn<SnapshotRow>(Loc.T("Snap_ColProperty"), r => r.Property),
                new ExportColumn<SnapshotRow>(Loc.T("Snap_ColValue"), r => r.Value)
            };
        }

        public void Dispose()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
        }
    }

    // ======================================================================
    // 比較結果の表示
    // ======================================================================

    /// <summary>差分の1行。</summary>
    public class DiffRow
    {
        public SnapshotDiffKind Kind { get; set; }
        public string KindLabel { get; set; }
        public string SectionLabel { get; set; }
        public string ItemName { get; set; }
        public string Property { get; set; }
        public string SavedValue { get; set; }
        public string CurrentValue { get; set; }
    }

    /// <summary>
    /// 保存ファイルと現在の構成の比較結果を表示する ViewModel（0.9 は表示のみ）。
    ///
    /// 1.0 の復元は、この差分をそのまま「何が変わるか」のプレビューとして使う予定。
    /// そのため、ここで表示の正しさ（何を差分とし、何を対象外とするか）を先に固めておく。
    /// </summary>
    public class DiffViewModel : ViewModelBase, IDisposable
    {
        private readonly SnapshotDiff _diff;
        private readonly DesktopGroupSnapshot _saved;
        private readonly DesktopGroupSnapshot _current;
        private readonly string _savedPath;
        private bool _showReordered = true;

        public DiffViewModel(SnapshotDiff diff, DesktopGroupSnapshot saved, DesktopGroupSnapshot current, string savedPath)
        {
            _diff = diff ?? throw new ArgumentNullException(nameof(diff));
            _saved = saved ?? throw new ArgumentNullException(nameof(saved));
            _current = current ?? throw new ArgumentNullException(nameof(current));
            _savedPath = savedPath;

            Rows = new ObservableCollection<DiffRow>();
            ApplyFilter();

            Loc.LanguageChanged += OnLanguageChanged;
        }

        public ObservableCollection<DiffRow> Rows { get; private set; }

        public string Title
        {
            get { return Loc.F("Diff_Title", _current.DesktopGroupName); }
        }

        public string SavedInfo
        {
            get
            {
                return Loc.F("Diff_SavedInfo", _saved.DesktopGroupName, _saved.CapturedAt, _saved.SiteName,
                    System.IO.Path.GetFileName(_savedPath ?? string.Empty));
            }
        }

        public string CurrentInfo
        {
            get { return Loc.F("Diff_CurrentInfo", _current.DesktopGroupName, _current.CapturedAt, _current.SiteName); }
        }

        /// <summary>
        /// 比較の前提に関する注意。DG 名やサイトが違うファイルとの比較は、
        /// 差分の大半が「別物だから」になり、意味のある比較にならないことが多い。
        /// 止めはしない（意図的にテンプレートと比べることもある）が、必ず目に入るように出す。
        /// </summary>
        public string Caution
        {
            get
            {
                var notes = new List<string>();
                if (!string.Equals(_saved.DesktopGroupName, _current.DesktopGroupName, StringComparison.OrdinalIgnoreCase))
                    notes.Add(Loc.F("Diff_CautionGroup", _saved.DesktopGroupName, _current.DesktopGroupName));
                if (!string.IsNullOrEmpty(_saved.SiteName) && !string.IsNullOrEmpty(_current.SiteName)
                    && !string.Equals(_saved.SiteName, _current.SiteName, StringComparison.OrdinalIgnoreCase))
                    notes.Add(Loc.F("Diff_CautionSite", _saved.SiteName, _current.SiteName));
                return string.Join("  ", notes.ToArray());
            }
        }

        public bool HasCaution
        {
            get { return !string.IsNullOrEmpty(Caution); }
        }

        public string Summary
        {
            get
            {
                if (!_diff.HasChanges && _diff.Count(SnapshotDiffKind.NotComparable) == 0)
                {
                    return _diff.Count(SnapshotDiffKind.Reordered) == 0
                        ? Loc.T("Diff_NoChanges")
                        : Loc.F("Diff_NoChangesButReordered", _diff.Count(SnapshotDiffKind.Reordered));
                }

                return Loc.F("Diff_Summary",
                    _diff.Count(SnapshotDiffKind.Changed),
                    _diff.Count(SnapshotDiffKind.OnlyInSaved),
                    _diff.Count(SnapshotDiffKind.OnlyInCurrent),
                    _diff.Count(SnapshotDiffKind.Reordered),
                    _diff.Count(SnapshotDiffKind.NotComparable));
            }
        }

        /// <summary>比較対象外にしたプロパティ。黙って捨てず、名前を示す。</summary>
        public string ExcludedNote
        {
            get
            {
                return _diff.ExcludedProperties.Count == 0
                    ? string.Empty
                    : Loc.F("Diff_Excluded", _diff.ExcludedProperties.Count, string.Join(", ", _diff.ExcludedProperties.ToArray()));
            }
        }

        /// <summary>並び順だけの違いも表示するか。</summary>
        public bool ShowReordered
        {
            get { return _showReordered; }
            set { if (SetProperty(ref _showReordered, value)) ApplyFilter(); }
        }

        private void ApplyFilter()
        {
            Rows.Clear();

            var entries = _diff.Entries
                .Where(e => _showReordered || e.Kind != SnapshotDiffKind.Reordered)
                .OrderBy(e => SnapshotLabels.Order(e.Kind))
                .ThenBy(e => SectionOrder(e.SectionId))
                .ThenBy(e => e.ItemName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Property, StringComparer.Ordinal);

            foreach (var e in entries)
            {
                Rows.Add(new DiffRow
                {
                    Kind = e.Kind,
                    KindLabel = SnapshotLabels.Kind(e.Kind),
                    SectionLabel = SnapshotLabels.Section(e.SectionId),
                    ItemName = e.ItemName,
                    Property = e.Property,
                    SavedValue = e.SavedValue,
                    CurrentValue = e.CurrentValue
                });
            }
        }

        private static int SectionOrder(string sectionId)
        {
            if (sectionId == DesktopGroupSnapshot.DesktopGroupSectionId) return -1;
            return Array.IndexOf(DesktopGroupSnapshot.SectionIds, sectionId);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyFilter();
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(SavedInfo));
            RaisePropertyChanged(nameof(CurrentInfo));
            RaisePropertyChanged(nameof(Caution));
            RaisePropertyChanged(nameof(Summary));
            RaisePropertyChanged(nameof(ExcludedNote));
        }

        public static IList<ExportColumn<DiffRow>> ExportColumns()
        {
            return new List<ExportColumn<DiffRow>>
            {
                new ExportColumn<DiffRow>(Loc.T("Diff_ColKind"), r => r.KindLabel),
                new ExportColumn<DiffRow>(Loc.T("Snap_ColSection"), r => r.SectionLabel),
                new ExportColumn<DiffRow>(Loc.T("Snap_ColItem"), r => r.ItemName),
                new ExportColumn<DiffRow>(Loc.T("Snap_ColProperty"), r => r.Property),
                new ExportColumn<DiffRow>(Loc.T("Diff_ColSaved"), r => r.SavedValue),
                new ExportColumn<DiffRow>(Loc.T("Diff_ColCurrent"), r => r.CurrentValue)
            };
        }

        public void Dispose()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
        }
    }
}
