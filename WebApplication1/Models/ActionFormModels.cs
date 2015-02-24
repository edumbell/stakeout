using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Collections;


namespace WebApplication1.Models
{
	public class NightFormModel
	{

		public string GameId { get; set; }
		public IEnumerable<System.Web.Mvc.SelectListItem> OtherPlayers { get; set; }
		public int TurnId { get; set; }
		public string ActorId { get; set; }
		public NightActionEnum Action { get; set; }
		public string Whom { get; set; }
		public bool IsVampire;
	}

	public class DayFormModel
	{
		public string GameId { get; set; }
		public int TurnId { get; set; }
		public string ActorId { get; set; }
		public IEnumerable<System.Web.Mvc.SelectListItem> OtherPlayers { get; set; }

		public IEnumerable<System.Web.Mvc.SelectListItem> AllPlayers { get; set; }
		public string KillWhom { get; set; }
		public string JailWhom { get; set; }

	}

	public class CommsFormModel
	{
		public string GameId { get; set; }
		public string ActorId { get; set; }
		public string WhomId { get; set; }
		public IEnumerable<System.Web.Mvc.SelectListItem> OtherPlayers { get; set; }
		public IEnumerable<System.Web.Mvc.SelectListItem> Options()
		{
			var opts = new List<CommsTypeEnum>();
			opts.Add(CommsTypeEnum.IWasBitten);
			opts.Add(CommsTypeEnum.Slept);
			opts.Add(CommsTypeEnum.WentOut);
			opts.Add(CommsTypeEnum.GenericLie);
			return opts.ToSelectList();
		}
		public CommsTypeEnum CommsType { get; set; }
	}
}