using System;

namespace MediaBrowser.Model.Dlna;

[Flags]
public enum DlnaFlags : ulong
{
	BackgroundTransferMode = 0x400000uL,
	ByteBasedSeek = 0x20000000uL,
	ConnectionStall = 0x200000uL,
	DlnaV15 = 0x100000uL,
	InteractiveTransferMode = 0x800000uL,
	PlayContainer = 0x10000000uL,
	RtspPause = 0x2000000uL,
	S0Increase = 0x8000000uL,
	SenderPaced = 0x80000000uL,
	SnIncrease = 0x4000000uL,
	StreamingTransferMode = 0x1000000uL,
	TimeBasedSeek = 0x40000000uL
}
