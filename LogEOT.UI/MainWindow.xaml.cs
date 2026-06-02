using LogEOT.Core.Models;
using LogEOT.Infrastructure.Exporters;
using LogEOT.Infrastructure.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace LogEOT.UI;

public partial class MainWindow : Window
{
    public ObservableCollection<KeyConfig> Keys { get; set; } = new();

    private List<LogResult>? _results;
    private Views.AnalyzeLogsView? _analyzeView;
    private Views.DownloadLogsView? _downloadView;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;
        MainContent.Content = new Views.WelcomeView();
        LogMessage("Application started. Please select a function to begin.");
    }

    public void LogMessage(string message)
    {
        ResultLogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        ResultLogBox.ScrollToEnd();
    }

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.AnalyzeLogsView)
        {
            if (_analyzeView == null)
            {
                _analyzeView = new Views.AnalyzeLogsView(LogMessage);
                _analyzeView.RunRequested += async (s, ev) => await RunAnalysisAndExportAsync();
            }
            MainContent.Content = _analyzeView;
        }
    }

    private async Task RunAnalysisAndExportAsync()
    {
        var view = (Views.AnalyzeLogsView)MainContent.Content;
        var folder = view.FolderTextBox.Text;

        if (string.IsNullOrWhiteSpace(folder))
        {
            LogMessage("Error: Please select a folder.");
            return;
        }

        var keys = GetActiveKeys();
        bool isAudioLog = view.IsAudioLog;

        if (!isAudioLog && keys.Count == 0)
        {
            LogMessage("Error: Please input at least one key.");
            return;
        }

        try
        {
            LogMessage($"Starting analysis in folder: {folder}");
            if (isAudioLog)
                LogMessage("Mode: Audio Logs Analysis");
            else
                LogMessage($"Search Keys count: {keys.Count}");

            var service = new LogProcessingService();

            _results = await Task.Run(() =>
                service.ProcessFolder(folder, keys, isAudioLog)
            );

            LogMessage($"Analyze finished successfully. Logs processed: {_results.Count}");

            if (_results is null or { Count: 0 })
            {
                LogMessage("Error: No data to export.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel File|*.xlsx",
                FileName = isAudioLog ? "Audio_Log_Result.xlsx" : "LogEOT_Result.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                LogMessage("Export cancelled by user.");
                return;
            }

            LogMessage($"Exporting to {dialog.FileName}...");
            var exporter = new ExcelExporter();
            
            if (isAudioLog)
            {
                exporter.ExportAudio(dialog.FileName, _results);
            }
            else
            {
                var exportKeys = GetActiveKeys();
                exporter.Export(dialog.FileName, _results, exportKeys);
            }

            LogMessage("Export completed successfully.");
        }
        catch (Exception ex)
        {
            LogMessage($"Error during operation: {ex.Message}");
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        MainContent.Content = new Views.WelcomeView();
        LogMessage("Returned to Home screen.");
    }

    private List<(string Key, string AltKey, string ColumnName)> GetActiveKeys()
    {
        return Keys
            .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => (Key: x.Key, AltKey: x.AltKey ?? "", ColumnName: string.IsNullOrWhiteSpace(x.ColumnName) ? x.Key : x.ColumnName))
            .ToList();
    }

    private void Nav_Pending_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.DownloadLogsView)
        {
            _downloadView ??= new Views.DownloadLogsView(LogMessage);
            MainContent.Content = _downloadView;
        }
    }
}