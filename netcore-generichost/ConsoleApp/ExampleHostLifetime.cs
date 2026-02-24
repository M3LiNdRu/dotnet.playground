using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp
{
    class ExampleHostLifetime : IHostLifetime
    {
        private readonly ILogger<ExampleHostLifetime> _logger;
        private readonly IHost _host;

        public ExampleHostLifetime(ILogger<ExampleHostLifetime> logger)
        {
            _logger = logger;
        }

        Task IHostLifetime.StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ExampleHostLifetime.StopAsync has been called");

            return Task.CompletedTask;
        }

        Task IHostLifetime.WaitForStartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ExampleHostLifetime.WaitForStartAsync has been called");

            return Task.CompletedTask;
        }
    }
}
