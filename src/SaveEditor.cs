using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace StellarAdvanced
{
    sealed class CatalogItem
    {
        public string Alias, Name, Category, Dlc;
        public override string ToString() { return Name + (String.IsNullOrEmpty(Dlc) ? "" : " [" + Dlc + "]"); }
        public static List<CatalogItem> Load()
        {
            var items = new List<CatalogItem>();
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("wardrobe.tsv"), Encoding.UTF8))
            {
                string line; while ((line = reader.ReadLine()) != null)
                {
                    string[] fields = line.Split('\t'); if (fields.Length != 4) continue;
                    items.Add(new CatalogItem { Alias = fields[0], Name = fields[1], Category = fields[2], Dlc = fields[3] });
                }
            }
            return items;
        }
    }
    sealed class EditPlan
    {
        public byte[] Bytes;
        public string SourceHash;
        public readonly List<string> Changes = new List<string>(), Limits = new List<string>();
        public int AddedItems, UpdatedRecords, UpdatedQuantities;
        public string BackupPath;
        public string OutputPath;
        public bool SourceMissing;
        public string Report()
        {
            var text = new StringBuilder();
            text.AppendLine("CHANGE REVIEW · CHECK YOUR MODIFIED COPY IN GAME");
            text.AppendLine("Items added: " + AddedItems + "; quantities changed: " + UpdatedQuantities + "; progress records: " + UpdatedRecords);
            foreach (string change in Changes) text.AppendLine("+ " + change);
            text.AppendLine(); text.AppendLine("LIMITATIONS OF THIS OPERATION");
            foreach (string limit in Limits) text.AppendLine("· " + limit);
            text.AppendLine("This review has not modified the original file."); return text.ToString();
        }
    }
    sealed class SaveEditor
    {
        public readonly Archive Archive;
        public readonly byte[] Original;
        public readonly string SourcePath;
        public readonly List<Value> Inventory, Records;
        public readonly Value Obtained;
        public SaveEditor(string path)
        {
            SourcePath = SaveInput.Validate(path);
            Original = File.ReadAllBytes(SourcePath);
            Archive = Archive.Parse(Original);
            if (!Original.SequenceEqual(Archive.Serialize())) throw new InvalidDataException("This structure cannot be preserved exactly. Use a different reader for inspection only.");
            Value item = MapStruct(Archive, "DataMap_SBItem", "Item");
            Inventory = RequireField(item, "Inventory").Elements;
            Obtained = RequireField(item, "ItemOtaineSet");
            Node achievement = Archive.Field("SBAchievement");
            if (achievement == null) throw new InvalidDataException("Collection records are missing.");
            Records = achievement.Require("AchievementList").Elements;
            if (Inventory == null || Obtained.Elements == null || Records == null) throw new InvalidDataException("Unsupported inventory or progress schema.");
        }
        static Value RequireField(Value value, string name)
        {
            Node field = value.Field(name); if (field == null || field.Data.IsRaw) throw new InvalidDataException("Missing compatible field: " + name); return field.Data;
        }
        static Value MapStruct(Archive archive, string field, string key)
        {
            Node map = archive.Field(field); if (map == null || map.Data.Entries == null) throw new InvalidDataException("Missing compatible map: " + field);
            Pair match = map.Data.Entries.Find(delegate(Pair pair) { return pair.Key.Text == key; });
            if (match == null || match.Item.Fields == null) throw new InvalidDataException("Missing section: " + key); return match.Item;
        }
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        public HashSet<string> Owned()
        {
            var owned = new HashSet<string>(Obtained.Elements.Where(delegate(Value v) { return v.Text != null; }).Select(delegate(Value v) { return v.Text; }), StringComparer.Ordinal);
            foreach (Value entry in Inventory)
            {
                Node instance = entry.Field("ItemInstance");
                if (instance != null && instance.Field("ItemAlias") != null) owned.Add(instance.Require("ItemAlias").Text);
                Node direct = entry.Field("ItemAlias"); if (direct != null && direct.Data.Text != null && direct.Data.Text != "None") owned.Add(direct.Data.Text);
            }
            return owned;
        }
        public int Cans()
        {
            return Records.Count(delegate(Value record) { Node alias = record.Field("AchievementAlias"), progress = record.Field("ProgressValue");
                return alias != null && progress != null && alias.Data.Text.StartsWith("Acquire_Item_Can_", StringComparison.Ordinal) && progress.Data.Integer > 0; });
        }
        public bool HasNierData { get { return Owned().Any(delegate(string alias) { return alias != null && alias.IndexOf("Nier", StringComparison.OrdinalIgnoreCase) >= 0; }); } }
        static Value QuantityTarget(Value entry, string alias)
        {
            Node nested = entry.Field("ItemInstance");
            if (nested != null && nested.Field("ItemAlias") != null && nested.Require("ItemAlias").Text == alias) return nested.Data;
            Node direct = entry.Field("ItemAlias"); return direct != null && direct.Data.Text == alias ? entry : null;
        }
        static bool CountType(Value value) { return !value.IsRaw && (value.Type == "IntProperty" || value.Type == "UInt32Property"); }
        public List<StackItem> Stacks()
        {
            var output = new List<StackItem>();
            foreach (StackItem definition in InventoryCatalog.Items.Values)
            {
                List<Value> targets = Inventory.Select(delegate(Value entry) { return QuantityTarget(entry, definition.Alias); }).Where(delegate(Value value) { return value != null; }).ToList();
                if (targets.Count != 1 || targets[0].Field("ItemCount") == null) continue;
                Value count = targets[0].Field("ItemCount").Data;
                if (!CountType(count) || count.Integer < 0 || count.Integer > 100000000) continue;
                StackItem item = definition.DefinitionCopy(); item.Amount = count.Integer;
                Node charge = targets[0].Field("ItemChargeCount"); item.Charges = charge == null || charge.Data.IsRaw ? count.Integer : charge.Data.Integer;
                item.Maximum = Math.Max(item.Maximum, checked((int)item.Amount)); output.Add(item);
            }
            return output.OrderBy(delegate(StackItem item) { return item.Alias == "BetaCrystal" ? "0" : item.Category + item.Name; }).ToList();
        }
        static void ApplyQuantities(Value items, IDictionary<string, int> requested, EditPlan plan)
        {
            if (requested == null) return;
            List<Value> inventory = RequireField(items, "Inventory").Elements;
            foreach (KeyValuePair<string, int> request in requested)
            {
                StackItem definition;
                if (!InventoryCatalog.Items.TryGetValue(request.Key, out definition)) throw new InvalidDataException("Quantity editing is not allowed for this item: " + request.Key);
                List<Value> targets = inventory.Select(delegate(Value entry) { return QuantityTarget(entry, request.Key); }).Where(delegate(Value value) { return value != null; }).ToList();
                if (targets.Count != 1) throw new InvalidDataException("The item must exist exactly once in the inventory: " + definition.Name);
                Value count = RequireField(targets[0], "ItemCount");
                if (!CountType(count)) throw new InvalidDataException("Unsupported quantity type: " + definition.Name);
                long old = count.Integer;
                if (old < 0 || old > 100000000) throw new InvalidDataException("Invalid original quantity: " + definition.Name);
                int limit = Math.Max(definition.Maximum, checked((int)old));
                if (request.Value < 0 || request.Value > limit) throw new InvalidDataException("Quantity out of range for " + definition.Name + ": 0–" + limit);
                Node chargeNode = targets[0].Field("ItemChargeCount");
                if (chargeNode != null && !CountType(chargeNode.Data)) throw new InvalidDataException("Unsupported charge type.");
                long oldCharge = chargeNode == null ? old : chargeNode.Data.Integer;
                if (oldCharge < 0 || oldCharge > 100000000) throw new InvalidDataException("Invalid original charges: " + definition.Name);
                int newCharge = definition.IsCurrency || oldCharge == old ? request.Value : checked((int)Math.Min(oldCharge, request.Value));
                if (old == request.Value && oldCharge == newCharge) continue;
                count.Set(request.Value); if (chargeNode != null) chargeNode.Data.Set(newCharge);
                plan.UpdatedQuantities++; plan.Changes.Add(definition.Name + ": " + old + " → " + request.Value + (oldCharge != newCharge ? "; charges " + oldCharge + " → " + newCharge : ""));
            }
        }
        public bool DlcPresent(string dlc)
        {
            if (String.IsNullOrEmpty(dlc)) return true;
            if (dlc == "nier") return HasNierData;
            if (dlc == "nikke") return Owned().Any(delegate(string alias) { return alias != null && alias.IndexOf("Nikke", StringComparison.OrdinalIgnoreCase) >= 0; });
            // Seasonal and entitlement-specific unlocks need a separate verified rule.
            return false;
        }
        static Value RecordTemplate(List<Value> records)
        {
            Value template = records.Find(delegate(Value value) { return value.Field("AchievementAlias") != null && value.Field("ProgressValue") != null && value.Field("bCompleted") != null && value.Field("bRecievedReward") != null; });
            if (template == null) throw new InvalidDataException("No compatible progress template was found."); return template;
        }
        static void Progress(List<Value> records, string alias, int amount, EditPlan plan)
        {
            Value record = records.Find(delegate(Value v) { Node field = v.Field("AchievementAlias"); return field != null && field.Data.Text == alias; });
            if (record != null)
            {
                Value progress = RequireField(record, "ProgressValue"); if (progress.Integer >= amount) return;
                progress.Set(amount);
            }
            else
            {
                record = Writer.CloneValue(RecordTemplate(records));
                RequireField(record, "AchievementAlias").Set(alias); RequireField(record, "ProgressValue").Set(amount);
                // Public completion sample uses progress values, not completed/reward booleans.
                RequireField(record, "bCompleted").Set(false); RequireField(record, "bRecievedReward").Set(false); records.Add(record);
            }
            plan.UpdatedRecords++;
        }
        static Value InventoryTemplate(List<Value> inventory)
        {
            Value template = inventory.Find(delegate(Value entry)
            {
                Node instance = entry.Field("ItemInstance"); if (instance == null || instance.Data.Fields == null || instance.Field("ItemAlias") == null) return false;
                string alias = instance.Require("ItemAlias").Text;
                return alias != null && alias.StartsWith("BS_", StringComparison.Ordinal);
            });
            if (template == null) throw new InvalidDataException("The save needs at least one actual outfit in its inventory to create a compatible entry."); return template;
        }
        static void AddItem(Value items, List<Value> records, CatalogItem item, EditPlan plan)
        {
            List<Value> inventory = RequireField(items, "Inventory").Elements;
            Value obtained = RequireField(items, "ItemOtaineSet");
            bool actual = inventory.Any(delegate(Value entry) { Node instance = entry.Field("ItemInstance");
                return instance != null && instance.Field("ItemAlias") != null && instance.Require("ItemAlias").Text == item.Alias; });
            if (!actual)
            {
                Value entry = Writer.CloneValue(InventoryTemplate(inventory)); Value instance = RequireField(entry, "ItemInstance");
                RequireField(instance, "ItemAlias").Set(item.Alias);
                RequireField(instance, "ItemCount").Set(1); RequireField(instance, "ItemChargeCount").Set(1);
                RequireField(instance, "Equiped").Set(false);
                Node field = instance.Field("SavedGuid");
                if (field == null || field.Data.Raw == null || field.Data.Raw.Length != 16) throw new InvalidDataException("Unsupported inventory identifier.");
                field.Data.Raw = Guid.NewGuid().ToByteArray();
                field = instance.Field("PocketGuid"); if (field != null && field.Data.Raw != null && field.Data.Raw.Length == 16) field.Data.Raw = new byte[16];
                field = instance.Field("ItemVisualAlias"); if (field != null) field.Data.Set("None");
                field = instance.Field("bFavorites"); if (field != null) field.Data.Set(false);
                field = instance.Field("MatVarIndex"); if (field != null) field.Data.Set(0);
                inventory.Add(entry); plan.AddedItems++; plan.Changes.Add("Inventory: " + item.Name + " (" + item.Alias + ")");
            }
            if (!obtained.Elements.Any(delegate(Value v) { return v.Text == item.Alias; })) { obtained.Elements.Add(Writer.Text(item.Alias)); plan.Changes.Add("Ownership record: " + item.Name); }
            Progress(records, "Acquire_Item_" + item.Alias, 1, plan);
        }
        static readonly string[] NierWorldAliases = {
            "WLA_10_EnvS_215", "WLA_30_EnvS_121", "WLA_50_EnvS_055", "WLA_10_EnvS_213", "WLA_40_EnvS_185", "WLA_40_EnvS_186",
            "WLB_20_EnvS_195", "WLB_50_EnvS_242", "WLB_30_EnvS_081", "WLB_50_EnvS_243", "WLB_10_EnvS_163", "WLB_10_EnvS_162",
            "WLB_10_EnvS_161", "WLA_10_EnvS_214", "WLA_10_EnvS_216"
        };
        public EditPlan Build(IEnumerable<CatalogItem> requested, bool allCans, bool nierCollection)
        { return Build(requested, allCans, nierCollection, null); }
        public EditPlan Build(IEnumerable<CatalogItem> requested, bool allCans, bool nierCollection, IDictionary<string, int> quantities)
        {
            var selected = requested.GroupBy(delegate(CatalogItem i) { return i.Alias; }).Select(delegate(IGrouping<string, CatalogItem> g) { return g.First(); }).ToList();
            // Whitelist selection from the embedded catalog, never arbitrary aliases or save-derived commands.
            var allowed = CatalogItem.Load().ToDictionary(delegate(CatalogItem item) { return item.Alias; }, StringComparer.Ordinal);
            foreach (CatalogItem item in selected)
            {
                CatalogItem definition;
                if (!allowed.TryGetValue(item.Alias, out definition)) throw new InvalidDataException("Item not allowed: " + item.Alias);
                if (!DlcPresent(definition.Dlc)) throw new InvalidDataException("No verifiable existing data for this content was found in the save: " + item.Name);
                if (definition.Alias == "BS_19") allCans = true;
                if (definition.Dlc == "nier") nierCollection = true;
            }
            if (nierCollection && !HasNierData) throw new InvalidDataException("No existing NieR data was found in the save. This version does not activate DLC or assume it is available.");
            var edited = Archive.Parse(Original); Value items = MapStruct(edited, "DataMap_SBItem", "Item");
            List<Value> records = edited.Field("SBAchievement").Require("AchievementList").Elements;
            var plan = new EditPlan { SourceHash = Hash(Original) };
            ApplyQuantities(items, quantities, plan);
            foreach (CatalogItem item in selected) AddItem(items, records, item, plan);
            if (allCans)
            {
                for (int i = 1; i <= 49; i++) Progress(records, "Acquire_Item_Can_" + i.ToString("000"), 1, plan);
                foreach (int threshold in new int[] { 1, 7, 14, 21, 28, 35, 42, 49 }) Progress(records, "CollectCan_" + threshold, threshold, plan);
                AddItem(items, records, allowed["BS_19"], plan);
                plan.Changes.Add("Cans: 49 pickup records and 8 collection milestones, together with Black Pearl.");
                plan.Limits.Add("Can records alone do not identify their physical containers.");
            }
            if (nierCollection)
            {
                for (int i = 1; i <= 15; i++) Progress(records, "Ach_Album_Unlock_Nier_" + i.ToString("00") + "_Used", 1, plan);
                foreach (string alias in NierWorldAliases) Progress(records, "ChangeState_" + alias + "_Nier", 1, plan);
                Progress(records, "Ach_Album_Unlock_Nier_WLA", 1, plan); Progress(records, "Ach_Album_Unlock_Nier_WLB", 1, plan);
                plan.Changes.Add("NieR: 15 collection entries, 15 pickup markers and 2 regional entries.");
                plan.Limits.Add("NieR markers do not represent an Emil purchase. Balances only change when selected in the item editor.");
            }
            plan.Limits.Add("Exact outfit-to-chest or quest links remain unverified. World states and story completion are not forced.");
            plan.Limits.Add("These records were researched from a public save. Verify their behavior in game, especially after saving and reloading.");
            if (Archive.HasEvas) plan.Changes.Add("EVAS v1 format is preserved and its integrity checksum is recalculated.");
            plan.Bytes = edited.Serialize();
            Archive verified = Archive.Parse(plan.Bytes);
            if (!plan.Bytes.SequenceEqual(verified.Serialize())) throw new InvalidDataException("The modified save does not preserve its structure when read again.");
            foreach (Node node in Archive.Fields)
            {
                if (node.Name == "DataMap_SBItem" || node.Name == "SBAchievement") continue;
                Node after = verified.Field(node.Name);
                if (after == null || !Writer.NodeBytes(node).SequenceEqual(Writer.NodeBytes(after))) throw new InvalidDataException("A change was detected outside inventory or collections: " + node.Name);
            }
            return plan;
        }
        public void SaveCopy(EditPlan plan, string destination)
        {
            Process[] games = Process.GetProcessesByName("SB-Win64-Shipping");
            bool running = games.Length > 0; foreach (Process game in games) game.Dispose();
            if (running) throw new InvalidOperationException("Close Stellar Blade before saving a modified copy.");
            if (plan == null || plan.Bytes == null || Hash(Original) != plan.SourceHash) throw new InvalidOperationException("There is no valid review. Reopen the save and prepare your changes again.");
            plan.SourceMissing = !File.Exists(SourcePath);
            if (!plan.SourceMissing && Hash(File.ReadAllBytes(SourcePath)) != plan.SourceHash) throw new InvalidOperationException("The source save has changed. Reopen it and review your changes again.");
            string requestedTarget = Path.GetFullPath(destination);
            if (!String.Equals(Path.GetExtension(requestedTarget), ".sav", StringComparison.OrdinalIgnoreCase)) requestedTarget += ".sav";
            string parent = Path.GetDirectoryName(requestedTarget); Directory.CreateDirectory(parent);
            string target = UniqueTarget(requestedTarget, SourcePath);
            // A permanent verified original is kept even when saving a modified COPY.
            // Backups are never thinned or removed automatically.
            string backupDirectory = Path.Combine(Path.GetDirectoryName(target), "CopiasSeguridad"); Directory.CreateDirectory(backupDirectory);
            string backupName = Path.GetFileNameWithoutExtension(SourcePath) + "-original-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sav";
            string backupPath = Path.Combine(backupDirectory, backupName);
            using (var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { backup.Write(Original, 0, Original.Length); backup.Flush(true); }
            if (Hash(File.ReadAllBytes(backupPath)) != plan.SourceHash) throw new IOException("The backup could not be verified. The modified file will not be saved.");
            plan.BackupPath = backupPath;
            File.WriteAllText(backupPath + ".txt", "PERMANENT ORIGINAL SAVE BACKUP\r\nUTC date: " + DateTime.UtcNow.ToString("u") + "\r\nSource: " + Path.GetFileName(SourcePath) + "\r\nSHA256: " + plan.SourceHash + "\r\n\r\nTo restore: close the game, keep a separate copy of your current progress, and copy this .sav to an appropriate slot in your own save folder. This app never deletes this backup automatically.\r\n\r\n" + plan.Report(), new UTF8Encoding(true));
            Archive.Parse(plan.Bytes); string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(plan.Bytes, 0, plan.Bytes.Length); stream.Flush(true); }
                byte[] written = File.ReadAllBytes(temporary); if (!written.SequenceEqual(plan.Bytes)) throw new IOException("Written file verification failed.");
                File.Move(temporary, target);
                plan.OutputPath = target;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static string UniqueTarget(string requested, string source)
        {
            string directory = Path.GetDirectoryName(requested), stem = Path.GetFileNameWithoutExtension(requested), extension = Path.GetExtension(requested);
            string candidate = requested; int suffix = 1;
            while (File.Exists(candidate) || String.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
            {
                if (suffix > 10000) throw new IOException("Too many copies have this name. Choose another folder.");
                candidate = Path.Combine(directory, stem + "-copia-" + suffix.ToString("00") + extension); suffix++;
            }
            return candidate;
        }
    }
}
