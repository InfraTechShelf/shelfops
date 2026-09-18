using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CitrixAdminTool.Core.Configuration.Json;
using CitrixAdminTool.Core.Models;

namespace CitrixAdminTool.Core.Configuration
{
    /// <summary>設定ファイルの読み書きに失敗したことを表す例外。</summary>
    public class SiteConfigException : Exception
    {
        public SiteConfigException(string message) : base(message) { }
        public SiteConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// sites.json（管理対象サイトの接続設定）の読み書き。
    ///
    /// 【資格情報は絶対に保存しない】
    /// 永続化するのは接続先の情報と AuthMode のみ。パスワードやトークンの類は
    /// このファイルにも、他のどこにも書き出さない。
    ///
    /// 【保存先】
    /// - コンソール検証版: 実行フォルダ直下の sites.json（自己完結フォルダ運用のため）
    /// - WPF版（フェーズ2）: %APPDATA%\CitrixAdminTool\sites.json
    /// 両方を <see cref="GetDefaultConsolePath"/> / <see cref="GetDefaultRoamingPath"/> で提供する。
    /// </summary>
    public static class SiteConfigStore
    {
        public const string DefaultFileName = "sites.json";

        /// <summary>
        /// UTF-8（BOM無し）。BOM有りだと他ツールでの扱いが煩雑になるため付けない。
        /// 読み込み側はBOM有りも受け付ける。
        /// </summary>
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        /// <summary>コンソール検証版の既定パス: 実行ファイルと同じフォルダの sites.json。</summary>
        public static string GetDefaultConsolePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFileName);
        }

