using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CitrixAdminTool.Core.Authentication
{
    /// <summary>
    /// 偽装およびワーカープロセス起動に必要なWin32 APIの宣言。
    /// </summary>
    internal static class NativeMethods
    {
        // ---- CreateProcessWithLogonW 用 ----

        /// <summary>
        /// LOGON_NETCREDENTIALS_ONLY。指定資格情報は**外向きのネットワーク認証にのみ**使い、
        /// ローカルの識別子は現在のユーザーのまま維持する（`runas /netonly` 相当）。
        /// 対象アカウントに実行端末へのローカルログオン権限が無くてよい。
        /// </summary>
        public const int LOGON_NETCREDENTIALS_ONLY = 0x00000002;

        /// <summary>子プロセスにコンソールウィンドウを作らせない。</summary>
        public const uint CREATE_NO_WINDOW = 0x08000000;

        /// <summary>子プロセスに新しいコンソールを割り当てる。CREATE_NO_WINDOW と併用する。</summary>
        public const uint CREATE_NEW_CONSOLE = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        /// <summary>
        /// 指定した資格情報でプロセスを起動する。
        /// パスワードは呼び出し側が SecureString からアンマネージドへ一瞬だけ展開し、
        /// この呼び出しの直後にゼロクリアすること（マネージド string にはしない）。
        /// lpCommandLine は関数内で書き換えられ得るため StringBuilder で渡す。
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessWithLogonW(
            string lpUsername,
            string lpDomain,
            IntPtr lpPassword,
            int dwLogonFlags,
            string lpApplicationName,
            System.Text.StringBuilder lpCommandLine,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        public const uint WAIT_OBJECT_0 = 0x00000000;
        public const uint WAIT_TIMEOUT = 0x00000102;
        public const uint STILL_ACTIVE = 259;

        // ---- LogonUser 用（フェーズ2から。現在はGUI通常経路では未使用） ----

        /// <summary>
        /// LOGON32_LOGON_NEW_CREDENTIALS。
        ///
        /// 【なぜこのログオン種別か】
        /// ローカルの識別子は現在のユーザーのまま維持し、**外向きのネットワーク認証だけ**を
        /// 指定した資格情報で行う。`runas /netonly` と同じ挙動。
        ///
        /// これが本ツールの用途に正しい理由:
        ///   - 目的は「別のADアカウントでDDCに認証すること」だけであり、
        ///     ローカルマシンへのログオン権限は不要。
        ///   - LOGON32_LOGON_INTERACTIVE だと対象アカウントが実行端末に
        ///     ローカルログオンできる必要があり、管理用アカウントでは
        ///     ポリシーで禁止されていることが多い。
        ///   - 資格情報の正しさはローカルでは検証されず、実際にDDCへ接続した時点で
        ///     初めて判定される。したがって「LogonUserは成功したが接続は認証エラー」
        ///     という結果があり得る（UI側でその旨を案内すること）。
        /// </summary>
        public const int LOGON32_LOGON_NEW_CREDENTIALS = 9;

        /// <summary>
        /// LOGON32_PROVIDER_WINNT50。
        /// LOGON32_LOGON_NEW_CREDENTIALS と組み合わせる場合はこのプロバイダを指定する。
        /// </summary>
        public const int LOGON32_PROVIDER_WINNT50 = 3;

        /// <summary>
        /// 指定した資格情報でログオントークンを取得する。
        /// パスワードは <see cref="IntPtr"/> で受け取り、呼び出し側が
        /// SecureString から一時的にアンマネージドへ展開して即座にゼロクリアする
        /// （マネージド string にすると回収されるまでメモリに残るため）。
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            IntPtr lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out SafeAccessTokenHandle phToken);
    }
}
