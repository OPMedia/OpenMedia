using System;

namespace MediaBrowser.Model.IO;

[Flags]
public enum FileOpenOptions
{
	WriteThrough = int.MinValue,
	None = 0,
	Encrypted = 0x4000,
	DeleteOnClose = 0x4000000,
	SequentialScan = 0x8000000,
	RandomAccess = 0x10000000,
	Asynchronous = 0x40000000
}
