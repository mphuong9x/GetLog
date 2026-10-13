# 07 — Probes tái hiện production-support durable workflow

Ngày: 2026-09-09. Revision review: `db480dffe3372b72028848eaa62fbb47d7b141b7`.
Báo cáo chính: [07-production-support-durable-workflow-report.md](07-production-support-durable-workflow-report.md).

Các probe dưới đây xác nhận hành vi lỗi hiện tại. Assertion thành công nghĩa là đã tái hiện finding; không có nghĩa implementation đạt acceptance criteria sau sửa. Tất cả dữ liệu là giả. Không đọc agent-state, không gọi server thật, không dùng database production.

## C# / SQLite

Tạo thư mục `MProjectAgent.Tests/obj/review07-probes` tại root repository. Lưu hai block tương ứng thành `Review07.csproj` và `Program.cs`, sau đó chạy từ root:

```powershell
dotnet run --project MProjectAgent.Tests/obj/review07-probes/Review07.csproj
```

Dùng package cache/restore phù hợp môi trường; phiên review dùng NuGet cache local có sẵn. Assembly name của harness là `MProjectAgent.Tests` để sử dụng InternalsVisibleTo hiện có. Harness reference project Agent thật; không chứa bản sao implementation của store/service. State giả được tạo bên dưới output harness; không trỏ tới ProgramData.

Các scenario: `retention` (R07-04, có control prune không reconcile), `failed-resolved` và `failed-predecessor` (R07-03), `force-resolved-pending` (R07-05), `stale-modal` (R07-02), `startup-schema` (R07-01), `stale-sync` (R07-09), `inflight-without-lease` (R07-06). Probe lease dùng SQLite trigger để inject lỗi ghi transition, sau đó gỡ trigger và tăng clock hai ngày. Fake API ở scenario remote closure mô phỏng 409 không có receipt theo nhánh server đã đối chiếu.

`Review07.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>MProjectAgent.Tests</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../../../MProjectAgent/MProjectAgent.csproj" /></ItemGroup>
</Project>

```

Program.cs:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MProject.ProductionSupport.Contracts;
using MProjectAgent.Configuration;
using MProjectAgent.Ipc.Contracts;
using MProjectAgent.Support;

