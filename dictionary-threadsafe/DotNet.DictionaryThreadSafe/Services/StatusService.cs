using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.DictionaryThreadSafe.Extensions;
using DotNet.DictionaryThreadSafe.Models;

namespace DotNet.DictionaryThreadSafe.Services
{
    public class StatusService : IStatusService
    {
        public async Task Manage(Status status)
        {
            var lastValue = status.Get<int>("LastValue");

            Thread.Sleep(500);
            lastValue = new Random().Next();

            status.AddOrSet("Thread", "StatusService");
            status.AddOrSet("LastValue", lastValue);
        }
    }
}
