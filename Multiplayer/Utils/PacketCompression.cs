using UnityEngine;
using System;
using System.IO;
using System.IO.Compression;

public static class PacketCompression
{
    public static byte[] Compress(byte[] data)
    {
        using (var outputStream = new MemoryStream())
        {
            using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }
    }

    public static byte[] Decompress(byte[] compressedData)
    {
        return Decompress(compressedData, int.MaxValue);
    }

    public static byte[] Decompress(
        byte[] compressedData,
        int maxDecompressedBytes)
    {
        if (compressedData == null)
            throw new ArgumentNullException(nameof(compressedData));
        if (maxDecompressedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDecompressedBytes));

        using (var inputStream = new MemoryStream(compressedData))
        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        using (var outputStream = new MemoryStream())
        {
            var buffer = new byte[8192];
            int totalBytes = 0;
            int bytesRead;
            while ((bytesRead = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maxDecompressedBytes)
                {
                    throw new InvalidDataException(
                        $"Decompressed payload exceeds {maxDecompressedBytes} bytes.");
                }

                outputStream.Write(buffer, 0, bytesRead);
            }

            return outputStream.ToArray();
        }
    }
}