var probeRoot = Path.Combine(AppContext.BaseDirectory, "synthetic-state", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(probeRoot);
var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

async Task Run(string name, Func<Fixture, Task> action)
{
    var fixture = new Fixture(Path.Combine(probeRoot, name), now);
    await action(fixture);
}

await Run("retention", async f =>
{
    await f.Store.ApplyReconcileAsync(f.ServerRequest, f.Time.Now);
    await f.Store.ApplyReconcileAsync(null, f.Time.Now);
    f.Time.Now += TimeSpan.FromDays(31);
    var api = new FakeApi(); // current remains absent throughout the production cycle
    var delivery = f.Delivery(api);
    await delivery.InitializeAsync(CancellationToken.None);
    var remaining = await f.Store.GetOperationsAsync();
    Check(remaining.Count == 1, "R07 retention: terminal closed graph still present after 31 days and startup reconcile");
    f.Time.Now += TimeSpan.FromDays(31);
    var directPrune = await f.Store.PruneClosedTerminalGraphsAsync(f.Time.Now - TimeSpan.FromDays(30));
    Check(directPrune == 1, "R07 retention control: direct prune without preceding reconcile deletes the graph");
});

await Run("failed-resolved", async f =>
{
    await f.Store.ApplyReconcileAsync(f.ServerRequest, f.Time.Now);
    var first = await f.Service.UpdateAsync(Input(SupportRequestStatuses.Resolved, "bad-user"), CancellationToken.None);
    Check(first.IsSuccess, "R07 failed-resolved: original local update accepted");
    await f.Store.TryClaimNextAsync(f.Time.Now);
    await f.Store.RecordDeliveryFailureAsync(first.Response!.Receipt.OperationId,
        SupportOutboxState.NeedsUserAction, ProductionSupportErrorCodes.EmployeeInvalid);
    var corrected = await f.Service.UpdateAsync(Input(SupportRequestStatuses.Resolved, "te-valid"), CancellationToken.None);
    Check(!corrected.IsSuccess && corrected.ErrorCode == IpcErrorCodes.InvalidArgument,
        "R07 failed-resolved: corrected employee rejected before supersede; failed operation remains stuck");
});

await Run("failed-predecessor", async f =>
{
    await f.Store.ApplyReconcileAsync(f.ServerRequest, f.Time.Now);
    var first = await f.Service.UpdateAsync(Input(SupportRequestStatuses.InProgress, "bad-user"), CancellationToken.None);
    await f.Service.UpdateAsync(Input(SupportRequestStatuses.InProgress, "te-valid"), CancellationToken.None);
    await f.Store.TryClaimNextAsync(f.Time.Now);
    await f.Store.RecordDeliveryFailureAsync(first.Response!.Receipt.OperationId,
        SupportOutboxState.NeedsUserAction, ProductionSupportErrorCodes.EmployeeInvalid);
    var corrected = await f.Service.UpdateAsync(Input(SupportRequestStatuses.InProgress, "te-valid"), CancellationToken.None);
    var operations = await f.Store.GetOperationsAsync();
    Check(corrected.IsSuccess && operations.Single(o => o.OperationId == first.Response.Receipt.OperationId).State == SupportOutboxState.NeedsUserAction
        && await f.Store.TryClaimNextAsync(f.Time.Now) is null,
        "R07 failed-predecessor: correction acknowledged but appended behind blocked child; nothing dispatchable");
});

await Run("force-resolved-pending", async f =>
{
    await f.Store.ApplyReconcileAsync(f.ServerRequest, f.Time.Now);
    var local = await f.Service.UpdateAsync(Input(SupportRequestStatuses.InProgress, "te-valid"), CancellationToken.None);
    var api = new FakeApi { RejectUpdate = true };
    var delivery = f.Delivery(api);
    await delivery.InitializeAsync(CancellationToken.None);
    await delivery.RunCycleAsync(CancellationToken.None);
    f.Time.Now += TimeSpan.FromMinutes(10);
    await delivery.RunCycleAsync(CancellationToken.None);
    var snapshot = await f.Store.GetRuntimeSnapshotAsync();
    Check(snapshot.CurrentRequest is null && api.UpdateCalls == 2
        && snapshot.Operations.Single(o => o.OperationId == local.Response!.Receipt.OperationId).State == SupportOutboxState.RetryScheduled,
        "R07 remote closure: no current request but obsolete update repeats request_not_current indefinitely");
});

await Run("stale-modal", async f =>
{
    await f.Store.ApplyReconcileAsync(f.ServerRequest, f.Time.Now);
    var dialogSnapshot = (await f.Store.GetRuntimeSnapshotAsync()).CurrentRequest!;
    var newId = Guid.NewGuid();
    await f.Store.ApplyReconcileAsync(null, f.Time.Now);
    await f.Store.ApplyReconcileAsync(new ProductionSupportRequestDto
    {
        Id = newId, ComputerId = f.ServerRequest.ComputerId, Status = ProductionSupportRequestStatus.Open,
        ReporterUsername = "synthetic-pd", ReportedAtUtc = f.Time.Now
    }, f.Time.Now);
    // The current IPC DTO has no request identity field, exactly as Launcher sends it.
    var submitOldDialog = await f.Service.UpdateAsync(Input(SupportRequestStatuses.Resolved, "te-valid"), CancellationToken.None);
    Check(submitOldDialog.IsSuccess && submitOldDialog.Response!.Receipt.CanonicalRequestId == newId
        && dialogSnapshot.CanonicalRequestId != newId,
        "R07 stale modal: update prepared for A durably targets new request B");
});

await Run("startup-schema", async f =>
{
    Directory.CreateDirectory(Path.GetDirectoryName(f.Store.DatabasePath)!);
    await using (var db = new SqliteConnection("Data Source=" + f.Store.DatabasePath))
    {
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "PRAGMA user_version=999;";
        await command.ExecuteNonQueryAsync();
    }
    using var worker = new ProductionSupportDeliveryWorker(f.Delivery(new FakeApi()), new(),
        Options.Create(new SupportOutboxOptions()), NullLogger<ProductionSupportDeliveryWorker>.Instance);
    var observed = false;
    try
    {
        await worker.StartAsync(CancellationToken.None);
        if (worker.ExecuteTask is not null) await worker.ExecuteTask;
    }
    catch (SupportOutboxCorruptionException) { observed = true; }
    Check(observed, "R07 startup: unsupported local schema escapes BackgroundService initialization");
});

await Run("stale-sync", async f =>
{
    await f.Store.ApplyReconcileAsync(null, f.Time.Now);
    await f.Store.RecordReconcileFailureAsync(ProductionSupportErrorCodes.TemporarilyUnavailable);
    var status = await f.Service.GetStatusAsync(CancellationToken.None);
    Check(status.SyncState == SupportSyncStates.Synced && status.SafeLastErrorCode is not null,
        "R07 status: failed reconcile with empty queue still reports Synced");
});

await Run("inflight-without-lease", async f =>
{
    await f.Store.ApplyReconcileAsync(f.ServerRequest, f.Time.Now);
    var local = await f.Service.UpdateAsync(Input(SupportRequestStatuses.InProgress, "te-valid"), CancellationToken.None);
    var api = new FakeApi
    {
        BeforeUpdate = async () =>
        {
            await Sql(f.Store.DatabasePath, "CREATE TRIGGER review07_block BEFORE UPDATE ON SupportOutboxOperations WHEN OLD.State='InFlight' BEGIN SELECT RAISE(ABORT, 'synthetic unavailable write'); END;");
            throw new IOException("synthetic transport failure while local writes are unavailable");
        }
    };
    var delivery = f.Delivery(api);
    try { await delivery.RunCycleAsync(CancellationToken.None); }
    catch (IOException) { }
    await Sql(f.Store.DatabasePath, "DROP TRIGGER review07_block;");
    f.Time.Now += TimeSpan.FromDays(2);
    await delivery.RunCycleAsync(CancellationToken.None);
    var row = (await f.Store.GetOperationsAsync()).Single(o => o.OperationId == local.Response!.Receipt.OperationId);
    Check(row.State == SupportOutboxState.InFlight && api.UpdateCalls == 1,
        "R07 lease: failed delivery and failed retry persistence leave InFlight for two days after storage recovers");
});

SqliteConnection.ClearAllPools();
Console.WriteLine("All synthetic observations confirmed. No live station/server state used.");

static SupportUpdateRequestDto Input(string status, string username) => new()
{ Status = status, EmployeeUsername = username, Note = "synthetic review update" };
static void Check(bool condition, string label)
{
    if (!condition) throw new Exception("Observation did not reproduce: " + label);
    Console.WriteLine(label);
}
static async Task Sql(string path, string text)
{
    await using var connection = new SqliteConnection("Data Source=" + path);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = text;
    await command.ExecuteNonQueryAsync();
}

sealed class MutableTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
sealed class EmptyPreview : IProductionSupportPreviewService
{
    public Task<SupportPreviewResponseDto> CreateAsync(CancellationToken ct) => Task.FromResult(new SupportPreviewResponseDto());
    public bool TryGet(string? id, out SupportPreviewResponseDto preview) { preview = new(); return false; }
}
sealed class Fixture
{
    public MutableTime Time { get; }
    public SupportOutboxStore Store { get; }
    public ProductionSupportAgentService Service { get; }
    public ProductionSupportRequestDto ServerRequest { get; }
    public Fixture(string root, DateTimeOffset now)
    {
        Time = new(now);
        Store = new(Options.Create(new AgentOptions { StateDirectory = root }),
            Options.Create(new SupportOutboxOptions()), Time, NullLogger<SupportOutboxStore>.Instance);
        Service = new(new EmptyPreview(), Store, new(), Time, Options.Create(new AgentOptions { ServerUrl = "https://synthetic.example/" }));
        ServerRequest = new() { Id = Guid.NewGuid(), ComputerId = Guid.NewGuid(),
            Status = ProductionSupportRequestStatus.Open, ReporterUsername = "synthetic-pd", ReportedAtUtc = now };
    }
    public ProductionSupportDeliveryService Delivery(FakeApi api) => new(Store, api, Time,
        Options.Create(new SupportOutboxOptions()), () => 0.5, NullLogger<ProductionSupportDeliveryService>.Instance);
}
sealed class FakeApi : IProductionSupportApiClient
{
    public Func<Task>? BeforeUpdate { get; init; }
    public bool RejectUpdate { get; init; }
    public int UpdateCalls { get; private set; }
    public Task<ProductionSupportApiResult<ProductionSupportCurrentResponse>> GetCurrentAsync(CancellationToken ct)
        => Task.FromResult(ProductionSupportApiResult<ProductionSupportCurrentResponse>.Success(new()));
    public Task<ProductionSupportApiResult<ProductionSupportCreateResponse>> CreateAsync(ProductionSupportCreateRequest request, CancellationToken ct)
        => throw new NotSupportedException();
    public async Task<ProductionSupportApiResult<ProductionSupportUpdateResponse>> UpdateAsync(Guid id, ProductionSupportUpdateRequest request, CancellationToken ct)
    {
        UpdateCalls++;
        if (BeforeUpdate is not null) await BeforeUpdate();
        if (!RejectUpdate) throw new NotSupportedException();
        return ProductionSupportApiResult<ProductionSupportUpdateResponse>.Failed(new(
            ProductionSupportErrorCodes.RequestNotCurrent, System.Net.HttpStatusCode.Conflict, true,
            ProductionSupportClientDisposition.Reconcile, ProductionSupportApiFailureKind.Http));
    }
}

```
## React / active query observers

Lưu hai block dưới thành `MProjectFrontend/review07-probes.test.tsx` và `MProjectFrontend/review07-page-probe.test.tsx`, rồi chạy từ `MProjectFrontend`:

```powershell
node node_modules/vitest/vitest.mjs run review07-probes.test.tsx review07-page-probe.test.tsx
```

Hai test đầu dùng component isolation và useSupportList thật, chỉ mock auth/API. Chúng kiểm DOM và số request sau khi cache đã được purge, không chỉ kiểm cache entry. Test trang dùng page thật với query state lỗi giả để kiểm empty message và alert. Ba test đã tái hiện R07-07/R07-08; warning jsdom về pseudo-element không làm test fail.

`review07-probes.test.tsx`:
```tsx
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, expect, it, vi } from 'vitest';
import SupportQueryIsolation from './src/components/auth/SupportQueryIsolation';
import { useSupportList } from './src/hooks/api/support/use-production-support';
import { AUTH_TOKEN_CHANGED_EVENT } from './src/utils/token';

