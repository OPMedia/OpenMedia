namespace MediaBrowser.Model.MediaInfo;

public interface IBlurayExaminer
{
	BlurayDiscInfo GetDiscInfo(string path);
}
