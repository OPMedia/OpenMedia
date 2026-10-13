using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;

namespace MediaBrowser.Controller.Net;

public abstract class BasePeriodicWebSocketListener<TReturnDataType, TStateType> : IWebSocketListener, IDisposable where TReturnDataType : class where TStateType : WebSocketListenerState, new()
{
	protected readonly List<Tuple<IWebSocketConnection, CancellationTokenSource, TStateType>> ActiveConnections = new List<Tuple<IWebSocketConnection, CancellationTokenSource, TStateType>>();

	protected ILogger Logger;

	protected readonly CultureInfo UsCulture = new CultureInfo("en-US");

	protected abstract string Name { get; }

	protected abstract Task<TReturnDataType> GetDataToSend(TStateType state, CancellationToken cancellationToken);

	protected BasePeriodicWebSocketListener(ILogger logger)
	{
		if (logger == null)
		{
			throw new ArgumentNullException("logger");
		}
		Logger = logger;
	}

	public Task ProcessMessage(WebSocketMessageInfo message)
	{
		if (message == null)
		{
			throw new ArgumentNullException("message");
		}
		if (string.Equals(((WebSocketMessage<string>)message).MessageType, Name + "Start", StringComparison.OrdinalIgnoreCase))
		{
			Start(message);
		}
		if (string.Equals(((WebSocketMessage<string>)message).MessageType, Name + "Stop", StringComparison.OrdinalIgnoreCase))
		{
			Stop(message);
		}
		return Task.CompletedTask;
	}

	protected virtual void ParseMessageParams(string[] values)
	{
	}

	private void Start(WebSocketMessageInfo message)
	{
		string[] array = ((WebSocketMessage<string>)message).Data.Split(new char[1] { ',' });
		long.Parse(array[0], UsCulture);
		long intervalMs = long.Parse(array[1], UsCulture);
		if (array.Length > 2)
		{
			ParseMessageParams(array.Skip(2).ToArray());
		}
		CancellationTokenSource item = new CancellationTokenSource();
		Logger.Debug("{1} Begin transmitting over websocket to {0}", new object[2]
		{
			message.Connection.RemoteEndPoint,
			GetType().Name
		});
		TStateType item2 = new TStateType
		{
			IntervalMs = intervalMs
		};
		lock (ActiveConnections)
		{
			ActiveConnections.Add(new Tuple<IWebSocketConnection, CancellationTokenSource, TStateType>(message.Connection, item, item2));
		}
	}

	protected void SendData(bool force)
	{
		Tuple<IWebSocketConnection, CancellationTokenSource, TStateType>[] array;
		lock (ActiveConnections)
		{
			array = ActiveConnections.Where((Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> c) =>
			{
				if (c.Item1.State == WebSocketState.Open && !c.Item2.IsCancellationRequested)
				{
					TStateType item = c.Item3;
					if (force || (DateTime.UtcNow - item.DateLastSendUtc).TotalMilliseconds >= (double)item.IntervalMs)
					{
						return true;
					}
				}
				return false;
			}).ToArray();
		}
		Tuple<IWebSocketConnection, CancellationTokenSource, TStateType>[] array2 = array;
		foreach (Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> tuple in array2)
		{
			SendData(tuple);
		}
	}

	private async void SendData(Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> tuple)
	{
		IWebSocketConnection connection = tuple.Item1;
		try
		{
			TStateType state = tuple.Item3;
			CancellationToken cancellationToken = tuple.Item2.Token;
			TReturnDataType val = await GetDataToSend(state, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (val != null)
			{
				await connection.SendAsync<TReturnDataType>(new WebSocketMessage<TReturnDataType>
				{
					MessageType = Name,
					Data = val
				}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				state.DateLastSendUtc = DateTime.UtcNow;
			}
		}
		catch (OperationCanceledException)
		{
			if (tuple.Item2.IsCancellationRequested)
			{
				DisposeConnection(tuple);
			}
		}
		catch (Exception ex2)
		{
			Logger.ErrorException("Error sending web socket message {0}", ex2, new object[1] { Name });
			DisposeConnection(tuple);
		}
	}

	private void Stop(WebSocketMessageInfo message)
	{
		lock (ActiveConnections)
		{
			Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> tuple = ActiveConnections.FirstOrDefault((Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> c) => c.Item1 == message.Connection);
			if (tuple != null)
			{
				DisposeConnection(tuple);
			}
		}
	}

	private void DisposeConnection(Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> connection)
	{
		Logger.Debug("{1} stop transmitting over websocket to {0}", new object[2]
		{
			connection.Item1.RemoteEndPoint,
			GetType().Name
		});
		try
		{
			connection.Item2.Cancel();
			connection.Item2.Dispose();
		}
		catch (ObjectDisposedException)
		{
		}
		ActiveConnections.Remove(connection);
	}

	protected virtual void Dispose(bool dispose)
	{
		if (!dispose)
		{
			return;
		}
		lock (ActiveConnections)
		{
			Tuple<IWebSocketConnection, CancellationTokenSource, TStateType>[] array = ActiveConnections.ToArray();
			foreach (Tuple<IWebSocketConnection, CancellationTokenSource, TStateType> connection in array)
			{
				DisposeConnection(connection);
			}
		}
	}

	public void Dispose()
	{
		Dispose(dispose: true);
	}
}
