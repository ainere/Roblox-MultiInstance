// Roblox Multi-Instance: a tray app that does what the Process Explorer steps do, automatically.
// It watches for RobloxPlayerBeta.exe and closes the ROBLOX_singletonEvent handle inside it,
// so the next Roblox launch doesn't see an existing instance and quit.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RobloxMultiInstance
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Flags exist for testing: --process NAME and --object NAME (repeatable), --dry-run (log only, close nothing).
            var processNames = new List<string>();
            var objectNames = new List<string>();
            bool dryRun = Array.IndexOf(args, "--dry-run") >= 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--process") processNames.Add(args[i + 1]);
                if (args[i] == "--object") objectNames.Add(args[i + 1]);
            }
            if (processNames.Count == 0) processNames.Add("RobloxPlayerBeta");
            // Only the event gates a second launch; every running client also holds ROBLOX_singletonMutex without it mattering.
            if (objectNames.Count == 0) objectNames.Add("ROBLOX_singletonEvent");

            bool createdNew;
            using (new Mutex(true, @"Local\RobloxMultiInstance_Tray", out createdNew))
            {
                if (!createdNew) return;

                Application.EnableVisualStyles();
                Application.Run(new TrayContext(new SingletonCloser(processNames.ToArray(), objectNames.ToArray(), dryRun)));
            }
        }
    }

    sealed class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "RobloxMultiInstance";

        readonly NotifyIcon icon;
        readonly SingletonCloser closer;

        public TrayContext(SingletonCloser closer)
        {
            this.closer = closer;

            var status = new ToolStripMenuItem("Unlocked 0 instances") { Enabled = false };
            var pause = new ToolStripMenuItem("Pause") { CheckOnClick = true };
            var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsStartupEnabled() };
            var openLog = new ToolStripMenuItem("Open log");
            var exit = new ToolStripMenuItem("Exit");

            pause.CheckedChanged += (s, e) => closer.Paused = pause.Checked;
            startup.CheckedChanged += (s, e) => SetStartupEnabled(startup.Checked);
            openLog.Click += (s, e) => { if (File.Exists(Log.Path)) Process.Start(Log.Path); };
            exit.Click += (s, e) => Shutdown();

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), pause, startup, openLog, new ToolStripSeparator(), exit });
            menu.Opening += (s, e) => status.Text = "Unlocked " + closer.ClosedCount + (closer.ClosedCount == 1 ? " instance" : " instances");

            icon = new NotifyIcon { Icon = MakeIcon(), Text = "Roblox Multi-Instance", ContextMenuStrip = menu, Visible = true };

            closer.Start();
        }

        void Shutdown()
        {
            icon.Visible = false;
            closer.Stop();
            ExitThread();
        }

        static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        static void SetStartupEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enabled) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(RunValue, false);
            }
        }

        // The installed Roblox client's own icon with an infinity sign in the bottom-right corner: a black
        // body with a cyan-to-magenta core. The icon is read from the client at runtime so no Roblox
        // artwork has to ship with this app.
        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            using (var halo = new Pen(Color.White, 6.4f) { LineJoin = LineJoin.Round })
            using (var body = new Pen(Color.Black, 4.4f) { LineJoin = LineJoin.Round })
            using (var coreBrush = new LinearGradientBrush(new RectangleF(10, 18, 20, 12), Color.FromArgb(0, 229, 255), Color.FromArgb(255, 43, 214), LinearGradientMode.Horizontal))
            using (var core = new Pen(coreBrush, 2.2f) { LineJoin = LineJoin.Round })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                using (var robloxIcon = LoadRobloxIcon())
                {
                    if (robloxIcon != null) g.DrawIcon(robloxIcon, new Rectangle(0, 0, 32, 32));
                    else using (var blue = new SolidBrush(Color.FromArgb(45, 90, 255))) g.FillRectangle(blue, 0, 0, 32, 32);
                }

                // White halo under the black body keeps the sign readable on the blue tile at 16px.
                var points = InfinityPoints(20f, 24f, 19f, 10f);
                g.DrawPolygon(halo, points);
                g.DrawPolygon(body, points);
                g.DrawPolygon(core, points);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // Lemniscate of Bernoulli, stretched vertically so the loops stay open at small sizes.
        static PointF[] InfinityPoints(float centerX, float centerY, float width, float height)
        {
            const int steps = 72;
            const double maxY = 0.35355;    // peak of sin(t)cos(t)/(1+sin(t)^2)
            var points = new PointF[steps];
            for (int i = 0; i < steps; i++)
            {
                double t = 2 * Math.PI * i / steps;
                double denom = 1 + Math.Sin(t) * Math.Sin(t);
                points[i] = new PointF(
                    centerX + (float)(width / 2 * Math.Cos(t) / denom),
                    centerY + (float)(height / 2 * Math.Sin(t) * Math.Cos(t) / denom / maxY));
            }
            return points;
        }

        static Icon LoadRobloxIcon()
        {
            try
            {
                var versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\Versions");
                if (!Directory.Exists(versions)) return null;

                var dirs = new DirectoryInfo(versions).GetDirectories();
                Array.Sort(dirs, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                foreach (var dir in dirs)
                {
                    var exe = Path.Combine(dir.FullName, "RobloxPlayerBeta.exe");
                    if (File.Exists(exe)) return Icon.ExtractAssociatedIcon(exe);
                }
            }
            catch { }
            return null;
        }
    }

    sealed class SingletonCloser
    {
        static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);      // keep scanning briefly after the first hit in case more than one object is configured
        static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(2);     // stop scanning a process that never shows a singleton
        static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(30);
        const int NameBufSize = 2048;

        sealed class Target
        {
            public DateTime FirstSeen = DateTime.UtcNow;
            public DateTime FirstClosed = DateTime.MaxValue;
            public DateTime NextScan = DateTime.MinValue;
            public int OpenFailures;
            public bool Done;
        }

        readonly string[] processNames;
        readonly string[] objectNames;
        readonly bool dryRun;
        readonly Dictionary<int, Target> targets = new Dictionary<int, Target>();
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        Thread thread;

        IntPtr tableBuf = Marshal.AllocHGlobal(4 << 20);
        int tableBufSize = 4 << 20;
        readonly IntPtr nameBuf = Marshal.AllocHGlobal(NameBufSize);

        // Our own handles, kept only to learn which ObjectTypeIndex means Event / Mutant on this Windows build.
        readonly ManualResetEvent probeEvent = new ManualResetEvent(false);
        readonly Mutex probeMutex = new Mutex();
        int eventType = -1, mutantType = -1;

        volatile bool paused;
        int closedCount;

        public SingletonCloser(string[] processNames, string[] objectNames, bool dryRun)
        {
            this.processNames = processNames;
            this.objectNames = objectNames;
            this.dryRun = dryRun;
        }

        public bool Paused { get { return paused; } set { paused = value; Log.Write(value ? "Paused" : "Resumed"); } }
        public int ClosedCount { get { return Thread.VolatileRead(ref closedCount); } }

        public void Start()
        {
            Log.Write("Started" + (dryRun ? " (dry run, nothing will be closed)" : "") + ", watching " + string.Join(", ", processNames) + " for " + string.Join(", ", objectNames));
            thread = new Thread(Run) { IsBackground = true, Name = "SingletonCloser" };
            thread.Start();
        }

        public void Stop()
        {
            stop.Set();
            if (thread != null) thread.Join(2000);
            Log.Write("Stopped");
        }

        void Run()
        {
            while (!stop.WaitOne(250))
            {
                if (paused) continue;
                try { Tick(); }
                catch (Exception ex) { Log.Write("Error: " + ex.Message); }
            }
        }

        void Tick()
        {
            var live = LivePids();

            foreach (int pid in new List<int>(targets.Keys))
                if (!live.Contains(pid)) targets.Remove(pid);
            foreach (int pid in live)
                if (!targets.ContainsKey(pid))
                {
                    targets[pid] = new Target();
                    Log.Write("Roblox process detected, PID " + pid);
                }

            var now = DateTime.UtcNow;
            var open = new Dictionary<int, IntPtr>();
            try
            {
                foreach (var kv in targets)
                {
                    var t = kv.Value;
                    if (t.Done || now < t.NextScan) continue;

                    var age = now - t.FirstSeen;
                    if (now - t.FirstClosed > Grace) { t.Done = true; continue; }
                    if (t.FirstClosed == DateTime.MaxValue && age > GiveUp)
                    {
                        t.Done = true;
                        Log.Write("PID " + kv.Key + ": no singleton handle found, giving up");
                        continue;
                    }

                    IntPtr hProc = Native.OpenProcess(Native.PROCESS_DUP_HANDLE, false, kv.Key);
                    if (hProc == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (t.OpenFailures == 0 || t.OpenFailures == 20 || t.OpenFailures == 200)
                            Log.Write("PID " + kv.Key + ": OpenProcess failed (Win32 error " + err + (err == 5 ? ", access denied; if Roblox runs as admin, run this as admin too" : "") + ")");
                        t.OpenFailures++;
                        t.NextScan = now + TimeSpan.FromSeconds(1);
                        continue;
                    }

                    open[kv.Key] = hProc;
                    if (t.FirstClosed == DateTime.MaxValue && age > SlowAfter) t.NextScan = now + TimeSpan.FromSeconds(3);
                }

                if (open.Count > 0) ScanHandles(open, now);
            }
            finally
            {
                foreach (var h in open.Values) Native.CloseHandle(h);
            }
        }

        HashSet<int> LivePids()
        {
            var pids = new HashSet<int>();
            foreach (var name in processNames)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    pids.Add(p.Id);
                    p.Dispose();
                }
            return pids;
        }

        void ScanHandles(Dictionary<int, IntPtr> procs, DateTime now)
        {
            int status;
            while (true)
            {
                int needed;
                status = Native.NtQuerySystemInformation(Native.SystemExtendedHandleInformation, tableBuf, tableBufSize, out needed);
                if (status != Native.STATUS_INFO_LENGTH_MISMATCH) break;

                Marshal.FreeHGlobal(tableBuf);
                tableBufSize = Math.Max(needed, tableBufSize) + (1 << 20);
                tableBuf = Marshal.AllocHGlobal(tableBufSize);
            }
            if (status < 0) { Log.Write("NtQuerySystemInformation failed: 0x" + status.ToString("X8")); return; }

            long count = Marshal.ReadInt64(tableBuf);
            if (eventType < 0) ResolveTypeIndexes(count);

            for (long i = 0; i < count; i++)
            {
                long off = Native.TableHeaderSize + i * Native.TableEntrySize;
                IntPtr hProc;
                if (!procs.TryGetValue((int)Marshal.ReadInt64(tableBuf, (int)off + 8), out hProc)) continue;

                int type = (ushort)Marshal.ReadInt16(tableBuf, (int)off + 30);
                if (type != eventType && type != mutantType) continue;

                IntPtr handle = Marshal.ReadIntPtr(tableBuf, (int)off + 16);
                string name = QueryName(hProc, handle);
                if (name == null || !IsSingletonName(name)) continue;

                int pid = (int)Marshal.ReadInt64(tableBuf, (int)off + 8);
                IntPtr ignored;
                if (dryRun || Native.DuplicateHandle(hProc, handle, IntPtr.Zero, out ignored, 0, false, Native.DUPLICATE_CLOSE_SOURCE))
                {
                    Interlocked.Increment(ref closedCount);
                    var t = targets[pid];
                    if (t.FirstClosed == DateTime.MaxValue) t.FirstClosed = now;
                    Log.Write("PID " + pid + (dryRun ? ": found (not closing) " : ": closed ") + name);
                }
                else
                {
                    Log.Write("PID " + pid + ": could not close " + name + " (Win32 error " + Marshal.GetLastWin32Error() + ")");
                }
            }
        }

        void ResolveTypeIndexes(long count)
        {
            long self = Process.GetCurrentProcess().Id;
            long ev = probeEvent.SafeWaitHandle.DangerousGetHandle().ToInt64();
            long mx = probeMutex.SafeWaitHandle.DangerousGetHandle().ToInt64();

            for (long i = 0; i < count; i++)
            {
                long off = Native.TableHeaderSize + i * Native.TableEntrySize;
                if (Marshal.ReadInt64(tableBuf, (int)off + 8) != self) continue;

                long value = Marshal.ReadIntPtr(tableBuf, (int)off + 16).ToInt64();
                int type = (ushort)Marshal.ReadInt16(tableBuf, (int)off + 30);
                if (value == ev) eventType = type;
                else if (value == mx) mutantType = type;
            }
            Log.Write("Handle type indexes: Event=" + eventType + " Mutant=" + mutantType);
        }

        string QueryName(IntPtr hProc, IntPtr handle)
        {
            IntPtr dup;
            if (!Native.DuplicateHandle(hProc, handle, Native.GetCurrentProcess(), out dup, 0, false, Native.DUPLICATE_SAME_ACCESS))
                return null;
            try
            {
                int ignored;
                if (Native.NtQueryObject(dup, Native.ObjectNameInformation, nameBuf, NameBufSize, out ignored) < 0) return null;
                int bytes = (ushort)Marshal.ReadInt16(nameBuf);
                if (bytes == 0) return null;
                return Marshal.PtrToStringUni(Marshal.ReadIntPtr(nameBuf, 8), bytes / 2);
            }
            finally { Native.CloseHandle(dup); }
        }

        bool IsSingletonName(string name)
        {
            foreach (var n in objectNames)
                if (name.EndsWith("\\" + n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    static class Log
    {
        public static readonly string Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RobloxMultiInstance.log");
        static readonly object gate = new object();

        static Log()
        {
            try { if (File.Exists(Path) && new FileInfo(Path).Length > 512 * 1024) File.Delete(Path); }
            catch { }
        }

        public static void Write(string message)
        {
            lock (gate)
            {
                try { File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine); }
                catch { }
            }
        }
    }

    static class Native
    {
        public const uint PROCESS_DUP_HANDLE = 0x0040;
        public const uint DUPLICATE_CLOSE_SOURCE = 0x1;
        public const uint DUPLICATE_SAME_ACCESS = 0x2;
        public const int SystemExtendedHandleInformation = 64;
        public const int ObjectNameInformation = 1;
        public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        // SYSTEM_HANDLE_INFORMATION_EX on x64: 16-byte header, then 40-byte entries
        // { Object, UniqueProcessId, HandleValue, GrantedAccess:4, BackTrace:2, ObjectTypeIndex:2, Attributes:4, Reserved:4 }
        public const long TableHeaderSize = 16;
        public const long TableEntrySize = 40;

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DuplicateHandle(IntPtr srcProcess, IntPtr srcHandle, IntPtr dstProcess, out IntPtr dstHandle, uint access, bool inherit, uint options);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();
    }
}
