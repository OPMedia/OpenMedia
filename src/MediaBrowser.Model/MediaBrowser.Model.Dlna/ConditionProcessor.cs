using System;
using System.Globalization;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Model.Dlna;

public class ConditionProcessor
{
	public bool IsVideoConditionSatisfied(ProfileCondition condition, int? width, int? height, int? videoBitDepth, int? videoBitrate, string videoProfile, double? videoLevel, float videoFramerate, int? packetLength, TransportStreamTimestamp? timestamp, bool? isAnamorphic, bool? isInterlaced, int? refFrames, int? numVideoStreams, int? numAudioStreams, string videoCodecTag, bool? isAvc, VideoRangeType? videoRangeType = null)
	{
		return condition.Property switch
		{
			ProfileConditionValue.VideoRangeType => IsConditionSatisfied(condition, videoRangeType),
			ProfileConditionValue.IsInterlaced => IsConditionSatisfied(condition, isInterlaced), 
			ProfileConditionValue.IsAnamorphic => IsConditionSatisfied(condition, isAnamorphic), 
			ProfileConditionValue.IsAvc => IsConditionSatisfied(condition, isAvc), 
			ProfileConditionValue.VideoFramerate => IsConditionSatisfied(condition, videoFramerate), 
			ProfileConditionValue.VideoLevel => IsConditionSatisfied(condition, videoLevel), 
			ProfileConditionValue.VideoProfile => IsConditionSatisfied(condition, videoProfile), 
			ProfileConditionValue.VideoCodecTag => IsConditionSatisfied(condition, videoCodecTag), 
			ProfileConditionValue.PacketLength => IsConditionSatisfied(condition, packetLength), 
			ProfileConditionValue.VideoBitDepth => IsConditionSatisfied(condition, videoBitDepth), 
			ProfileConditionValue.VideoBitrate => IsConditionSatisfied(condition, videoBitrate), 
			ProfileConditionValue.Height => IsConditionSatisfied(condition, height), 
			ProfileConditionValue.Width => IsConditionSatisfied(condition, width), 
			ProfileConditionValue.RefFrames => IsConditionSatisfied(condition, refFrames), 
			ProfileConditionValue.NumAudioStreams => IsConditionSatisfied(condition, numAudioStreams), 
			ProfileConditionValue.NumVideoStreams => IsConditionSatisfied(condition, numVideoStreams), 
			ProfileConditionValue.VideoTimestamp => IsConditionSatisfied(condition, timestamp), 
			_ => true, 
		};
	}

	public bool IsImageConditionSatisfied(ProfileCondition condition, int? width, int? height)
	{
		return condition.Property switch
		{
			ProfileConditionValue.Height => IsConditionSatisfied(condition, height), 
			ProfileConditionValue.Width => IsConditionSatisfied(condition, width), 
			_ => throw new ArgumentException("Unexpected condition on image file: " + condition.Property), 
		};
	}

	public bool IsAudioConditionSatisfied(ProfileCondition condition, int? audioChannels, int? audioBitrate, int? audioSampleRate, int? audioBitDepth)
	{
		return condition.Property switch
		{
			ProfileConditionValue.AudioBitrate => IsConditionSatisfied(condition, audioBitrate), 
			ProfileConditionValue.AudioChannels => IsConditionSatisfied(condition, audioChannels), 
			ProfileConditionValue.AudioSampleRate => IsConditionSatisfied(condition, audioSampleRate), 
			ProfileConditionValue.AudioBitDepth => IsConditionSatisfied(condition, audioBitDepth), 
			_ => throw new ArgumentException("Unexpected condition on audio file: " + condition.Property), 
		};
	}

	public bool IsVideoAudioConditionSatisfied(ProfileCondition condition, int? audioChannels, int? audioBitrate, int? audioSampleRate, int? audioBitDepth, string audioProfile, bool? isSecondaryTrack)
	{
		return condition.Property switch
		{
			ProfileConditionValue.AudioProfile => IsConditionSatisfied(condition, audioProfile), 
			ProfileConditionValue.AudioBitrate => IsConditionSatisfied(condition, audioBitrate), 
			ProfileConditionValue.AudioChannels => IsConditionSatisfied(condition, audioChannels), 
			ProfileConditionValue.IsSecondaryAudio => IsConditionSatisfied(condition, isSecondaryTrack), 
			ProfileConditionValue.AudioSampleRate => IsConditionSatisfied(condition, audioSampleRate), 
			ProfileConditionValue.AudioBitDepth => IsConditionSatisfied(condition, audioBitDepth), 
			_ => throw new ArgumentException("Unexpected condition on audio file: " + condition.Property), 
		};
	}

