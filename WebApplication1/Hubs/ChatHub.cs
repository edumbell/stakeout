using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;

namespace WebApplication1.Hubs
{
	public class ChatHub : Hub
	{
		public void Hello()
		{
			Clients.All.hello();
		}

		public void Update(string text, string colour, string id, bool finish)
		{
			Clients.All.UpdateText(text, colour, id, finish);
		}

	}
}