using System;

namespace CitrixAdminTool.Core.Connection
{
    /// <summary>
    /// Citrix Broker SDK（モジュール／スナップイン）をロードできなかったことを表す例外。
    ///
    /// これは「そのDDCに繋がらない」ではなく「このマシンにSDKが無い」という
    /// 実行環境側の問題であり、DDCを変えても結果は変わらない。
    /// 呼び出し側はこの例外を他の接続失敗と区別し、残りのDDC／サイトへの
    /// 試行を打ち切って一度だけ報告すること。
    /// </summary>
    public class SdkLoadException : Exception
    {
        public SdkLoadException(string message) : base(message) { }
        public SdkLoadException(string message, Exception inner) : base(message, inner) { }
    }
}
