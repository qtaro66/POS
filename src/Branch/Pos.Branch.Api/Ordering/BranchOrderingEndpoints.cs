using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Pos.Branch.Api.Security;
using Pos.Contracts.Ordering;

namespace Pos.Branch.Api.Ordering;

public static class BranchOrderingEndpoints
{
    public const string Prefix = "/api/v1/ordering";
    public const long MaxRequestBodySize = 64 * 1024;

    public static IEndpointRouteBuilder MapBranchOrderingIfEnabled(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetSection(BranchOrderingOptions.SectionName).GetValue<bool>("Enabled")) { return endpoints; }
        var group = endpoints.MapGroup(Prefix).RequireAuthorization(BranchPosAuthorizationDefaults.CompleteIdentityPolicyName);
        group.MapPost("/tables/open", BranchOrderingHttpHandlers.OpenAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .Produces<BranchOrderingCommandResponse>().Produces<BranchOrderingCommandResponse>(409)
            .WithName("OpenBranchTable");
        group.MapPost("/orders", BranchOrderingHttpHandlers.SubmitAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .Produces<BranchOrderingCommandResponse>().Produces<BranchOrderingCommandResponse>(409)
            .WithName("SubmitBranchOrder");
        group.MapGet("/commands/{requestId}", BranchOrderingHttpHandlers.StatusAsync)
            .Produces<BranchOrderingCommandResponse>().Produces<BranchOrderingCommandResponse>(404).WithName("GetOrderingCommandStatus");
        group.MapGet("/table-sessions", BranchOrderingHttpHandlers.TablesAsync)
            .Produces<BranchActiveTableResponse>().WithName("ListActiveBranchTableSessions");
        group.MapGet("/table-sessions/{tableSessionId}/menu", BranchOrderingHttpHandlers.MenuAsync)
            .Produces<BranchSessionMenuResponse>().WithName("GetBranchSessionMenu");
        return endpoints;
    }

    public static IApplicationBuilder UseBranchOrderingGuards(this IApplicationBuilder application, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetSection(BranchOrderingOptions.SectionName).GetValue<bool>("Enabled")) { return application; }
        return application.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(Prefix)) { await next(context); return; }
            BranchSecretResponseHeaders.Apply(context.Response);
            if (!context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
            if (context.Request.ContentLength > MaxRequestBodySize) { context.Response.StatusCode = 413; return; }
            var original = context.Request.Body;
            context.Request.Body = new LimitedRequestStream(original);
            try { await next(context); }
            catch (BadHttpRequestException exception) when (exception.StatusCode == 413 && !context.Response.HasStarted)
            {
                context.Response.StatusCode = 413;
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6 && !context.Response.HasStarted)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new BranchOrderingError(1, "BranchDatabaseBusy"), context.RequestAborted);
            }
            finally { context.Request.Body = original; }
        });
    }

    // Enforce the same bound for unknown-length/chunked JSON bodies, including in non-Kestrel hosts.
    private sealed class LimitedRequestStream(Stream inner) : Stream
    {
        private long _read;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        private int Allowed(int requested) => (int)Math.Min(requested, Math.Max(1, MaxRequestBodySize - _read + 1));
        private int Count(int count)
        {
            _read += count;
            if (_read > MaxRequestBodySize) { throw new BadHttpRequestException("Ordering body exceeds its size limit.", 413); }
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, Allowed(count)));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
