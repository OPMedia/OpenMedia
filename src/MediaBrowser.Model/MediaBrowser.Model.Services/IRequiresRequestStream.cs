using System.IO;

namespace MediaBrowser.Model.Services;

public interface IRequiresRequestStream
{
	Stream RequestStream { get; set; }
}
