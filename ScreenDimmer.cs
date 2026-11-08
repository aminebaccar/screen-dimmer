// Screen Dimmer: darkens the whole screen with a full-screen color matrix (the same
// compositor-level mechanism Windows Color Filters use), so nothing can draw above it.
// Lives in the tray.
//   Ctrl+Alt+D       toggle
//   Ctrl+Alt+Up      lighter
//   Ctrl+Alt+Down    darker

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Screen Dimmer")]
[assembly: AssemblyProduct("Screen Dimmer")]
[assembly: AssemblyDescription("Dims the whole screen from the system tray")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Mohamed Amine Baccar")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Native
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    [StructLayout(LayoutKind.Sequential)]
    public struct ColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)] public float[] m; // 5x5, row-major
    }

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("magnification.dll")] public static extern bool MagInitialize();
    [DllImport("magnification.dll")] public static extern bool MagUninitialize();
    [DllImport("magnification.dll")] public static extern bool MagSetFullscreenColorEffect(ref ColorEffect effect);
}

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (new Mutex(true, "ScreenDimmer.SingleInstance", out created))
        {
            if (!created) return;
            if (!Native.MagInitialize())
            {
                MessageBox.Show("Could not initialize the Windows magnification API.", "Screen Dimmer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // Never leave the screen dimmed if something goes wrong.
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ScreenFilter.Reset();
            Application.ThreadException += (s, e) => { ScreenFilter.Reset(); Application.Exit(); };
            try
            {
                Application.EnableVisualStyles();
                Application.Run(new DimmerContext());
            }
            finally
            {
                ScreenFilter.Reset();
                Native.MagUninitialize();
            }
        }
    }
}

static class ScreenFilter
{
    // darkness 0 = untouched, 1 = black.
    public static bool Apply(double darkness)
    {
        float f = (float)(1.0 - darkness);
        var e = new Native.ColorEffect { m = new float[25] };
        e.m[0] = f; e.m[6] = f; e.m[12] = f; // R, G, B scale
        e.m[18] = 1f; e.m[24] = 1f;          // alpha, translation row
        return Native.MagSetFullscreenColorEffect(ref e);
    }

    public static void Reset() { Apply(0); }
}

class HotkeyWindow : NativeWindow, IDisposable
{
    public event Action<int> Pressed;
    readonly List<int> ids = new List<int>();

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // message-only window
    }

    public bool Register(int id, uint mods, uint vk)
    {
        if (!Native.RegisterHotKey(Handle, id, mods, vk)) return false;
        ids.Add(id);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
}

class DimmerContext : ApplicationContext
{
    const double Min = 0.10, Max = 0.90, Step = 0.05, Default = 0.50, FadeStep = 0.06;
    const int HkToggle = 1, HkDarker = 2, HkLighter = 3;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "ScreenDimmer";
    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenDimmer", "level.txt");

    readonly NotifyIcon tray;
    readonly HotkeyWindow hotkeys;
    readonly System.Windows.Forms.Timer fadeTimer;
    readonly ToolStripMenuItem toggleItem, levelItem, startupItem;
    readonly Icon icon;

    double level = Default; // darkness when on
    double current;         // darkness currently applied
    bool on;

