using System.Collections.Generic;

namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// 認証モード。
    /// IntegratedWindows: ツール実行ユーザーのADアカウント（Kerberos/NTLM）でDDCに認証する。
    /// PromptForCredential: 接続の都度、別のADアカウントの資格情報を入力させる（永続化しない）。
    /// </summary>
    public enum AuthMode
    {
        IntegratedWindows,
        PromptForCredential
    }

    /// <summary>
    /// 管理対象のCVADサイト1つ分の接続設定。
    /// このオブジェクトはJSONに永続化される。資格情報そのものは決して保持しない。
    /// </summary>
    public class SiteConnection
    {
        /// <summary>サイトを一意に識別するID（例: "site-tokyo-prod"）。</summary>
        public string Id { get; set; }

        /// <summary>UI表示用の名称（例: "東京本番サイト"）。</summary>
        public string DisplayName { get; set; }

        /// <summary>優先的に接続するDDCのFQDN。</summary>
        public string PrimaryDdc { get; set; }

        /// <summary>
        /// プライマリDDCが応答しない場合に順に試行する代替DDCのFQDN群。
        /// 複数DDC構成での可用性確保のために使う。
        /// </summary>
        public List<string> AlternateDdcs { get; set; } = new List<string>();

        /// <summary>認証モード。</summary>
        public AuthMode AuthMode { get; set; } = AuthMode.IntegratedWindows;

        /// <summary>
        /// 表示用のラベル。DisplayNameが未設定ならIdで代替する。
        /// </summary>
        public string Label
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                if (!string.IsNullOrWhiteSpace(Id)) return Id;
                return "(名称未設定のサイト)";
            }
        }

        /// <summary>
        /// プライマリと代替を合わせた、試行順のDDCリストを返す。
        /// 空白要素は除外し、重複は除去する（同じDDCを二度試行しても意味がないため）。
        /// </summary>
        public IEnumerable<string> GetDdcsInTryOrder()
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(PrimaryDdc))
            {
                var primary = PrimaryDdc.Trim();
                seen.Add(primary);
                yield return primary;
            }

            if (AlternateDdcs != null)
            {
                foreach (var ddc in AlternateDdcs)
                {
                    if (string.IsNullOrWhiteSpace(ddc)) continue;

                    var trimmed = ddc.Trim();
                    if (seen.Add(trimmed))
                        yield return trimmed;
                }
            }
        }
    }
}
