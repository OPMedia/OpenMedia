using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Server.Implementations.ScheduledTasks
{
    /// <summary>
    /// Class StartupTaskTrigger
    /// </summary>
    public class StartupTrigger : ITaskTrigger
    {
        public int DelayMs { get; set; }

        /// <summary>
        /// Gets or sets the options of this task.
        /// </summary>
        public TaskOptions TaskOptions { get; set; }

        public StartupTrigger()
        {
            DelayMs = 3000;
        }

        /// <summary>
        /// Stars waiting for the trigger action
        /// </summary>
        /// <param name="lastResult">The last result.</param>
        /// <param name="isApplicationStartup">if set to <c>true</c> [is application startup].</param>
        public void Start(TaskResult lastResult, ILogger logger, string taskName, bool isApplicationStartup)
        {
            if (isApplicationStartup)
            {
                // ITaskTrigger.Start 是 void 契约，延迟触发是刻意的后台行为
                _ = StartAsync(DelayMs, logger);
            }
        }

        private async Task StartAsync(int delayMs, ILogger logger)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);

                OnTriggered();
            }
            catch (Exception ex)
            {
                logger.ErrorException("Error in startup trigger", ex);
            }
        }

        /// <summary>
        /// Stops waiting for the trigger action
        /// </summary>
        public void Stop()
        {
        }

        /// <summary>
        /// Occurs when [triggered].
        /// </summary>
        public event EventHandler<EventArgs> Triggered;

        /// <summary>
        /// Called when [triggered].
        /// </summary>
        private void OnTriggered()
        {
            if (Triggered != null)
            {
                Triggered(this, EventArgs.Empty);
            }
        }
    }
}
