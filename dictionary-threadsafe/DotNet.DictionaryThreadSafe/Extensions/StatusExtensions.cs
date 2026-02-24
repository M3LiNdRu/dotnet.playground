using System;
using System.Diagnostics;
using DotNet.DictionaryThreadSafe.Models;

namespace DotNet.DictionaryThreadSafe.Extensions
{
    public static class StatusExtensions
    {
        public static T Get<T>(this Status status, string key)
        {
            Trace.WriteLine("Get");

            if (status?.Data == null || !status.Data.ContainsKey(key))
                return default(T);

            return (T)status.Data[key];
        }

        public static void AddOrSet<T>(this Status status, string key, T value)
        {
            Trace.WriteLine("AddOrSet");

            if (status.Data.ContainsKey(key))
            {
                status.Data[key] = value;
            }
            else
            {
                status.Data.Add(key, value);
            }
        }
    }
}
