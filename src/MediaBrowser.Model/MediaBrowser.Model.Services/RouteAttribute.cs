using System;

namespace MediaBrowser.Model.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class RouteAttribute : Attribute
{
	public string Path { get; set; }

	public string Summary { get; set; }

	public string Description { get; set; }

	public bool IsHidden { get; set; }

	public string Notes { get; set; }

	public string Verbs { get; set; }

	public int Priority { get; set; }

	public RouteAttribute(string path)
		: this(path, null)
	{
	}

	public RouteAttribute(string path, string verbs)
	{
		Path = path;
		Verbs = verbs;
	}

	protected bool Equals(RouteAttribute other)
	{
		if (base.Equals(other) && string.Equals(Path, other.Path) && string.Equals(Summary, other.Summary) && string.Equals(Notes, other.Notes) && string.Equals(Verbs, other.Verbs))
		{
			return Priority == other.Priority;
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (this == obj)
		{
			return true;
		}
		if (obj.GetType() != GetType())
		{
			return false;
		}
		return Equals((RouteAttribute)obj);
	}

	public override int GetHashCode()
	{
		return (((((((((base.GetHashCode() * 397) ^ ((Path != null) ? Path.GetHashCode() : 0)) * 397) ^ ((Summary != null) ? Summary.GetHashCode() : 0)) * 397) ^ ((Notes != null) ? Notes.GetHashCode() : 0)) * 397) ^ ((Verbs != null) ? Verbs.GetHashCode() : 0)) * 397) ^ Priority;
	}
}