        /// <summary>WPF版（フェーズ2）の既定パス: %APPDATA%\CitrixAdminTool\sites.json。</summary>
        public static string GetDefaultRoamingPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShelfOps");
            return Path.Combine(dir, DefaultFileName);
        }

        /// <summary>
        /// 設定ファイルを読み込む。ファイルが存在しない場合は
        /// <see cref="FileNotFoundException"/> を投げる（呼び出し側でテンプレート生成等を判断させる）。
        /// </summary>
        public static List<SiteConnection> Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("設定ファイルが見つかりません: " + path, path);

            string text;
            try
            {
                // detectEncodingFromByteOrderMarks: BOM付きUTF-8/UTF-16で保存されていても読めるように。
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    text = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                throw new SiteConfigException(
                    "設定ファイルを読み取れませんでした: " + path + " / " + ex.Message, ex);
            }

            object root;
            try
            {
                root = JsonParser.Parse(text);
            }
            catch (JsonParseException ex)
            {
                throw new SiteConfigException(
                    "設定ファイルのJSONが不正です: " + path + Environment.NewLine + "  " + ex.Message, ex);
            }

            return ParseRoot(root, path);
        }

        private static List<SiteConnection> ParseRoot(object root, string path)
        {
            var rootObj = root as IDictionary<string, object>;
            if (rootObj == null)
                throw new SiteConfigException(
                    "設定ファイルの最上位はJSONオブジェクト { \"sites\": [...] } である必要があります: " + path);

            object sitesValue;
            if (!rootObj.TryGetValue("sites", out sitesValue))
                throw new SiteConfigException("設定ファイルに \"sites\" 配列がありません: " + path);

            var sitesArray = sitesValue as List<object>;
            if (sitesArray == null)
                throw new SiteConfigException("\"sites\" は配列である必要があります: " + path);

            var result = new List<SiteConnection>();
            for (int i = 0; i < sitesArray.Count; i++)
            {
                var entry = sitesArray[i] as IDictionary<string, object>;
                if (entry == null)
                    throw new SiteConfigException(
                        string.Format("\"sites\" の {0} 番目の要素がオブジェクトではありません: {1}", i + 1, path));

                result.Add(ParseSite(entry, i + 1, path));
            }

            return result;
        }

        private static SiteConnection ParseSite(IDictionary<string, object> entry, int index, string path)
        {
            var site = new SiteConnection
            {
                Id = GetString(entry, "id"),
                DisplayName = GetString(entry, "displayName"),
                PrimaryDdc = GetString(entry, "primaryDdc"),
                AlternateDdcs = GetStringList(entry, "alternateDdcs", index, path),
                AuthMode = ParseAuthMode(GetString(entry, "authMode"), index, path)
            };

            if (string.IsNullOrWhiteSpace(site.Id))
                throw new SiteConfigException(
                    string.Format("\"sites\" の {0} 番目に \"id\" がありません: {1}", index, path));

            if (string.IsNullOrWhiteSpace(site.PrimaryDdc) && site.AlternateDdcs.Count == 0)
                throw new SiteConfigException(
                    string.Format("サイト \"{0}\" にDDCが1つも設定されていません（primaryDdc または alternateDdcs が必要）: {1}",
                                  site.Id, path));

            return site;
        }

        private static AuthMode ParseAuthMode(string raw, int index, string path)
        {
            // 未指定は統合Windows認証とみなす（フェーズ1で唯一実装されているモード）。
            if (string.IsNullOrWhiteSpace(raw)) return AuthMode.IntegratedWindows;

            AuthMode mode;
            if (!Enum.TryParse(raw.Trim(), true, out mode))
                throw new SiteConfigException(string.Format(
                    "\"sites\" の {0} 番目の \"authMode\" が不正です: \"{1}\"（有効な値: IntegratedWindows / PromptForCredential）: {2}",
                    index, raw, path));

            return mode;
        }

        private static string GetString(IDictionary<string, object> obj, string key)
        {
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return null;
            return value as string ?? value.ToString();
        }

        private static List<string> GetStringList(IDictionary<string, object> obj, string key, int index, string path)
        {
            var result = new List<string>();

            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return result;

            var array = value as List<object>;
            if (array == null)
                throw new SiteConfigException(string.Format(
                    "\"sites\" の {0} 番目の \"{1}\" は配列である必要があります: {2}", index, key, path));

            foreach (var item in array)
            {
                if (item == null) continue;
                var s = item as string ?? item.ToString();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
            }

            return result;
        }

        /// <summary>設定を保存する。親フォルダが無ければ作成する。</summary>
        public static void Save(string path, IEnumerable<SiteConnection> sites)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (sites == null) throw new ArgumentNullException(nameof(sites));

            var siteObjects = new List<object>();
            foreach (var site in sites)
            {
                // Dictionary の列挙順で書き出されるため、挿入順＝仕様書のJSON例の順序になる。
                var entry = new Dictionary<string, object>
                {
                    { "id", site.Id },
                    { "displayName", site.DisplayName },
                    { "primaryDdc", site.PrimaryDdc },
                    { "alternateDdcs", new List<object>(ToObjectList(site.AlternateDdcs)) },
                    { "authMode", site.AuthMode.ToString() }
                };
                siteObjects.Add(entry);
            }

            var root = new Dictionary<string, object> { { "sites", siteObjects } };
            var json = JsonWriter.Write(root) + "\r\n";

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, json, FileEncoding);
            }
            catch (Exception ex)
            {
                throw new SiteConfigException(
                    "設定ファイルを保存できませんでした: " + path + " / " + ex.Message, ex);
            }
        }

        private static IEnumerable<object> ToObjectList(IEnumerable<string> values)
        {
            if (values == null) yield break;
            foreach (var v in values) yield return v;
        }

        /// <summary>
        /// 記入例入りのテンプレート sites.json を書き出す。
        ///
        /// 検証環境にはClaude Codeも開発ツールも無く、その場で設定ファイルを
        /// 用意する必要があるため、ツール自身がひな形を出せるようにしてある。
        /// </summary>
        public static void WriteTemplate(string path)
        {
            const string template =
@"{
  // CVAD管理ツール サイト定義ファイル
  //
  // このファイルには接続先の情報だけを書きます。
  // パスワード等の資格情報は絶対に書かないでください（ツールも保存しません）。
  //
  //   id            : サイトを識別する任意のID（重複不可）
  //   displayName   : 画面・ログに表示される名前
  //   primaryDdc    : 最初に接続を試みるDDCのFQDN
  //   alternateDdcs : primaryDdcが失敗したとき順に試す代替DDCのFQDN
  //   authMode      : IntegratedWindows（現在のログオンユーザーで接続）
  //                   ※ PromptForCredential はフェーズ2で実装予定
  //
  // 下の2件はサンプルです。実際のDDCのFQDNに書き換えてください。

  ""sites"": [
    {
      ""id"": ""site-tokyo-prod"",
      ""displayName"": ""東京本番サイト"",
      ""primaryDdc"": ""ddc01.corp.example.com"",
      ""alternateDdcs"": [""ddc02.corp.example.com""],
      ""authMode"": ""IntegratedWindows""
    },
    {
      ""id"": ""site-osaka-dr"",
      ""displayName"": ""大阪DRサイト"",
      ""primaryDdc"": ""ddc-dr01.corp.example.com"",
      ""alternateDdcs"": [],
      ""authMode"": ""IntegratedWindows""
    }
  ]
}
";
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, template, FileEncoding);
        }
    }
}
