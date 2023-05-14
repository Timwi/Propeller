using System.ServiceProcess;
using System.Threading;
using RT.Services;

namespace RT.Propeller
{
    class PropellerService : SelfService
    {
        private bool _isStandalone = false;
        private volatile bool _terminate = false;
        private PropellerEngine _engine = new PropellerEngine();

        public string SettingsPath { get; set; }

        public PropellerService()
        {
            ServiceName = "PropellerService";
            ServiceDisplayName = "PropellerService";
            ServiceDescription = "Provides powerful, flexible and secure HTTP-based functionality on global enterprise systems by leveraging dynamic API synergy through an extensible architecture.";
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
            _engine.Start(SettingsPath);
        }

        protected override void OnStop()
        {
            _engine.Shutdown(false);
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
