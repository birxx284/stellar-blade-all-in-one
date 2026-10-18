using System;
using System.IO;
using System.Text;

namespace StellarAdvanced
{
    // EVAS v1: 8-byte prefix, GVAS body, trailing little-endian IEEE CRC32.
    // CRC covers [8, length-4). Format references (no source copied):
    // lotress/CNSSaveConverter savefile.py SaveFile.serialize;
    // wuxiao00j/stellar-blade-macos-save-editor SaveParser.parse.
    static class EvasEnvelope
    {
        static readonly uint[] table = MakeTable();
        static uint[] MakeTable()
        {
            var values = new uint[256];
            for (uint i = 0; i < values.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0u : 0xEDB88320u);
                values[i] = value;
            }
            return values;
        }
        public static bool IsEvas(byte[] bytes)
        { return bytes != null && bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "EVAS"; }
        public static uint Crc32(byte[] bytes, int start, int count)
        {
            if (bytes == null || start < 0 || count < 0 || start > bytes.Length - count) throw new ArgumentOutOfRangeException("count");
            uint value = UInt32.MaxValue;
            for (int i = start; i < start + count; i++) value = table[(value ^ bytes[i]) & 255] ^ (value >> 8);
            return value ^ UInt32.MaxValue;
        }
        static void ValidateHeader(byte[] bytes)
        {
            if (!IsEvas(bytes) || bytes.Length < 16) throw new InvalidDataException("The EVAS file is incomplete: its header, data or final checksum is missing.");
            uint version = BitConverter.ToUInt32(bytes, 4);
            if (version != 1) throw new InvalidDataException("EVAS version " + version + " is unsupported. This editor supports EVAS version 1 and GVAS.");
            if (Encoding.ASCII.GetString(bytes, 8, 4) != "GVAS") throw new InvalidDataException("The EVAS file does not contain GVAS data at the expected offset. It may be incomplete or use another variant.");
        }
        public static void Validate(byte[] bytes)
        {
            ValidateHeader(bytes);
            uint stored = BitConverter.ToUInt32(bytes, bytes.Length - 4), calculated = Crc32(bytes, 8, bytes.Length - 12);
            if (stored != calculated) throw new InvalidDataException("EVAS: the CRC32 integrity check does not match (stored " + stored.ToString("X8") + ", calculated " + calculated.ToString("X8") + "). Copy the original file again with the game closed. It has not been changed or automatically repaired.");
        }
        public static void UpdateChecksum(byte[] bytes)
        {
            ValidateHeader(bytes);
            Buffer.BlockCopy(BitConverter.GetBytes(Crc32(bytes, 8, bytes.Length - 12)), 0, bytes, bytes.Length - 4, 4);
        }
    }
}
