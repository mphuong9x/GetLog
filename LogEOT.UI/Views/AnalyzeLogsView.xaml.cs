using LogEOT.Core.Models;
using System.Windows.Controls;

namespace LogEOT.UI.Views;

public partial class AnalyzeLogsView : System.Windows.Controls.UserControl
{
    private readonly Action<string>? _logMessage;

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
}
