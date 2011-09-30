using System.ServiceProcess;
using System.Threading;
using RT.Services;

namespace Propeller
{
    class PropellerService : SelfService
    {
        private volatile bool _terminate = false;
        private bool _isStandalone = false;

        public PropellerService()
        {
            ServiceName = "PropellerService";
            ServiceDisplayName = "PropellerService";
            ServiceDescription = "Provides powerful, flexible HTTP-based functionality on global enterprise systems by leveraging dynamic API synergy through an extensible architecture.";
            ServiceStartMode = ServiceStartMode.Manual;
        }

        public void RunAsStandalone(string[] args)
        {
            _isStandalone = true;
            _terminate = false;

            OnStart(args);

            while (!_terminate)
                Thread.Sleep(1000);

            OnStop();
        }

        protected override void OnStart(string[] args)
        {
            PropellerProgram.Engine.Start();
        }

        protected override void OnStop()
        {
            PropellerProgram.Engine.Shutdown(false);
        }

        public void Shutdown()
        {
            if (_isStandalone)
                _terminate = true;
            else
                StopSelf();
        }
    }
}
