using System.Reflection;
using System.Text.Json;
using MProject.Application.Models;
using MProject.Application.Services;
using MProject.Domain.Enums;
using MProject.Infrastructure;
using MProjectAgent.Configuration;
using MProjectAgent.Tests;

const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
var results = new List<object>();

// Exercise the actual scanner through its existing temporary-directory fixture.
using (var fixture = new TestResultScannerTests())
{
    var scanner = Field<MProjectAgent.Services.TestResultScanner>(fixture, "_scanner");
    var options = Field<TestLogOptions>(fixture, "_options");
    options.MaxRecordsPerBatch = 2;
    var root = fixture.GetType().GetProperty("Root", Private)!.GetValue(fixture);
    var write = fixture.GetType().GetMethod("WriteLog", Private)!;
    var collect = scanner.GetType().GetMethod("CollectBatch", Private)!;
    var cursorType = scanner.GetType().GetNestedType("TestResultCursor", BindingFlags.NonPublic)!;
    object cursor = cursorType.GetProperty("Empty")!.GetValue(null)!;
    var t = DateTimeOffset.UtcNow.AddHours(-1);
    void Log(string name, DateTimeOffset stamp) => write.Invoke(fixture, [name, DateTime.Today, stamp]);
    object Batch() => collect.Invoke(scanner, [root, cursor, CancellationToken.None])!;
    List<MProjectAgent.Models.TestResultFileReport> Files(object batch) => (List<MProjectAgent.Models.TestResultFileReport>)batch.GetType().GetProperty("Files")!.GetValue(batch)!;
    void Advance(object batch) => cursor = batch.GetType().GetProperty("NextCursor")!.GetValue(batch)!;
    Log("b.log", t); Log("c.log", t); Advance(Batch());
    Log("a-late.log", t);
    var seenLate = false;
    for (var i = 1; i <= 4; i++)
    {
        Log($"fresh-{i}-1.log", t.AddMinutes(i * 5));
        Log($"fresh-{i}-2.log", t.AddMinutes(i * 5));
        var batch = Batch();
        seenLate |= Files(batch).Any(f => f.RelativePath.EndsWith("a-late.log"));
        Advance(batch);
    }
    var idle = Batch();
    var lateAfterIdle = Files(idle).Any(f => f.RelativePath.EndsWith("a-late.log"));
    if (seenLate || lateAfterIdle) throw new Exception("Starvation reproduction changed.");
    results.Add(new { probe = "late-file-full-batches", freshCycles = 4, seenLate, lateAfterIdle, observed = "Late file aged out of 15-minute overlap before idle replay." });
}

using (var fixture = new MProject.Tests.AgentServiceTests())
{
    await fixture.ReportTestMetrics_UpsertsCounterPerSlot();
    var service = Field<AgentService>(fixture, "_service");
    var context = Field<AppDbContext>(fixture, "_context");
    var agent = context.Agents.Single();
    var now = DateTimeOffset.UtcNow;
    await service.ReportTestMetricsAsync(agent.Id, new ReportTestMetricsRequest { CollectedAt = now, Slots = [new() { SlotIndex = 0, PassCount = 200 }] });
    await service.ReportTestMetricsAsync(agent.Id, new ReportTestMetricsRequest { CollectedAt = now.AddMinutes(-5), Slots = [new() { SlotIndex = 0, PassCount = 100 }] });
    var counter = context.StationTestCounters.Single();
    if (counter.PassCount != 100) throw new Exception("Stale metrics reproduction changed.");
    results.Add(new { probe = "out-of-order-metrics", latestSent = 200, stored = counter.PassCount, counter.CollectedAt });

    const string path = "MDL/FT2/MO1/PC-01/2026-09-09/FAIL_AABB_MDL_FT2_PC-01_20260909080000_MO1_RF_LOW.log";
    var noSfc = await service.ReportTestResultsAsync(agent.Id, new() { Files = [new() { Source = TestLogSource.NoSfc, RelativePath = path, FileModifiedAt = now }] });
    var sfis = await service.ReportTestResultsAsync(agent.Id, new() { Files = [new() { Source = TestLogSource.Sfis, RelativePath = path, FileModifiedAt = now }] });
    var row = context.TestResultRecords.Single();
    if (sfis.Accepted != 0 || row.Source != TestLogSource.NoSfc) throw new Exception("Cross-source reproduction changed.");
    results.Add(new { probe = "cross-source-basename", firstAccepted = noSfc.Accepted, secondAccepted = sfis.Accepted, sfis.Duplicates, storedSource = row.Source.ToString() });
    results.Add(new { probe = "mtime-as-tested-time", filenameTime = "2026-09-09 08:00:00 (offset unspecified)", fileModifiedAt = now, storedTestedAt = row.TestedAt, parsed = row.ParseStatus.ToString() });
}

var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.Length > 0) File.WriteAllText(args[0], json);
