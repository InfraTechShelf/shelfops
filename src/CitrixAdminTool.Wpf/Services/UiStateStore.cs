using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CitrixAdminTool.Core.Configuration.Json;

namespace CitrixAdminTool.Wpf.Services
{
    /// <summary>
    /// UIの状態（選択中サイト・表示言語など）の保存。
    ///
    /// sites.json とは別ファイルにしている。sites.json は管理者が手編集し、
    /// 環境間で配って回る「設定」であるのに対し、こちらはその端末限りの
    /// 使い勝手の情報なので、混ぜると設定の受け渡しが煩雑になるため。
    /// </summary>
    public static class UiStateStore
    {
        private const string FileName = "ui-state.json";
        private const string LastSelectedSiteIdKey = "lastSelectedSiteId";
        private const string LanguageKey = "language";

        private static string GetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShelfOps",
                FileName);
        }

        /// <summary>
        /// 保存済みの状態をすべて読む。ファイルが無い・壊れている場合は空を返す。
        ///
        /// 項目ごとにファイルを書き換えると、片方を保存したときにもう片方が消える。
        /// 常にファイル全体を読んでから書き戻す。
        /// </summary>
        private static Dictionary<string, object> LoadAll()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path)) return new Dictionary<string, object>();

                var root = JsonParser.Parse(File.ReadAllText(path, Encoding.UTF8))
                    as IDictionary<string, object>;
                if (root == null) return new Dictionary<string, object>();

                return new Dictionary<string, object>(root);
            }
            catch
            {
                // UIの利便性のための情報でしかないので、壊れていても黙って無視する。
                return new Dictionary<string, object>();
            }
        }

        private static string LoadValue(string key)
        {
            object value;
            return LoadAll().TryGetValue(key, out value) ? value as string : null;
        }

        private static void SaveValue(string key, string value)
        {
            try
            {
                var path = GetPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var root = LoadAll();
                root[key] = value;

                File.WriteAllText(path, JsonWriter.Write(root) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // 保存に失敗しても機能上の実害はないため握りつぶす。
            }
        }

        /// <summary>前回選択していたサイトのIDを読む。無ければ null。</summary>
        public static string LoadLastSelectedSiteId()
        {
            return LoadValue(LastSelectedSiteIdKey);
        }

        public static void SaveLastSelectedSiteId(string siteId)
        {
            SaveValue(LastSelectedSiteIdKey, siteId);
        }

        /// <summary>
        /// 利用者が明示的に選んだ表示言語（"ja" / "en"）。未選択なら null。
        /// null のときは呼び出し側がOSの表示言語から判定する。
        /// </summary>
        public static string LoadLanguage()
        {
            return LoadValue(LanguageKey);
        }

        public static void SaveLanguage(string language)
        {
            SaveValue(LanguageKey, language);
        }
    }
}
