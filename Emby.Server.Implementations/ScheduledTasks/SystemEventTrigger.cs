using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Tasks;

namespace Emby.Server.Implementations.ScheduledTasks
{
    /// <summary>
    /// Class SystemEventTrigger
    /// </summary>
    public class SystemEventTrigger : ITaskTrigger
    {
        /// <summary>
        /// Gets or sets the system event.
        /// </summary>
        /// <value>The system event.</value>
        public SystemEvent SystemEvent { get; set; }

        /// <summary>
        /// Gets or sets the options of this task.
        /// </summary>
        public TaskOptions TaskOptions { get; set; }

        private readonly ISystemEvents _systemEvents;
        private ILogger _logger;

        public SystemEventTrigger(ISystemEvents systemEvents)
        {
            _systemEvents = systemEvents;
        }

        /// <summary>
        /// Stars waiting for the trigger action
        /// </summary>
        /// <param name="lastResult">The last result.</param>
        /// <param name="isApplicationStartup">if set to <c>true</c> [is application startup].</param>
        public void Start(TaskResult lastResult, ILogger logger, string taskName, bool isApplicationStartup)
        {
            _logger = logger;

            switch (SystemEvent)
            {
                case SystemEvent.WakeFromSleep:
                    _systemEvents.Resume += _systemEvents_Resume;
                    break;
            }
        }

        private void _systemEvents_Resume(object sender, EventArgs e)
        {
            // 系统事件回调必须保持 void；延迟触发在异步助手中执行并自行记录异常
            _ = ResumeAsync();
        }

        private async Task ResumeAsync()
        {
            try
            {
                if (SystemEvent == SystemEvent.WakeFromSleep)
                {
                    // This value is a bit arbitrary, but add a delay to help ensure network connections have been restored before running the task
                    await Task.Delay(10000).ConfigureAwait(false);

                    OnTriggered();
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error in system event trigger", ex);
            }
        }

        /// <summary>
        /// Stops waiting for the trigger action
        /// </summary>
        public void Stop()
        {
            _systemEvents.Resume -= _systemEvents_Resume;
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
