using Microsoft.Extensions.Hosting;

namespace Api.Services;

public class ConsoleKeyListenerHostedService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run a non-blocking loop that listens for console key presses
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.C)
                    {
                        Console.Clear();
                        Console.WriteLine($"Console cleared at {DateTime.Now:O}");
                    }
                }
            }
            catch
            {
                // In environments without an interactive console, KeyAvailable/ReadKey can throw.
                // Back off to avoid tight loop.
                await Task.Delay(1000, stoppingToken);
            }

            await Task.Delay(50, stoppingToken);
        }
    }
}
