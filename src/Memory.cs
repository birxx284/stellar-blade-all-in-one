using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace StellarAssist
{
    enum NumberKind { Integer, Float }
    enum MatchKind { Exact, Decreased, Increased, Changed, Unchanged }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryInfo
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State, Protect, Type;
    }

    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] data, UIntPtr length, out UIntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr handle, IntPtr address, byte[] data, UIntPtr length, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern UIntPtr VirtualQueryEx(IntPtr handle, IntPtr address, out MemoryInfo info, UIntPtr length);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);
    }

    sealed class MemoryRange
    {
        public long Address, Length, Allocation;
        public MemoryRange(long address, long length, long allocation) { Address = address; Length = length; Allocation = allocation; }
    }

    sealed class GameMemory : IDisposable
    {
        public readonly Process Process;
        IntPtr handle;
        public GameMemory(Process process, bool testOnly)
        {
            if (!testOnly && !String.Equals(process.ProcessName, "SB-Win64-Shipping", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected process is not Stellar Blade.");
            Process = process;
            handle = Native.OpenProcess(0x0400 | 0x0010 | 0x0020 | 0x0008, false, process.Id);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not connect. Run the game and this app with the same permissions.");
        }
        public bool Alive { get { try { return handle != IntPtr.Zero && !Process.HasExited; } catch { return false; } } }
        public bool Read(long address, byte[] data, int size)
        {
            UIntPtr done;
            return Alive && Native.ReadProcessMemory(handle, new IntPtr(address), data, (UIntPtr)(uint)size, out done) && done.ToUInt64() == (ulong)size;
        }
        public MemoryInfo Query(long address)
        {
            MemoryInfo info;
            if (!Alive || Native.VirtualQueryEx(handle, new IntPtr(address), out info, (UIntPtr)(uint)Marshal.SizeOf(typeof(MemoryInfo))) == UIntPtr.Zero)
                throw new InvalidOperationException("The memory region is no longer available. Recalibrate.");
            return info;
        }
        static bool Writable(MemoryInfo info)
        {
            return info.State == 0x1000 && info.Type == 0x20000 && (info.Protect & 0x100) == 0 &&
                ((info.Protect & 0xff) == 0x04 || (info.Protect & 0xff) == 0x08);
        }
        public IEnumerable<MemoryRange> Ranges()
        {
            long address = 0;
            while (Alive && address < 0x7fffffffffffL)
            {
                MemoryInfo info;
                if (Native.VirtualQueryEx(handle, new IntPtr(address), out info, (UIntPtr)(uint)Marshal.SizeOf(typeof(MemoryInfo))) == UIntPtr.Zero) yield break;
                long start = info.BaseAddress.ToInt64();
                long length = (long)info.RegionSize.ToUInt64();
                if (length <= 0 || start > long.MaxValue - length) yield break;
                if (Writable(info)) yield return new MemoryRange(start, length, info.AllocationBase.ToInt64());
                long next = start + length;
                if (next <= address) yield break;
                address = next;
            }
        }
        public bool TryValue(long address, NumberKind kind, out double value)
        {
            byte[] buffer = new byte[4]; value = 0;
            if (!Read(address, buffer, 4)) return false;
            value = Numbers.Decode(buffer, 0, kind);
            return Numbers.Valid(value);
        }
        public void Write(long address, long allocation, NumberKind kind, double value)
        {
            if (!Numbers.Valid(value) || value < 0 || value > 1000000 || (kind == NumberKind.Integer && value != Math.Truncate(value)))
                throw new InvalidOperationException("Enter a number from 0 to 1,000,000; counters require whole numbers.");
            MemoryInfo info = Query(address);
            long end = info.BaseAddress.ToInt64() + (long)info.RegionSize.ToUInt64();
            if (!Writable(info) || info.AllocationBase.ToInt64() != allocation || address > end - 4)
                throw new InvalidOperationException("The location has changed. Recalibrate before writing.");
            double current;
            if (!TryValue(address, kind, out current) || current < 0 || current > 1000000)
                throw new InvalidOperationException("The value is no longer valid. Recalibrate.");
            byte[] buffer = kind == NumberKind.Integer ? BitConverter.GetBytes((int)value) : BitConverter.GetBytes((float)value);
            UIntPtr done;
            if (!Native.WriteProcessMemory(handle, new IntPtr(address), buffer, (UIntPtr)4u, out done) || done.ToUInt64() != 4)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not modify the value.");
        }
        public void Dispose() { if (handle != IntPtr.Zero) { Native.CloseHandle(handle); handle = IntPtr.Zero; } Process.Dispose(); }
    }

    static class Numbers
    {
        public static double Decode(byte[] data, int offset, NumberKind kind)
        { return kind == NumberKind.Integer ? (double)BitConverter.ToInt32(data, offset) : (double)BitConverter.ToSingle(data, offset); }
        public static bool Valid(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public static bool Match(double current, double previous, MatchKind match, double exact, NumberKind kind)
        {
            if (!Valid(current)) return false;
            double tolerance = kind == NumberKind.Float ? Math.Max(0.0001, Math.Abs(match == MatchKind.Exact ? exact : previous) * 0.000001) : 0;
            switch (match)
            {
                case MatchKind.Exact: return Math.Abs(current - exact) <= tolerance;
                case MatchKind.Decreased: return current < previous - tolerance;
                case MatchKind.Increased: return current > previous + tolerance;
                case MatchKind.Changed: return Math.Abs(current - previous) > tolerance;
                default: return Math.Abs(current - previous) <= tolerance;
            }
        }
    }

    sealed class Candidate
    {
        public long Address, Allocation;
        public double Value;
    }

    // Snapshots stay on disk, so an unknown-value search does not fill RAM.
    // Each block contains a snapshot plus a bit mask of surviving four-byte values.
    sealed class ScanSession : IDisposable
    {
        const int BlockSize = 1024 * 1024;
        const long MaxSnapshotBytes = 8L * 1024 * 1024 * 1024;
        readonly GameMemory memory;
        readonly string directory;
        string snapshot;
        public readonly NumberKind Kind;
        public long Count { get; private set; }
        public long BytesRead { get; private set; }
        public long BytesSkipped { get; private set; }
        public readonly List<Candidate> Preview = new List<Candidate>();
        public ScanSession(GameMemory memory, NumberKind kind)
        {
            this.memory = memory; Kind = kind;
            directory = Path.Combine(Path.GetTempPath(), "StellarAssist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        static void BitSet(byte[] mask, int index) { mask[index >> 3] |= (byte)(1 << (index & 7)); }
        static bool BitGet(byte[] mask, int index) { return (mask[index >> 3] & (1 << (index & 7))) != 0; }
        void Add(long address, long allocation, double value)
        {
            Count++;
            if (Preview.Count < 200) Preview.Add(new Candidate { Address = address, Allocation = allocation, Value = value });
        }
        void SaveBlock(BinaryWriter writer, long address, long allocation, byte[] buffer, int size, byte[] mask)
        {
            writer.Write(address); writer.Write(allocation); writer.Write(size);
            writer.Write(buffer, 0, size); writer.Write(mask);
        }
        public void First(bool unknown, double exact, CancellationToken token, Action<string> progress, IEnumerable<MemoryRange> overrideRanges)
        {
            string next = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Count = 0; BytesRead = 0; BytesSkipped = 0; Preview.Clear();
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.Create(next)))
                {
                    byte[] data = new byte[BlockSize];
                    foreach (MemoryRange range in overrideRanges ?? memory.Ranges())
                    {
                        for (long offset = 0; offset < range.Length; offset += BlockSize)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!memory.Alive) throw new InvalidOperationException("The game has closed.");
                            int size = (int)Math.Min(BlockSize, range.Length - offset); size -= size % 4;
                            if (size == 0) continue;
                            if (writer.BaseStream.Position + size + (size / 4 + 7) / 8 + 20 > MaxSnapshotBytes)
                                throw new InvalidOperationException("The scan needs more than 8 GB of temporary storage. Use an exact number or a game state with less loaded memory.");
                            if (!memory.Read(range.Address + offset, data, size)) { BytesSkipped += size; continue; }
                            BytesRead += size;
                            byte[] mask = new byte[(size / 4 + 7) / 8]; bool any = false;
                            for (int i = 0; i < size; i += 4)
                            {
                                double value = Numbers.Decode(data, i, Kind);
                                bool match = unknown ? Numbers.Valid(value) && value >= 0 && value <= 1000000 && (value == 0 || Math.Abs(value) >= 0.0001)
                                    : Numbers.Match(value, 0, MatchKind.Exact, exact, Kind);
                                if (match) { BitSet(mask, i / 4); Add(range.Address + offset + i, range.Allocation, value); any = true; }
                            }
                            if (any) SaveBlock(writer, range.Address + offset, range.Allocation, data, size, mask);
                            progress(String.Format("Read {0:N0} MB · {1:N0} candidates", BytesRead / 1048576, Count));
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                if (snapshot != null) File.Delete(snapshot);
                snapshot = next;
            }
            catch { File.Delete(next); Count = 0; Preview.Clear(); throw; }
        }
        public void Filter(MatchKind match, double exact, CancellationToken token, Action<string> progress)
        {
            if (snapshot == null) throw new InvalidOperationException("Start a scan first.");
            string next = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            long oldCount = Count; var oldPreview = new List<Candidate>(Preview);
            Count = 0; Preview.Clear(); long blocks = 0;
            try
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(snapshot)))
                using (BinaryWriter writer = new BinaryWriter(File.Create(next)))
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!memory.Alive) throw new InvalidOperationException("The game has closed.");
                        long address = reader.ReadInt64(), allocation = reader.ReadInt64(); int size = reader.ReadInt32();
                        byte[] before = reader.ReadBytes(size), oldMask = reader.ReadBytes((size / 4 + 7) / 8);
                        byte[] current = new byte[size], mask = new byte[oldMask.Length]; bool any = false;
                        MemoryInfo info;
                        try { info = memory.Query(address); } catch { continue; }
                        if (info.AllocationBase.ToInt64() != allocation || !memory.Read(address, current, size)) continue;
                        for (int i = 0; i < size; i += 4)
                        {
                            if (!BitGet(oldMask, i / 4)) continue;
                            double value = Numbers.Decode(current, i, Kind);
                            if (value < 0 || value > 1000000) continue;
                            if (Numbers.Match(value, Numbers.Decode(before, i, Kind), match, exact, Kind))
                            { BitSet(mask, i / 4); Add(address + i, allocation, value); any = true; }
                        }
                        if (any) SaveBlock(writer, address, allocation, current, size, mask);
                        if (++blocks % 16 == 0) progress(String.Format("Filtering · {0:N0} candidates", Count));
                    }
                }
                token.ThrowIfCancellationRequested();
                File.Delete(snapshot); snapshot = next;
            }
            catch { File.Delete(next); Count = oldCount; Preview.Clear(); Preview.AddRange(oldPreview); throw; }
        }
        public void Dispose()
        {
            // Only remove the private, randomly named directory created by this instance.
            try { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); } catch { }
        }
    }
}
