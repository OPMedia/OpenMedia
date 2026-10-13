namespace MediaBrowser.Controller.Entities;

public interface IHasShares
{
	Share[] Shares { get; set; }
}
