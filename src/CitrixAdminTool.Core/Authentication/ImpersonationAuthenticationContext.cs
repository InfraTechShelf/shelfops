using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CitrixAdminTool.Core.Authentication
{
    /// <summary>
    /// 別のADアカウントでDDCへ認証するためのコンテキスト（フェーズ2）。
    ///
    /// 【なぜ偽装なのか】
    /// Brokerコマンドレットの多くは汎用的な -Credential を持たない。そのため
    /// コマンドレットに資格情報を渡すのではなく、LogonUser で取得したトークンによる
    /// 偽装コンテキスト内でRunspaceごと実行する。WinRMリモーティングは使わない
    /// （DDC側に追加構成を強いるため）。
    ///
    /// 【資格情報の扱い】
    /// パスワードは <see cref="SecureString"/> でのみ受け取り、LogonUser に渡す
    /// 一瞬だけアンマネージドメモリへ展開して直後にゼロクリアする。
    /// マネージドな string には一切変換しない。当然どこにも永続化しない。
    /// このオブジェクトはトークンだけを保持し、Disposeで解放する。
    /// </summary>
    [Obsolete("プロセス内偽装は接続プールの再利用により指定資格情報が無視される不具合があった" +
              "（SPEC_phase2.md §4.6）。別資格情報での接続は WorkerConnectionService 経由で" +
              "別プロセスとして起動する方式に置き換えた（SPEC_phase3.md）。この型は互換のため残す。")]
    public sealed class ImpersonationAuthenticationContext : IAuthenticationContext
    {
        private readonly string _displayUserName;
        private SafeAccessTokenHandle _token;

        private ImpersonationAuthenticationContext(SafeAccessTokenHandle token, string displayUserName)
        {
            _token = token;
            _displayUserName = displayUserName;
        }

        /// <summary>
        /// 資格情報からログオントークンを取得してコンテキストを作る。
        /// </summary>
        /// <param name="domain">ドメイン名。UPN形式(user@domain)のときは null でよい。</param>
        /// <param name="userName">ユーザー名。</param>
        /// <param name="password">パスワード。呼び出し側が所有権を持ち、使用後に破棄すること。</param>
        /// <exception cref="Win32Exception">LogonUserに失敗した場合。</exception>
        public static ImpersonationAuthenticationContext Create(string domain, string userName, SecureString password)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("ユーザー名が空です。", nameof(userName));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            var passwordPtr = IntPtr.Zero;
            try
            {
                passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(password);

                SafeAccessTokenHandle token;
                var ok = NativeMethods.LogonUser(
                    userName,
                    string.IsNullOrWhiteSpace(domain) ? null : domain,
                    passwordPtr,
                    NativeMethods.LOGON32_LOGON_NEW_CREDENTIALS,
                    NativeMethods.LOGON32_PROVIDER_WINNT50,
                    out token);

                if (!ok || token == null || token.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (token != null) token.Dispose();
                    throw new Win32Exception(error,
                        "指定した資格情報でログオントークンを取得できませんでした（Win32エラー "
                        + error + "）。ユーザー名・ドメイン・パスワードを確認してください。");
                }

                return new ImpersonationAuthenticationContext(token, FormatUserName(domain, userName));
            }
            finally
            {
                // 展開したパスワードは必ずゼロクリアして解放する。
                if (passwordPtr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
            }
        }

        private static string FormatUserName(string domain, string userName)
        {
            return string.IsNullOrWhiteSpace(domain) ? userName : domain + "\\" + userName;
        }

        public string Description
        {
            get { return "別資格情報（" + _displayUserName + " / ネットワーク認証のみ）"; }
        }

        /// <summary>別アカウントでのネットワーク認証を意図するので true。</summary>
        public bool ChangesNetworkIdentity
        {
            get { return true; }
        }

        /// <summary>
        /// 接続に使うことを**意図した**アカウント名を返す。
        ///
        /// 【重要】これは実際に認証されたアカウントではない。
        /// LOGON32_LOGON_NEW_CREDENTIALS では資格情報の正しさがローカルで検証されず、
        /// また WindowsIdentity.GetCurrent() はローカル識別子（元のユーザー）を返すため、
        /// **どちらのアカウントでサーバーに認証されたかをツール側から知る手段が無い**。
        /// 呼び出し側はこの値を「指定値」として扱い、事実として報告してはならない。
        /// </summary>
        public string GetRequestedUserName()
        {
            return _displayUserName;
        }

        /// <summary>
        /// 偽装コンテキスト内で処理を実行する。
        ///
        /// 内部の処理は同期的でなければならない（await をまたぐと偽装が引き継がれない）。
        /// 本ツールのSDK呼び出しは同期実行なので問題ない。
        /// </summary>
        public T Run<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (_token == null) throw new ObjectDisposedException(nameof(ImpersonationAuthenticationContext));

            return WindowsIdentity.RunImpersonated(_token, action);
        }

        public void Dispose()
        {
            var token = _token;
            _token = null;

            if (token != null) token.Dispose();
        }
    }
}
