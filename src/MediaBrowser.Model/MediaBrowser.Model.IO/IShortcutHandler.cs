namespace MediaBrowser.Model.IO;

public interface IShortcutHandler
{
	string Extension { get; }

	string Resolve(string shortcutPath);

	void Create(string shortcutPath, string targetPath);
}
