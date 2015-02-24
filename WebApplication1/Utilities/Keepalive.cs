using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Threading.Tasks;

namespace WebApplication1.Utilities
{
	public class Keepalive
	{
		private static Keepalive _Singleton = new Keepalive();
		public static void EnsureStarted(HttpContext context)
		{
			if (!_Singleton._Started)
			{
				lock (_Singleton)
				{
					if (!_Singleton._Started)
					{
						_Singleton._Started = true;
						_Singleton._Url = "http://" + context.Request.Url.Host;

						System.Threading.ThreadPool.QueueUserWorkItem(delegate
						{
							_Singleton.Bump();
						}, null);
					}
				}
			}

		}
		private bool _Started;
		private string _Url;
		public async Task Bump()
		{
			await Task.Delay(TimeSpan.FromMinutes(10), System.Threading.CancellationToken.None);
			using (var client = new System.Net.WebClient())
			{
				client.DownloadString(_Url);
			}
			Bump();
		}
	}
}