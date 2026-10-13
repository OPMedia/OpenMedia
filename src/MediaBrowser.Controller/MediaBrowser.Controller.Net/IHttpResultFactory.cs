using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public interface IHttpResultFactory
{
	object GetResult(IRequest requestContext, byte[] content, string contentType, IDictionary<string, string> responseHeaders = null);

	object GetResult(IRequest requestContext, Stream content, string contentType, IDictionary<string, string> responseHeaders = null);

	object GetResult(string content, string contentType, IDictionary<string, string> responseHeaders = null);

	object GetResult(IRequest requestContext, string content, string contentType, IDictionary<string, string> responseHeaders = null);

	object GetRedirectResult(string url);

	object GetResult<T>(IRequest requestContext, T result, IDictionary<string, string> responseHeaders = null) where T : class;

	Task<object> GetStaticResult(IRequest requestContext, Guid cacheKey, DateTime? lastDateModified, TimeSpan? cacheDuration, string contentType, Func<Task<Stream>> factoryFn, IDictionary<string, string> responseHeaders = null, bool isHeadRequest = false);

	Task<object> GetStaticResult(IRequest requestContext, StaticResultOptions options);

	Task<object> GetStaticFileResult(IRequest requestContext, string path, FileShareMode fileShare = (FileShareMode)1);

	Task<object> GetStaticFileResult(IRequest requestContext, StaticFileResultOptions options);
}
