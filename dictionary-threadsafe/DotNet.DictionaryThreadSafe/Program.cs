using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DotNet.DictionaryThreadSafe.Models;
using DotNet.DictionaryThreadSafe.Services;

namespace DotNet.DictionaryThreadSafe
{
    public class MainClass
    {
    
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var statusService = new StatusService();
            var newStatusService = new NewStatusService();


            Task task1 = Task.Factory.StartNew(async () => {
                try
                {
                    var status = new Status() { Id = Guid.NewGuid(), Name = "Task1", Data = new Dictionary<string, object>() };
                    for (int i = 0; i < 10000000; i++)
                    {
                        await statusService.Manage(status);
                    }
                } catch (Exception ex)
                {
                    Trace.WriteLine($"Task1 - {ex.Message}");
                }

            });

            Task task2 = Task.Factory.StartNew(async () => {

                try
                {
                    var status = new Status() { Id = Guid.NewGuid(), Name = "Task2", Data = new Dictionary<string, object>() };
                    for (int i = 0; i < 10000000; i++)
                    {
                        await newStatusService.Manage(status);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Task2 - {ex.Message}");
                }
            });


            Console.WriteLine("All threads complete");
            Console.ReadLine();
        }
    }
}