let auth = { isLoggedIn: true, me: { userId: 'synthetic-user', activeTeamIds: ['old-team'] } };
vi.mock('./src/hooks/use-auth', () => ({ default: () => auth }));
const getList = vi.fn(async () => ({ items: [{ computerName: 'old-team-data' }], total: 1 }));
vi.mock('./src/api/productionSupport', () => ({ productionSupportApi: { getList: (...args: unknown[]) => getList(...args) } }));
afterEach(() => {
  cleanup();
  auth = { isLoggedIn: true, me: { userId: 'synthetic-user', activeTeamIds: ['old-team'] } };
  getList.mockClear();
});
function ActiveSupportView() {
  const query = useSupportList(auth.me.userId, {});
  return <div>{query.data?.items[0]?.computerName ?? 'no-data'}</div>;
}
function View({ client }: { client: QueryClient }) {
  return <QueryClientProvider client={client}><SupportQueryIsolation /><ActiveSupportView /></QueryClientProvider>;
}
it('R07: token boundary clears cache but leaves an active support view showing the removed data', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<View client={client} />);
  await screen.findByText('old-team-data');
  act(() => window.dispatchEvent(new Event(AUTH_TOKEN_CHANGED_EVENT)));
  await waitFor(() => expect(client.getQueryCache().getAll()).toHaveLength(0));
  expect(screen.getByText('old-team-data')).toBeTruthy();
  expect(getList).toHaveBeenCalledTimes(1);
  client.clear();
});
it('R07: changing active teams leaves prior scope visible after purge without an immediate refetch', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const view = render(<View client={client} />);
  await screen.findByText('old-team-data');
  auth = { isLoggedIn: true, me: { userId: 'synthetic-user', activeTeamIds: ['new-team'] } };
  view.rerender(<View client={client} />);
  await waitFor(() => expect(client.getQueryCache().getAll()).toHaveLength(0));
  expect(screen.getByText('old-team-data')).toBeTruthy();
  expect(getList).toHaveBeenCalledTimes(1);
  client.clear();
});

