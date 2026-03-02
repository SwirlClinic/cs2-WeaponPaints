using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public class RefreshListener : IDisposable
{
	private readonly HttpListener _listener;
	private readonly WeaponPaints _plugin;
	private readonly CancellationTokenSource _cts = new();
	private readonly int _port;

	public RefreshListener(WeaponPaints plugin, int port)
	{
		_plugin = plugin;
		_port = port;
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://*:{_port}/");
	}

	public void Start()
	{
		try
		{
			_listener.Start();
			_ = Task.Run(() => ListenLoop(_cts.Token));
			Utility.Log($"[RefreshListener] Listening on port {_port}");
		}
		catch (Exception ex)
		{
			Utility.Log($"[RefreshListener] Failed to start: {ex.Message}");
		}
	}

	private async Task ListenLoop(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _listener.IsListening)
		{
			try
			{
				var context = await _listener.GetContextAsync();
				_ = Task.Run(() => HandleRequest(context));
			}
			catch (HttpListenerException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (Exception ex)
			{
				Utility.Log($"[RefreshListener] Error accepting request: {ex.Message}");
			}
		}
	}

	private async Task HandleRequest(HttpListenerContext context)
	{
		var response = context.Response;
		try
		{
			if (context.Request.HttpMethod != "POST" ||
				context.Request.Url?.AbsolutePath.TrimEnd('/') != "/refresh")
			{
				response.StatusCode = 404;
				return;
			}

			string steamId;
			using (var reader = new StreamReader(context.Request.InputStream))
			{
				steamId = (await reader.ReadToEndAsync()).Trim();
			}

			if (string.IsNullOrEmpty(steamId))
			{
				response.StatusCode = 400;
				var errorBytes = Encoding.UTF8.GetBytes("Missing steamid");
				await response.OutputStream.WriteAsync(errorBytes);
				return;
			}

			_plugin.RefreshPlayerBySteamId(steamId);

			response.StatusCode = 200;
			var okBytes = Encoding.UTF8.GetBytes("OK");
			await response.OutputStream.WriteAsync(okBytes);
		}
		catch (Exception ex)
		{
			response.StatusCode = 500;
			var errBytes = Encoding.UTF8.GetBytes(ex.Message);
			await response.OutputStream.WriteAsync(errBytes);
		}
		finally
		{
			response.Close();
		}
	}

	public void Dispose()
	{
		_cts.Cancel();
		try { _listener.Stop(); } catch { }
		try { _listener.Close(); } catch { }
		_cts.Dispose();
	}
}
