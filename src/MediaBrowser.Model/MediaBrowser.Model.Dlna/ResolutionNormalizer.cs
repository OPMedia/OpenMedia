using System;

namespace MediaBrowser.Model.Dlna;

public class ResolutionNormalizer
{
	private static readonly ResolutionConfiguration[] Configurations = new ResolutionConfiguration[6]
	{
		new ResolutionConfiguration(426, 320000),
		new ResolutionConfiguration(640, 400000),
		new ResolutionConfiguration(720, 950000),
		new ResolutionConfiguration(1280, 2500000),
		new ResolutionConfiguration(1920, 4000000),
		new ResolutionConfiguration(3840, 35000000)
	};

	public static ResolutionOptions Normalize(int? inputBitrate, int? inputWidth, int? inputHeight, int outputBitrate, string inputCodec, string outputCodec, int? maxWidth, int? maxHeight)
	{
		if (inputBitrate.HasValue && outputBitrate >= inputBitrate.Value && (maxWidth.HasValue || maxHeight.HasValue))
		{
			return new ResolutionOptions
			{
				MaxWidth = maxWidth,
				MaxHeight = maxHeight
			};
		}
		ResolutionConfiguration resolutionConfiguration = GetResolutionConfiguration(outputBitrate);
		if (resolutionConfiguration != null)
		{
			int? num = maxWidth;
			maxWidth = Math.Min(resolutionConfiguration.MaxWidth, maxWidth ?? resolutionConfiguration.MaxWidth);
			if (!num.HasValue || num.Value != maxWidth.Value)
			{
				maxHeight = null;
			}
		}
		return new ResolutionOptions
		{
			MaxWidth = maxWidth,
			MaxHeight = maxHeight
		};
	}

	private static ResolutionConfiguration GetResolutionConfiguration(int outputBitrate)
	{
		ResolutionConfiguration resolutionConfiguration = null;
		ResolutionConfiguration[] configurations = Configurations;
		foreach (ResolutionConfiguration resolutionConfiguration2 in configurations)
		{
			if (outputBitrate <= resolutionConfiguration2.MaxBitrate)
			{
				return resolutionConfiguration ?? resolutionConfiguration2;
			}
			resolutionConfiguration = resolutionConfiguration2;
		}
		return null;
	}
}
