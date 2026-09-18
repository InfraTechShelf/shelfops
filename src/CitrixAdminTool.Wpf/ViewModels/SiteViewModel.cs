using System;
using System.Collections.Generic;
using System.Linq;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Mvvm;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.ViewModels
{
    /// <summary>サイト一覧に出す接続状態。</summary>
    public enum SiteStatus
    {
        Unknown,
        Testing,
        Connected,
        Failed
    }

    /// <summary>
    /// 編集可能なサイト1件。<see cref="SiteConnection"/> をUI向けに包む。
    ///
    /// 代替DDCはモデルではList、UIでは複数行テキスト（1行1台）として扱う。
    /// DataGridで行を足すより、コピー＆ペーストで一気に流し込める方が
    /// 実際の運用（既存の手順書やExcelからの転記）に合うため。
    /// </summary>
    public class SiteViewModel : ViewModelBase
    {
        private string _id;
        private string _displayName;
        private string _primaryDdc;
        private string _alternateDdcsText;
        private AuthMode _authMode;
        private SiteStatus _status;
        private string _statusDetail;

        public SiteViewModel()
        {
            _id = string.Empty;
            _displayName = string.Empty;
            _primaryDdc = string.Empty;
            _alternateDdcsText = string.Empty;
            _authMode = AuthMode.IntegratedWindows;
            _status = SiteStatus.Unknown;
        }

        public static SiteViewModel FromModel(SiteConnection site)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));

            return new SiteViewModel
            {
                _id = site.Id ?? string.Empty,
                _displayName = site.DisplayName ?? string.Empty,
                _primaryDdc = site.PrimaryDdc ?? string.Empty,
                _alternateDdcsText = site.AlternateDdcs == null
                    ? string.Empty
                    : string.Join(Environment.NewLine, site.AlternateDdcs.ToArray()),
                _authMode = site.AuthMode
            };
        }

        /// <summary>
        /// このサイトの複製を作る。DDC構成と認証モードを引き継ぎ、IDと表示名は呼び出し側が付け直す。
        ///
        /// <see cref="ToModel"/> → <see cref="FromModel"/> を経由することで、
        /// 代替DDCの複数行テキストのパース／再構成を既存ロジックに任せられる
        /// （プロパティを1つずつ写すとコピー漏れが起きる）。
        ///
        /// 接続状態（Status / StatusDetail）は意図的に引き継がない。
        /// 複製しただけで接続確認済みに見えると誤解を生むため。
        /// </summary>
        public SiteViewModel CreateCopy(string newId, string newDisplayName)
        {
            var copy = FromModel(ToModel());
            copy.Id = newId ?? string.Empty;
            copy.DisplayName = newDisplayName ?? string.Empty;
            return copy;
        }

        public SiteConnection ToModel()
        {
            return new SiteConnection
            {
                Id = (Id ?? string.Empty).Trim(),
                DisplayName = (DisplayName ?? string.Empty).Trim(),
                PrimaryDdc = (PrimaryDdc ?? string.Empty).Trim(),
                AlternateDdcs = ParseAlternateDdcs(),
                AuthMode = AuthMode
            };
        }

        private List<string> ParseAlternateDdcs()
        {
            if (string.IsNullOrWhiteSpace(AlternateDdcsText))
                return new List<string>();

            return AlternateDdcsText
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        public string Id
        {
            get { return _id; }
            set { if (SetProperty(ref _id, value)) RaisePropertyChanged(nameof(Label)); }
        }

        public string DisplayName
        {
            get { return _displayName; }
            set { if (SetProperty(ref _displayName, value)) RaisePropertyChanged(nameof(Label)); }
        }

        public string PrimaryDdc
        {
            get { return _primaryDdc; }
            set { SetProperty(ref _primaryDdc, value); }
        }

        public string AlternateDdcsText
        {
            get { return _alternateDdcsText; }
            set { SetProperty(ref _alternateDdcsText, value); }
        }

        public AuthMode AuthMode
        {
            get { return _authMode; }
            set { if (SetProperty(ref _authMode, value)) RaisePropertyChanged(nameof(RequiresCredential)); }
        }

        /// <summary>接続時に資格情報の入力が必要か。</summary>
        public bool RequiresCredential
        {
            get { return _authMode == AuthMode.PromptForCredential; }
        }

        /// <summary>一覧に出す表示名。未入力ならIDで代替する。</summary>
        public string Label
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_displayName)) return _displayName;
                if (!string.IsNullOrWhiteSpace(_id)) return _id;
                return Loc.T("Site_NewSiteUnnamed");
            }
        }

        public SiteStatus Status
        {
            get { return _status; }
            set { if (SetProperty(ref _status, value)) RaisePropertyChanged(nameof(StatusGlyph)); }
        }

        /// <summary>一覧の左端に出す状態記号。</summary>
        public string StatusGlyph
        {
            get
            {
                switch (_status)
                {
                    case SiteStatus.Connected: return "○";
                    case SiteStatus.Failed: return "×";
                    case SiteStatus.Testing: return "…";
                    default: return "−";
                }
            }
        }

        /// <summary>状態の補足（接続先DDCや失敗理由の要約）。</summary>
        public string StatusDetail
        {
            get { return _statusDetail; }
            set { SetProperty(ref _statusDetail, value); }
        }

        /// <summary>
        /// 保存・接続の前に最低限の入力を検証する。
        /// 問題があればメッセージを返し、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                return Loc.T("Site_ValidateId");

            var model = ToModel();
            if (!model.GetDdcsInTryOrder().Any())
                return Loc.T("Site_ValidateDdc");

            return null;
        }
    }
}
