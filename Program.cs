using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HamsterUpdater;

internal static class Program
{
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        string folder = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)));
        using var mutex = new Mutex(false, @"Local\HamsterUpdaterUi-" + key);
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { MessageBox.Show("The updater is already running. Click its icon by the clock.", "Hamster Updater"); return; }
        try { using var context = new UpdaterContext(); Application.Run(context); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Hamster Updater", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { mutex.ReleaseMutex(); }
    }
}
internal sealed class Preferences { public int Seconds { get; set; } = 30; }
internal sealed class ReleaseState
{
    public string Tag { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
internal sealed class UpdaterData
{
    public int Seconds { get; set; } = 30;
    public string Tag { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
internal sealed class UpdaterContext : ApplicationContext
{
    const string Repository = "https://github.com/BlazePlayze333/Hampster";
    readonly string folder = Path.GetFullPath(AppContext.BaseDirectory);
    readonly Form popup = new() { Text = "Hamster Updater", ShowInTaskbar = false, TopMost = true, FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.Manual, ClientSize = new Size(430, 305), MaximizeBox = false, MinimizeBox = false };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Starting..." };
    readonly Label countdown = new() { Dock = DockStyle.Fill };
    readonly NumericUpDown seconds = new() { Minimum = 15, Maximum = 86400, Value = 30, Width = 90 };
    readonly Button check = new() { Text = "Check now", AutoSize = true }, pause = new() { Text = "Pause", AutoSize = true }, exit = new() { Text = "Exit updater", AutoSize = true };
    readonly ContextMenuStrip menu = new();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem menuCheck, menuPause;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    readonly HttpClient metadata = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(40) };
    readonly HttpClient download = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(10) };
    readonly UpdaterData data;
    bool automatic = true, busy, exiting;
    int interval = 30;
    string lastCheck = "Not yet";
    DateTimeOffset nextCheck = DateTimeOffset.Now;
    string Target => Path.Combine(folder, "DesktopHamster.exe");
    string DataFile => Path.Combine(folder, "HamsterUpdater.json");
    string OldStateFile => Path.Combine(folder, ".hamster-csharp-updater-release.json");
    string OldSettingsFile => Path.Combine(folder, ".hamster-csharp-updater-settings.json");

    UpdaterData LoadData(out string? warning)
    {
        warning = null;
        if (File.Exists(DataFile))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<UpdaterData>(File.ReadAllText(DataFile)) ?? throw new IOException("Empty updater data.");
                File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.Hidden);
                return loaded;
            }
            catch (Exception ex)
            {
                warning = "Could not load or hide saved data: " + ex.Message;
                return new UpdaterData();
            }
        }
        var result = new UpdaterData();
        bool migrated = false;
        try
        {
            if (File.Exists(OldSettingsFile))
            {
                var old = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(OldSettingsFile)) ?? throw new IOException("Empty old settings.");
                result.Seconds = old.Seconds;
                migrated = true;
            }
            if (File.Exists(OldStateFile))
            {
                var old = JsonSerializer.Deserialize<ReleaseState>(File.ReadAllText(OldStateFile)) ?? throw new IOException("Empty old release record.");
                result.Tag = old.Tag;
                result.Sha256 = old.Sha256;
                migrated = true;
            }
            if (migrated)
            {
                WriteData(result);
                // Remove legacy files only after the combined file has been saved and hidden.
                if (File.Exists(OldSettingsFile)) File.Delete(OldSettingsFile);
                if (File.Exists(OldStateFile)) File.Delete(OldStateFile);
            }
        }
        catch (Exception ex) { warning = "Data migration incomplete; old files may remain: " + ex.Message; }
        return result;
    }
    void WriteData(UpdaterData value)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        // OpenOrCreate avoids truncating an existing file through FileMode.Create when it is hidden.
        using (var stream = new FileStream(DataFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            File.SetAttributes(DataFile, File.GetAttributes(DataFile) | FileAttributes.Hidden);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
            stream.SetLength(stream.Position);
            stream.Flush(true);
        }
    }
    void SaveData() { data.Seconds = interval; WriteData(data); }

    public UpdaterContext()
    {
        metadata.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopHamster-Updater/1.1");
        download.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopHamster-Updater/1.1");
        data = LoadData(out string? startupWarning);
        if (data.Seconds >= 15 && data.Seconds <= 86400) interval = data.Seconds;
        seconds.Value = interval;
        popup.Font = new Font("Segoe UI", 10); popup.AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int h in new[] { 30, 50, 32, 43, 43, 50 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        layout.Controls.Add(new Label { Text = "Desktop Hamster Updater", Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true }, 0, 0);
        layout.Controls.Add(status, 0, 1); layout.Controls.Add(countdown, 0, 2);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var apply = new Button { Text = "Apply", AutoSize = true };
        options.Controls.Add(new Label { Text = "Every", AutoSize = true, Margin = new Padding(0, 7, 6, 0) }); options.Controls.Add(seconds);
        options.Controls.Add(new Label { Text = "seconds", AutoSize = true, Margin = new Padding(6, 7, 8, 0) }); options.Controls.Add(apply); layout.Controls.Add(options, 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill }; buttons.Controls.AddRange(new Control[] { check, pause, exit }); layout.Controls.Add(buttons, 0, 4);
        layout.Controls.Add(new Label { Text = "Click away to hide. Checking continues in the tray.", Dock = DockStyle.Fill }, 0, 5); popup.Controls.Add(layout);
        menu.Items.Add("Status / settings", null, (_, _) => ShowPopup());
        menuCheck = new ToolStripMenuItem("Check now"); menuCheck.Click += async (_, _) => await CheckAsync(); menu.Items.Add(menuCheck);
        menuPause = new ToolStripMenuItem("Pause automatic checks"); menuPause.Click += (_, _) => ToggleAuto(); menu.Items.Add(menuPause);
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit updater", null, (_, _) => RequestExit());
        tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Hamster updater - click for status", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPopup(); };
        check.Click += async (_, _) => await CheckAsync(); pause.Click += (_, _) => ToggleAuto(); exit.Click += (_, _) => RequestExit();
        apply.Click += (_, _) =>
        {
            interval = (int)seconds.Value; nextCheck = DateTimeOffset.Now.AddSeconds(interval);
            try { SaveData(); SetStatus($"Interval saved: {interval} seconds."); }
            catch (Exception ex) { SetStatus("Interval changed for this session. Could not save: " + ex.Message); }
        };
        popup.Deactivate += (_, _) => popup.Hide();
        popup.FormClosing += (_, e) => { if (!exiting) { e.Cancel = true; popup.Hide(); } };
        timer.Tick += async (_, _) =>
        {
            if (busy) countdown.Text = "Operation in progress...";
            else if (automatic)
            {
                int remaining = Math.Max(0, (int)Math.Ceiling((nextCheck - DateTimeOffset.Now).TotalSeconds));
                countdown.Text = $"Next check: {remaining}s | Last: {lastCheck}";
                if (remaining == 0) await CheckAsync();
            }
            else countdown.Text = "Paused | Last check: " + lastCheck;
        };
        SetStatus(startupWarning ?? $"Automatic checks every {interval} seconds.");
        if (startupWarning != null)
        {
            MessageBox.Show(startupWarning, "Hamster Updater", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        timer.Start();
    }
    void ShowPopup()
    {
        Point p = Cursor.Position; Rectangle area = Screen.FromPoint(p).WorkingArea;
        popup.Location = new Point(Math.Clamp(p.X - popup.Width + 20, area.Left, Math.Max(area.Left, area.Right - popup.Width)), Math.Clamp(p.Y - popup.Height - 8, area.Top, Math.Max(area.Top, area.Bottom - popup.Height)));
        popup.Show(); popup.Activate();
    }
    void SetStatus(string text) { status.Text = text; tray.Text = text.Length > 60 ? text[..60] : text; }
    void ToggleAuto()
    {
        automatic = !automatic; pause.Text = automatic ? "Pause" : "Resume"; menuPause.Text = automatic ? "Pause automatic checks" : "Resume automatic checks";
        if (automatic) { nextCheck = DateTimeOffset.Now; SetStatus("Automatic checking enabled."); } else SetStatus("Paused. An operation already running will finish.");
    }
    void RequestExit()
    {
        exiting = true; automatic = false; check.Enabled = false; menuCheck.Enabled = false; pause.Enabled = false; exit.Enabled = false;
        if (busy) SetStatus("Exiting when the current operation finishes safely..."); else ExitThread();
    }
    async Task CheckAsync()
    {
        if (busy || exiting) return;
        busy = true; check.Enabled = false; menuCheck.Enabled = false;
        string temp = Path.Combine(folder, ".hamster-download-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            SetStatus("Checking latest GitHub release...");
            using var request = new HttpRequestMessage(HttpMethod.Head, Repository + "/releases/latest");
            using var response = await metadata.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            int code = (int)response.StatusCode; Uri? location = response.Headers.Location;
            if (code is not (301 or 302 or 303 or 307 or 308) || location == null) throw new IOException("No public latest release was returned by GitHub.");
            Uri release = location.IsAbsoluteUri ? location : new Uri(new Uri(Repository + "/"), location);
            const string prefix = "/BlazePlayze333/Hampster/releases/tag/";
            if (release.Scheme != "https" || !release.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !release.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected release address. Update stopped.");
            string tag = release.AbsolutePath[prefix.Length..];
            if (File.Exists(Target) && data.Tag == tag && await HashAsync(Target) == data.Sha256) { SetStatus("Up to date. No download or restart."); return; }
            SetStatus("Downloading release " + Uri.UnescapeDataString(tag) + "...");
            using (var asset = await download.GetAsync(Repository + "/releases/download/" + tag + "/DesktopHamster.exe", HttpCompletionOption.ResponseHeadersRead))
            {
                asset.EnsureSuccessStatusCode(); long? expected = asset.Content.Headers.ContentLength; long received = 0;
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await using (var input = await asset.Content.ReadAsStreamAsync(timeout.Token))
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                {
                    byte[] buffer = new byte[131072]; int count; var progress = Stopwatch.StartNew();
                    while ((count = await input.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token); received += count;
                        if (progress.ElapsedMilliseconds > 750) { SetStatus($"Downloading... {received / 1048576d:0.0} MB"); progress.Restart(); }
                    }
                }
                if (expected.HasValue && expected.Value != received) throw new IOException("Incomplete download. Installed EXE unchanged.");
            }
            using (var file = File.OpenRead(temp)) { if (file.Length < 1024 || file.ReadByte() != 77 || file.ReadByte() != 90) throw new IOException("Downloaded file is not a Windows EXE."); }
            SetStatus("Comparing the EXEs..."); string hash = await HashAsync(temp);
            bool identical = File.Exists(Target) && await HashAsync(Target) == hash;
            if (!identical)
            {
                await CloseHamsterAsync();
                SetStatus("Backing up and installing update...");
                string backup = Path.Combine(folder, "DesktopHamster.previous.exe");
                if (File.Exists(Target)) File.Replace(temp, Target, backup, true); else File.Move(temp, Target);
            }
            data.Tag = tag; data.Sha256 = hash;
            string? saveWarning = null;
            try { SaveData(); } catch (Exception ex) { saveWarning = " Could not save update record: " + ex.Message; }
            if (identical) SetStatus("EXE unchanged. No replacement or restart." + saveWarning);
            else
            {
                SetStatus("Starting updated hamster...");
                Process.Start(new ProcessStartInfo(Target) { UseShellExecute = true, WorkingDirectory = folder });
                SetStatus("Update installed. Hamster restarted." + saveWarning);
            }
        }
        catch (Exception ex) { SetStatus("Update error: " + ex.Message); }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            busy = false; lastCheck = DateTime.Now.ToString("HH:mm:ss"); nextCheck = DateTimeOffset.Now.AddSeconds(interval);
            check.Enabled = !exiting; menuCheck.Enabled = !exiting;
            if (exiting) ExitThread();
        }
    }
    static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
    async Task CloseHamsterAsync()
    {
        foreach (var process in Process.GetProcessesByName("DesktopHamster"))
        using (process)
        {
            string? path; try { path = process.MainModule?.FileName; } catch { continue; }
            if (path == null || !Path.GetFullPath(path).Equals(Target, StringComparison.OrdinalIgnoreCase)) continue;
            if (process.HasExited) continue;
            IntPtr handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero) EnumWindows((h, _) => { GetWindowThreadProcessId(h, out uint id); if (id == (uint)process.Id) { handle = h; return false; } return true; }, IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new IOException("Exit the hamster from his tray menu, then Check now.");
            SetStatus("Closing hamster cleanly...");
            if (!PostMessage(handle, 0x10, IntPtr.Zero, IntPtr.Zero)) throw new IOException("Could not ask the hamster to close.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { throw new IOException("Hamster did not close. Use Exit in his tray menu."); }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { exiting = true; timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); popup.Dispose(); metadata.Dispose(); download.Dispose(); }
        base.Dispose(disposing);
    }
    delegate bool WindowCallback(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
}
