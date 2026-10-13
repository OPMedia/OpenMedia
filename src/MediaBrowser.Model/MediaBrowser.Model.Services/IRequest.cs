using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Services;

public interface IRequest
{
	object OriginalRequest { get; }

	IResponse Response { get; }

	string OperationName { get; set; }

	string Verb { get; }

	object Dto { get; set; }

	string ContentType { get; }

	bool IsLocal { get; }

	string UserAgent { get; }

	IDictionary<string, Cookie> Cookies { get; }

	string ResponseContentType { get; set; }

	Dictionary<string, object> Items { get; }

	QueryParamCollection Headers { get; }

	QueryParamCollection QueryString { get; }

	string RawUrl { get; }

	string AbsoluteUri { get; }

	string UserHostAddress { get; }

	string RemoteIp { get; }

	string Authorization { get; }

	bool IsSecureConnection { get; }

	string[] AcceptTypes { get; }

	string PathInfo { get; }

	Stream InputStream { get; }

	long ContentLength { get; }

	IHttpFile[] Files { get; }

	Uri UrlReferrer { get; }

	Task<QueryParamCollection> GetFormData();
}
