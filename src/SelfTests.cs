using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StellarAssist
{
    static class SelfTests
    {
        static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            output.AppendLine("PASS: " + description);
        }
        static StringBuilder output;
        public static int Fixture()
        {
            IntPtr block = Native.VirtualAlloc(IntPtr.Zero, (UIntPtr)4096u, 0x3000, 0x04);
            if (block == IntPtr.Zero) return 1;
            try
            {
                Marshal.WriteInt32(block, 0, 12345); Marshal.WriteInt32(block, 4, 7);
                Marshal.WriteInt32(block, 8, BitConverter.ToInt32(BitConverter.GetBytes(850f), 0));
                Marshal.WriteInt32(block, 12, BitConverter.ToInt32(BitConverter.GetBytes(55.5f), 0));
                Console.WriteLine(block.ToInt64().ToString(CultureInfo.InvariantCulture)); Console.Out.Flush();
                string command;
                while ((command = Console.ReadLine()) != null)
                {
                    string[] parts = command.Split(' ');
                    if (parts[0] == "quit") break;
                    if (parts[0] == "setint") Marshal.WriteInt32(block, int.Parse(parts[1]), int.Parse(parts[2]));
                    else if (parts[0] == "setfloat") Marshal.WriteInt32(block, int.Parse(parts[1]), BitConverter.ToInt32(BitConverter.GetBytes(float.Parse(parts[2], CultureInfo.InvariantCulture)), 0));
                    else if (parts[0] == "int") { Console.WriteLine(Marshal.ReadInt32(block, int.Parse(parts[1]))); Console.Out.Flush(); continue; }
                    else if (parts[0] == "float") { Console.WriteLine(BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(block, int.Parse(parts[1]))), 0).ToString(CultureInfo.InvariantCulture)); Console.Out.Flush(); continue; }
                    Console.WriteLine("OK"); Console.Out.Flush();
                }
            }
            finally { Native.VirtualFree(block, UIntPtr.Zero, 0x8000); }
            return 0;
        }
        static string Command(Process child, string command)
        { child.StandardInput.WriteLine(command); child.StandardInput.Flush(); return child.StandardOutput.ReadLine(); }

        public static int Run(string resultFile)
        {
            output = new StringBuilder(); output.AppendLine("Stellar Assist - native integration tests");
            Process child = null; int exitCode = 0;
            try
            {
                Check(IntPtr.Size == 8 && Marshal.SizeOf(typeof(MemoryInfo)) == 48, "64-bit process and correct native memory-info layout");
                child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--fixture")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true });
                long address = long.Parse(child.StandardOutput.ReadLine(), CultureInfo.InvariantCulture);
                using (var memory = new GameMemory(child, true))
                {
                    long allocation = memory.Query(address).AllocationBase.ToInt64();
                    var ranges = new List<MemoryRange> { new MemoryRange(address, 4096, allocation) };
                    Check(memory.Alive, "attach to an independent test process");
                    byte[] data = new byte[4]; Check(memory.Read(address, data, 4) && BitConverter.ToInt32(data, 0) == 12345, "read remote memory");
                    Check(!memory.Read(1, data, 4), "reject inaccessible remote address");
                    using (var scan = new ScanSession(memory, NumberKind.Integer))
                    {
                        scan.First(false, 12345, CancellationToken.None, delegate { }, ranges);
                        Check(scan.Count == 1 && scan.Preview[0].Address == address, "find integer money by exact value");
                        Check(Command(child, "setint 0 12340") == "OK", "game-side money change");
                        scan.Filter(MatchKind.Exact, 12340, CancellationToken.None, delegate { });
                        Check(scan.Count == 1, "refine by updated exact integer");
                        memory.Write(address, allocation, NumberKind.Integer, 99999);
                        Check(Command(child, "int 0") == "99999", "apply money and verify in remote process");
                        var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool rejected = false;
                        try { scan.Filter(MatchKind.Exact, 99999, cancelled.Token, delegate { }); } catch (OperationCanceledException) { rejected = true; }
                        Check(rejected && scan.Count == 1, "cancel filter and preserve the preceding results");
                        scan.Filter(MatchKind.Changed, 0, CancellationToken.None, delegate { }); Check(scan.Count == 1, "changed-value filter");
                    }
                    using (var scan = new ScanSession(memory, NumberKind.Integer))
                    {
                        scan.First(false, 7, CancellationToken.None, delegate { }, ranges);
                        Check(scan.Count == 1 && scan.Preview[0].Address == address + 4, "locate a specific consumable count");
                        memory.Write(address + 4, allocation, NumberKind.Integer, 12);
                        Check(Command(child, "int 4") == "12", "apply consumable count and verify remotely");
                    }
                    using (var scan = new ScanSession(memory, NumberKind.Float))
                    {
                        scan.First(true, 0, CancellationToken.None, delegate { }, ranges);
                        Check(scan.Count > 2, "unknown-value snapshot includes zero-valued gauges");
                        Command(child, "setfloat 8 625"); scan.Filter(MatchKind.Decreased, 0, CancellationToken.None, delegate { });
                        Check(scan.Count == 1 && scan.Preview[0].Address == address + 8, "find unknown health by decrease");
                        scan.Filter(MatchKind.Unchanged, 0, CancellationToken.None, delegate { }); Check(scan.Count == 1, "unchanged-value filter");
                        Command(child, "setfloat 8 850"); scan.Filter(MatchKind.Increased, 0, CancellationToken.None, delegate { });
                        Check(scan.Count == 1, "increased-value filter");
                        memory.Write(address + 8, allocation, NumberKind.Float, 900.25);
                        Check(Command(child, "float 8") == "900.25", "apply floating-point health and verify remotely");
                    }
                    using (var scan = new ScanSession(memory, NumberKind.Float))
                    {
                        scan.First(false, 55.5, CancellationToken.None, delegate { }, ranges);
                        Check(scan.Count == 1 && scan.Preview[0].Address == address + 12, "find floating-point energy");
                        memory.Write(address + 12, allocation, NumberKind.Float, 80);
                        Check(Command(child, "float 12") == "80", "apply energy and verify remotely");
                    }
                    bool blocked = false;
                    try { memory.Write(address, allocation + 4096, NumberKind.Integer, 1); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked, "reject a changed allocation identity");
                    blocked = false; try { memory.Write(address, allocation, NumberKind.Integer, 1.5); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked, "reject fractional inventory counters");
                    blocked = false; try { memory.Write(address, allocation, NumberKind.Float, Double.NaN); } catch (InvalidOperationException) { blocked = true; }
                    Check(blocked, "reject non-finite writes");
                    Check(!Numbers.Match(Double.NaN, 0, MatchKind.Exact, 0, NumberKind.Float), "reject non-finite scan values");
                    Check(Numbers.Match(55.50001, 0, MatchKind.Exact, 55.5, NumberKind.Float), "floating-point comparison tolerance");
                    Command(child, "quit"); child.WaitForExit(5000);
                    Check(!memory.Alive, "detect target process shutdown");
                }
                child = null;
            }
            catch (Exception error) { output.AppendLine(error.ToString()); exitCode = 1; }
            finally
            {
                if (child != null) { try { if (!child.HasExited) child.Kill(); } catch { } child.Dispose(); }
                output.AppendLine(exitCode == 0 ? "ALL TESTS PASSED. Stellar Blade compatibility has NOT been tested." : "TEST FAILURE.");
                if (resultFile != null) File.WriteAllText(resultFile, output.ToString(), new UTF8Encoding(true));
                Console.Write(output.ToString());
            }
            return exitCode;
        }
    }
}
