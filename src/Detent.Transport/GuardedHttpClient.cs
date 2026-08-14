using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using Detent.Core.Security;

namespace Detent.Transport;

/// <summary>
/// The only way this tool talks to a remote host.
/// </summary>
/// <remarks>
/// Every control in <c>docs/arch/security-model.md</c> §1 that concerns HTTP is
/// enforced here rather than at the call sites, because a control that each
/// caller has to remember is a control that one caller will forget. This is also
/// the concrete reason ADR-0003 rejects the official SDK: redirect policy,
/// address re-vetting, and a hard body cap are not knobs an SDK exposes.
/// </remarks>
public sealed class GuardedHttpClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly TransportOptions _options;

    public GuardedHttpClient(TransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;

        RequireSupportedScheme(options.Target);
        RequireCertificateValidationOrLoopback(options);

        var handler = new SocketsHttpHandler
        {
            // Followed by hand instead. The address behind a redirect target has
            // to be vetted before the connection is made, and the built-in
            // follower gives no point at which to do that.
            AllowAutoRedirect = false,

            // Every connection this client opens goes to an address that passed
            // the guard, and to that address specifically. Resolving inside the
            // connect callback rather than beforehand is what closes the DNS
            // rebinding window: there is no gap between the check and the
            // connect for a second lookup to slip into.
            ConnectCallback = ConnectAsync,

            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };

        if (options.AllowInvalidCertificates)
        {
            // Justified above by RequireCertificateValidationOrLoopback, which
            // has already refused this for anything but a loopback target
            // outside CI. Narrowed to that case, this is a developer convenience
            // against their own machine rather than a downgrade.
#pragma warning disable CA5359 // Do not disable certificate validation
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            };
#pragma warning restore CA5359
        }

        _client = new HttpClient(handler, disposeHandler: true)
        {
            // The budget is a whole-operation one held by the caller's token. A
            // per-request timeout on top of it would let a slow server stay just
            // inside each request while running the capture past its budget.
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = TransportLimits.MaxResponseBytes,
        };
    }

    /// <summary>
    /// Posts a JSON body and reads the answer under every cap in
    /// <see cref="TransportLimits"/>.
    /// </summary>
    public async Task<GuardedResponse> PostJsonAsync(
        Uri target,
        string json,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(json);

        var current = target;

        for (var hop = 0; hop <= TransportLimits.MaxRedirects; hop++)
        {
            using var request = BuildRequest(current, json, headers);
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode))
            {
                return await ReadAsync(response, cancellationToken).ConfigureAwait(false);
            }

            current = ResolveRedirect(current, response);
        }

        throw new TransportException(
            $"Refusing to follow more than {TransportLimits.MaxRedirects} redirects from {_options.Target}. "
            + "A redirect chain this long is a loop or a probe, not a relocation.");
    }

    public void Dispose() => _client.Dispose();

    private HttpRequestMessage BuildRequest(
        Uri target,
        string json,
        IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        // Streamable HTTP lets a server answer a single request with either a
        // JSON body or an SSE stream, at its discretion, so both are declared.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_options.BearerToken is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    /// <summary>
    /// Vets a redirect target before it becomes the next request.
    /// </summary>
    /// <remarks>
    /// Cross-host redirects are refused rather than re-vetted. The address guard
    /// would catch a redirect to link-local, but it cannot catch a redirect to
    /// an unrelated public host that happens to hold someone else's credentials,
    /// and this client sends a bearer token.
    /// </remarks>
    private static Uri ResolveRedirect(Uri from, HttpResponseMessage response)
    {
        var location = response.Headers.Location
            ?? throw new TransportException(
                $"{from} answered {(int)response.StatusCode} with no Location header.");

        var target = location.IsAbsoluteUri ? location : new Uri(from, location);

        RequireSupportedScheme(target);

        if (!string.Equals(target.Host, from.Host, StringComparison.OrdinalIgnoreCase)
            || target.Port != from.Port)
        {
            throw new TransportException(
                $"Refusing a cross-host redirect from {from.Host}:{from.Port.ToString(CultureInfo.InvariantCulture)} "
                + $"to {Sanitizer.SanitizeForMessage(target.Host)}:{target.Port.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (from.Scheme == Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttps)
        {
            throw new TransportException(
                $"Refusing a redirect from https to {target.Scheme} at {target.Host}.");
        }

        return target;
    }

    /// <summary>
    /// Reads the body under the size cap, streaming rather than buffering first.
    /// </summary>
    /// <remarks>
    /// A declared Content-Length is checked as a courtesy and not trusted: a
    /// hostile server will simply not declare one, or declare a small one and
    /// send more. The cap that matters is the one counted while reading.
    /// </remarks>
    private static async Task<GuardedResponse> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } declared
            && declared > TransportLimits.MaxResponseBytes)
        {
            throw new TransportException(
                $"Response declares {declared.ToString(CultureInfo.InvariantCulture)} bytes, "
                + $"over the {TransportLimits.MaxResponseBytes.ToString(CultureInfo.InvariantCulture)} byte cap.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];

            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > TransportLimits.MaxResponseBytes)
                {
                    throw new TransportException(
                        "Response exceeds the "
                        + TransportLimits.MaxResponseBytes.ToString(CultureInfo.InvariantCulture)
                        + " byte cap. Aborted without reading the rest.");
                }

                buffer.Write(chunk, 0, read);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, values) in response.Headers)
            {
                if (values.FirstOrDefault() is { } value)
                {
                    headers[name] = value;
                }
            }

            return new GuardedResponse
            {
                Status = response.StatusCode,
                MediaType = response.Content.Headers.ContentType?.MediaType,
                Body = buffer.ToArray(),
                Headers = headers,
            };
        }
    }

    private async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var address = await ResolveAndVetAsync(host, cancellationToken).ConfigureAwait(false);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket
                .ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken)
                .ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Resolves a host and refuses it if any address it answers with is blocked.
    /// </summary>
    /// <remarks>
    /// The whole host is refused, not merely the offending address. A name that
    /// resolves to one routable address and one link-local address is a
    /// rebinding attempt, and picking the acceptable one is choosing to lose the
    /// race rather than to decline it.
    /// </remarks>
    private async Task<IPAddress> ResolveAndVetAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;

        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new TransportException(
                    $"Cannot resolve {Sanitizer.SanitizeForMessage(host)}.", ex);
            }
        }

        if (addresses.Length == 0)
        {
            throw new TransportException(
                $"{Sanitizer.SanitizeForMessage(host)} resolved to no addresses.");
        }

        if (_options.AllowedHosts.Contains(host))
        {
            return addresses[0];
        }

        foreach (var address in addresses)
        {
            if (AddressGuard.BlockReason(address) is { } reason)
            {
                throw new TransportException(
                    $"Refusing to connect to {Sanitizer.SanitizeForMessage(host)}: "
                    + $"it resolves to {address} ({reason}). "
                    + $"Pass --allow-host {Sanitizer.SanitizeForMessage(host)} if this is deliberate.");
            }
        }

        return addresses[0];
    }

    private static void RequireSupportedScheme(Uri target)
    {
        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
        {
            throw new TransportException(
                $"Unsupported scheme '{Sanitizer.SanitizeForMessage(target.Scheme, 20)}'. "
                + "The HTTP transport accepts http and https; stdio targets need --allow-exec.");
        }
    }

    /// <summary>
    /// Refuses <c>--insecure</c> anywhere it is not a developer's own machine.
    /// </summary>
    private static void RequireCertificateValidationOrLoopback(TransportOptions options)
    {
        if (!options.AllowInvalidCertificates)
        {
            return;
        }

        if (DetectedCiVariable() is { } variable)
        {
            throw new TransportException(
                $"Refusing --insecure: {variable} is set, so this is a pipeline. "
                + "A pipeline that skips certificate validation once skips it forever.");
        }

        if (!options.Target.IsLoopback)
        {
            throw new TransportException(
                $"Refusing --insecure for {options.Target.Host}, which is not loopback. "
                + "The flag exists for a self-signed certificate on your own machine.");
        }
    }

    private static string? DetectedCiVariable()
    {
        // The generic CI variable first, then the vendors that do not set it.
        string[] variables = ["CI", "GITHUB_ACTIONS", "GITLAB_CI", "TF_BUILD", "JENKINS_URL", "TEAMCITY_VERSION"];

        return variables.FirstOrDefault(
            v => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v)));
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;
}

/// <summary>A response that survived every transport control.</summary>
public sealed record GuardedResponse
{
    public required HttpStatusCode Status { get; init; }

    public required string? MediaType { get; init; }

    /// <summary>The body, at most <see cref="TransportLimits.MaxResponseBytes"/>.</summary>
    public required byte[] Body { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}
