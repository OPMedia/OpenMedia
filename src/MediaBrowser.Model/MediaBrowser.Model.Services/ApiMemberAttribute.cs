using System;

namespace MediaBrowser.Model.Services;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public class ApiMemberAttribute : Attribute
{
	public string Verb { get; set; }

	public string ParameterType { get; set; }

	public string Name { get; set; }

	public string Description { get; set; }

	public string DataType { get; set; }

	public bool IsRequired { get; set; }

	public bool AllowMultiple { get; set; }

	[Obsolete("This is not used for API documentation and is otherwise unused as well.")]
	public string Route { get; set; }

	[Obsolete("This is not used for API documentation and is otherwise unused as well.")]
	public bool ExcludeInSchema { get; set; }
}
