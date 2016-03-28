using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;
using WebApplication1.Models;

namespace WebApplication1.Hubs
{
	public class StakeHub : Hub
	{
		//public Player GetCurrentPlayer()
		//{
		//	return theGame.Players.Where(p => p.ConnectionId == this.Context.ConnectionId).Single();
		//}

		public void PushAllPlayerStatuses(Game theGame)
		{
			foreach (var p in theGame.Players.Where(p => p.Strategy == StrategyEnum.Human))
			{
				var html = p.NameSpan + " "
					+ (p.IsMayor ? "M" : "")
					+ (p.IsVampire ? "<span class='bites'>V</span>" : "");
				if (!p.IsVampire)
				{
					html += "<span class='bites'>";
					for (var x = 0; x < p.Bites; x++)
					{
						html += "x";
					}
					html += "</span>";
				}
				Clients.Client(p.ConnectionId).showPlayerStatus(html);
			}

		}
		public void AddAI(string gameId)
		{
			var theGame = Game.GetGame(gameId);
			string[] botNames = { "Zelda", "Xavier", "X-Ray", "Yehudi", "YolkFace", "Yasmin", "Wade", "Warrick", "Wanda", "123", "456", "789" };

			var name = botNames[new Random().Next(990) % botNames.Count()];
			while (theGame.Players.Where(x => x.Name == name).Any())
			{
				name = botNames[new Random().Next(990) % botNames.Count()];
			}

			var r = new Random();
			var p = new Player()
			{
				Game = theGame,
				Name = name,
				Strategy = StrategyEnum.AI,
				Id = Guid.NewGuid().ToString()
			};
			p.AI = new AI()
				{
					Hub = this,
					Me = p
				};
			p.SetColour();
			theGame.Players.Add(p);
			Announce(theGame, p.NameSpan + " (a bot) joined");
		}

		public void AnnounceComms(Game game, CommsEvent comms, bool accusing = false, string customMessage = null)
		{
			string lied = accusing ? " lied! -- " : " ";
			game.AddToLog(comms);
			game.AnnounceToAIs(comms);
			string msg = comms.Speaker.NameSpan + "> ";
			if (customMessage != null)
			{
				Announce(game, msg  + customMessage);
			}
			else
			{
				switch (comms.EventType)
				{
					case CommsTypeEnum.IWasBitten:
						msg += "I've been bitten!";
						break;
					case CommsTypeEnum.WillSleep:
						msg += "I'm staying at home";
						break;
					case CommsTypeEnum.Slept:
						msg += comms.Whom.NameSpan + lied + " stayed at home";
						break;
					case CommsTypeEnum.GenericLie:
						msg += comms.Whom.NameSpan + " is a liar!";
						break;
					case CommsTypeEnum.WentOut:
						if (comms.Where == null)
						{
							msg += comms.Whom.NameSpan + lied + " went out";
						}
						else
						{
							msg += comms.Whom.NameSpan + lied + " was at " + comms.Where.NameSpan + "'s";
						}
						break;
				}
				Announce(game, msg);
			}
		}

		public void LinkConnectionToPlayer(string gameId, string pid)
		{
			var theGame = Game.GetGame(gameId);
			var p = theGame.GetPlayer(pid);
			if (p != null)
			{
				p.ConnectionId = this.Context.ConnectionId;
				p.AI.Hub = this;
				var all = AllClientsInGame(theGame);

				Announce(theGame, p.NameSpan + " joined");

			}
			var present = Game.PlayerListToString(theGame.Players.Except(new[] { p }));
			if (present.Any())
			{
				SendPrivate(p, present + " are already here.");
			}
		}

		public void Announce(Game game, string message)
		{
			System.Threading.Thread.Sleep(100);
			var all = AllClientsInGame(game);
			foreach (var client in all)
			{
				client.announce(message);
			}
		}

		public void Send(string gameId, string pid, string message)
		{
			var theGame = Game.GetGame(gameId);
			System.Threading.Thread.Sleep(100);
			var p = theGame.GetPlayer(pid);
			var all = AllClientsInGame(p.Game);
			foreach (var client in all)
			{

				client.addNewMessageToPage(p.Name +
					(p.IsDead ? "'s ghost" : "")
					, message, p.Colour);
			}
		}

		public void CheckForRefresh(string gameId)
		{
			var theGame = Game.GetGame(gameId);
			if (theGame != null && theGame.HasStarted)
			{
				var client = Clients.Client(this.Context.ConnectionId);
				if (theGame.CurrentTurn().NightComplete)
					client.startDay(theGame.CurrentTurn().Id);
				else
					client.startNight(theGame.CurrentTurn().Id);
			}
		}


