using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using LogEOT.Core.Models;
using LogEOT.Infrastructure.Services;
using WinForms = System.Windows.Forms;

namespace LogEOT.UI.Views;

public partial class DownloadLogsView : System.Windows.Controls.UserControl
{
    private readonly Action<string>? _logMessage;

    public DownloadLogsView(Action<string>? logMessage = null)
    {
        InitializeComponent();
        _logMessage = logMessage;
    }

    private void Log(string message) => _logMessage?.Invoke(message);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WinForms.FolderBrowserDialog();
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            OutputDirBox.Text = dialog.SelectedPath;
        }
    }

    private void ListMacCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (MacFilePanel != null)
            MacFilePanel.Visibility = Visibility.Visible;
    }

    private void ListMacCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (MacFilePanel != null)
            MacFilePanel.Visibility = Visibility.Collapsed;
    }

    private void BrowseMac_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt",
            Title = "Select MAC List File"
        };
        if (dialog.ShowDialog() == true)
        {
            MacFileBox.Text = dialog.FileName;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var model = ModelTextBox.Text?.Trim();
        var mo = MoTextBox.Text?.Trim();
        var outputDir = OutputDirBox.Text?.Trim();
        var startDate = StartDatePicker.SelectedDate;
        var endDate = EndDatePicker.SelectedDate;
        bool pass = PassCheckBox.IsChecked ?? false;
        bool fail = FailCheckBox.IsChecked ?? false;

        var selectedServers = new List<string>();
        if (Server52CheckBox.IsChecked == true) selectedServers.Add("192.168.240.52");
        if (Server20CheckBox.IsChecked == true) selectedServers.Add("192.168.240.20");
        if (Server21CheckBox.IsChecked == true) selectedServers.Add("192.168.240.21");

        if (selectedServers.Count == 0)
        {
            Log("Please select at least one server.");
            return;
        }

        if (string.IsNullOrEmpty(model))
        {
            Log("Model is required for SFTP Download.");
            return;
        }

        if (string.IsNullOrEmpty(outputDir))
        {
            Log("Output directory is required.");
            return;
        }

        if (!pass && !fail)
        {
            Log("Please select at least PASS or FAIL logs.");
            return;
        }

        bool listMac = ListMacCheckBox.IsChecked ?? false;
        var macFile = MacFileBox.Text?.Trim();
        HashSet<string>? macSet = null;

        if (listMac)
        {
            if (string.IsNullOrEmpty(macFile) || !System.IO.File.Exists(macFile))
            {
                Log("Please select a valid MAC list file.");
                return;
            }

            try
            {
                macSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lines = System.IO.File.ReadAllLines(macFile);
                foreach (var line in lines)
                {
                    var mac = line.Trim();
                    if (mac.Length > 0)
                        macSet.Add(mac);
                }
                Log($"Loaded {macSet.Count} MACs from file.");
                if (macSet.Count == 0)
                {
                    Log("No valid MACs found in the selected file.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"Error reading MAC file: {ex.Message}");
                return;
            }
        }

        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            Log("Starting SFTP log download...");
            Log($"- Output Dir: {outputDir}");
            Log($"- Model: {model}");
            if (!string.IsNullOrEmpty(mo)) Log($"- MO: {mo}");
            if (startDate.HasValue) Log($"- Start Date: {startDate.Value:yyyy-MM-dd}");
            if (endDate.HasValue) Log($"- End Date: {endDate.Value:yyyy-MM-dd}");
            if (listMac && macSet != null) Log($"- MAC List: {macFile} ({macSet.Count} MACs)");

            DownloadProgress.Value = 0;
            ProgressText.Text = "0/0";

            var options = new ScanOptions
            {
                Model = model,
                MoFilter = mo,
                StartDate = startDate,
                EndDate = endDate,
                Pass = pass,
                Fail = fail,
                KeepRootFolder = KeepRootFolderCheckBox.IsChecked ?? false,
                MacList = macSet
            };

            var service = new SftpDownloadService();
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            await service.DownloadLogsAsync(selectedServers, outputDir, options,
            msg =>
            {
                dispatcher.BeginInvoke(() => Log(msg));
            },
            (done, total) =>
            {
                // Throttle: chỉ cập nhật UI mỗi ~1% (hoặc khi hoàn tất) để tránh dồn Dispatcher
                // khi tải rất nhiều file nhỏ từ tối đa 10 luồng song song.
                int step = total > 200 ? total / 100 : 1;
                if (done != total && done % step != 0) return;

                // BeginInvoke: không chặn luồng tải; Math.Max tránh thanh tiến độ giật lùi khi cập nhật out-of-order.
                dispatcher.BeginInvoke(() =>
                {
                    DownloadProgress.Maximum = total > 0 ? total : 1;
                    if (done == 0 || done >= DownloadProgress.Value)
                        DownloadProgress.Value = done;
                    ProgressText.Text = $"{done}/{total}";
                });
            });

            bool removeDup = RemoveDuplicateCheckBox.IsChecked ?? false;
            if (removeDup && !string.IsNullOrEmpty(outputDir) && System.IO.Directory.Exists(outputDir))
            {
                try
                {
                    Log("Scanning for duplicate logs...");
                    await Task.Run(() => RemoveDuplicateLogs(outputDir));
                }
                catch (Exception ex)
                {
                    Log($"Error removing duplicates: {ex.Message}");
                }
            }
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

    private void RemoveDuplicateLogs(string rootDir)
    {
        int removed = 0;
        try
        {
            var directories = new List<string>(System.IO.Directory.GetDirectories(rootDir, "*", System.IO.SearchOption.AllDirectories));
            directories.Insert(0, rootDir);

            foreach (var dir in directories)
            {
                var logDict = new Dictionary<string, string>();
                var files = System.IO.Directory.GetFiles(dir, "*.log", System.IO.SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    string prefix = fileName.Length >= 17 ? fileName.Substring(0, 17) : fileName;

                    if (!logDict.ContainsKey(prefix))
                    {
                        logDict[prefix] = file;
                    }
                    else
                    {
                        try
                        {
                            System.IO.File.Delete(file);
                            removed++;
                        }
                        catch { }
                    }
                }
            }

            if (removed > 0)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    Log($"Removed {removed} duplicate log file(s)."));
            }
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                Log($"Error removing duplicates: {ex.Message}"));
        }
    }
}
