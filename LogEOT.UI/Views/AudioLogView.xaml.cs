using System.Windows;
using System.Windows.Controls;
using LogEOT.Core.Models;
using LogEOT.Infrastructure.Exporters;
using LogEOT.Infrastructure.Services;
using WinForms = System.Windows.Forms;

namespace LogEOT.UI.Views;

public partial class AudioLogView : System.Windows.Controls.UserControl
{
    private readonly Action<string>? _logMessage;
    private readonly Action<string, bool>? _notify;

    public AudioLogView(Action<string>? logMessage = null, Action<string, bool>? notify = null)
    {
        InitializeComponent();
        _logMessage = logMessage;
        _notify = notify;
    }

    private void Log(string message) => _logMessage?.Invoke(message);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WinForms.FolderBrowserDialog();
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            InputDirBox.Text = dialog.SelectedPath;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var inputDir = InputDirBox.Text?.Trim();

        if (string.IsNullOrEmpty(inputDir) || !System.IO.Directory.Exists(inputDir))
        {
            Log("Please select a valid audio log folder.");
            return;
        }

        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            Log($"Parsing audio logs in: {inputDir}");

            var service = new LogProcessingService();
            var noKeys = new List<(string Key, string AltKey, string ColumnName)>();

            var results = await Task.Run(() => service.ProcessFolder(inputDir, noKeys, isAudioLog: true));

            int fileCount = results.Count;
            int withData = results.Count(r => r.AudioMetricGroups is { Count: > 0 });
            Log($"Files parsed: {fileCount}. Units with audio data: {withData}.");

            if (withData == 0)
            {
                SummaryText.Text = "No audio data found. Check that the folder contains audio test logs.";
                Log("No audio data found.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel File|*.xlsx",
                FileName = "Audio_Cpk_Data.xlsx",
                InitialDirectory = inputDir
            };

            if (dialog.ShowDialog() != true)
            {
                Log("Export cancelled by user.");
                return;
            }

            var exporter = new ExcelExporter();
            exporter.ExportAudio(dialog.FileName, results);

            SummaryText.Text = $"{withData} unit(s) exported to:\n{dialog.FileName}";
            Log($"Export completed: {dialog.FileName}");
            _notify?.Invoke($"Audio export complete — {withData} unit(s)", true);
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }
}
