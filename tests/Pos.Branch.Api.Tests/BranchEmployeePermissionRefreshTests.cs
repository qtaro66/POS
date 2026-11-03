using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeePermissionRefreshTests
{
    [Fact]
    public async Task Manual_refresh_shares_the_worker_and_retains_permissions_during_a_central_outage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cache = new BranchEmployeePermissionCache("B1", path);
            using var central = new CentralResponse();
            using var worker = new BranchEmployeePermissionPollingService(
                central, cache, NullLogger<BranchEmployeePermissionPollingService>.Instance);
            var pc = BranchSoldOutAdjustmentHttpHandlerTests.Principal("PcPos", null, true);
            var mobile = BranchSoldOutAdjustmentHttpHandlerTests.Principal("Mobile", null, true);
            var denied = await BranchEmployeePermissionRefreshHttpHandler.HandleAsync(
                mobile, new DefaultHttpContext(), cache, worker, CancellationToken.None);
            Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
            Assert.Equal(0, central.Calls);

            var request = BranchEmployeePermissionRefreshHttpHandler.HandleAsync(
                pc, new DefaultHttpContext(), cache, worker, CancellationToken.None);
            await central.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(PermissionRefreshOutcome.Busy, await worker.RefreshAsync(CancellationToken.None));
            central.Complete.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BranchEmployeePermissionSnapshot(1, "B1", 1, [new("EMP1", true, true)]))
            });
            Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await request).StatusCode);
            Assert.True(cache.CanSetSoldOut("B1", "EMP1"));
            Assert.Equal(PermissionRefreshOutcome.Unavailable, await worker.RefreshAsync(CancellationToken.None));
            Assert.True(cache.CanSetSoldOut("B1", "EMP1"));
            Assert.Equal(2, central.Calls);
            Assert.Equal(TimeSpan.FromHours(1), BranchEmployeePermissionPollingService.Interval);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CentralResponse : HttpMessageHandler, IHttpClientFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public HttpClient CreateClient(string name) =>
            new(this, disposeHandler: false) { BaseAddress = new Uri("https://central.example.test") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(BranchEmployeePermissionPollingService.Route, request.RequestUri?.AbsolutePath);
            if (++Calls != 1) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
            Started.SetResult();
            return Complete.Task.WaitAsync(cancellationToken);
        }
    }
}
