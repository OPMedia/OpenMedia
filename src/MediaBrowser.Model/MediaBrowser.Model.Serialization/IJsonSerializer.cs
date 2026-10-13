using System;
using System.IO;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Serialization;

public interface IJsonSerializer
{
	void SerializeToStream(object obj, Stream stream);

	void SerializeToFile(object obj, string file);

	object DeserializeFromFile(Type type, string file);

	T DeserializeFromFile<T>(string file) where T : class;

	T DeserializeFromStream<T>(Stream stream);

	Task<T> DeserializeFromStreamAsync<T>(Stream stream);

	T DeserializeFromString<T>(string text);

	object DeserializeFromStream(Stream stream, Type type);

	Task<object> DeserializeFromStreamAsync(Stream stream, Type type);

	object DeserializeFromString(string json, Type type);

	string SerializeToString(object obj);
}
