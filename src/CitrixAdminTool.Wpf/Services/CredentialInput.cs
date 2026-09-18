using System;
using System.Security;

namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// 資格情報プロンプトで入力された内容。
    ///
    /// パスワードは <see cref="SecureString"/> でのみ保持し、
    /// 使用側は必ず using で破棄すること。**永続化は一切しない**。
    /// </summary>
    public sealed class CredentialInput : IDisposable
    {
        public CredentialInput(string domain, string userName, SecureString password)
        {
            Domain = domain;
            UserName = userName;
            Password = password;
        }

        /// <summary>ドメイン名。UPN形式(user@domain)で入力された場合は空でよい。</summary>
        public string Domain { get; private set; }

        public string UserName { get; private set; }

        public SecureString Password { get; private set; }

        public void Dispose()
        {
            var password = Password;
            Password = null;

            if (password != null) password.Dispose();
        }
    }
}
