using System;

namespace ApplicationServices.Modules
{
    public class CommonModule : ICommonModule
    {
        private static DateTime _timestamp;

        public CommonModule()
        {
            _timestamp = DateTime.UtcNow;
        }

        void ICommonModule.UpdateTimestamp() { _timestamp = DateTime.UtcNow; }

        string ICommonModule.PrintTimestamp() => _timestamp.ToString();
    }
}
