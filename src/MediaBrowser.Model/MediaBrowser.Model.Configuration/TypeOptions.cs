using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Configuration;

public class TypeOptions
{
	public static Dictionary<string, ImageOption[]> DefaultImageOptions = new Dictionary<string, ImageOption[]>
	{
		{
			"Movie",
			new ImageOption[7]
			{
				new ImageOption
				{
					Limit = 1,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Art
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Disc
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Primary
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Banner
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Thumb
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Logo
				}
			}
		},
		{
			"MusicVideo",
			new ImageOption[7]
			{
				new ImageOption
				{
					Limit = 1,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Art
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Disc
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Primary
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Banner
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Thumb
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Logo
				}
			}
		},
		{
			"Series",
			new ImageOption[6]
			{
				new ImageOption
				{
					Limit = 1,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Art
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Primary
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Banner
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Thumb
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Logo
				}
			}
		},
		{
			"MusicAlbum",
			new ImageOption[2]
			{
				new ImageOption
				{
					Limit = 0,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Disc
				}
			}
		},
		{
			"MusicArtist",
			new ImageOption[4]
			{
				new ImageOption
				{
					Limit = 1,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Banner
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Art
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Logo
				}
			}
		},
		{
			"BoxSet",
			new ImageOption[7]
			{
				new ImageOption
				{
					Limit = 1,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Primary
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Thumb
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Logo
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Art
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Disc
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Banner
				}
			}
		},
		{
			"Season",
			new ImageOption[4]
			{
				new ImageOption
				{
					Limit = 0,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Primary
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Banner
				},
				new ImageOption
				{
					Limit = 0,
					Type = ImageType.Thumb
				}
			}
		},
		{
			"Episode",
			new ImageOption[2]
			{
				new ImageOption
				{
					Limit = 0,
					MinWidth = 1280,
					Type = ImageType.Backdrop
				},
				new ImageOption
				{
					Limit = 1,
					Type = ImageType.Primary
				}
			}
		}
	};

	public static ImageOption DefaultInstance = new ImageOption();

	public string Type { get; set; }

	public string[] MetadataFetchers { get; set; }

	public string[] MetadataFetcherOrder { get; set; }

	public string[] ImageFetchers { get; set; }

	public string[] ImageFetcherOrder { get; set; }

	public ImageOption[] ImageOptions { get; set; }

	public ImageOption GetImageOptions(ImageType type)
	{
		ImageOption[] imageOptions = ImageOptions;
		foreach (ImageOption imageOption in imageOptions)
		{
			if (imageOption.Type == type)
			{
				return imageOption;
			}
		}
		if (DefaultImageOptions.TryGetValue(Type, out var value))
		{
			imageOptions = value;
			foreach (ImageOption imageOption2 in imageOptions)
			{
				if (imageOption2.Type == type)
				{
					return imageOption2;
				}
			}
		}
		return DefaultInstance;
	}

	public int GetLimit(ImageType type)
	{
		return GetImageOptions(type).Limit;
	}

	public int GetMinWidth(ImageType type)
	{
		return GetImageOptions(type).MinWidth;
	}

	public bool IsEnabled(ImageType type)
	{
		return GetLimit(type) > 0;
	}

	public TypeOptions()
	{
		MetadataFetchers = Array.Empty<string>();
		MetadataFetcherOrder = Array.Empty<string>();
		ImageFetchers = Array.Empty<string>();
		ImageFetcherOrder = Array.Empty<string>();
		ImageOptions = Array.Empty<ImageOption>();
	}
}
