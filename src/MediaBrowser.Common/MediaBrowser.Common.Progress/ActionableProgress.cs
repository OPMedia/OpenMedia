using System;

namespace MediaBrowser.Common.Progress;

public class ActionableProgress<T> : IProgress<T>
{
	private Action<T> _action;

	public event EventHandler<T> ProgressChanged;

	public void RegisterAction(Action<T> action)
	{
		_action = action;
	}

	public void Report(T value)
	{
		if (ProgressChanged != null)
		{
			ProgressChanged(this, value);
		}
		_action?.Invoke(value);
	}
}
