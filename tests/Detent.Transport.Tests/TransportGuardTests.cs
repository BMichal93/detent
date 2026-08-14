using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace Detent.Transport.Tests;

/// <summary>
/// The hostile fixtures from docs/arch/testing.md §5, one per control in
/// security-model.md §1.
/// </summary>
/// <remarks>
/// A control without a fixture that exercises it is an untested claim, and these
/// are the claims the README makes.
/// </remarks>
public sealed class TransportGuardTests
{
    private static TransportOptions Targeting(string url) => new() { Target = new Uri(url) };

    private static async Task<TransportException> CaptureFailureAsync(
        FakeServer server,
        CancellationToken cancellationToken = default)
    {
        using var probe = new StreamableHttpProbe(server.Options());
        return await Assert.ThrowsAsync<TransportException>(() => probe.CaptureAsync(cancellationToken));
    }

    /// <summary>
    /// The blocklist applied to the target itself, before a packet is sent.
    /// </summary>
    [Fact]
    public async Task Link_local_targets_are_refused()
    {
        using var probe = new StreamableHttpProbe(Targeting("http://169.254.169.254/mcp"));

        var error = await Assert.ThrowsAsync<TransportException>(
            () => probe.CaptureAsync(CancellationToken.None));

        Assert.Contains("link-local", error.Message, StringComparison.Ordinal);
        Assert.Contains("--allow-host", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Loopback is blocked like any other reserved range. The fixtures in this
    /// assembly opt in explicitly, which is the intended shape of the escape
    /// hatch rather than a workaround for it.
    /// </summary>
    [Fact]
    public async Task Loopback_is_refused_without_an_explicit_allow()
    {
        await using var server = await FakeServer.StartAsync(new McpScript().Handler);

        using var probe = new StreamableHttpProbe(new TransportOptions
        {
            Target = new Uri(server.BaseAddress, "/mcp"),
        });

        var error = await Assert.ThrowsAsync<TransportException>(
            () => probe.CaptureAsync(CancellationToken.None));

        Assert.Contains("loopback", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://example.com/mcp")]
    [InlineData("file:///etc/passwd")]
    public void Non_http_schemes_are_refused(string url)
    {
        var error = Assert.Throws<TransportException>(() => new StreamableHttpProbe(Targeting(url)));

        Assert.Contains("Unsupported scheme", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A redirect into link-local is the textbook SSRF pivot. It is refused as a
    /// cross-host redirect, which fires before the address guard would.
    /// </summary>
    [Fact]
    public async Task Redirects_to_another_host_are_refused()
    {
        await using var server = await FakeServer.StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "http://169.254.169.254/mcp";
            return Task.CompletedTask;
        });

        var error = await CaptureFailureAsync(server);

        Assert.Contains("cross-host redirect", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redirect_loops_stop_at_the_hop_cap()
    {
        var hops = 0;

        await using var server = await FakeServer.StartAsync(context =>
        {
            hops++;
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "/mcp";
            return Task.CompletedTask;
        });

        var error = await CaptureFailureAsync(server);

        Assert.Contains("more than 3 redirects", error.Message, StringComparison.Ordinal);
        Assert.Equal(TransportLimits.MaxRedirects + 1, hops);
    }

    /// <summary>
    /// The server offers far more than the cap. The client must stop reading
    /// rather than buffer it and decide afterwards.
    /// </summary>
    [Fact]
    public async Task Oversized_bodies_are_refused()
    {
        await using var server = await FakeServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";

            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)' ');

            try
            {
                // 100 MB on offer. The client should abort at 10 MB, so most of
                // this is never written.
                for (var i = 0; i < 1600; i++)
                {
                    await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: the client hung up, which is the point of the test.
            }
        });

        var error = await CaptureFailureAsync(server);

        Assert.Contains("byte cap", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A declared Content-Length over the cap is rejected before the body is
    /// read at all.
    /// </summary>
    [Fact]
    public async Task Oversized_declared_lengths_are_refused_before_reading()
    {
        await using var server = await FakeServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = TransportLimits.MaxResponseBytes + 1L;

            // The headers have to actually reach the client for it to reject
            // them, and Kestrel holds them until the body starts. The trickle
            // that follows is never read: the client decides on the header
            // alone, which is the behaviour under test.
            try
            {
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    await context.Response.Body.WriteAsync(new byte[] { (byte)' ' }, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    await Task.Delay(20, context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: the client hung up on the header.
            }
        });

        var error = await CaptureFailureAsync(server);

        Assert.Contains("over the", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Depth is enforced by the parser, so a document deep enough to overflow a
    /// later tree walk is never materialised.
    /// </summary>
    [Fact]
    public async Task Documents_deeper_than_the_cap_are_refused()
    {
        var deep = new StringBuilder()
            .Append('[', 10_000)
            .Append(']', 10_000)
            .ToString();

        await using var server = await FakeServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(deep, context.RequestAborted);
        });

        var error = await CaptureFailureAsync(server);

        Assert.Contains("depth cap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listings_larger_than_the_item_cap_are_refused()
    {
        var tools = new JsonArray();

        for (var i = 0; i <= TransportLimits.MaxItemsPerListing; i++)
        {
            tools.Add(new JsonObject { ["name"] = i.ToString("D5", CultureInfo.InvariantCulture) });
        }

        await using var server = await FakeServer.StartAsync(new McpScript { Tools = tools }.Handler);

        var error = await CaptureFailureAsync(server);

        Assert.Contains("more than 5000 entries", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A server returning a fresh cursor forever is an unbounded loop that the
    /// item cap alone does not close.
    /// </summary>
    [Fact]
    public async Task Endless_pagination_stops_at_the_page_cap()
    {
        await using var server = await FakeServer.StartAsync(
            new McpScript { PaginateEndlessly = true }.Handler);

        var error = await CaptureFailureAsync(server);

        Assert.Contains("paginated past 100 pages", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Descriptions_longer_than_the_cap_are_refused()
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "verbose",
                ["description"] = new string('x', TransportLimits.MaxDescriptionChars + 1),
            },
        };

        await using var server = await FakeServer.StartAsync(new McpScript { Tools = tools }.Handler);

        var error = await CaptureFailureAsync(server);

        Assert.Contains("character cap", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A trickle stays inside any per-request timeout by design, which is why
    /// the budget is a whole-operation one. Cancelled here through the caller's
    /// token rather than by waiting out the real 30 second budget.
    /// </summary>
    [Fact]
    public async Task A_trickling_server_does_not_hold_the_capture_open()
    {
        await using var server = await FakeServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";

            while (!context.RequestAborted.IsCancellationRequested)
            {
                await context.Response.Body.WriteAsync(new byte[] { (byte)' ' }, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(50, context.RequestAborted);
            }
        });

        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var probe = new StreamableHttpProbe(server.Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.CaptureAsync(caller.Token));
    }

    [Fact]
    public async Task An_unauthorized_server_names_the_token_variable()
    {
        await using var server = await FakeServer.StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });

        var error = await CaptureFailureAsync(server);

        Assert.Contains(TransportOptions.TokenVariable, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The token goes in a header and never near an argument list. Asserted on
    /// the wire rather than by reading the call site, because the call site is
    /// what a later edit changes.
    /// </summary>
    [Fact]
    public async Task A_bearer_token_is_sent_as_a_header()
    {
        var authorization = new List<string?>();
        var script = new McpScript();

        await using var server = await FakeServer.StartAsync(async context =>
        {
            authorization.Add(context.Request.Headers.Authorization.FirstOrDefault());
            await script.Handler(context);
        });

        using var probe = new StreamableHttpProbe(server.Options() with { BearerToken = "secret-value" });
        await probe.CaptureAsync(CancellationToken.None);

        Assert.All(authorization, header => Assert.Equal("Bearer secret-value", header));
    }

    /// <summary>
    /// A stateful server hands out a session id at initialize and rejects every
    /// later call without it.
    /// </summary>
    [Fact]
    public async Task The_session_id_is_echoed_on_later_calls()
    {
        var sessions = new List<string?>();
        var script = new McpScript { Tools = [new JsonObject { ["name"] = "a" }] };

        await using var server = await FakeServer.StartAsync(async context =>
        {
            await script.Handler(context);
            sessions.Add(context.Request.Headers["Mcp-Session-Id"].FirstOrDefault());
        });

        using var probe = new StreamableHttpProbe(server.Options());
        await probe.CaptureAsync(CancellationToken.None);

        Assert.Null(sessions[0]);
        Assert.All(sessions.Skip(1), session => Assert.Equal("session-fake-mcp", session));
    }

    /// <summary>
    /// The flag exists for a self-signed certificate on a developer's own
    /// machine. Anywhere else it is a downgrade, and in a pipeline it is a
    /// permanent one.
    /// </summary>
    [Fact]
    public void Skipping_certificate_validation_is_refused_off_loopback()
        => Assert.Throws<TransportException>(() => new StreamableHttpProbe(new TransportOptions
        {
            Target = new Uri("https://example.com/mcp"),
            AllowInvalidCertificates = true,
        }));
}
