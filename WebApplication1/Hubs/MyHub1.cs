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
		public Player GetCurrentPlayer()
		{
			return Game.TheGame.Players.Where(p => p.ConnectionId == this.Context.ConnectionId).Single();
		}

		public void PushAllPlayerStatuses()
		{
			foreach (var p in Game.TheGame.Players.Where(p => p.Strategy == StrategyEnum.Human))
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
		public void AddAI()
		{
			string[] botNames = { "Zelda", "Xavier", "X-Ray", "Yehudi", "YolkFace", "Yasmin","Wade","Warrick", "Wanda", "123","456","789" };

			var name = botNames[ new Random().Next(990) % botNames.Count()];
			while (Game.TheGame.Players.Where(x => x.Name == name).Any())
			{
				name = botNames[new Random().Next(990) % botNames.Count()];
			}

			var r = new Random();
			var p = new Player()
			{
				Name =   name,
				Strategy = StrategyEnum.AI,
				Id = Guid.NewGuid().ToString()
			};
			p.AI = new AI()
				{
					Hub = this,
					Me = p
				};
			Game.TheGame.Players.Add(p);
			Clients.All.addNewMessageToPage(p.Name, "joined", p.Colour);
		}


		public void LinkConnectionToPlayer(string pid)
		{
			var p = Game.GetPlayer(pid);
			if (p != null)
			{
				p.ConnectionId = this.Context.ConnectionId;
				Clients.All.addNewMessageToPage(p.Name, "joined", p.Colour);
			}
		}

		public void Announce(string message)
		{
			System.Threading.Thread.Sleep(300);
			Clients.All.announce(message);
		}

		public void Send(string pid, string message)
		{
			System.Threading.Thread.Sleep(100);
			var p = Game.GetPlayer(pid);
			Clients.All.addNewMessageToPage(p.Name, message, p.Colour);
		}

		public void CheckForRefresh()
		{
			if (Game.TheGame != null && Game.TheGame.HasStarted)
			{
				var player = GetCurrentPlayer();
				var client = Clients.Client(this.Context.ConnectionId);
				if (Game.TheGame.CurrentTurn().NightComplete)
					client.startDay(Game.TheGame.CurrentTurn().Id);
				else
					client.startNight(Game.TheGame.CurrentTurn().Id);
			}
		}


		public void Start()
		{
			if (!Game.TheGame.HasStarted)
			{
				Game.TheGame.StartGame(this);
				PushAllPlayerStatuses();
				foreach (var p in Game.TheGame.Players)
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
				var mayor = Game.TheGame.Players.Where(x => x.IsMayor).Single();
				Announce(mayor.Name + " is the mayor (gets to decide split votes).  Let's hope " + mayor.Name + " isn't a vampire!");
				Clients.All.gameStarted();
				SendStartNight(1);
				Game.TheGame.GetNightInstructionsFromAIs();
			}
		}

		public void SendStartNight(int id)
		{
			Clients.All.startNIght(id);
			string msg = "------------- Night " + (id);
			Clients.All.privateMessage(msg);
			Announce(msg);
		}

		public void SendStartDay(int id)
		{
			Clients.All.startDay(id);
			PushAllPlayerStatuses();
			var msg = "-----===----- Morning " + (id);
			Clients.All.privateMessage(msg);
			Announce(msg);
		}

		public void DisplayVotes(double needed)
		{
			string result = "";
			foreach (var p in Game.TheGame.MobilePlayers)
			{
				var jailMeVoters = Game.TheGame.CurrentTurn().DayInstructions.Where(d => d.JailVote == p).Select(x => x.Actor);
				var jailMeWeight = Game.TheGame.CurrentTurn().DayInstructions.Where(d => d.JailVote == p).Sum(d => d.Weight);
				var passedClass = jailMeWeight >= needed ? "passed" : "";
				var verb = jailMeWeight >= needed ? "passed by" : "proposed by";
				if (jailMeVoters.Any())
				{
					result += "<div class=\"" + passedClass + "\"><span class=\"voteleft\">Jail " + p.NameSpan + "</span><span class=\"voteright\"><span class=\"help\"> " + verb + " </span> " + Game.TheGame.PlayerListToString(jailMeVoters, ", ") + "</span></div>";
				}
			}

			foreach (var p in Game.TheGame.MobilePlayers)
			{
				var stakeMeVoters = Game.TheGame.CurrentTurn().DayInstructions.Where(d => d.StakeVote == p).Select(x => x.Actor);
				var stakeMeWeight = Game.TheGame.CurrentTurn().DayInstructions.Where(d => d.StakeVote == p).Sum(d => d.Weight);
				var passedClass = stakeMeWeight >= needed ? "passed" : "";
				var verb = stakeMeWeight >= needed ? "passed by" : "proposed by";
				if (stakeMeVoters.Any())
				{
					result += "<div class=\"" + passedClass + "\"><span class=\"voteleft\">Stake " + p.NameSpan + "</span><span class=\"voteright\"><span class=\"help\"> " + verb + " </span> " + Game.TheGame.PlayerListToString(stakeMeVoters, ", ") + "</span></div>";
				}
			}
			Clients.All.displayActionsEntered(result);
		}

		public void DisplayNightActionsEntered()
		{
			var entered = Game.TheGame.CurrentTurn().NightInstructions.Select(x => x.Actor);
			var stillToEnter = Game.TheGame.MobilePlayers.Except(
				entered);
			if (stillToEnter.Any())
			{
				Clients.All.displayWaitingOn("Still waiting on: " + Game.TheGame.PlayerListToString(stillToEnter));
			}
			else
			{
				Clients.All.displayWaitingOn("");
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

	}
}
