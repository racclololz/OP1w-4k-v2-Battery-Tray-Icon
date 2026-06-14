using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OP1wBatteryTray;

internal sealed class TrayAppContext : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "OP1wBatteryTray";
    private const string ConfigToolFileName = "Endgame_Gear_OP1w_4k_v2_Configuration_Tool_v1_02.exe";
    private const int IconSize = 128;
    private const int PollIntervalMs = 60_000;
    private const int StaleAfterMs = 5 * PollIntervalMs;

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _startupMenuItem;
    private Icon? _currentIcon;
    private int? _lastPercent;
    private DateTime _lastReadUtc = DateTime.MinValue;
    private bool _refreshInProgress;

    public TrayAppContext()
    {
        _startupMenuItem = new ToolStripMenuItem("Run at startup")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        _startupMenuItem.CheckedChanged += (_, _) => SetStartupEnabled(_startupMenuItem.Checked);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        _notifyIcon = new NotifyIcon
        {
            Text = "OP1w battery: checking...",
            Icon = CreateIcon(null, false),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _notifyIcon.ContextMenuStrip.Items.Add(_startupMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitItem);
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenConfigurationTool();
        };

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress) return;
        _refreshInProgress = true;

        try
        {
            if (_lastPercent is null) _notifyIcon.Text = "OP1w battery: checking...";

            var reading = await Task.Run(ReadWithRetries);
            if (reading is null)
            {
                if (_lastPercent is int percent && DateTime.UtcNow - _lastReadUtc < TimeSpan.FromMilliseconds(StaleAfterMs))
                {
                    UpdateIcon(percent, true, $"OP1w: {percent}%");
                    return;
                }

                UpdateIcon(null, false, "OP1w: unavailable");
                return;
            }

            _lastPercent = reading.Percent;
            _lastReadUtc = DateTime.UtcNow;
            UpdateIcon(reading.Percent, true, $"OP1w: {reading.Percent}%");
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private static BatteryReading? ReadWithRetries()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var reading = BatteryReader.TryRead();
            if (reading is not null) return reading;
            Thread.Sleep(750);
        }

        return null;
    }

    private void UpdateIcon(int? percent, bool connected, string text)
    {
        var oldIcon = _currentIcon;
        _currentIcon = CreateIcon(percent, connected);
        _notifyIcon.Icon = _currentIcon;
        _notifyIcon.Text = text.Length <= 127 ? text : text[..127];
        oldIcon?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        base.ExitThreadCore();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(RunValueName) as string;
        return string.Equals(Unquote(value), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            key.SetValue(RunValueName, Quote(Application.ExecutablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(RunValueName, false);
        }
    }

    private static string Quote(string path) => $"\"{path}\"";

    private static string? Unquote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    private static void OpenConfigurationTool()
    {
        var toolPath = Path.Combine(AppContext.BaseDirectory, ConfigToolFileName);
        if (!File.Exists(toolPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = toolPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore launch failures so the tray app stays battery-only.
        }
    }

    private static Icon CreateIcon(int? percent, bool connected)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var fill = !connected || percent is null
            ? Color.FromArgb(120, 120, 120)
            : percent < 20
                ? Color.FromArgb(220, 45, 45)
                : percent < 50
                    ? Color.FromArgb(230, 170, 30)
                    : Color.FromArgb(35, 165, 90);

        using var fillBrush = new SolidBrush(fill);

        using var path = RoundedRect(new Rectangle(0, 0, IconSize, IconSize), 18);
        g.FillPath(fillBrush, path);

        var label = percent is null ? "?" : percent.Value.ToString();
        using var font = CreateFittedFont(g, label);
        using var textBrush = new SolidBrush(Color.White);
        using var outlineBrush = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var textRect = new RectangleF(-6, -11, IconSize + 12, IconSize + 18);
        DrawTextOutline(g, label, font, outlineBrush, textRect, format);
        g.DrawString(label, font, textBrush, textRect, format);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Font CreateFittedFont(Graphics g, string label)
    {
        var size = label.Length >= 3 ? 74f : 104f;
        while (size > 28f)
        {
            var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(label, font);
            if (measured.Width <= IconSize + 12f && measured.Height <= IconSize + 4f) return font;
            font.Dispose();
            size -= 1f;
        }

        return new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static void DrawTextOutline(Graphics g, string label, Font font, Brush brush, RectangleF textRect, StringFormat format)
    {
        const float outline = 4f;
        var offsets = new[]
        {
            new PointF(-outline, 0),
            new PointF(outline, 0),
            new PointF(0, -outline),
            new PointF(0, outline),
            new PointF(-outline, -outline),
            new PointF(outline, -outline),
            new PointF(-outline, outline),
            new PointF(outline, outline)
        };

        foreach (var offset in offsets)
        {
            g.DrawString(label, font, brush, new RectangleF(
                textRect.X + offset.X,
                textRect.Y + offset.Y,
                textRect.Width,
                textRect.Height), format);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
