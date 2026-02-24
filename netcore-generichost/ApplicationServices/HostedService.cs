using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApplicationServices
{
    public class HostedService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly Guid _id;

        private readonly ILogger<HostedService> _logger;

        public HostedService(ILogger<HostedService> logger, IHostApplicationLifetime appLifetime)
        {
            _id = Guid.NewGuid();
            _logger = logger;


            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("1. StartAsync is called for hosted service with id: {id}", _id);

            return Task.CompletedTask;
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("4. StopAsync is called for hosted service with id: {id}", _id);

            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _logger.LogInformation("2. OnStarted has been called.");
        }

        private void OnStopping()
        {
            _logger.LogInformation("3. OnStopping has been called.");
        }

        private void OnStopped()
        {
            _logger.LogInformation("5. OnStopped has been called.");
        }
    }
}