using System;
using System.Threading.Tasks;
using DotNet.DictionaryThreadSafe.Models;

namespace DotNet.DictionaryThreadSafe.Services
{
    public interface IStatusService
    {
        Task Manage(Status status);
    }
}