	private bool IsConditionSatisfied(ProfileCondition condition, int? currentValue)
	{
		if (!currentValue.HasValue)
		{
			return !condition.IsRequired;
		}
		if (int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			switch (condition.Condition)
			{
			case ProfileConditionType.Equals:
			case ProfileConditionType.EqualsAny:
				return currentValue.Value.Equals(result);
			case ProfileConditionType.GreaterThanEqual:
				return currentValue.Value >= result;
			case ProfileConditionType.LessThanEqual:
				return currentValue.Value <= result;
			case ProfileConditionType.NotEquals:
				return !currentValue.Value.Equals(result);
			default:
				throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition);
			}
		}
		return false;
	}

	private bool IsConditionSatisfied(ProfileCondition condition, VideoRangeType? currentValue)
	{
		// SDR 是所有客户端的基线动态范围，未知来源也不做限制；声明的 VideoRangeType 只约束 HDR 来源。
		if (!currentValue.HasValue || currentValue.Value == VideoRangeType.Unknown || currentValue.Value == VideoRangeType.SDR)
		{
			return true;
		}
		switch (condition.Condition)
		{
		case ProfileConditionType.EqualsAny:
			foreach (string candidate in condition.Value?.Split('|') ?? Array.Empty<string>())
			{
				if (VideoRangeMatches(currentValue.Value, candidate.Trim()))
				{
					return true;
				}
			}
			return false;
		case ProfileConditionType.Equals:
			return VideoRangeMatches(currentValue.Value, condition.Value?.Trim());
		case ProfileConditionType.NotEquals:
			return !VideoRangeMatches(currentValue.Value, condition.Value?.Trim());
		default:
			throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition);
		}
	}

	private static bool VideoRangeMatches(VideoRangeType source, string declared)
	{
		if (string.IsNullOrEmpty(declared))
		{
			return false;
		}
		if (StringHelper.EqualsIgnoreCase(declared, "HDR"))
		{
			return IsHdrRange(source);
		}
		return Enum.TryParse(declared, ignoreCase: true, out VideoRangeType declaredRange) && declaredRange == source;
	}

	private static bool IsHdrRange(VideoRangeType range)
	{
		return range is VideoRangeType.HDR10 or VideoRangeType.HLG or VideoRangeType.DolbyVision
			or VideoRangeType.DolbyVisionWithHDR10 or VideoRangeType.DolbyVisionWithSDR;
	}

	private bool IsConditionSatisfied(ProfileCondition condition, string currentValue)
	{
		if (string.IsNullOrEmpty(currentValue))
		{
			return !condition.IsRequired;
		}
		string value = condition.Value;
		return condition.Condition switch
		{
			ProfileConditionType.EqualsAny => ListHelper.ContainsIgnoreCase(value.Split(new char[1] { '|' }), currentValue), 
			ProfileConditionType.Equals => StringHelper.EqualsIgnoreCase(currentValue, value), 
			ProfileConditionType.NotEquals => !StringHelper.EqualsIgnoreCase(currentValue, value), 
			_ => throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition), 
		};
	}

	private bool IsConditionSatisfied(ProfileCondition condition, bool? currentValue)
	{
		if (!currentValue.HasValue)
		{
			return !condition.IsRequired;
		}
		if (bool.TryParse(condition.Value, out var result))
		{
			return condition.Condition switch
			{
				ProfileConditionType.Equals => currentValue.Value == result, 
				ProfileConditionType.NotEquals => currentValue.Value != result, 
				_ => throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition), 
			};
		}
		return false;
	}

	private bool IsConditionSatisfied(ProfileCondition condition, float currentValue)
	{
		if (currentValue <= 0f)
		{
			return !condition.IsRequired;
		}
		if (float.TryParse(condition.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
		{
			return condition.Condition switch
			{
				ProfileConditionType.Equals => currentValue.Equals(result), 
				ProfileConditionType.GreaterThanEqual => currentValue >= result, 
				ProfileConditionType.LessThanEqual => currentValue <= result, 
				ProfileConditionType.NotEquals => !currentValue.Equals(result), 
				_ => throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition), 
			};
		}
		return false;
	}

	private bool IsConditionSatisfied(ProfileCondition condition, double? currentValue)
	{
		if (!currentValue.HasValue)
		{
			return !condition.IsRequired;
		}
		if (double.TryParse(condition.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
		{
			return condition.Condition switch
			{
				ProfileConditionType.Equals => currentValue.Value.Equals(result), 
				ProfileConditionType.GreaterThanEqual => currentValue.Value >= result, 
				ProfileConditionType.LessThanEqual => currentValue.Value <= result, 
				ProfileConditionType.NotEquals => !currentValue.Value.Equals(result), 
				_ => throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition), 
			};
		}
		return false;
	}

	private bool IsConditionSatisfied(ProfileCondition condition, TransportStreamTimestamp? timestamp)
	{
		if (!timestamp.HasValue)
		{
			return !condition.IsRequired;
		}
		TransportStreamTimestamp transportStreamTimestamp = (TransportStreamTimestamp)Enum.Parse(typeof(TransportStreamTimestamp), condition.Value, ignoreCase: true);
		return condition.Condition switch
		{
			ProfileConditionType.Equals => timestamp == transportStreamTimestamp, 
			ProfileConditionType.NotEquals => timestamp != transportStreamTimestamp, 
			_ => throw new InvalidOperationException("Unexpected ProfileConditionType: " + condition.Condition), 
		};
	}
}
