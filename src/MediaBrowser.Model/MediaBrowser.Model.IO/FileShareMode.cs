namespace MediaBrowser.Model.IO;

public enum FileShareMode
{
	None,
	Read,
	Write,
	ReadWrite,
	/// <summary>
	/// 读 + 写 + 删除共享：允许其他进程在句柄打开期间删除/重命名文件
	/// （ffmpeg 9 的 .tmp+rename 原子写播放列表需要 FILE_SHARE_DELETE）。
	/// </summary>
	ReadWriteDelete
}
