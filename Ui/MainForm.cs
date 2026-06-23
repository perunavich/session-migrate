using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SessionMigrate.Ui;

// Hosts the WebView2 UI and relays messages between the web layer and the Core.
public sealed class MainForm : Form, IFolderPicker
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Bridge _bridge;

    public MainForm()
    {
        _bridge = new Bridge(this);

        Text = "session-migrate";
        Width = 1180;
        Height = 800;
        MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x10, 0x11, 0x14);
        Controls.Add(_web);
        Load += async (_, _) => await InitializeWebViewAsync();
    }

    public string? Pick(string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private async Task InitializeWebViewAsync()
    {
        await _web.EnsureCoreWebView2Async();
        CoreWebView2 core = _web.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        string webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        core.SetVirtualHostNameToFolderMapping(
            "app", webRoot, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate("https://app/index.html");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string response = await _bridge.HandleAsync(e.WebMessageAsJson);
            _web.CoreWebView2.PostWebMessageAsJson(response);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The form closed or the WebView navigated while a reply was in flight — nothing to do.
        }
    }
}
