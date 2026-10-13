using System;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Model.Dto;

public class MetadataEditorInfo
{
	public ParentalRating[] ParentalRatingOptions { get; set; }

	public CountryInfo[] Countries { get; set; }

	public CultureDto[] Cultures { get; set; }

	public ExternalIdInfo[] ExternalIdInfos { get; set; }

	public string ContentType { get; set; }

	public NameValuePair[] ContentTypeOptions { get; set; }

	public MetadataEditorInfo()
	{
		ParentalRatingOptions = new ParentalRating[0];
		Countries = new CountryInfo[0];
		Cultures = new CultureDto[0];
		ExternalIdInfos = new ExternalIdInfo[0];
		ContentTypeOptions = Array.Empty<NameValuePair>();
	}
}
