using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StellarAdvanced
{
    static class EvasTests
    {
        public static byte[] Wrap(byte[] body)
        {
            byte[] bytes = new byte[body.Length + 12];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("EVAS"), 0, bytes, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1u), 0, bytes, 4, 4);
            Buffer.BlockCopy(body, 0, bytes, 8, body.Length);
            EvasEnvelope.UpdateChecksum(bytes); return bytes;
        }
        public static void Run(string directory, byte[] sample, byte[] missingOutfit, Action<bool, string> check)
        {
            Action<Action, string> reject = delegate(Action operation, string description)
            {
                bool rejected = false; try { operation(); } catch (InvalidDataException) { rejected = true; }
                check(rejected, description);
            };
            byte[] vector = Encoding.ASCII.GetBytes("123456789");
            check(EvasEnvelope.Crc32(vector, 0, vector.Length) == 0xCBF43926u, "CRC32 matches the standard 123456789 vector");
            check(EvasEnvelope.Crc32(new byte[0], 0, 0) == 0, "CRC32 of empty input is zero");
            // Independent oracle: Python zlib.crc32 on the unchanged public GVAS sample.
            check(EvasEnvelope.Crc32(sample, 0, sample.Length) == 0xF739AEB9u, "15 MB public sample CRC32 matches independent Python zlib result F739AEB9");
            byte[] wrappedSample = Wrap(sample);
            check(BitConverter.ToUInt32(wrappedSample, wrappedSample.Length - 4) == 0xF739AEB9u, "EVAS trailer stores the independently verified checksum little-endian");
            var roundtrip = Archive.Parse(wrappedSample);
            check(roundtrip.HasEvas && wrappedSample.SequenceEqual(roundtrip.Serialize()), "EVAS v1 read/write without edits is byte-identical including prefix and trailer");
            byte[] original = Wrap(missingOutfit); string source = Path.Combine(directory, "evas-before.sav"); File.WriteAllBytes(source, original);
            var editor = new SaveEditor(source);
            check(editor.Archive.HasEvas && editor.Stacks().Count > 0 && !editor.Owned().Contains("BS_19"), "EVAS v1 opens with editable inventory and the expected missing outfit");
            var outfit = CatalogItem.Load().Find(delegate(CatalogItem c) { return c.Alias == "BS_19"; });
            var plan = editor.Build(new CatalogItem[] { outfit }, false, false, new Dictionary<string, int> { { "BetaCrystal", 600000 }, { "HP_Potion", 123 } });
            check(plan.UpdatedQuantities == 2 && plan.AddedItems == 1 && plan.UpdatedRecords == 58, "EVAS combines money, consumables, wardrobe and collection changes");
            check(plan.Bytes.Length > original.Length && plan.Bytes.Take(8).SequenceEqual(original.Take(8)), "growing EVAS inventory preserves the complete v1 prefix");
            check(BitConverter.ToUInt32(plan.Bytes, plan.Bytes.Length - 4) != BitConverter.ToUInt32(original, original.Length - 4), "changing the body replaces the old EVAS checksum");
            EvasEnvelope.Validate(plan.Bytes);
            check(true, "modified EVAS passes CRC32 validation before saving");
            string destination = Path.Combine(directory, "evas-after.sav"); editor.SaveCopy(plan, destination); var after = new SaveEditor(plan.OutputPath);
            check(after.Archive.HasEvas && after.Stacks().Find(delegate(StackItem s) { return s.Alias == "BetaCrystal"; }).Amount == 600000, "saved EVAS reopens as EVAS with the requested money");
            check(after.Owned().Contains("BS_19") && after.Cans() == 49, "saved EVAS reopens with Black Pearl and 49 can records");
            check(File.ReadAllBytes(source).SequenceEqual(original) && File.ReadAllBytes(plan.BackupPath).SequenceEqual(original), "EVAS source and permanent residual backup remain byte-identical to the original");
            check(after.Build(new CatalogItem[] { outfit }, false, false, new Dictionary<string, int> { { "BetaCrystal", 600000 }, { "HP_Potion", 123 } }).Bytes.SequenceEqual(plan.Bytes), "repeating EVAS edits is idempotent including the checksum");
            foreach (Node field in editor.Archive.Fields)
            {
                if (field.Name == "DataMap_SBItem" || field.Name == "SBAchievement") continue;
                check(Writer.NodeBytes(field).SequenceEqual(Writer.NodeBytes(after.Archive.Field(field.Name))), "EVAS preserves section: " + field.Name);
            }
            byte[] corrupt = (byte[])original.Clone(); corrupt[1000] ^= 1;
            reject(delegate { Archive.Parse(corrupt); }, "EVAS body corruption fails integrity validation");
            corrupt = (byte[])original.Clone(); corrupt[corrupt.Length - 1] ^= 1;
            reject(delegate { Archive.Parse(corrupt); }, "EVAS checksum corruption fails integrity validation");
            byte[] unknown = (byte[])original.Clone(); unknown[4] = 2;
            reject(delegate { Archive.Parse(unknown); }, "unknown EVAS version is rejected instead of guessed");
            byte[] shortFile = Encoding.ASCII.GetBytes("EVAS");
            reject(delegate { Archive.Parse(shortFile); }, "truncated EVAS envelope is rejected");
            byte[] noCrc = original.Take(original.Length - 4).ToArray();
            reject(delegate { Archive.Parse(noCrc); }, "EVAS with a missing trailing checksum is rejected");
            byte[] badBody = (byte[])original.Clone(); badBody[8] = 0;
            reject(delegate { Archive.Parse(badBody); }, "EVAS with a missing nested GVAS header is rejected");
        }
    }
}
