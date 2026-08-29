using LogEOT.Core.Models;
using LogEOT.Infrastructure.Exporters;
using LogEOT.Infrastructure.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LogEOT.UI;

public partial class MainWindow : Window
{
    public ObservableCollection<KeyConfig> Keys { get; set; } = new();

    private List<LogResult>? _results;
    private Views.AnalyzeLogsView? _analyzeView;
    private Views.DownloadLogsView? _downloadView;
    private Views.GetMacView? _getMacView;
    private Views.AudioLogView? _audioLogView;
    private Views.RemoveDuplicateView? _removeDuplicateView;
    private Views.CompareMacView? _compareMacView;
    private DispatcherTimer? _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        ResetAnalyzeKeys();
        DataContext = this;
        MainContent.Content = new Views.WelcomeView();
        LogMessage("Application started. Please select a function to begin.");
    }

    public void LogMessage(string message)
    {
        ResultLogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        ResultLogBox.ScrollToEnd();
    }

    // Prominent colored banner over the log box to announce task completion.
    public void ShowStatus(string message, bool success = true)
    {
        StatusText.Text = (success ? "✔  " : "✖  ") + message;
        StatusBanner.Background = new SolidColorBrush(HexColor(success ? "#DCFCE7" : "#FEE2E2"));
        StatusText.Foreground = new SolidColorBrush(HexColor(success ? "#166534" : "#991B1B"));
        StatusBanner.Visibility = Visibility.Visible;

        if (_statusTimer == null)
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer!.Stop();
                StatusBanner.Visibility = Visibility.Collapsed;
            };
        }
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static System.Windows.Media.Color HexColor(string hex) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.AnalyzeLogsView)
        {
            if (_analyzeView == null)
            {
                _analyzeView = new Views.AnalyzeLogsView(LogMessage);
                _analyzeView.RunRequested += async (s, ev) => await RunAnalysisAndExportAsync();
                _analyzeView.ResetRequested += (s, ev) =>
                {
                    ResetAnalyzeKeys();
                    LogMessage("Search table reset.");
                };
            }
            MainContent.Content = _analyzeView;
        }
    }

    private async Task RunAnalysisAndExportAsync()
    {
        var view = (Views.AnalyzeLogsView)MainContent.Content;
        var folder = view.FolderTextBox.Text;
        var selection = view.SelectedLogSelection;

        if (string.IsNullOrWhiteSpace(folder))
        {
            LogMessage("Error: Please select a folder.");
            return;
        }

        var keys = GetActiveKeys();

        if (keys.Count == 0)
        {
            LogMessage("Error: Please input at least one key.");
            return;
        }

        try
        {
            LogMessage($"Starting analysis in folder: {folder}");
            LogMessage($"Log type: {selection}");
            LogMessage($"Search Keys count: {keys.Count}");

            var service = new LogProcessingService();

            _results = await Task.Run(() => service.ProcessFolder(folder, keys, selection: selection));

            LogMessage($"Analyze finished successfully. Logs processed: {_results.Count}");

            if (_results is null or { Count: 0 })
            {
                LogMessage("Error: No data to export.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel File|*.xlsx",
                FileName = "LogEOT_Result.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                LogMessage("Export cancelled by user.");
                return;
            }

            LogMessage($"Exporting to {dialog.FileName}...");
            var exporter = new ExcelExporter();
            var exportKeys = GetActiveKeys();
            exporter.Export(dialog.FileName, _results, exportKeys);

            LogMessage("Export completed successfully.");
            ShowStatus($"Export complete — {_results.Count} log(s) processed");
        }
        catch (Exception ex)
        {
            LogMessage($"Error during operation: {ex.Message}");
        }
    }

    private void GetMac_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.GetMacView)
        {
            _getMacView ??= new Views.GetMacView(LogMessage, ShowStatus);
            MainContent.Content = _getMacView;
        }
    }

    private void AudioLog_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.AudioLogView)
        {
            _audioLogView ??= new Views.AudioLogView(LogMessage, ShowStatus);
            MainContent.Content = _audioLogView;
        }
    }

    private void RemoveDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.RemoveDuplicateView)
        {
            _removeDuplicateView ??= new Views.RemoveDuplicateView(LogMessage, ShowStatus);
            MainContent.Content = _removeDuplicateView;
        }
    }

    private void CompareMac_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.CompareMacView)
        {
            _compareMacView ??= new Views.CompareMacView(LogMessage, ShowStatus);
            MainContent.Content = _compareMacView;
        }
    }

    private List<(string Key, string AltKey, string ColumnName)> GetActiveKeys()
    {
        return Keys
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => (Key: x.Key, AltKey: x.AltKey ?? "", ColumnName: string.IsNullOrWhiteSpace(x.ColumnName) ? x.Key : x.ColumnName))
            .ToList();
    }

    private void ResetAnalyzeKeys()
    {
        Keys.Clear();
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        MainContent.Content = new Views.WelcomeView();
    }

    private void Nav_Pending_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is not Views.DownloadLogsView)
        {
            _downloadView ??= new Views.DownloadLogsView(LogMessage, ShowStatus);
            MainContent.Content = _downloadView;
        }
    }
}
