using System.IO;

namespace MediaBrowser.Model.IO;

public interface IZipClient
{
	void ExtractAll(string sourceFile, string targetPath, bool overwriteExistingFiles);

	void ExtractAll(Stream source, string targetPath, bool overwriteExistingFiles);

	void ExtractAllFromGz(Stream source, string targetPath, bool overwriteExistingFiles);

	void ExtractFirstFileFromGz(Stream source, string targetPath, string defaultFileName);

	void ExtractAllFromZip(Stream source, string targetPath, bool overwriteExistingFiles);

	void ExtractAllFrom7z(string sourceFile, string targetPath, bool overwriteExistingFiles);

	void ExtractAllFrom7z(Stream source, string targetPath, bool overwriteExistingFiles);

	void ExtractAllFromTar(string sourceFile, string targetPath, bool overwriteExistingFiles);

	void ExtractAllFromTar(Stream source, string targetPath, bool overwriteExistingFiles);
}
