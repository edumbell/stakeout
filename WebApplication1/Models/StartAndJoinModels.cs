using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{

	public class CreateGameModel
	{
		[DisplayName("Your Name")]
		[Required(ErrorMessage = "And by what name shall we know you?")]
		public string PlayerName { get; set; }

		[DisplayName("Game Name")]
		//[Required (ErrorMessage="Please enter a name for your game")]
		public string Name { get; set; }
		public bool DebugAllowed { get; set; }

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
		public bool DebugAllowed { get; set; }
	}
}