using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Detent.Transport.Tests;

/// <summary>
/// An in-process HTTP server that answers exactly how a test tells it to.
/// </summary>
/// <remarks>
/// docs/arch/testing.md §5 lists the fixtures the transport has to survive: an
/// oversized body, a document too deep to parse, redirects into link-local, a
/// redirect loop, a trickle, and a stream that never ends. A control without a
/// fixture that exercises it is an untested claim, so this exists to make each
/// of them cheap to write.
/// <para>
/// It binds to loopback, which the address guard blocks by design. Tests pass
/// the host through <c>AllowedHosts</c>, which exercises the <c>--allow-host</c>
/// opt-in as a side effect of needing it.
/// </para>
/// </remarks>
internal sealed class FakeServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<FakeServer> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port: 0));
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Run(handler);

        await app.StartAsync();

        var address = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses
            .First();

        return new FakeServer(app, new Uri(address));
    }

    /// <summary>The options a test needs to talk to this server at all.</summary>
    public TransportOptions Options(params string[] extraAllowedHosts) => new()
    {
        Target = new Uri(BaseAddress, "/mcp"),
        AllowedHosts = new HashSet<string>(
            extraAllowedHosts.Append(BaseAddress.Host),
            StringComparer.OrdinalIgnoreCase),
    };

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
