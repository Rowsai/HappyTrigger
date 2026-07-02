using System;
using System.IO;

namespace HappyTrigger;

public static class ImageDimensionReader
{
    public static bool TryRead(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (TryReadPng(reader, out width, out height))
            {
                return true;
            }

            stream.Position = 0;
            if (TryReadGif(reader, out width, out height))
            {
                return true;
            }

            stream.Position = 0;
            if (TryReadBmp(reader, out width, out height))
            {
                return true;
            }

            stream.Position = 0;
            if (TryReadWebp(reader, out width, out height))
            {
                return true;
            }

            stream.Position = 0;
            if (TryReadJpeg(reader, out width, out height))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return width > 0 && height > 0;
    }

    private static bool TryReadPng(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;

        var signature = reader.ReadBytes(24);
        if (signature.Length < 24)
        {
            return false;
        }

        if (signature[0] != 0x89 || signature[1] != 0x50 || signature[2] != 0x4E || signature[3] != 0x47)
        {
            return false;
        }

        width = ReadBigEndianInt32(signature, 16);
        height = ReadBigEndianInt32(signature, 20);
        return width > 0 && height > 0;
    }

    private static bool TryReadGif(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;

        var header = reader.ReadBytes(10);
        if (header.Length < 10)
        {
            return false;
        }

        var isGif = header[0] == 'G' && header[1] == 'I' && header[2] == 'F';
        if (!isGif)
        {
            return false;
        }

        width = header[6] | (header[7] << 8);
        height = header[8] | (header[9] << 8);
        return width > 0 && height > 0;
    }

    private static bool TryReadBmp(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;

        var header = reader.ReadBytes(26);
        if (header.Length < 26)
        {
            return false;
        }

        if (header[0] != 'B' || header[1] != 'M')
        {
            return false;
        }

        width = BitConverter.ToInt32(header, 18);
        height = Math.Abs(BitConverter.ToInt32(header, 22));
        return width > 0 && height > 0;
    }

    private static bool TryReadWebp(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;

        var header = reader.ReadBytes(30);
        if (header.Length < 30)
        {
            return false;
        }

        var isRiff = header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F';
        var isWebp = header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P';
        if (!isRiff || !isWebp)
        {
            return false;
        }

        if (header[12] == 'V' && header[13] == 'P' && header[14] == '8' && header[15] == 'X')
        {
            width = 1 + header[24] + (header[25] << 8) + (header[26] << 16);
            height = 1 + header[27] + (header[28] << 8) + (header[29] << 16);
            return width > 0 && height > 0;
        }

        if (header[12] == 'V' && header[13] == 'P' && header[14] == '8' && header[15] == ' ')
        {
            // Lossy WebP. The frame header normally starts at byte 20.
            if (header.Length >= 30 && header[23] == 0x9D && header[24] == 0x01 && header[25] == 0x2A)
            {
                width = (header[26] | (header[27] << 8)) & 0x3FFF;
                height = (header[28] | (header[29] << 8)) & 0x3FFF;
                return width > 0 && height > 0;
            }
        }

        return false;
    }

    private static bool TryReadJpeg(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8)
        {
            return false;
        }

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var prefix = reader.ReadByte();
            if (prefix != 0xFF)
            {
                continue;
            }

            byte marker;
            do
            {
                marker = reader.ReadByte();
            }
            while (marker == 0xFF);

            if (marker == 0xD9 || marker == 0xDA)
            {
                break;
            }

            var length = ReadBigEndianUInt16(reader);
            if (length < 2)
            {
                return false;
            }

            if (IsJpegStartOfFrame(marker))
            {
                reader.ReadByte();
                height = ReadBigEndianUInt16(reader);
                width = ReadBigEndianUInt16(reader);
                return width > 0 && height > 0;
            }

            reader.BaseStream.Seek(length - 2, SeekOrigin.Current);
        }

        return false;
    }

    private static bool IsJpegStartOfFrame(byte marker)
    {
        return marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
    }

    private static int ReadBigEndianInt32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }

    private static ushort ReadBigEndianUInt16(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (ushort)((high << 8) | low);
    }
}
