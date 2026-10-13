namespace MediaBrowser.Controller.Sorting;

public static class SortHelper
{
	private enum ChunkType
	{
		Alphanumeric,
		Numeric
	}

	public static bool InChunk(char ch, char otherCh)
	{
		ChunkType chunkType = ChunkType.Alphanumeric;
		if (char.IsDigit(otherCh))
		{
			chunkType = ChunkType.Numeric;
		}
		if ((chunkType == ChunkType.Alphanumeric && char.IsDigit(ch)) || (chunkType == ChunkType.Numeric && !char.IsDigit(ch)))
		{
			return false;
		}
		return true;
	}
}
