using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApplication1.Models
{
	public class JoinModel
	{
		public string Name { get; set; }
		public string Colour { get; set; }

	}

	public class StartGameModel
	{
		public string PlayerId { get; set; }

	}
}