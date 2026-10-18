// GVAS reader adapted from perillaroc/sblade-explorer (gvas.rs),
// Copyright 2026 perillaroc, Apache-2.0. See THIRD_PARTY_NOTICES.txt and LICENSE.txt.
// The preserving writer and mutation model are additions for this application.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StellarAdvanced
{
    sealed class Pair { public Value Key, Item; }
    sealed class Value
    {
        public string Type;
        public object Scalar, Original;
        public byte[] Raw, ScalarBytes;
        public List<Node> Fields;
        public List<Value> Elements;
        public List<Pair> Entries;
        public bool Prefix;
        public Node ElementHeader;
        public bool IsRaw { get { return Raw != null; } }
        public Node Field(string name) { return Fields == null ? null : Fields.Find(delegate(Node n) { return n.Name == name; }); }
        public void Set(object scalar) { if (IsRaw || Fields != null || Elements != null || Entries != null) throw new InvalidDataException("Not a scalar field."); Scalar = scalar; }
        public string Text { get { return Scalar as string; } }
        public long Integer { get { return Convert.ToInt64(Scalar); } }
    }
    sealed class Node
    {
        public string Name, Type, StructName, Inner, KeyType, ValueType;
        public byte[] Header;
        public int SizeOffset;
        public long Size;
        public Value Data;
        public Node Field(string name) { return Data == null ? null : Data.Field(name); }
        public Value Require(string name)
        {
            Node node = Field(name);
            if (node == null || node.Data.IsRaw) throw new InvalidDataException("Missing compatible field: " + name);
            return node.Data;
        }
        public Node Clone() { var reader = new Reader(Writer.NodeBytes(this)); return reader.Property(0); }
    }
    sealed class Archive
    {
        public byte[] Header, Footer;
        public bool HasEvas { get; private set; }
        public List<Node> Fields;
        public Node Field(string name) { return Fields.Find(delegate(Node node) { return node.Name == name; }); }
        public static Archive Parse(byte[] bytes)
        {
            if (bytes.Length > 128 * 1024 * 1024) throw new InvalidDataException("The save exceeds the 128 MB size limit.");
            bool evas = EvasEnvelope.IsEvas(bytes);
            if (evas) EvasEnvelope.Validate(bytes);
            var reader = new Reader(bytes);
            reader.Position = evas ? 8 : 0;
            if (Encoding.ASCII.GetString(reader.Bytes(4)) != "GVAS") throw new InvalidDataException("Not a compatible GVAS save.");
            uint version = reader.UInt32(); reader.UInt32();
            if (version >= 3) reader.UInt32();
            reader.Bytes(6); reader.UInt32(); reader.String(); reader.UInt32();
            uint count = reader.UInt32(); if (count > 10000) throw new InvalidDataException("Unsupported header."); reader.Bytes(checked((int)count * 20));
            if (reader.String() != "/Script/SB.SBSaveGame") throw new InvalidDataException("The file is not a main save for Stellar Blade.");
            int headerEnd = reader.Position;
            var archive = new Archive { Header = Reader.Slice(bytes, 0, headerEnd), HasEvas = evas, Fields = reader.Fields(0) };
            if (evas && reader.Position > bytes.Length - 4) throw new InvalidDataException("EVAS data overlaps the final checksum.");
            // Fields() consumes the final None tag. Preserve it with the footer verbatim.
            int noneSize = 4 + Encoding.UTF8.GetByteCount("None") + 1;
            archive.Footer = Reader.Slice(bytes, reader.Position - noneSize, bytes.Length - reader.Position + noneSize);
            return archive;
        }
        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            {
                stream.Write(Header, 0, Header.Length);
                foreach (Node field in Fields) { byte[] data = Writer.NodeBytes(field); stream.Write(data, 0, data.Length); }
                stream.Write(Footer, 0, Footer.Length);
                byte[] bytes = stream.ToArray();
                if (HasEvas) EvasEnvelope.UpdateChecksum(bytes);
                return bytes;
            }
        }
    }
    sealed class Reader
    {
        readonly byte[] data;
        public int Position;
        public Reader(byte[] bytes) { data = bytes; }
        public static byte[] Slice(byte[] bytes, int offset, int count)
        { if (offset < 0 || count < 0 || offset > bytes.Length - count) throw new InvalidDataException("Read exceeds the save bounds."); byte[] result = new byte[count]; Buffer.BlockCopy(bytes, offset, result, 0, count); return result; }
        public byte[] Bytes(int count) { byte[] result = Slice(data, Position, count); Position += count; return result; }
        public byte Byte() { return Bytes(1)[0]; }
        public int Int32() { return BitConverter.ToInt32(Bytes(4), 0); }
        public uint UInt32() { return BitConverter.ToUInt32(Bytes(4), 0); }
        public long Int64() { return BitConverter.ToInt64(Bytes(8), 0); }
        public string String()
        {
            int count = Int32(); if (count == int.MinValue || Math.Abs(count) > 1000000) throw new InvalidDataException("Unsupported string.");
            if (count == 0) return "";
            return count < 0 ? Encoding.Unicode.GetString(Bytes(checked(-count * 2))).TrimEnd('\0') : Encoding.UTF8.GetString(Bytes(count)).TrimEnd('\0');
        }
        int Count() { int count = Int32(); if (count < 0 || count > 5000000) throw new InvalidDataException("Unsupported count."); return count; }
        Node Tag()
        {
            int start = Position; string name = String(); if (name == "None" || name == "") return null;
            string type = String(); int sizeOffset = Position - start; long size = Int64();
            if (size < 0 || size > data.Length) throw new InvalidDataException("Unsupported field size.");
            var node = new Node { Name = name, Type = type, Size = size, SizeOffset = sizeOffset };
            switch (type)
            {
                case "StructProperty": node.StructName = String(); Bytes(16); break;
                case "ArrayProperty": case "SetProperty": node.Inner = String(); break;
                case "MapProperty": node.KeyType = String(); node.ValueType = String(); break;
                case "ByteProperty": String(); break;
                case "EnumProperty": String(); node.Inner = String(); break;
            }
            Byte(); node.Header = Slice(data, start, Position - start); return node;
        }
        public List<Node> Fields(int depth)
        {
            if (depth > 80) throw new InvalidDataException("The save structure is too deep.");
            var fields = new List<Node>(); Node node;
            while ((node = Property(depth + 1)) != null) fields.Add(node);
            return fields;
        }
        Value Scalar(string type)
        {
            int start = Position; object scalar;
            switch (type)
            {
                case "StrProperty": case "NameProperty": case "EnumProperty": scalar = String(); break;
                case "IntProperty": scalar = Int32(); break;
                case "Int64Property": scalar = Int64(); break;
                case "UInt32Property": scalar = UInt32(); break;
                case "UInt64Property": scalar = BitConverter.ToUInt64(Bytes(8), 0); break;
                case "FloatProperty": scalar = BitConverter.ToSingle(Bytes(4), 0); break;
                case "DoubleProperty": scalar = BitConverter.ToDouble(Bytes(8), 0); break;
                case "Int8Property": scalar = (sbyte)Byte(); break;
                case "Int16Property": scalar = BitConverter.ToInt16(Bytes(2), 0); break;
                case "UInt16Property": scalar = BitConverter.ToUInt16(Bytes(2), 0); break;
                case "BoolProperty": scalar = Byte() != 0; break;
                case "ByteProperty": scalar = Byte(); break;
                default: throw new InvalidDataException("Unrecognized scalar type: " + type);
            }
            return new Value { Type = type, Scalar = scalar, Original = scalar, ScalarBytes = Slice(data, start, Position - start) };
        }
        Value Plain(string type, string inner, string key, string item, int depth)
        {
            if (depth > 80) throw new InvalidDataException("Structure is too deep.");
            var value = new Value { Type = type };
            if (type == "StructProperty") { value.Fields = Fields(depth + 1); return value; }
            if (type == "MapProperty")
            {
                int count = Count(); value.Entries = new List<Pair>();
                for (int i = 0; i < count; i++) value.Entries.Add(new Pair { Key = Plain(key, null, null, null, depth + 1), Item = Plain(item, null, null, null, depth + 1) });
                return value;
            }
            if (type == "ArrayProperty" || type == "SetProperty")
            {
                int count = Count(); value.Elements = new List<Value>();
                if (type == "ArrayProperty" && inner == "StructProperty")
                {
                    value.ElementHeader = Tag();
                    if (value.ElementHeader == null || value.ElementHeader.Type != "StructProperty") throw new InvalidDataException("Missing element header.");
                }
                for (int i = 0; i < count; i++) value.Elements.Add(Plain(inner, null, null, null, depth + 1));
                return value;
            }
            return Scalar(type);
        }
        public Node Property(int depth)
        {
            Node node = Tag(); if (node == null) return null;
            int start = Position, end = checked(start + (int)node.Size);
            if (end > data.Length) throw new InvalidDataException("Field exceeds file bounds.");
            try
            {
                if (node.Type == "BoolProperty") { node.Data = Scalar(node.Type); end = Position; }
                else
                {
                    bool prefix = false;
                    if (node.Type == "MapProperty" || node.Type == "SetProperty") { prefix = Int32() == 0; if (!prefix) Position = start; }
                    node.Data = node.Type == "ByteProperty" && node.Size != 1 ? Scalar("NameProperty") : Plain(node.Type, node.Inner, node.KeyType, node.ValueType, depth);
                    node.Data.Prefix = prefix;
                    if (Position != end) throw new InvalidDataException("Unrecognized structure size.");
                }
            }
            catch (InvalidDataException) { node.Data = new Value { Type = node.Type, Raw = Slice(data, start, (int)node.Size) }; }
            Position = end; return node;
        }
    }
    static class Writer
    {
        static void String(BinaryWriter writer, string value)
        { byte[] data = Encoding.UTF8.GetBytes(value + "\0"); writer.Write(data.Length); writer.Write(data); }
        public static byte[] NodeBytes(Node node)
        {
            byte[] payload = ValueBytes(node.Data), header = (byte[])node.Header.Clone();
            if (node.Type != "BoolProperty") Buffer.BlockCopy(BitConverter.GetBytes((long)payload.Length), 0, header, node.SizeOffset, 8);
            using (var stream = new MemoryStream()) { stream.Write(header, 0, header.Length); stream.Write(payload, 0, payload.Length); return stream.ToArray(); }
        }
        public static byte[] ValueBytes(Value value)
        {
            if (value.Raw != null) return value.Raw;
            if (value.ScalarBytes != null && Object.Equals(value.Scalar, value.Original)) return value.ScalarBytes;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                if (value.Fields != null)
                {
                    foreach (Node field in value.Fields) writer.Write(NodeBytes(field)); String(writer, "None");
                }
                else if (value.Entries != null)
                {
                    if (value.Prefix) writer.Write(0); writer.Write(value.Entries.Count);
                    foreach (Pair entry in value.Entries) { writer.Write(ValueBytes(entry.Key)); writer.Write(ValueBytes(entry.Item)); }
                }
                else if (value.Elements != null)
                {
                    if (value.Prefix) writer.Write(0); writer.Write(value.Elements.Count);
                    using (var elementStream = new MemoryStream())
                    {
                        foreach (Value element in value.Elements) { byte[] bytes = ValueBytes(element); elementStream.Write(bytes, 0, bytes.Length); }
                        byte[] elements = elementStream.ToArray();
                        if (value.ElementHeader != null)
                        {
                            byte[] header = (byte[])value.ElementHeader.Header.Clone();
                            // Unreal's struct-array tag carries the combined serialized element size.
                            Buffer.BlockCopy(BitConverter.GetBytes((long)elements.Length), 0, header, value.ElementHeader.SizeOffset, 8); writer.Write(header);
                        }
                        writer.Write(elements);
                    }
                }
                else
                {
                    switch (value.Type)
                    {
                        case "NameProperty": case "StrProperty": case "EnumProperty": String(writer, (string)value.Scalar); break;
                        case "IntProperty": writer.Write(Convert.ToInt32(value.Scalar)); break;
                        case "Int64Property": writer.Write(Convert.ToInt64(value.Scalar)); break;
                        case "UInt32Property": writer.Write(Convert.ToUInt32(value.Scalar)); break;
                        case "UInt64Property": writer.Write(Convert.ToUInt64(value.Scalar)); break;
                        case "FloatProperty": writer.Write(Convert.ToSingle(value.Scalar)); break;
                        case "DoubleProperty": writer.Write(Convert.ToDouble(value.Scalar)); break;
                        case "Int8Property": writer.Write(Convert.ToSByte(value.Scalar)); break;
                        case "Int16Property": writer.Write(Convert.ToInt16(value.Scalar)); break;
                        case "UInt16Property": writer.Write(Convert.ToUInt16(value.Scalar)); break;
                        case "BoolProperty": writer.Write(Convert.ToBoolean(value.Scalar)); break;
                        case "ByteProperty": writer.Write(Convert.ToByte(value.Scalar)); break;
                        default: throw new InvalidDataException("Cannot serialize: " + value.Type);
                    }
                }
                return stream.ToArray();
            }
        }
        public static Value CloneValue(Value value)
        {
            // Clone tagged struct fields, preserving all raw/unknown fields.
            if (value.Fields == null) throw new InvalidDataException("A compatible structure is required.");
            var clone = new Value { Type = value.Type, Fields = new List<Node>() };
            foreach (Node field in value.Fields) clone.Fields.Add(field.Clone()); return clone;
        }
        public static Value Text(string text) { return new Value { Type = "NameProperty", Scalar = text }; }
    }
}
