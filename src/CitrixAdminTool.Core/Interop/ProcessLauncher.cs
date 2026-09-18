using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using CitrixAdminTool.Core.Authentication;

namespace CitrixAdminTool.Core.Interop
{
    /// <summary>子プロセスの終了状況。</summary>
    public sealed class ProcessRunResult
    {
        public bool TimedOut { get; set; }
        public uint ExitCode { get; set; }
    }

    /// <summary>
    /// ワーカープロセスを起動して終了を待つ。
    ///
    /// 統合認証は <see cref="System.Diagnostics.Process"/> で足りるが、
    /// 別資格情報は netonly ログオンが必要で
    /// <see cref="NativeMethods.CreateProcessWithLogonW"/> を直接呼ぶ。
    /// どちらも標準ユーザー権限で動く。
    /// </summary>
    public static class ProcessLauncher
    {
        /// <summary>GUIと同じユーザーでワーカーを起動し、終了を待つ。</summary>
        public static ProcessRunResult RunIntegrated(string exePath, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var proc = Process.Start(psi))
            {
                if (proc == null)
                    throw new InvalidOperationException("ワーカープロセスを起動できませんでした: " + exePath);

                if (!proc.WaitForExit(timeoutMs))
                {
                    TryKill(proc);
                    return new ProcessRunResult { TimedOut = true };
                }

                return new ProcessRunResult { ExitCode = (uint)proc.ExitCode };
            }
        }

        /// <summary>
        /// 指定した資格情報のネットワークID（netonly）でワーカーを起動し、終了を待つ。
        ///
        /// パスワードは SecureString から一瞬だけアンマネージドへ展開し、
        /// CreateProcessWithLogonW に渡した直後にゼロクリアする。
        /// マネージド string には一切変換しない。
        /// </summary>
        public static ProcessRunResult RunWithCredential(
            string exePath, string arguments, string domain, string userName, SecureString password, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("ユーザー名が空です。", nameof(userName));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            // 実行ファイル名も含めてコマンドラインを組む（lpApplicationName を渡すため
            // 先頭にexeパスを置く。空白を含むので引用符で囲う）。
            var commandLine = new StringBuilder();
            commandLine.Append('"').Append(exePath).Append('"');
            if (!string.IsNullOrEmpty(arguments))
                commandLine.Append(' ').Append(arguments);

            var startupInfo = new NativeMethods.STARTUPINFO();
            startupInfo.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));

            NativeMethods.PROCESS_INFORMATION processInfo;
            var passwordPtr = IntPtr.Zero;

            try
            {
                passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(password);

                var ok = NativeMethods.CreateProcessWithLogonW(
                    userName,
                    string.IsNullOrWhiteSpace(domain) ? null : domain,
                    passwordPtr,
                    NativeMethods.LOGON_NETCREDENTIALS_ONLY,
                    exePath,
                    commandLine,
                    NativeMethods.CREATE_NO_WINDOW,
                    IntPtr.Zero,
                    System.IO.Path.GetDirectoryName(exePath),
                    ref startupInfo,
                    out processInfo);

                if (!ok)
                {
                    var err = Marshal.GetLastWin32Error();
                    throw new Win32Exception(err,
                        "ワーカープロセスを別資格情報で起動できませんでした（Win32エラー " + err + "）。"
                        + "ユーザー名・ドメインを確認してください。");
                }
            }
            finally
            {
                if (passwordPtr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
            }

            try
            {
                var wait = NativeMethods.WaitForSingleObject(processInfo.hProcess, (uint)timeoutMs);

                if (wait == NativeMethods.WAIT_TIMEOUT)
                {
                    NativeMethods.TerminateProcess(processInfo.hProcess, 1);
                    return new ProcessRunResult { TimedOut = true };
                }

                uint exitCode;
                NativeMethods.GetExitCodeProcess(processInfo.hProcess, out exitCode);
                return new ProcessRunResult { ExitCode = exitCode };
            }
            finally
            {
                if (processInfo.hThread != IntPtr.Zero) NativeMethods.CloseHandle(processInfo.hThread);
                if (processInfo.hProcess != IntPtr.Zero) NativeMethods.CloseHandle(processInfo.hProcess);
            }
        }

        private static void TryKill(Process proc)
        {
            try { if (!proc.HasExited) proc.Kill(); }
            catch { /* 既に終了している等は無視 */ }
        }
    }
}
