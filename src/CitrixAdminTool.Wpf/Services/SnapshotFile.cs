using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CitrixAdminTool.Core.Configuration.Json;
using CitrixAdminTool.Core.Models;
using CitrixAdminTool.Wpf.Services.Localization;

namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// デリバリーグループ構成のスナップショットをファイルに保存・読み込みする。
    ///
    /// 形式は JSON（docs/SPEC_phase5_deliverygroups.md §3.2）。人が開いて読めるよう整形して書く。
    /// 文字コードは UTF-8（BOM 無し）。JSON の標準に合わせる。読み込み側は BOM 付きも受け付ける
    /// （メモ帳で編集して保存し直されることがあるため）。
    ///
    /// ファイルには資格情報は含まれないが、ユーザー／グループ名と SID、マシン名、
    /// アプリの実行パスが含まれる。ログと同じ扱いで共有すること（クイックスタートに明記）。
    /// </summary>
    public static class SnapshotFile
    {
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        /// <summary>既定のファイル名。DG 名・サイト・日時をファイル名に持たせる。</summary>
        public static string BuildFileName(DesktopGroupSnapshot snapshot, string siteId)
        {
            return string.Format("ShelfOps_dg_{0}_{1}_{2:yyyyMMdd_HHmmss}.json",
                Sanitize(snapshot.DesktopGroupName), Sanitize(siteId), DateTime.Now);
        }

        public static bool TrySave(string path, DesktopGroupSnapshot snapshot, out string error)
        {
            error = null;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(path, JsonWriter.Write(snapshot.ToDict()) + Environment.NewLine, FileEncoding);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 読み込む。失敗時は null を返し、利用者に見せる理由を <paramref name="error"/> に入れる。
        /// 別のファイル（sites.json など）や新しい版の形式を選んだ場合も、分かる言葉で止める。
        /// </summary>
        public static DesktopGroupSnapshot TryLoad(string path, out string error)
        {
            error = null;

            string text;
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                    text = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                error = Loc.F("Snap_LoadReadFailed", ex.Message);
                return null;
            }

            object root;
            try
            {
                root = JsonParser.Parse(text);
            }
            catch (Exception ex)
            {
                error = Loc.F("Snap_LoadNotJson", ex.Message);
                return null;
            }

            var dict = root as IDictionary<string, object>;
            try
            {
                return DesktopGroupSnapshot.FromDict(dict);
            }
            catch (InvalidDataException ex)
            {
                error = ex.Message == "formatVersion" ? Loc.T("Snap_LoadTooNew") : Loc.T("Snap_LoadWrongFormat");
                return null;
            }
            catch (Exception ex)
            {
                error = Loc.F("Snap_LoadNotJson", ex.Message);
                return null;
            }
        }

        /// <summary>ファイルに残す「どのツールで採ったか」。</summary>
        public static string ToolName()
        {
            var version = "unknown";
            try
            {
                var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion)) version = attr.InformationalVersion;
            }
            catch { }
            return "ShelfOps " + version;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";

            var sb = new StringBuilder(value.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in value) sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
            return sb.ToString();
        }
    }
}
