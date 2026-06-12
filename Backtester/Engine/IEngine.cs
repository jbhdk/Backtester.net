using System;

namespace Backtester.Engine
{
    public interface IEngine
    {
        void Start();
        void Stop();
        void RunOnce();
    }
}
