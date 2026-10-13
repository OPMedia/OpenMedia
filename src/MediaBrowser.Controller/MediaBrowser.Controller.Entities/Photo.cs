using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Photo : BaseItem
{
	[IgnoreDataMember]
	public override bool SupportsLocalMetadata => false;

	[IgnoreDataMember]
	public override string MediaType => "Photo";

	[IgnoreDataMember]
	public override Folder LatestItemsIndexContainer => AlbumEntity;

	[IgnoreDataMember]
	public PhotoAlbum AlbumEntity
	{
		get
		{
			foreach (BaseItem parent in GetParents())
			{
				if (parent is PhotoAlbum result)
				{
					return result;
				}
			}
			return null;
		}
	}

	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownloadAsSingleMedia();

	public string CameraMake { get; set; }

	public string CameraModel { get; set; }

	public string Software { get; set; }

	public double? ExposureTime { get; set; }

	public double? FocalLength { get; set; }

	public ImageOrientation? Orientation { get; set; }

	public double? Aperture { get; set; }

	public double? ShutterSpeed { get; set; }

	public double? Latitude { get; set; }

	public double? Longitude { get; set; }

	public double? Altitude { get; set; }

	public int? IsoSpeedRating { get; set; }

	public override bool CanDownload()
	{
		return CanDownloadAsSingleMedia();
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Invalid comparison between Unknown and I4
		double num = Width;
		double num2 = Height;
		if (num > 0.0 && num2 > 0.0)
		{
			if (Orientation.HasValue)
			{
				int value = (int)Orientation.Value;
				if (value - 5 <= 3)
				{
					double num3 = num2;
					num2 = num;
					num = num3;
				}
			}
			return num / num2;
		}
		return base.GetDefaultPrimaryImageAspectRatio();
	}
}