		public void Start(string gameId)
		{
			var theGame = Game.GetGame(gameId);
			if (!theGame.HasStarted)
			{
				theGame.StartGame(this);
				PushAllPlayerStatuses(theGame);
				foreach (var p in theGame.Players)
				{
					if (p.IsVampire)
					{
						SendPrivate(p, "You are a vampire. Don't tell anyone! There may or may not be other vampires...");
					}
					else
					{
						SendPrivate(p, "You are a villager.  Don't act suspiciously, becuase there is at least one vampire in the village, and only you know for sure that you aren't a vampire.");
					}
				}
				var mayor = theGame.Players.Where(x => x.IsMayor).Single();
				Announce(theGame, mayor.Name + " is the mayor (gets to decide split votes).  Let's hope " + mayor.Name + " isn't a vampire!");

				var all = AllClientsInGame(theGame);
				foreach (var client in all)
				{
					//client.gameStarted();
				}

				//SendStartNight(theGame, 1);
				//theGame.GetNightInstructionsFromAIs();
			}
		}

		public void SendStartNight(Game game, int id)
		{
			var all = AllClientsInGame(game);
			string msg = "------------- Night " + (id);
			foreach (var client in all)
			{
				client.startNIght(id);
				client.privateMessage(msg);
			}
			Announce(game, msg);
		}

		public void SendStartDay(Game game, int id)
		{
			var all = AllClientsInGame(game);
			var msg = "-----===----- Morning " + (id);
			foreach (var client in all)
			{
				client.startDay(id);
				client.privateMessage(msg);
			}
			PushAllPlayerStatuses(game);
			Announce(game, msg);
		}

		public void DisplayVotes(Game game, double needed)
		{
			string result = "";
			foreach (var p in game.MobilePlayers)
			{
				var jailMeVoters = game.CurrentTurn().DayInstructions.Where(d => d.JailVote == p).Select(x => x.Actor);
				var jailMeWeight = game.CurrentTurn().DayInstructions.Where(d => d.JailVote == p).Sum(d => d.Weight);
				var passedClass = jailMeWeight >= needed ? "passed" : "";
				var verb = jailMeWeight >= needed ? "passed by" : "proposed by";
				if (jailMeVoters.Any())
				{
					result += "<div class=\"" + passedClass + "\"><span class=\"voteleft\">Jail " + p.NameSpan + "</span><span class=\"voteright\"><span class=\"help\"> " + verb + " </span> " + Game.PlayerListToString(jailMeVoters, ", ") + "</span></div>";
				}
			}

			foreach (var p in game.MobilePlayers)
			{
				var stakeMeVoters = game.CurrentTurn().DayInstructions.Where(d => d.StakeVote == p).Select(x => x.Actor);
				var stakeMeWeight = game.CurrentTurn().DayInstructions.Where(d => d.StakeVote == p).Sum(d => d.Weight);
				var passedClass = stakeMeWeight >= needed ? "passed" : "";
				var verb = stakeMeWeight >= needed ? "passed by" : "proposed by";
				if (stakeMeVoters.Any())
				{
					result += "<div class=\"" + passedClass + "\"><span class=\"voteleft\">Stake " + p.NameSpan + "</span><span class=\"voteright\"><span class=\"help\"> " + verb + " </span> " + Game.PlayerListToString(stakeMeVoters, ", ") + "</span></div>";
				}
			}
			var all = AllClientsInGame(game);
			foreach (var client in all)
			{
				client.displayActionsEntered(result);
			}
		}

		public void DisplayNightActionsEntered(Game theGame)
		{
			var entered = theGame.CurrentTurn().NightInstructions.Select(x => x.Actor);
			var stillToEnter = theGame.MobilePlayers.Except(
				entered);

			string msg = "";
			if (stillToEnter.Any())
			{
				msg = "Still waiting on: " + Game.PlayerListToString(stillToEnter);
			}
			var all = AllClientsInGame(theGame);
			foreach (var client in all)
			{
				client.displayWaitingOn(msg);
			}
		}


		public void SendPrivate(Player player, string message)
		{

			if (player.Strategy == StrategyEnum.Human)
			{
				System.Threading.Thread.Sleep(10);
				Clients.Client(player.ConnectionId).privateMessage(message);
			}
		}

		private IEnumerable<dynamic> AllClientsInGame(Game game)
		{
			var result = game.Players.Where(p => p.Strategy == StrategyEnum.Human)
				.Select(p => Clients.Client(p.ConnectionId));
			return result;
		}

	}
}
