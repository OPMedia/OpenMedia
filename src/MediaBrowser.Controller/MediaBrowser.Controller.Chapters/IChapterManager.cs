using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Chapters;

public interface IChapterManager
{
	void SaveChapters(string itemId, List<ChapterInfo> chapters);
}
