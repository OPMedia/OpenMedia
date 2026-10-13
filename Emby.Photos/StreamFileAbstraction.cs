using System.IO;
using TagLib;

namespace Emby.Photos
{
    /// <summary>
    /// Provides a TagLib file abstraction backed by a stream.
    /// Replaces TagLib.Portable's StreamFileAbstraction (TagLibSharp does not ship one).
    /// </summary>
    public class StreamFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _readStream;
        private readonly Stream _writeStream;

        public StreamFileAbstraction(string name, Stream readStream, Stream writeStream)
        {
            Name = name;
            _readStream = readStream;
            _writeStream = writeStream;
        }

        public string Name { get; }

        public Stream ReadStream
        {
            get { return _readStream; }
        }

        public Stream WriteStream
        {
            get { return _writeStream; }
        }

        public void CloseStream(Stream stream)
        {
            // Don't dispose the stream here - the caller owns it
        }
    }
}
