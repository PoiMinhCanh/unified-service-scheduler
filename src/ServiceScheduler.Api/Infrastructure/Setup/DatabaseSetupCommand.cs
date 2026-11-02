namespace ServiceScheduler.Api.Infrastructure.Setup;

/// <summary>
/// Explicit privileged initialization mode. This runs before web-host construction and exits
/// without starting HTTP; the API itself never executes migrations or seeds.
/// </summary>
public static class DatabaseSetupCommand
{
    private static readonly TimeSpan SetupDeadline = TimeSpan.FromSeconds(120);

    public static async Task<int> RunAsync()
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            var options = SchedulerSetupOptions.FromConfiguration(configuration);

            using var loggerFactory = LoggerFactory.Create(logging =>
            {
                logging.AddSimpleConsole(console =>
                {
                    console.SingleLine = true;
                    console.TimestampFormat = "HH:mm:ss ";
                });
                logging.SetMinimumLevel(LogLevel.Information);
            });

            var runner = new DatabaseSetupRunner(loggerFactory.CreateLogger<DatabaseSetupRunner>());
            using var cancellationTokenSource = new CancellationTokenSource(SetupDeadline);

            await runner.RunAsync(options, TimeProvider.System, cancellationTokenSource.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Database setup did not complete within 120 seconds.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Database setup failed: {exception.Message}");
            return 1;
        }
    }
}
