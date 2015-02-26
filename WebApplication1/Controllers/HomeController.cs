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

		public ActionResult Warn()
		{
			Game.UpdateInProgress = true;
			foreach (var g in Game.GameList)
			{
				g.Announce("WARNING, UPDATE IN PROGRESS, YOUR GAME MAY BE INTERRUPTED IN A FEW MINUTES.  SORRY!");
			}
			return Content(DateTime.Now.ToShortTimeString());
		}

		public ActionResult Index()
		{

			WebApplication1.Utilities.Keepalive.EnsureStarted(System.Web.HttpContext.Current);
			return View();
		}

		public ActionResult CreateGame(CreateGameModel model)
		{
			var existing = Game.GameList.Where(g => g.Name == model.Name).FirstOrDefault();
			if (existing == null)
			{
				if (string.IsNullOrEmpty(model.Name))
					model.Name = model.PlayerName + " " + DateTime.Now.ToString("hh:mm");
				var newGame = new Game()
				{
					DebugAllowed = model.DebugAllowed,
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
					GameId = model.GameId,
					DebugAllowed = theGame.DebugAllowed
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
			player.SetColour();
			return View("Interface", new StartGameModel()
			{
				PlayerId = player.Id,
				GameId = model.GameId,
				DebugAllowed = theGame.DebugAllowed
			});

		}
		public ActionResult GetPlayerList(string gameId)
		{
			var game = Game.GameList.Where(g => g.GameId == gameId).Single();
			return PartialView("_PlayerList", game);
		}

		public ActionResult GetDebug(string gameId, string playerId)
		{
			var game = Game.GameList.Where(g => g.GameId == gameId).Single();
			if (!game.DebugAllowed)
			{
				return Content("Naughty naughty!");
			}
			var player = game.GetPlayer(playerId);
			var allLogs = game.Log.Where(l => l.Subject == player || l.Whom == player)
				.OrderBy(l => l.Turn);
			string currentTurn = "";
			string logResult = "<h4 class='debug-heading'>Debug info for " + player.NameSpan
				+ (player.IsVampire ? " V" : "");

			logResult += "<span class='bites'>";
			for (var x = 0; x < player.Bites; x++)
			{
				logResult += "x";
			}
			logResult += "</span>";
			logResult += "</h4>";
			if (player.AI != null)
			{
				logResult += "Estiamted suspicion against self: " + player.AI.SuspicionAgainstMe + "</br>";
			}
			if (player.Strategy == StrategyEnum.AI)
			{
				foreach (var r in player.AI.Relations)
				{
					var otherPlayer = game.GetPlayer(r.PlayerId);
					string relation = otherPlayer.NameSpan;
					relation += " <span class='enmity'>e:" + r.Enmity.ToString("0.0") + "</span>";
					relation += " <span class='suspicion'>s:" + r.DarkSideSuspicion.ToString("0.0") + "</span>";
					relation += " b " + r.GussedBites.ToString("0.0");
					logResult += relation + "<br/>";
				}
			}

			foreach (var log in allLogs)
			{
				if (log.Turn != currentTurn)
				{
					currentTurn = log.Turn;
					logResult += currentTurn + "<br/>";
				}
				logResult += log.ToString() + "<br/>";
			}
			if (player.AI != null)
				logResult += player.AI.TraceLog;
			return Content(logResult);
		}

		public ActionResult DayInstruction(DayFormModel model)
		{
			var theGame = Game.GetGame(model.GameId);
			if (theGame.CurrentTurn().DayComplete
	|| theGame.CurrentTurn().Id != model.TurnId)
			{
				theGame.Error += "Turn already complete! theGame.CurrentTur().Id = " + theGame.CurrentTurn().Id
					+ ".  theGame.CurrentTurn().DayComplete = " + theGame.CurrentTurn().DayComplete
				 + ".  model.TurnId = " + model.TurnId
						;
				return Content("Turn already complete! Did you use the back button in your browser??");
			}
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

		public ActionResult CommsForm(string gameId, string pid)
		{
			var theGame = Game.GetGame(gameId);
			var player = theGame.GetPlayer(pid);
			if (player.IsDead)
			{
				return Content("");
			}
			var model = new CommsFormModel()
			{
				GameId = gameId,
				ActorId = player.Id,
				OtherPlayers = theGame.MobilePlayers.Exclude(pid).ToSelectList(),
			};

			return PartialView("_CommsActions", model);
		}

		public ActionResult SubmitComms(CommsFormModel model)
		{
			var theGame = Game.GetGame(model.GameId);
			var player = theGame.GetPlayer(model.ActorId);
			var comms = new CommsEvent(player, model.CommsType, false, theGame.GetPlayer(model.WhomId));
			theGame.Hub.AnnounceComms(theGame, comms);
			return Content(null);
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