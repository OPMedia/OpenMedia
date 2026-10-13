using System.IO;
using System.Text;
using System.Threading;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Subtitles;

public class JsonWriter : ISubtitleWriter
{
	private readonly IJsonSerializer _json;

	public JsonWriter(IJsonSerializer json)
	{
		_json = json;
	}

	public void Write(SubtitleTrackInfo info, Stream stream, CancellationToken cancellationToken)
	{
		using StreamWriter streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
		string value = _json.SerializeToString((object)info);
		streamWriter.Write(value);
	}
}
