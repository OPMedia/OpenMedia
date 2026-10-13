namespace MediaBrowser.Model.Querying;

public enum ItemFilter
{
	IsFolder = 1,
	IsNotFolder = 2,
	IsUnplayed = 3,
	IsPlayed = 4,
	IsFavorite = 5,
	IsResumable = 7,
	Likes = 8,
	Dislikes = 9,
	IsFavoriteOrLikes = 10
}
