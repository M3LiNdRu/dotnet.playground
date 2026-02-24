using System;
using System.Collections.Generic;

namespace DotNet.DictionaryThreadSafe.Models
{
    public class Status
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public IDictionary<string, object> Data { get; set; }
    }
}
