using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace LogEOT.UI.Views;

public partial class GetMacView : System.Windows.Controls.UserControl
{
    // 12-char MAC that follows "PASS_" in a filename, e.g. PASS_74FA29779A0B_...
    private static readonly Regex MacRegex = new(@"PASS_([0-9A-Fa-f]{12})", RegexOptions.Compiled);

    private readonly Action<string>? _logMessage;
    private readonly Action<string, bool>? _notify;

    public GetMacView(Action<string>? logMessage = null, Action<string, bool>? notify = null)
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

    private async void GetMac_Click(object sender, RoutedEventArgs e)
    {
        var inputDir = InputDirBox.Text?.Trim();

        if (string.IsNullOrEmpty(inputDir) || !System.IO.Directory.Exists(inputDir))
        {
            Log("Please select a valid input folder.");
            return;
        }

        bool recursive = RecursiveCheckBox.IsChecked ?? false;
        bool removeDup = RemoveDuplicateCheckBox.IsChecked ?? false;

        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            Log($"Scanning MACs in: {inputDir}");

            var (macs, fileCount, duplicates) = await Task.Run(() => ExtractMacs(inputDir, recursive, removeDup));

            Log($"Files scanned: {fileCount}. MACs found: {macs.Count}" + (duplicates > 0 ? $" (skipped {duplicates} duplicate(s))." : "."));

            if (macs.Count == 0)
            {
                SummaryText.Text = "No MAC found. Make sure the filenames start with \"PASS_\".";
                Log("No MAC found.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text File|*.txt",
                FileName = "MAC_list.txt",
                InitialDirectory = inputDir
            };

            if (dialog.ShowDialog() != true)
            {
                Log("Save cancelled by user.");
                return;
            }

            System.IO.File.WriteAllLines(dialog.FileName, macs);

            SummaryText.Text = $"{macs.Count} MAC(s) saved to:\n{dialog.FileName}";
            Log($"Saved {macs.Count} MAC(s) to: {dialog.FileName}");
            _notify?.Invoke($"Get MAC complete — {macs.Count} MAC(s) saved", true);
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

    private static (List<string> Macs, int FileCount, int Duplicates) ExtractMacs(string dir, bool recursive, bool removeDuplicate)
    {
        var option = recursive ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly;
        var files = System.IO.Directory.EnumerateFiles(dir, "*", option);

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fileCount = 0;
        int duplicates = 0;

        foreach (var file in files)
        {
            fileCount++;
            var match = MacRegex.Match(System.IO.Path.GetFileName(file));
            if (!match.Success) continue;

            var mac = match.Groups[1].Value;
            if (removeDuplicate && !seen.Add(mac))
            {
                duplicates++;
                continue;
            }
            result.Add(mac);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return (result, fileCount, duplicates);
    }
}
