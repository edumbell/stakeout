using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebApplication1.Models;
namespace WebApplication1.Controllers
{
	public class HomeController : Controller
	{


		public ActionResult Index()
		{
			return View();
		}

		public ActionResult Join(JoinModel model)
		{
			if (model.Name == null)
				throw new Exception("Tried to join with null name?");
			if (Game.TheGame.Turns.Any())
			{
				var existing = Game.TheGame.Players.Where(p => p.Name == model.Name).FirstOrDefault();
				if (existing != null)
				{
					return View("Interface", new StartGameModel()
					{
						PlayerId = existing.Id
					});
				}
				throw new Exception("Game has already started");
			}
			else
			{
				var p = new Player()
				{
					Bites = 0,
					Name = model.Name,
					Strategy = StrategyEnum.Human,
					Id = Guid.NewGuid().ToString()
				};
				Game.TheGame.Players.Add(p);
				return View("Interface", new StartGameModel()
				{
					PlayerId = p.Id
				});
			}

		}

		public ActionResult DayInstruction(DayFormModel model)
		{
			if (Game.TheGame.CurrentTurn().DayComplete
	|| Game.TheGame.CurrentTurn().Id != model.TurnId)
				return Content("Turn already complete! Did you use the back button in your browser??");
			else
			{
				var instruction = new DayInstruction(Game.TheGame, model);
				Game.TheGame.ProcessDayInstruction(instruction);
				return Content(null);
			}
		}

		public ActionResult NightInstruction(NightFormModel model)
		{
			if (Game.TheGame.CurrentTurn().NightComplete
				|| Game.TheGame.CurrentTurn().Id != model.TurnId)
				return Content("Turn already complete! Did you use the back button in your browser??");
			else
			{
				var instruction = new NightInstruction(Game.TheGame, model);
				Game.TheGame.ProcessNightInstruction(instruction);
				return Content(null);
			}
		}

		public ActionResult Restart()
		{
			Game.TheGame = new Game();
			return Content("Restarted");
		}

		public ActionResult GetActions(string pid)
		{
			var player = Game.GetPlayer(pid);
			if (player.IsDead)
			{
				return Content("You have been staked.  But don't worry, as a ghost, you can observe everything that goes on.... [coming soon]");
			}

			if (!Game.TheGame.CurrentTurn().NightComplete)
			{
				if (player.IsInJail)
				{
					return Content("It is night time.  You are in protective custody (i.e. jail).  At least you can't get bitten.");
				}
				else
				{
					var model = new NightFormModel()
					{
						ActorId = player.Id,
						OtherPlayers = Game.TheGame.MobilePlayers.Exclude(pid).ToSelectList(),
						IsVampire = player.IsVampire,
						TurnId = Game.TheGame.CurrentTurn().Id
					};
					return PartialView("_NightActions", model);
				}
			}
			else if (!Game.TheGame.CurrentTurn().DayComplete)
			{
				var model = new DayFormModel()
	{
		ActorId = player.Id,
		OtherPlayers = Game.TheGame.MobilePlayers.Exclude(pid).ToSelectList(),
		AllPlayers = Game.TheGame.MobilePlayers.ToSelectList(),
		TurnId = Game.TheGame.CurrentTurn().Id
	};

				return PartialView("_DayActions", model);
			}
			else
			{
				return Content("Turn already complete. Something has gone wrong.");
			}


		}
	}
}