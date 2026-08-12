using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace LogEOT.UI.Views;

public partial class CompareMacView : System.Windows.Controls.UserControl
{
    // 12-char MAC that follows "PASS_" in a filename, e.g. PASS_74FA29779A0B_...
    // Case-insensitive: a missed filename would be reported as a MAC the other folder lacks.
    private static readonly Regex MacRegex = new(@"PASS_([0-9A-Fa-f]{12})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Long diffs stay readable on screen; the saved report always holds every MAC.
    private const int PreviewLimit = 20;

    private readonly Action<string>? _logMessage;
    private readonly Action<string, bool>? _notify;

    public CompareMacView(Action<string>? logMessage = null, Action<string, bool>? notify = null)
    {
        InitializeComponent();
        _logMessage = logMessage;
        _notify = notify;
    }

    private void Log(string message) => _logMessage?.Invoke(message);

    private void BrowseA_Click(object sender, RoutedEventArgs e) => Browse(FolderABox);

    private void BrowseB_Click(object sender, RoutedEventArgs e) => Browse(FolderBBox);

    private static void Browse(System.Windows.Controls.TextBox target)
    {
        var dialog = new WinForms.FolderBrowserDialog();
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        var dirA = FolderABox.Text?.Trim() ?? "";
        var dirB = FolderBBox.Text?.Trim() ?? "";

        if (dirA.Length == 0 || !Directory.Exists(dirA))
        {
            Log("Please select a valid folder A.");
            return;
        }

        if (dirB.Length == 0 || !Directory.Exists(dirB))
        {
            Log("Please select a valid folder B.");
            return;
        }

        bool recursive = RecursiveCheckBox.IsChecked ?? false;

        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            Log($"Comparing MACs — A: {dirA} | B: {dirB}");

            var macsA = await Task.Run(() => CollectMacs(dirA, recursive));
            var macsB = await Task.Run(() => CollectMacs(dirB, recursive));

            var onlyA = Missing(macsA, macsB);
            var onlyB = Missing(macsB, macsA);

            Log($"Folder A: {macsA.Count} MAC. Folder B: {macsB.Count} MAC. Only in A: {onlyA.Count}. Only in B: {onlyB.Count}.");

            if (macsA.Count == 0 && macsB.Count == 0)
            {
                SummaryText.Text = "No MAC found in either folder. Make sure the filenames start with \"PASS_\".";
                Log("No MAC found.");
                return;
            }

            SummaryText.Text = $"Folder A: {macsA.Count} MAC · Folder B: {macsB.Count} MAC · in both: {macsA.Count - onlyA.Count}\n"
                             + Describe("Only in A", onlyA) + "\n"
                             + Describe("Only in B", onlyB);

            if (onlyA.Count == 0 && onlyB.Count == 0)
            {
                Log("No difference — both folders hold the same MACs.");
                _notify?.Invoke("Compare MAC complete — no difference", true);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text File|*.txt",
                FileName = "MAC_compare.txt",
                InitialDirectory = dirA
            };

            if (dialog.ShowDialog() != true)
            {
                Log("Save cancelled by user — result is shown above.");
                return;
            }

            File.WriteAllText(dialog.FileName, BuildReport(dirA, dirB, macsA, macsB, onlyA, onlyB));

            Log($"Saved compare report to: {dialog.FileName}");
            _notify?.Invoke($"Compare MAC complete — {onlyA.Count + onlyB.Count} MAC(s) differ", true);
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

    private static HashSet<string> CollectMacs(string dir, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var macs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(dir, "*", option))
        {
            var match = MacRegex.Match(Path.GetFileName(file));
            if (match.Success)
            {
                macs.Add(match.Groups[1].Value);
            }
        }

        return macs;
    }

    private static List<string> Missing(HashSet<string> source, HashSet<string> other) =>
        source.Where(mac => !other.Contains(mac))
              .OrderBy(mac => mac, StringComparer.OrdinalIgnoreCase)
              .ToList();

    private static string Describe(string label, List<string> macs)
    {
        if (macs.Count == 0) return $"{label}: none";

        var shown = string.Join(", ", macs.Take(PreviewLimit));
        return macs.Count > PreviewLimit
            ? $"{label} ({macs.Count}): {shown} … +{macs.Count - PreviewLimit} more"
            : $"{label} ({macs.Count}): {shown}";
    }

    private static string BuildReport(string dirA, string dirB, HashSet<string> macsA, HashSet<string> macsB, List<string> onlyA, List<string> onlyB)
    {
        var report = new StringBuilder();
        report.AppendLine("MAC comparison");
        report.AppendLine($"Folder A: {dirA} ({macsA.Count} MAC)");
        report.AppendLine($"Folder B: {dirB} ({macsB.Count} MAC)");
        report.AppendLine($"In both: {macsA.Count - onlyA.Count}");

        report.AppendLine();
        report.AppendLine($"Only in A ({onlyA.Count}):");
        foreach (var mac in onlyA) report.AppendLine(mac);

        report.AppendLine();
        report.AppendLine($"Only in B ({onlyB.Count}):");
        foreach (var mac in onlyB) report.AppendLine(mac);

        return report.ToString();
    }
}
