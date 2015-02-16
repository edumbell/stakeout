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

		public ActionResult CreateGame(CreateGameModel model)
		{
			var existing = Game.GameList.Where(g => g.Name == model.Name).FirstOrDefault();
			if (existing == null)
			{

				var newGame = new Game()
				{
					Name = model.Name,
					GameId = Guid.NewGuid().ToString()
				};
				Game.GameList.Add(newGame);

				var joinModel = new JoinModel()
				{
					Name = model.PlayerName,
					GameId = newGame.GameId
				};
				return Join(joinModel);
			}
			else
			{
				var joinModel = new JoinModel()
				{
					Name = model.PlayerName,
					GameId = existing.GameId
				};
				return Join(joinModel);
			}

		}

		public ActionResult Join(JoinModel model)
		{
			var theGame = Game.GetGame(model.GameId);
			if (model.Name == null)
				throw new Exception("Tried to join with null name?");
			var existing = theGame.Players.Where(p => p.Name == model.Name).FirstOrDefault();
			if (existing != null)
			{
				return View("Interface", new StartGameModel()
				{
					PlayerId = existing.Id,
					GameId = model.GameId
				});
			}

			if (theGame.HasStarted)
			{
				return Content("Game has already started");
			}
			var player = new Player()
			{
				Game = theGame,
				Bites = 0,
				Name = model.Name,
				Strategy = StrategyEnum.Human,
				Id = Guid.NewGuid().ToString()
			};
			theGame.Players.Add(player);
			return View("Interface", new StartGameModel()
			{
				PlayerId = player.Id,
				GameId = model.GameId
			});

		}

		public ActionResult DayInstruction(DayFormModel model)
		{
			var theGame = Game.GetGame(model.GameId);
			if (theGame.CurrentTurn().DayComplete
	|| theGame.CurrentTurn().Id != model.TurnId)
				return Content("Turn already complete! Did you use the back button in your browser??");
			else
			{
				var instruction = new DayInstruction(theGame, model);
				theGame.ProcessDayInstruction(instruction);
				return Content(null);
			}
		}

		public ActionResult NightInstruction(NightFormModel model)
		{
			var theGame = Game.GetGame(model.GameId);
			if (theGame.CurrentTurn().NightComplete
				|| theGame.CurrentTurn().Id != model.TurnId)
				return Content("Turn already complete! Did you use the back button in your browser??");
			else
			{
				var instruction = new NightInstruction(theGame, model);
				theGame.ProcessNightInstruction(instruction);
				return Content(null);
			}
		}

		public ActionResult GetActions(string gameId, string pid)
		{
			var theGame = Game.GetGame(gameId);
			var player = theGame.GetPlayer(pid);
			if (player.IsDead)
			{
				return Content("You have been staked.  But don't worry, as a ghost, you can observe everything that goes on.... [coming soon]");
			}

			if (!theGame.CurrentTurn().NightComplete)
			{
				if (player.IsInJail)
				{
					return Content("It is night time.  You are in protective custody (i.e. jail).  At least you can't get bitten.");
				}
				else
				{
					var model = new NightFormModel()
					{
						GameId = gameId,
						ActorId = player.Id,
						OtherPlayers = theGame.MobilePlayers.Exclude(pid).ToSelectList(),
						IsVampire = player.IsVampire,
						TurnId = theGame.CurrentTurn().Id
					};
					return PartialView("_NightActions", model);
				}
			}
			else if (!theGame.CurrentTurn().DayComplete)
			{
				var model = new DayFormModel()
	{
		GameId = gameId,
		ActorId = player.Id,
		OtherPlayers = theGame.MobilePlayers.Exclude(pid).ToSelectList(),
		AllPlayers = theGame.MobilePlayers.ToSelectList(),
		TurnId = theGame.CurrentTurn().Id
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