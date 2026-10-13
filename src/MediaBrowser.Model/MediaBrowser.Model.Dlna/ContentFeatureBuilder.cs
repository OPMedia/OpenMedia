using System;
using System.Collections.Generic;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Model.Dlna;

public class ContentFeatureBuilder
{
	private readonly DeviceProfile _profile;

	public ContentFeatureBuilder(DeviceProfile profile)
	{
		_profile = profile;
	}

	public string BuildImageHeader(string container, int? width, int? height, bool isDirectStream, string orgPn = null)
	{
		string text = ";DLNA.ORG_OP=" + DlnaMaps.GetImageOrgOpValue();
		string text2 = (isDirectStream ? ";DLNA.ORG_CI=0" : ";DLNA.ORG_CI=1");
		DlnaFlags flags = DlnaFlags.BackgroundTransferMode | DlnaFlags.DlnaV15 | DlnaFlags.InteractiveTransferMode | DlnaFlags.StreamingTransferMode;
		string text3 = $";DLNA.ORG_FLAGS={DlnaMaps.FlagsToString(flags)}";
		ResponseProfile imageMediaProfile = _profile.GetImageMediaProfile(container, width, height);
		if (string.IsNullOrEmpty(orgPn))
		{
			orgPn = imageMediaProfile?.OrgPn;
		}
		if (string.IsNullOrEmpty(orgPn))
		{
			orgPn = GetImageOrgPnValue(container, width, height);
		}
		return ((string.IsNullOrEmpty(orgPn) ? string.Empty : ("DLNA.ORG_PN=" + orgPn)) + text + text2 + text3).Trim(new char[1] { ';' });
	}

	public string BuildAudioHeader(string container, string audioCodec, int? audioBitrate, int? audioSampleRate, int? audioChannels, int? audioBitDepth, bool isDirectStream, long runtimeTicks, TranscodeSeekInfo transcodeSeekInfo)
	{
		string text = ";DLNA.ORG_OP=" + DlnaMaps.GetOrgOpValue(runtimeTicks > 0, isDirectStream, transcodeSeekInfo);
		string text2 = (isDirectStream ? ";DLNA.ORG_CI=0" : ";DLNA.ORG_CI=1");
		DlnaFlags flags = DlnaFlags.BackgroundTransferMode | DlnaFlags.DlnaV15 | DlnaFlags.InteractiveTransferMode | DlnaFlags.StreamingTransferMode;
		string text3 = $";DLNA.ORG_FLAGS={DlnaMaps.FlagsToString(flags)}";
		string text4 = _profile.GetAudioMediaProfile(container, audioCodec, audioChannels, audioBitrate, audioSampleRate, audioBitDepth)?.OrgPn;
		if (string.IsNullOrEmpty(text4))
		{
			text4 = GetAudioOrgPnValue(container, audioBitrate, audioSampleRate, audioChannels);
		}
		return ((string.IsNullOrEmpty(text4) ? string.Empty : ("DLNA.ORG_PN=" + text4)) + text + text2 + text3).Trim(new char[1] { ';' });
	}

	public List<string> BuildVideoHeader(string container, string videoCodec, string audioCodec, int? width, int? height, int? bitDepth, int? videoBitrate, TransportStreamTimestamp timestamp, bool isDirectStream, long runtimeTicks, string videoProfile, double? videoLevel, float videoFramerate, int? packetLength, TranscodeSeekInfo transcodeSeekInfo, bool? isAnamorphic, bool? isInterlaced, int? refFrames, int? numVideoStreams, int? numAudioStreams, string videoCodecTag, bool? isAvc)
	{
		string text = ";DLNA.ORG_OP=" + DlnaMaps.GetOrgOpValue(runtimeTicks > 0, isDirectStream, transcodeSeekInfo);
		string text2 = (isDirectStream ? ";DLNA.ORG_CI=0" : ";DLNA.ORG_CI=1");
		DlnaFlags flags = DlnaFlags.BackgroundTransferMode | DlnaFlags.DlnaV15 | DlnaFlags.InteractiveTransferMode | DlnaFlags.StreamingTransferMode;
		string text3 = $";DLNA.ORG_FLAGS={DlnaMaps.FlagsToString(flags)}";
		ResponseProfile videoMediaProfile = _profile.GetVideoMediaProfile(container, audioCodec, videoCodec, width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc);
		List<string> list = new List<string>();
		if (videoMediaProfile != null && !string.IsNullOrEmpty(videoMediaProfile.OrgPn))
		{
			list.AddRange(videoMediaProfile.OrgPn.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries));
		}
		else
		{
			string[] videoOrgPnValue = GetVideoOrgPnValue(container, videoCodec, audioCodec, width, height, timestamp);
			int num = 0;
			if (num < videoOrgPnValue.Length)
			{
				string item = videoOrgPnValue[num];
				list.Add(item);
			}
		}
		List<string> list2 = new List<string>();
		foreach (string item4 in list)
		{
			string item2 = ((string.IsNullOrEmpty(item4) ? string.Empty : ("DLNA.ORG_PN=" + item4)) + text + text2 + text3).Trim(new char[1] { ';' });
			list2.Add(item2);
		}
		if (list.Count == 0)
		{
			string item3 = (string.Empty + text + text2 + text3).Trim(new char[1] { ';' });
			list2.Add(item3);
		}
		return list2;
	}

	private string GetImageOrgPnValue(string container, int? width, int? height)
	{
		MediaFormatProfile? mediaFormatProfile = new MediaFormatProfileResolver().ResolveImageFormat(container, width, height);
		if (!mediaFormatProfile.HasValue)
		{
			return null;
		}
		return mediaFormatProfile.Value.ToString();
	}

	private string GetAudioOrgPnValue(string container, int? audioBitrate, int? audioSampleRate, int? audioChannels)
	{
		MediaFormatProfile? mediaFormatProfile = new MediaFormatProfileResolver().ResolveAudioFormat(container, audioBitrate, audioSampleRate, audioChannels);
		if (!mediaFormatProfile.HasValue)
		{
			return null;
		}
		return mediaFormatProfile.Value.ToString();
	}

	private string[] GetVideoOrgPnValue(string container, string videoCodec, string audioCodec, int? width, int? height, TransportStreamTimestamp timestamp)
	{
		return new MediaFormatProfileResolver().ResolveVideoFormat(container, videoCodec, audioCodec, width, height, timestamp);
	}
}
