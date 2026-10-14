// 复现 MusicBrainzArtistProvider 的请求+解析链（A3 排查）
using System.Net;
using System.Text;
using System.Xml;

var url = "https://www.musicbrainz.org/ws/2/artist/?query=\"Radiohead\"&dismax=true";
var handler = new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false
};
var client = new HttpClient(handler);
client.Timeout = Timeout.InfiniteTimeSpan;
var req = new HttpRequestMessage(HttpMethod.Get, url);
req.Headers.TryAddWithoutValidation("User-Agent", "Emby.Server/3.5.3");
var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
Console.WriteLine($"Status={(int)resp.StatusCode} FinalUrl={resp.RequestMessage?.RequestUri}");
using var ms = new MemoryStream();
await resp.Content.CopyToAsync(ms);
ms.Position = 0;
Console.WriteLine($"BodyLen={ms.Length} Head={Encoding.UTF8.GetString(ms.ToArray(), 0, (int)Math.Min(120, ms.Length))}");
ms.Position = 0;

// 同 GetResultsFromResponse 逻辑
using var oReader = new StreamReader(ms, Encoding.UTF8);
var settings = new XmlReaderSettings { CheckCharacters = false, IgnoreProcessingInstructions = true, IgnoreComments = true };
using var reader = XmlReader.Create(oReader, settings);
reader.MoveToContent();
reader.Read();
var count = 0;
while (!reader.EOF && reader.ReadState == ReadState.Interactive)
{
    if (reader.NodeType == XmlNodeType.Element)
    {
        Console.WriteLine($"Element={reader.Name}");
        if (reader.Name == "artist-list")
        {
            if (reader.IsEmptyElement) { reader.Read(); continue; }
            using var sub = reader.ReadSubtree();
            sub.MoveToContent();
            sub.Read();
            while (!sub.EOF && sub.ReadState == ReadState.Interactive)
            {
                if (sub.NodeType == XmlNodeType.Element && sub.Name == "artist")
                {
                    var id = sub.GetAttribute("id");
                    using var s2 = sub.ReadSubtree();
                    s2.MoveToContent();
                    s2.Read();
                    var name = "";
                    while (!s2.EOF && s2.ReadState == ReadState.Interactive)
                    {
                        if (s2.NodeType == XmlNodeType.Element && s2.Name == "name")
                            name = s2.ReadElementContentAsString();
                        else s2.Read();
                    }
                    if (count < 3) Console.WriteLine($"  artist id={id} name={name}");
                    count++;
                    sub.Read();
                }
                else sub.Read();
            }
        }
        else reader.Skip();
    }
    else reader.Read();
}
Console.WriteLine($"ParsedArtists={count}");
