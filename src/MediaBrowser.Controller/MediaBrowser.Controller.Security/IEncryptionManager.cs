namespace MediaBrowser.Controller.Security;

public interface IEncryptionManager
{
	string EncryptString(string value);

	string DecryptString(string value);
}
