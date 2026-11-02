using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Telemetry;

/// <summary>
/// Starts the pinned Collector, Prometheus, Tempo, and Grafana containers on a dedicated Docker
/// network using the repository's Compose configuration files. A process-wide gate serializes the
/// stack so the delivery and provisioning classes do not run two full stacks concurrently.
/// </summary>
public sealed class TelemetryStackFixture : IAsyncLifetime
{
    public const string CollectorImage =
        "otel/opentelemetry-collector:0.164.0@sha256:28b6d41ac51bff6b41ba5e8fe503569ffb9d6a1ea90f7a78419e86f34e44f750";

    public const string PrometheusImage =
        "prom/prometheus:v3.15.1@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2";

    public const string TempoImage =
        "grafana/tempo:3.1.1@sha256:64e2fbd56d9c2b3412a4b430a58ea59e91a670b1e75f920a0c702213ad145044";

    public const string GrafanaImage =
        "grafana/grafana:13.3.0@sha256:488984b716c0862639f91a698adbe2862677a5a07bad42da5f03f3030883b205";

    private static readonly SemaphoreSlim StackGate = new(1, 1);

    private bool _gateHeld;
    private INetwork? _network;
    private IContainer? _prometheus;
    private IContainer? _tempo;
    private IContainer? _grafana;
    private IContainer? _collector;
    private string? _collectorGrpcEndpoint;
    private string? _collectorHealthUrl;

    public string PrometheusBaseUrl => BaseUrl(_prometheus, 9090);

    public string TempoBaseUrl => BaseUrl(_tempo, 3200);

    public string GrafanaBaseUrl => BaseUrl(_grafana, 3000);

    // Captured after start because Testcontainers cannot report a mapping while the container is
    // stopped; the mapping is reused when the Collector starts again.
    public string CollectorGrpcEndpoint =>
        _collectorGrpcEndpoint ?? throw new InvalidOperationException("The Collector has not started.");

    public string CollectorHealthUrl =>
        _collectorHealthUrl ?? throw new InvalidOperationException("The Collector has not started.");

