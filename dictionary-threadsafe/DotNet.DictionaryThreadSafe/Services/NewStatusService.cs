using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.DictionaryThreadSafe.Extensions;
using DotNet.DictionaryThreadSafe.Models;

namespace DotNet.DictionaryThreadSafe.Services
{
    public class NewStatusService : IStatusService
    {

        public async Task Manage(Status status)
        {
            var lastValue = status.Get<long>("LastValue");

            Thread.Sleep(400);
            lastValue = new Random().Next();

            status.AddOrSet("Thread", "NewStatusService");
            status.AddOrSet("LastValue", lastValue);
        }
    }
}