    public DimmerContext()
    {
        LoadLevel();

        toggleItem = new ToolStripMenuItem("Dim screen", null, (s, e) => Toggle()) { ShortcutKeyDisplayString = "Ctrl+Alt+D" };
        levelItem = new ToolStripMenuItem { Enabled = false };
        var darker = new ToolStripMenuItem("Darker", null, (s, e) => Adjust(+Step)) { ShortcutKeyDisplayString = "Ctrl+Alt+Down" };
        var lighter = new ToolStripMenuItem("Lighter", null, (s, e) => Adjust(-Step)) { ShortcutKeyDisplayString = "Ctrl+Alt+Up" };
        startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleStartup()) { Checked = IsStartupEnabled() };
        var exit = new ToolStripMenuItem("Exit", null, (s, e) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] {
            toggleItem, new ToolStripSeparator(), levelItem, darker, lighter,
            new ToolStripSeparator(), startupItem, exit });

        icon = MakeIcon();
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };

        fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        fadeTimer.Tick += (s, e) => FadeTick();

        hotkeys = new HotkeyWindow();
        hotkeys.Pressed += OnHotkey;
        var failed = new List<string>();
        if (!hotkeys.Register(HkToggle, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.D)) failed.Add("Ctrl+Alt+D");
        if (!hotkeys.Register(HkDarker, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.Down)) failed.Add("Ctrl+Alt+Down");
        if (!hotkeys.Register(HkLighter, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.Up)) failed.Add("Ctrl+Alt+Up");
        if (failed.Count > 0)
            tray.ShowBalloonTip(4000, "Screen Dimmer", "Hotkey already in use: " + string.Join(", ", failed.ToArray()), ToolTipIcon.Warning);

        // Re-apply after events that can reset the compositor's color effect.
        SystemEvents.DisplaySettingsChanged += OnSystemChange;
        SystemEvents.SessionSwitch += OnSystemChange;
        SystemEvents.PowerModeChanged += OnSystemChange;

        UpdateUi();
    }

    void OnSystemChange(object sender, EventArgs e)
    {
        if (current > 0) ScreenFilter.Apply(current);
    }

    void OnHotkey(int id)
    {
        if (id == HkToggle) Toggle();
        else if (id == HkDarker) Adjust(+Step);
        else if (id == HkLighter) Adjust(-Step);
    }

    void Toggle()
    {
        on = !on;
        fadeTimer.Start();
        UpdateUi();
    }

    void Adjust(double delta)
    {
        level = Math.Max(Min, Math.Min(Max, Math.Round((level + delta) / Step) * Step));
        on = true;
        SaveLevel();
        fadeTimer.Start();
        UpdateUi();
    }

    void FadeTick()
    {
        double target = on ? level : 0;
        double diff = target - current;
        current = Math.Abs(diff) <= FadeStep ? target : current + Math.Sign(diff) * FadeStep;
        if (!ScreenFilter.Apply(current))
        {
            fadeTimer.Stop();
            on = false;
            current = 0;
            UpdateUi();
            tray.ShowBalloonTip(4000, "Screen Dimmer",
                "Windows refused the screen filter. Turn off Color filters or Magnifier and try again.", ToolTipIcon.Warning);
            return;
        }
        if (current == target) fadeTimer.Stop();
    }

    void UpdateUi()
    {
        int pct = (int)Math.Round(level * 100);
        toggleItem.Checked = on;
        levelItem.Text = "Darkness: " + pct + "%";
        tray.Text = "Screen Dimmer - " + (on ? pct + "%" : "off");
    }

    void LoadLevel()
    {
        try
        {
            double v;
            if (File.Exists(SettingsPath) &&
                double.TryParse(File.ReadAllText(SettingsPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                level = Math.Max(Min, Math.Min(Max, v));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void SaveLevel()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, level.ToString("0.00", CultureInfo.InvariantCulture));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool IsStartupEnabled()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            return key != null && key.GetValue(RunName) != null;
    }

    void ToggleStartup()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (IsStartupEnabled()) key.DeleteValue(RunName, false);
            else key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
        }
        startupItem.Checked = IsStartupEnabled();
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            using (var moon = new SolidBrush(Color.FromArgb(255, 230, 230, 235)))
            using (var cut = new SolidBrush(Color.Transparent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(moon, 3, 3, 26, 26);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.FillEllipse(cut, 12, -1, 24, 24);
            }
            IntPtr h = bmp.GetHicon();
            var result = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return result;
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= OnSystemChange;
        SystemEvents.SessionSwitch -= OnSystemChange;
        SystemEvents.PowerModeChanged -= OnSystemChange;
        fadeTimer.Stop();
        ScreenFilter.Reset();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.Dispose();
        icon.Dispose();
        base.ExitThreadCore();
    }
}
