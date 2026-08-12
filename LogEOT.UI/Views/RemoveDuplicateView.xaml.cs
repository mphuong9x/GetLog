using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace LogEOT.UI.Views;

public partial class RemoveDuplicateView : System.Windows.Controls.UserControl
{
    // Passing log: PASS_<12-char MAC>_<model>_<station>_<pc>_<yyyyMMddHHmmss>_<sn>.log
    private static readonly Regex MacRegex = new(@"^PASS_([0-9A-Fa-f]{12})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StampRegex = new(@"_(\d{14})(?=[_.]|$)", RegexOptions.Compiled);

    private const string DuplicateFolderName = "_Duplicates";

    private readonly Action<string>? _logMessage;
    private readonly Action<string, bool>? _notify;

    public RemoveDuplicateView(Action<string>? logMessage = null, Action<string, bool>? notify = null)
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

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var inputDir = InputDirBox.Text?.Trim() ?? "";

        if (inputDir.Length == 0 || !Directory.Exists(inputDir))
        {
            Log("Please select a valid input folder.");
            return;
        }

        bool recursive = RecursiveCheckBox.IsChecked ?? false;

        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            Log($"Scanning PASS logs in: {inputDir}");

            var (passCount, dupMacs, duplicates) = await Task.Run(() => FindDuplicates(inputDir, recursive));

            Log($"PASS logs scanned: {passCount}. Duplicates: {duplicates.Count} from {dupMacs} MAC(s).");

            if (duplicates.Count == 0)
            {
                SummaryText.Text = passCount == 0
                    ? "No PASS log found. Make sure the filenames start with \"PASS_\"."
                    : $"No duplicate found — all {passCount} PASS log(s) have a unique MAC.";
                Log("No duplicate to remove.");
                return;
            }

            var dupDir = Path.Combine(inputDir, DuplicateFolderName);
            var answer = System.Windows.MessageBox.Show(
                $"{duplicates.Count} duplicate log(s) from {dupMacs} MAC(s) will be moved to:\n{dupDir}\n\nThe newest log of each MAC stays in place. Continue?",
                "Remove Duplicate",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                Log("Remove duplicate cancelled by user.");
                return;
            }

            var (moved, errors) = await Task.Run(() => MoveToDuplicates(inputDir, duplicates));

            foreach (var error in errors)
            {
                Log($"Could not move {error}");
            }

            SummaryText.Text = $"{moved} duplicate log(s) moved to:\n{dupDir}";
            Log($"Moved {moved} duplicate log(s). Kept {passCount - moved} PASS log(s) in place.");
            _notify?.Invoke($"Remove duplicate complete — {moved} log(s) moved", errors.Count == 0);
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

    private static (int PassCount, int DupMacs, List<string> Duplicates) FindDuplicates(string dir, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var parked = Path.Combine(dir, DuplicateFolderName) + Path.DirectorySeparatorChar;

        var groups = new Dictionary<string, List<(string Path, DateTime Stamp)>>(StringComparer.OrdinalIgnoreCase);
        int passCount = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*", option))
        {
            // Logs parked by an earlier run are already handled — re-scanning them would nest _Duplicates.
            if (file.StartsWith(parked, StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileName(file);
            var match = MacRegex.Match(name);
            if (!match.Success) continue;

            passCount++;

            var mac = match.Groups[1].Value;
            if (!groups.TryGetValue(mac, out var runs))
            {
                runs = new List<(string, DateTime)>();
                groups[mac] = runs;
            }
            runs.Add((file, StampOf(file, name)));
        }

        var duplicates = new List<string>();
        int dupMacs = 0;

        foreach (var runs in groups.Values)
        {
            if (runs.Count < 2) continue;
            dupMacs++;
            // The last run of a unit reflects how it left the line, so keep that one.
            duplicates.AddRange(runs.OrderByDescending(x => x.Stamp).Skip(1).Select(x => x.Path));
        }

        duplicates.Sort(StringComparer.OrdinalIgnoreCase);
        return (passCount, dupMacs, duplicates);
    }

    // Test time comes from the filename; fall back to the file clock when the name has no timestamp.
    private static DateTime StampOf(string path, string fileName)
    {
        var match = StampRegex.Match(fileName);
        if (match.Success &&
            DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
        {
            return stamp;
        }
        return File.GetLastWriteTime(path);
    }

    private static (int Moved, List<string> Errors) MoveToDuplicates(string root, List<string> files)
    {
        var dupDir = Path.Combine(root, DuplicateFolderName);
        int moved = 0;
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                // Mirror the subfolder layout so a log can be put back where it came from.
                var target = Path.Combine(dupDir, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: true);
                moved++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return (moved, errors);
    }
}
