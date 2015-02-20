using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{

	public class StatusModel
	{
		public List<Player> Players { get; set; }
		public bool DebugAllowed { get; set; }

	}


}