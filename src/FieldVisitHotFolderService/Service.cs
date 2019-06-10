using System;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Common;
using log4net;

namespace FieldVisitHotFolderService
{
    [System.ComponentModel.DesignerCategory("Code")] // Stop Visual Studio from opening this file in useless "Designer" mode
    public class Service : ServiceBase
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        private CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();
        private Task FileDetectorTask { get; set; }
        private StatusIndicator StatusIndicator { get; set; }

        public void RunUntilStopped()
        {
            FileDetectorTask = StartAsynchronously();
            WaitForFileDetectorTask(true);
        }

        protected override void OnStart(string[] args)
        {
            if (args.Any())
                Log.Info($"Service is starting with args: {string.Join(" ", args)}");
            else
                Log.Info("Service is starting.");

            Program.GetContext(Context, args);

            base.OnStart(args);

            FileDetectorTask = StartAsynchronously();
        }

        private Task StartAsynchronously()
        {
            return RunFileProcessorAsync();
        }

        private async Task RunFileProcessorAsync()
        {
            Log.Info($"Starting {FileHelper.ExeNameAndVersion}.");

            StatusIndicator = new StatusIndicator();

            var fileProcessor = new FileDetector
            {
                Context = Context,
                CancellationToken = CancellationTokenSource.Token,
                StatusIndicator = StatusIndicator,
                CancellationAction = () => CancellationTokenSource.Cancel()
            };

            await Task.Run(() => fileProcessor.Run(), CancellationTokenSource.Token);
        }

        private void WaitForFileDetectorTask(bool shouldRethrowExpectedExceptions = false)
        {
            try
            {
                FileDetectorTask.Wait(CancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException exception)
            {
                exception.Handle(inner =>
                {
                    if (inner is OperationCanceledException) return true;
                    if (!(inner is ExpectedException expectedException)) return false;

                    if (shouldRethrowExpectedExceptions) throw inner;

                    Log.Error(expectedException.Message);
                    return false;
                });
            }
        }

        protected override void OnStop()
        {
            Log.Info("Service is stopping");

            base.OnStop();

            StopOrExitProcessOnError();

            Thread.Sleep(TimeSpan.FromSeconds(0.5));
        }

        private void StopOrExitProcessOnError()
        {
            try
            {
                CancellationTokenSource.Cancel();

                WaitForFileDetectorTask();

                CancellationTokenSource.Dispose();
                StatusIndicator.Dispose();
            }
            catch (Exception e)
            {
                StatusIndicator.Dispose();
                Log.Fatal("Failed to stop file processor normally; exiting process", e);
                Environment.Exit(20);
            }
        }
    }
}
