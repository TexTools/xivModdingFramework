using System.IO;
using System.IO.Compression;

namespace xivModdingFramework.Helpers
{
    public static class IOUtil
    {
        /// <summary>
        /// Compresses raw byte data.
        /// </summary>
        /// <param name="uncompressedBytes">The data to be compressed.</param>
        /// <returns>The compressed byte data.</returns>
        public static byte[] Compressor(byte[] uncompressedBytes)
        {
            using (var uMemoryStream = new MemoryStream(uncompressedBytes))
            {
                byte[] compbytes = null;
                using (var cMemoryStream = new MemoryStream())
                {
                    using (var deflateStream = new DeflateStream(cMemoryStream, CompressionMode.Compress))
                    {
                        uMemoryStream.CopyTo(deflateStream);
                        deflateStream.Close();
                        compbytes = cMemoryStream.ToArray();
                    }
                }
                return compbytes;
            }
        }

        /// <summary>
        /// Decompresses raw byte data.
        /// </summary>
        /// <param name="compressedBytes">The byte data to decompress.</param>
        /// <param name="uncompressedSize">The final size of the compressed data after decompression.</param>
        /// <returns>The decompressed byte data.</returns>
        public static byte[] Decompressor(byte[] compressedBytes, int uncompressedSize)
        {
            var decompressedBytes = new byte[uncompressedSize];

            using (var ms = new MemoryStream(compressedBytes))
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    ds.Read(decompressedBytes, 0, uncompressedSize);
                }
            }

            return decompressedBytes;
        }
    }
}