namespace Emby.Server.MediaEncoding.Api.Hls;

public interface IMasterHlsRequest
{
	bool EnableAdaptiveBitrateStreaming { get; set; }
}
