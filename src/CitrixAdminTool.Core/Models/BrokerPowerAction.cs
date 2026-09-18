namespace CitrixAdminTool.Core.Models
{
    /// <summary>
    /// マシンに対する電源操作。
    ///
    /// 値の名前は Citrix Broker SDK の <c>New-BrokerHostingPowerAction -Action</c> が
    /// 受け付ける名前とそのまま一致させてある（ToString() を渡せる）。
    /// SDK には TurnOn / Suspend / Resume もあるが、本ツールが対象にするのは
    /// 「セッションが動いているマシンを止める・再起動する」場面なので、この4つに絞る。
    /// </summary>
    public enum BrokerPowerAction
    {
        /// <summary>OSに正常終了を要求してシャットダウンする。</summary>
        Shutdown,

        /// <summary>OSに正常終了を要求して再起動する。</summary>
        Restart,

        /// <summary>電源を強制的に切る（OSに終了処理の機会を与えない）。</summary>
        TurnOff,

        /// <summary>強制的に電源を入れ直す（OSに終了処理の機会を与えない）。</summary>
        Reset
    }

    /// <summary><see cref="BrokerPowerAction"/> に関する判定。</summary>
    public static class BrokerPowerActions
    {
        /// <summary>
        /// 強制的な操作か（OSに終了処理の機会を与えないか）。
        ///
        /// 呼び出し側は、これが true のときに確認ダイアログの警告を強める。
        /// 正常終了系は応答しないマシンには効かず、強制系はデータ損失を伴う、
        /// という性質の違いは利用者に明示する必要がある。
        /// </summary>
        public static bool IsForced(BrokerPowerAction action)
        {
            return action == BrokerPowerAction.TurnOff || action == BrokerPowerAction.Reset;
        }
    }
}
