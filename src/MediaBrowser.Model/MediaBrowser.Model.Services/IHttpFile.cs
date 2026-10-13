using System.IO;

namespace MediaBrowser.Model.Services;

public interface IHttpFile
{
	string Name { get; }

	string FileName { get; }

	long ContentLength { get; }

	string ContentType { get; }

	Stream InputStream { get; }
}