```

review07-page-probe.test.tsx:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import i18n from './src/i18n';
import SupportRequestsPage from './src/pages/SupportRequests/SupportRequests';
vi.mock('./src/hooks/use-auth', () => ({ default: () => ({
  me: { userId: 'synthetic-user', activeTeamIds: [] }, hasNavigationPermission: () => false,
}) }));
vi.mock('./src/hooks/use-theme', () => ({ useTheme: () => ({ theme: 'light' }) }));
vi.mock('./src/hooks/api/support/use-production-support', async () => {
  const actual = await vi.importActual('./src/hooks/api/support/use-production-support');
  return {
    ...actual,
    useSupportList: () => ({ data: undefined, isError: true, error: new Error('synthetic 503'),
      isPending: false, isFetching: false, refetch: vi.fn() }),
    useSupportCounts: () => ({ data: undefined, isError: true }),
    useSupportFacets: () => ({ data: undefined, isPending: false }),
    useSupportDetail: () => ({ data: undefined, isPending: false, error: null }),
    useSupportRecoveryStations: () => ({ data: [], isPending: false }),
    useSupportRecovery: () => ({ reroute: { isPending: false }, forceResolve: { isPending: false } }),
  };
});
afterEach(cleanup);
it('R07: initial list failure is displayed as an empty queue with no alert', async () => {
  await i18n.changeLanguage('en');
  globalThis.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  Object.defineProperty(window, 'matchMedia', { writable: true, value: vi.fn().mockReturnValue({
    matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(),
    removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  }) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><SupportRequestsPage /></QueryClientProvider>);
  expect(screen.getByText('No support requests match these filters.')).toBeTruthy();
  expect(screen.queryByRole('alert')).toBeNull();
  client.clear();
});

```
