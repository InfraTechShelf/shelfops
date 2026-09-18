using System;
using System.IO;
using System.Text;

namespace CitrixAdminTool.Core.Interop
{
    /// <summary>
    /// 名前付きパイプ上で1メッセージ＝1JSON文字列をやり取りするための
    /// 長さプレフィックス方式のフレーミング。
    ///
    /// JsonWriter が複数行のJSONを出すため改行区切りは使えない。
    /// 「4バイトの長さ（リトルエンディアン）＋UTF-8本文」で1メッセージとする。
    /// </summary>
    public static class PipeMessaging
    {
        // 想定外に巨大な長さを受け取ったら（プロトコル不整合・破損）打ち切る安全弁。
        private const int MaxMessageBytes = 64 * 1024 * 1024;

        public static void WriteMessage(Stream stream, string text)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (text == null) text = string.Empty;

            var payload = Encoding.UTF8.GetBytes(text);
            var header = BitConverter.GetBytes(payload.Length);

            stream.Write(header, 0, header.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        /// <summary>
        /// 1メッセージを読む。相手がパイプを閉じた（EOF）場合は null を返す。
        /// </summary>
        public static string ReadMessage(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var header = new byte[4];
            if (!ReadExact(stream, header, 4)) return null; // EOF

            var length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > MaxMessageBytes)
                throw new InvalidDataException("パイプメッセージ長が不正です: " + length);

            if (length == 0) return string.Empty;

            var payload = new byte[length];
            if (!ReadExact(stream, payload, length))
                throw new EndOfStreamException("パイプメッセージ本文が途中で切れました。");

            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>
        /// count バイトを確実に読む。相手が閉じて1バイトも読めなければ false（EOF）。
        /// </summary>
        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            var read = 0;
            while (read < count)
            {
                var n = stream.Read(buffer, read, count - read);
                if (n == 0)
                {
                    if (read == 0) return false; // まだ何も読んでいない＝正常なEOF
                    throw new EndOfStreamException("パイプが途中で閉じられました。");
                }
                read += n;
            }
            return true;
        }
    }
}
