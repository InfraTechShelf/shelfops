namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// 資格情報の入力をユーザーに求める手段の抽象。
    /// ViewModelがWindowを直接開かないようにするための境界。
    /// </summary>
    public interface ICredentialPrompt
    {
        /// <summary>
        /// 資格情報の入力を求める。キャンセルされた場合は null を返す。
        /// </summary>
        /// <param name="siteLabel">どのサイト向けの入力かを示す表示名。</param>
        CredentialInput Prompt(string siteLabel);
    }
}
