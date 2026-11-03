using LogEOT.Core.Models;
using LogEOT.Infrastructure.Services;
using System.Windows.Controls;

namespace LogEOT.UI.Views;

public partial class AnalyzeLogsView : System.Windows.Controls.UserControl
{
    private readonly Action<string>? _logMessage;
    private readonly AnalyzeKeysConfigService _configService = new();
    private List<KeyConfig> _savedKeys = [];
    private string? _configPath;

    public AnalyzeLogsView(Action<string>? logMessage = null)
    {
        InitializeComponent();
        _logMessage = logMessage;
    }

    private void Browse_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderTextBox.Text = dialog.SelectedPath;
            _logMessage?.Invoke($"Folder selected: {dialog.SelectedPath}");
        }
    }

    public event EventHandler? RunRequested;
    public event EventHandler? ResetRequested;

    public LogSelection SelectedLogSelection =>
        AllLogsRadioButton.IsChecked == true
            ? LogSelection.All
            : FailLogsRadioButton.IsChecked == true
                ? LogSelection.Fail
                : LogSelection.Pass;

    private void Run_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Reset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetConfigButtonsEnabled(bool enabled)
    {
        SaveKeysButton.IsEnabled = enabled;
        LoadConfigButton.IsEnabled = enabled;
    }

    private IList<KeyConfig> TableKeys => KeysGrid.ItemsSource as IList<KeyConfig>
        ?? throw new InvalidOperationException("The search table is not available.");

    private bool CommitKeys()
    {
        if (KeysGrid.CommitEdit(DataGridEditingUnit.Cell, true)
            && KeysGrid.CommitEdit(DataGridEditingUnit.Row, true)) return true;
        _logMessage?.Invoke("Please finish editing the current row before saving or loading a config.");
        return false;
    }

    private List<KeyConfig> SnapshotKeys() => TableKeys.Select(key => new KeyConfig
    {
        Key = key.Key, AltKey = key.AltKey, ColumnName = key.ColumnName
    }).ToList();

    private bool HasUnsavedChanges() => TableKeys.Count != _savedKeys.Count
        || TableKeys.Where((key, index) => key.Key != _savedKeys[index].Key
            || key.AltKey != _savedKeys[index].AltKey || key.ColumnName != _savedKeys[index].ColumnName).Any();

    private void SaveKeys_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (!CommitKeys()) return;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Analyze Keys Config",
                Filter = "Key Config (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = _configPath ?? "AnalyzeKeys.json"
            };
            if (dialog.ShowDialog() != true) return;
            _configService.Save(dialog.FileName, TableKeys);
            _savedKeys = SnapshotKeys();
            _configPath = dialog.FileName;
            _logMessage?.Invoke($"Keys config saved: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowConfigError("save", ex);
        }
    }

    private void LoadConfig_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load Analyze Keys Config",
            Filter = "Key Config (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // Read and validate first; an invalid file must leave the table intact.
            var config = _configService.Load(dialog.FileName);
            if (!CommitKeys()) return;
            if (HasUnsavedChanges() && System.Windows.MessageBox.Show(
                    "The search table has unsaved changes. Replace it with the selected config?",
                    "Load Config", System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No)
                != System.Windows.MessageBoxResult.Yes) return;

            var keys = TableKeys;
            keys.Clear();
            foreach (var key in config.Keys) keys.Add(key);
            _savedKeys = SnapshotKeys();
            _configPath = dialog.FileName;
            _logMessage?.Invoke($"Keys config loaded: {config.Name} ({config.Keys.Count} rows) — {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowConfigError("load", ex);
        }
    }

    private void ShowConfigError(string action, Exception exception)
    {
        string message = $"Could not {action} keys config: {exception.Message}";
        _logMessage?.Invoke(message);
        System.Windows.MessageBox.Show(message, "Keys Config", System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }
}
