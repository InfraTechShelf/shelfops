using System;
using System.Security.Principal;

namespace CitrixAdminTool.Core.Authentication
{
    /// <summary>
    /// 統合Windows認証（Kerberos/NTLM）のコンテキスト。
    ///
    /// オンプレCVADでは、ドメイン参加済みマシンからコマンドを実行すれば
    /// 現在のユーザーコンテキストでDDCに認証されるため、接続のために行うべき
    /// 特別な処理は無い。よってこの実装は完全なパススルーである。
    ///
    /// 権限の主体はCitrix Delegated Administrationで付与されたCitrix管理者ロールであり、
    /// ローカル端末側の管理者昇格は不要。
    /// </summary>
    public sealed class IntegratedWindowsAuthenticationContext : IAuthenticationContext
    {
        public string Description
        {
            get { return "統合Windows認証（現在のログオンユーザー）"; }
        }

        /// <summary>統合認証はプロセスの実行ユーザーそのままなので false。</summary>
        public bool ChangesNetworkIdentity
        {
            get { return false; }
        }

        public string GetRequestedUserName()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return identity != null ? identity.Name : null;
                }
            }
            catch (Exception ex)
            {
                return "(取得失敗: " + ex.Message + ")";
            }
        }

        /// <summary>パススルー実行。偽装は行わない。</summary>
        public T Run<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return action();
        }

        public void Dispose()
        {
            // 解放すべきリソースは無い（偽装トークンを持たないため）。
        }
    }
}
