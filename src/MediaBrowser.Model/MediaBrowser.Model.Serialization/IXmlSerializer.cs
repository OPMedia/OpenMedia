using System;
using System.IO;

namespace MediaBrowser.Model.Serialization;

public interface IXmlSerializer
{
	object DeserializeFromStream(Type type, Stream stream);

	void SerializeToStream(object obj, Stream stream);

	void SerializeToFile(object obj, string file);

	object DeserializeFromFile(Type type, string file);

	object DeserializeFromBytes(Type type, byte[] buffer);
}
