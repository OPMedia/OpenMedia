using System;
using System.Threading.Tasks;

namespace Emby.Server.HttpServer.Routing
{
    public class ServiceMethod
    {
        public string Id { get; set; }

        public ActionInvokerFn ServiceAction { get; set; }
        public MediaBrowser.Model.Services.IHasRequestFilter[] RequestFilters { get; set; }

        public static string Key(Type serviceType, string method, string requestDtoName)
        {
            return serviceType.FullName + " " + method.ToUpper() + " " + requestDtoName;
        }
    }

    public delegate Task<object> InstanceExecFn(MediaBrowser.Model.Services.IRequest requestContext, object instance, object request);
    public delegate object ActionInvokerFn(object instance, object request);
    public delegate void VoidActionInvokerFn(object instance, object request);
}