    public async ValueTask InitializeAsync()
    {
        await StackGate.WaitAsync().ConfigureAwait(false);
        _gateHeld = true;

        try
        {
            var root = FindRepositoryRoot();
            var telemetryDirectory = Path.Combine(root, "telemetry");
            _network = new NetworkBuilder()
                .WithName($"scheduler-telemetry-{Guid.NewGuid():N}")
                .Build();
            await _network.CreateAsync().ConfigureAwait(false);

            _prometheus = new ContainerBuilder(PrometheusImage)
                .WithNetwork(_network)
                .WithNetworkAliases("prometheus")
                .WithBindMount(
                    Path.Combine(telemetryDirectory, "prometheus.yml"),
                    "/etc/prometheus/prometheus.yml",
                    AccessMode.ReadOnly)
                .WithPortBinding(9090, assignRandomHostPort: true)
                .WithCommand(
                    "--config.file=/etc/prometheus/prometheus.yml",
                    "--storage.tsdb.path=/prometheus",
                    "--storage.tsdb.retention.time=24h",
                    "--storage.tsdb.retention.size=512MB",
                    "--web.enable-otlp-receiver",
                    "--web.listen-address=0.0.0.0:9090")
                .Build();
            await _prometheus.StartAsync().ConfigureAwait(false);

            _tempo = new ContainerBuilder(TempoImage)
                .WithNetwork(_network)
                .WithNetworkAliases("tempo")
                .WithBindMount(
                    Path.Combine(telemetryDirectory, "tempo.yaml"),
                    "/etc/tempo/tempo.yaml",
                    AccessMode.ReadOnly)
                .WithPortBinding(3200, assignRandomHostPort: true)
                .WithCommand("-config.file=/etc/tempo/tempo.yaml")
                .Build();
            await _tempo.StartAsync().ConfigureAwait(false);

            _grafana = new ContainerBuilder(GrafanaImage)
                .WithNetwork(_network)
                .WithNetworkAliases("grafana")
                .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
                .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
                .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
                .WithBindMount(
                    Path.Combine(telemetryDirectory, "grafana", "provisioning"),
                    "/etc/grafana/provisioning",
                    AccessMode.ReadOnly)
                .WithBindMount(
                    Path.Combine(telemetryDirectory, "grafana", "dashboards"),
                    "/var/lib/grafana/dashboards",
                    AccessMode.ReadOnly)
                .WithPortBinding(3000, assignRandomHostPort: true)
                .Build();
            await _grafana.StartAsync().ConfigureAwait(false);

            // Fixed host ports keep the exporter endpoint stable across the Collector restart used
            // by the outage/recovery scenario; the stack gate prevents parallel stacks.
            var collectorGrpcPort = GetFreeTcpPort();
            var collectorHttpPort = GetFreeTcpPort();
            var collectorHealthPort = GetFreeTcpPort();
            _collectorGrpcEndpoint = $"http://127.0.0.1:{collectorGrpcPort}";
            _collectorHealthUrl = $"http://127.0.0.1:{collectorHealthPort}";
            _collector = new ContainerBuilder(CollectorImage)
                .WithNetwork(_network)
                .WithNetworkAliases("collector")
                .WithBindMount(
                    Path.Combine(telemetryDirectory, "collector.yaml"),
                    "/etc/otelcol/collector.yaml",
                    AccessMode.ReadOnly)
                .WithPortBinding(collectorGrpcPort, 4317)
                .WithPortBinding(collectorHttpPort, 4318)
                .WithPortBinding(collectorHealthPort, 13133)
                .WithCommand("--config=/etc/otelcol/collector.yaml")
                .Build();
            await _collector.StartAsync().ConfigureAwait(false);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await WaitForHttpAsync(client, $"{PrometheusBaseUrl}/-/ready", "Prometheus", TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
            await WaitForHttpAsync(client, $"{TempoBaseUrl}/ready", "Tempo", TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
            await WaitForHttpAsync(client, $"{GrafanaBaseUrl}/api/health", "Grafana", TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
            await WaitForHttpAsync(client, CollectorHealthUrl, "Collector", TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
            await WaitForHttpAsync(
                    client,
                    $"{GrafanaBaseUrl}/api/datasources/uid/prometheus",
                    "Grafana Prometheus data source",
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
            await WaitForHttpAsync(
                    client,
                    $"{GrafanaBaseUrl}/api/datasources/uid/tempo",
                    "Grafana Tempo data source",
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
            await WaitForHttpAsync(
                    client,
                    $"{GrafanaBaseUrl}/api/dashboards/uid/scheduler-overview",
                    "Grafana dashboard",
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopCollectorAsync(CancellationToken cancellationToken = default)
    {
        if (_collector is not null)
        {
            await _collector.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StartCollectorAsync(CancellationToken cancellationToken = default)
    {
        if (_collector is null)
        {
            throw new InvalidOperationException("The Collector container has not started.");
        }

        await _collector.StartAsync(cancellationToken).ConfigureAwait(false);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await WaitForHttpAsync(client, CollectorHealthUrl, "Collector", cancellationToken)
            .ConfigureAwait(false);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_gateHeld)
        {
            return;
        }

        _gateHeld = false;
        foreach (var container in new[] { _collector, _grafana, _tempo, _prometheus })
        {
            if (container is null)
            {
                continue;
            }

            try
            {
                await container.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup; Testcontainers will remove the remaining resources.
            }
        }

        if (_network is not null)
        {
            try
            {
                await _network.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        StackGate.Release();
    }

    private static string BaseUrl(IContainer? container, int containerPort)
    {
        if (container is null)
        {
            throw new InvalidOperationException("The telemetry stack has not started.");
        }

        return $"http://127.0.0.1:{container.GetMappedPublicPort(containerPort)}";
    }

    private static async Task WaitForHttpAsync(
        HttpClient client,
        string url,
        string description,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        Exception? lastException = null;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(60))
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastException = new HttpRequestException(
                    $"{description} returned {(int)response.StatusCode} {response.StatusCode}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"{description} did not become ready at {url} within 60 seconds (elapsed {DateTime.UtcNow - started}).",
            lastException);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "telemetry", "collector.yaml"))
                && File.Exists(Path.Combine(directory.FullName, "ServiceScheduler.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root (telemetry/collector.yaml and ServiceScheduler.slnx) from {AppContext.BaseDirectory}.");
    }
}
