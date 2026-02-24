using ApplicationServices.Modules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApplicationServices
{
    public class BackgroundService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly Guid _id;

        private readonly ILogger<BackgroundService> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ICommonModule _commonModule;

        public BackgroundService(ILogger<BackgroundService> logger, IHostApplicationLifetime applicationLifetime, ICommonModule commonModule)
        {
            _id = Guid.NewGuid();
            _logger = logger;
            _applicationLifetime = applicationLifetime;
            _commonModule = commonModule;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Executing background task in background service id {id}", _id);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Common module ts is: {ts}", _commonModule.PrintTimestamp());
                await Task.Delay(10000);
            }
            
            _logger.LogInformation("Finished background task in background service id {id}", _id);
        }
    }
}
