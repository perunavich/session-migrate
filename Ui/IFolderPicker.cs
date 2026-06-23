namespace SessionMigrate.Ui;

// Lets the bridge ask the host (the WinForms shell) to pick a destination folder.
public interface IFolderPicker
{
    // Returns the chosen path, or null if cancelled.
    string? Pick(string description);
}
