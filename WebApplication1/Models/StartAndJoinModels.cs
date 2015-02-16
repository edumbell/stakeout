using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.ComponentModel;

namespace WebApplication1.Models
{

	public class CreateGameModel
	{
		[DisplayName("Your Name")]
		public string PlayerName { get; set; }

		[DisplayName("Game Name")]
		public string Name { get; set; }

	}

	public class JoinModel
	{
		public string GameId { get; set; }
		public string Name { get; set; }
		public string Colour { get; set; }

	}

	public class StartGameModel
	{
		public string GameId { get; set; }
		public string PlayerId { get; set; }

	}
}