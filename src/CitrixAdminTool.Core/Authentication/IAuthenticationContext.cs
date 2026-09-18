using System;

namespace CitrixAdminTool.Core.Authentication
{
    /// <summary>
    /// Citrix SDKの呼び出しを、どのWindowsユーザーコンテキストで実行するかを表す抽象。
    ///
    /// 【なぜこの抽象が必要か】
    /// Brokerコマンドレットの多くは汎用的な -Credential を持たない。そのため
    /// 「実行中のプロセスとは別のADアカウント」で操作したい場合、コマンドレットに
    /// 資格情報を渡すのではなく、Win32 API LogonUser で取得したトークンによる
    /// 偽装（impersonation）コンテキスト内でRunspaceごと実行する必要がある。
    /// WinRMリモーティングは使わない（DDC側に追加構成を強いるため）。
    ///
    /// 【フェーズ1の実装範囲】
    /// 統合Windows認証のみ。実体は「何もしないパススルー」
    /// （<see cref="IntegratedWindowsAuthenticationContext"/>）。
    /// フェーズ2で偽装版の実装をこのインターフェースの背後に差し込むことで、
    /// 呼び出し側（CitrixConnectionService）を変更せずに済むようにしてある。
    /// </summary>
    public interface IAuthenticationContext : IDisposable
    {
        /// <summary>ログ表示用の、このコンテキストの説明（例: "統合Windows認証"）。</summary>
        string Description { get; }

        /// <summary>
        /// このコンテキストが、プロセスの実行ユーザーとは別のアカウントで
        /// ネットワーク認証を行おうとしているか。
        ///
        /// true の場合、**実際にどちらのアカウントでサーバーに認証されたかを
        /// ツール側で確認する手段が無い**ことに注意する。報告時はこれを踏まえ、
        /// 「指定したアカウント」と「確認できた事実」を区別して出すこと。
        /// </summary>
        bool ChangesNetworkIdentity { get; }

        /// <summary>
        /// このコンテキストで**接続に使うことを意図した**アカウント名。
        ///
        /// 【重要】これは「実際に認証されたアカウント」ではない。
        /// 統合認証ならプロセス実行ユーザーと一致するが、別資格情報モードでは
        /// 入力された値をそのまま返すだけであり、認証の成否とは無関係。
        /// </summary>
        string GetRequestedUserName();

        /// <summary>
        /// 指定した処理を、このコンテキストのユーザーとして実行する。
        /// 統合Windows認証ではそのまま呼び出すだけ（パススルー）。
        /// </summary>
        T Run<T>(Func<T> action);
    }
}
