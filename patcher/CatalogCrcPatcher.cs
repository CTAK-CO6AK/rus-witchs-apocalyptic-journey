using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WitchRusPatcher
{
    public class AbroRecord
    {
        public int Pos { get; set; }
        public uint Crc { get; set; }
        public uint Size { get; set; }
        public byte[] Hash16 { get; set; } = new byte[16];
    }

    public static class CatalogCrcPatcher
    {
        private static readonly Regex NameHashRegex = new Regex(@"_([0-9a-f]{32})\.bundle$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<AbroRecord> FindAbroRecords(byte[] data)
        {
            var records = new List<AbroRecord>();
            var nameOffsets = new List<int>();

            // Find "\x20\x00\x00\x00[0-9a-f]{32}"
            for (int i = 0; i <= data.Length - 36; i++)
            {
                if (data[i] == 0x20 && data[i + 1] == 0x00 && data[i + 2] == 0x00 && data[i + 3] == 0x00)
                {
                    bool isHex = true;
                    for (int j = 0; j < 32; j++)
                    {
                        byte b = data[i + 4 + j];
                        bool validHex = (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');
                        if (!validHex)
                        {
                            isHex = false;
                            break;
                        }
                    }
                    if (isHex)
                    {
                        nameOffsets.Add(i + 4);
                    }
                }
            }

            foreach (var noff in nameOffsets)
            {
                byte[] needle = BitConverter.GetBytes((uint)noff);
                int q = 0;
                while (true)
                {
                    q = IndexOf(data, needle, q);
                    if (q < 0) break;

                    int p = q - 4;
                    if (p >= 0 && p + 20 <= data.Length)
                    {
                        uint hoff = BitConverter.ToUInt32(data, p);
                        uint bnoff = BitConverter.ToUInt32(data, p + 4);
                        uint crc = BitConverter.ToUInt32(data, p + 8);
                        uint size = BitConverter.ToUInt32(data, p + 12);
                        uint cioff = BitConverter.ToUInt32(data, p + 16);

                        if (hoff > 0 && hoff + 16 <= data.Length && size > 0 && cioff <= data.Length)
                        {
                            var hash16 = new byte[16];
                            Array.Copy(data, hoff, hash16, 0, 16);
                            records.Add(new AbroRecord
                            {
                                Pos = p,
                                Crc = crc,
                                Size = size,
                                Hash16 = hash16
                            });
                        }
                    }
                    q++;
                }
            }

            return records;
        }

        private static int IndexOf(byte[] data, byte[] needle, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - needle.Length; i++)
            {
                if (data[i] == needle[0] &&
                    data[i + 1] == needle[1] &&
                    data[i + 2] == needle[2] &&
                    data[i + 3] == needle[3])
                {
                    return i;
                }
            }
            return -1;
        }

        public static (int found, int patched) ZeroCrc(byte[] data, string bundleName, long origSize, List<AbroRecord> records)
        {
            var match = NameHashRegex.Match(bundleName);
            byte[]? wantHash = null;
            if (match.Success)
            {
                wantHash = ConvertHexStringToByteArray(match.Groups[1].Value);
            }

            int found = 0;
            int patched = 0;

            foreach (var rec in records)
            {
                if (rec.Size != (uint)origSize)
                    continue;

                if (wantHash != null && !rec.Hash16.SequenceEqual(wantHash))
                    continue;

                found++;
                if (rec.Crc != 0)
                {
                    int crcOffset = rec.Pos + 8;
                    uint oldCrc = rec.Crc;
                    Array.Clear(data, crcOffset, 4);
                    rec.Crc = 0;
                    patched++;
                }
            }

            return (found, patched);
        }

        private static byte[] ConvertHexStringToByteArray(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
    }
}
