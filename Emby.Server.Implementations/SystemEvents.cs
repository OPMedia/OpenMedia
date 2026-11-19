using System;
using MediaBrowser.Common.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.System;

namespace Emby.Server.Implementations
{
    public class SystemEvents : ISystemEvents
    {
        // 四个事件由宿主平台信号触发；当前实现不主动 raise，但它们是
        // ISystemEvents 的公开订阅面，不能删除。用显式访问器消除未使用告警。
        private EventHandler _resume;
        private EventHandler _suspend;
        private EventHandler _sessionLogoff;
        private EventHandler _systemShutdown;

        public event EventHandler Resume
        {
            add { _resume += value; }
            remove { _resume -= value; }
        }

        public event EventHandler Suspend
        {
            add { _suspend += value; }
            remove { _suspend -= value; }
        }

        public event EventHandler SessionLogoff
        {
            add { _sessionLogoff += value; }
            remove { _sessionLogoff -= value; }
        }

        public event EventHandler SystemShutdown
        {
            add { _systemShutdown += value; }
            remove { _systemShutdown -= value; }
        }

        private readonly ILogger _logger;

        public SystemEvents(ILogger logger)
        {
            _logger = logger;
        }
    }
}
