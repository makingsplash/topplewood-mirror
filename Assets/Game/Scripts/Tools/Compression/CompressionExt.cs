using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Game
{
    public static class CompressionExt
    {
        public static string Decompress(this string input)
        {
            byte[] compressed = Convert.FromBase64String(input);
            byte[] decompressed = Decompress(compressed);
            return Encoding.UTF8.GetString(decompressed);
        }

        public static string Compress(this string input)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(input);
            byte[] compressed = Compress(encoded);
            return Convert.ToBase64String(compressed);
        }

        private static byte[] Decompress(byte[] input)
        {
            using (var source = new MemoryStream(input))
            {
                byte[] lengthBytes = new byte[4];
                var bytesRead = source.Read(lengthBytes, 0, 4);

                var length = BitConverter.ToInt32(lengthBytes, 0);
                using (var decompressionStream = new GZipStream(source, CompressionMode.Decompress))
                {
                    var result = new byte[length];
                    var decompressedCount = decompressionStream.Read(result, 0, length);
                    return result;
                }
            }
        }

        private static byte[] Compress(byte[] input)
        {
            using (var result = new MemoryStream())
            {
                var lengthBytes = BitConverter.GetBytes(input.Length);
                result.Write(lengthBytes, 0, 4);

                using (var compressionStream = new GZipStream(result, CompressionMode.Compress))
                {
                    compressionStream.Write(input, 0, input.Length);
                    compressionStream.Flush();

                }
                return result.ToArray();
            }
        }

        public static bool IsValidBase64String(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return false;
            }
            
            var buffer = new Span<byte>(new byte[base64.Length]);
            return Convert.TryFromBase64String(base64, buffer , out int bytesParsed);
        }
    }
}
