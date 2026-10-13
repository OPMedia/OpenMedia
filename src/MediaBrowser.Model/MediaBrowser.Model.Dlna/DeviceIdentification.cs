namespace MediaBrowser.Model.Dlna;

public class DeviceIdentification
{
	public string FriendlyName { get; set; }

	public string ModelNumber { get; set; }

	public string SerialNumber { get; set; }

	public string ModelName { get; set; }

	public string ModelDescription { get; set; }

	public string DeviceDescription { get; set; }

	public string ModelUrl { get; set; }

	public string Manufacturer { get; set; }

	public string ManufacturerUrl { get; set; }

	public HttpHeaderInfo[] Headers { get; set; }

	public DeviceIdentification()
	{
		Headers = new HttpHeaderInfo[0];
	}
}
