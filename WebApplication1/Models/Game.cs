using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApplication1.Models
{
	public class Game
	{
		public static Game TheGame = new Game();

		public Game()
		{
			Turns = new Dictionary<int, Turn>();
			Players = new List<Player>();
			//Turns.Add(0, new Turn());
		}

		public WebApplication1.Hubs.StakeHub Hub { get; set; }

		public List<Player> Players { get; set; }
		public Dictionary<int, Turn> Turns { get; set; }

		public static Player GetPlayer(string id)
		{
			return Game.TheGame.Players.Where(p => p.Id == id).SingleOrDefault();
		}


		public List<Player> MobilePlayers
		{
			get
			{
				return Players.Where(p => !p.IsDead && !p.IsInJail).ToList();
			}
		}

		private IOrderedEnumerable<Player> RandomPlayers()
		{
			var r = new Random();
			return Players.OrderBy(x => r.NextDouble());
		}

		public void StartGame(WebApplication1.Hubs.StakeHub hub)
		{
			Hub = hub;
			var n = Players.Count();
			var nvampires = 1;
			var r = new Random();
			RandomPlayers().First().IsMayor = true;
			RandomPlayers().First().IsVampire = true;

			foreach (var p in RandomPlayers())
			{
				if ((nvampires + 1) >= n / 2.0)
					break;
				if (r.NextDouble() > (n - 2) * 0.1)
				{
					p.IsVampire = true;
					nvampires++;
				}
			}
			Turns = new Dictionary<int, Turn>();
			StartTurn();
		}

		public void StartTurn()
		{
			var i = Turns.Keys.Any() ? Turns.Keys.Max() + 1 : 1;
			var t = new Turn()
			{
				Id = i,
				NightInstructions = new List<NightInstruction>(),
				DayInstructions = new List<DayInstruction>()
			};
			Turns.Add(i, t);
		}

		public Turn CurrentTurn()
		{
			var i = Turns.Keys.Max();
			return Turns[i];
		}

		public bool HasStarted
		{
			get
			{
				return Turns.Any();
			}
		}
		public void ProcessNightInstruction(NightInstruction i)
		{
			if (CurrentTurn().NightInstructions.Any())
			{
				var existing = CurrentTurn().NightInstructions.Where(x => x.Actor == i.Actor).FirstOrDefault();
				if (existing != null)
				{
					throw new Exception("Night instructions already given for player & turn.");
				}
			}
			CurrentTurn().NightInstructions.Add(i);
			Hub.DisplayNightActionsEntered();
			if (CurrentTurn().NightInstructions.Count() == MobilePlayers.Count())
			{
				FinishNight();
			}
		}

		public void ProcessDayInstruction(DayInstruction i)
		{
			if (CurrentTurn().DayInstructions.Any())
			{
				var existing = CurrentTurn().DayInstructions.Where(x => x.Actor == i.Actor).FirstOrDefault();
				if (existing != null)
				{
					throw new Exception("Day instructions already given for player & turn.");
				}
			}
			CurrentTurn().DayInstructions.Add(i);
			Hub.DisplayVotes();

			if (CurrentTurn().DayInstructions.Count() == MobilePlayers
				.Where(m => m.Strategy == StrategyEnum.Human)
				.Count())
			{
				// all human orders entered
				GetDayInstructionsFromAIs();
			}

			if (CurrentTurn().DayInstructions.Count() == MobilePlayers.Count())
			{
				FinishDay();
			}
		}

		public string PlayerListToString(IEnumerable<Player> players)
		{
			return string.Join(" and ", players.Select(p => p.NameSpan).ToArray());
		}

		public void FinishNight()
		{

			foreach (var i in CurrentTurn().NightInstructions)
			{
				var p = i.Actor;
				if (p.IsInJail)
					throw new Exception("Jailed actor submitted action?");
				if (i.Action == NightActionEnum.Bite)
				{
					if (!p.IsVampire)
						throw new Exception("Non-vampire tried to bite?");
					if (i.Whom.IsVampire)
					{
						Hub.SendPrivate(i.Whom, p.NameSpan + " came to try and bite you. You have a new vampire friend.");
						Hub.SendPrivate(p, "You went to bite " + i.Whom.NameSpan + " but they are a vampire! You have a new vampire friend.");
						if (i.Actor.Strategy == StrategyEnum.AI)
						{
							i.Actor.AI.TellFellowVampire(i.Whom.Id);
						}
					}
					else
					{
						Hub.SendPrivate(p, "You feasted on " + i.Whom.NameSpan + "'s blood while they slept. What's for pudding?");
						i.Whom.Bites++;
						string msg = "You were bitten in the night!";
						if (i.Whom.Bites == 2)
						{
							msg += "  This is your second bite. One more and you become a vampire. You might want to keep this a secret...";
						}
						Hub.SendPrivate(i.Whom, msg);
						if (i.Whom.Strategy == StrategyEnum.AI)
						{
							i.Whom.AI.TellBitten();
						}

					}
				}

				// send results of watching/biting
				if (i.Action == NightActionEnum.Watch || i.Action == NightActionEnum.Bite)
				{
					var watcheeInsruction = CurrentTurn().NightInstructions.Where(x => x.Actor == i.Whom).SingleOrDefault();
					if (watcheeInsruction == null)
						throw new Exception("Missing night instruction");
					var watcheeAction = watcheeInsruction.Action;
					if (watcheeAction == NightActionEnum.Sleep)
					{
						Hub.SendPrivate(p, i.Whom.NameSpan + " spent the night at home.");
					}
					else
					{
						Hub.SendPrivate(p, i.Whom.NameSpan + " snuck out of the house in the middle of the night!");
					}

					if (i.Actor.Strategy == StrategyEnum.AI)
					{
						i.Actor.AI.TellWatchResult(watcheeAction == NightActionEnum.Sleep);
					}

					// send 'met' messages

					var met = CurrentTurn().NightInstructions.Where(x => x.Whom == i.Whom && x != i).Select(x => x.Actor);
					if (met.Any())
					{
						var metlist = PlayerListToString(met);
						Hub.SendPrivate(p, "While lurking in " + i.Whom.NameSpan + "'s garden, you met " + metlist + "!");
					}
					else
					{
						Hub.SendPrivate(p, "Nobody else visited " + i.Whom.NameSpan + "'s house.");
					}
				}
			}
			if (Players.Where(p => p.IsInJail).Any())
			{
				Players.Where(p => p.IsInJail).Single().IsInJail = false;
			}
			foreach (var p in Players)
			{
				if (!p.IsVampire && p.Bites >= 3)
				{
					p.IsVampire = true;
					Hub.SendPrivate(p, "You are now a vampire! You must help the other vampires take over the village.");
				}
			}
			CurrentTurn().NightComplete = true;
			Hub.SendStartDay(CurrentTurn().Id);

		}

		public void GetNightInstructionsFromAIs()
		{
			foreach (var p in MobilePlayers.Where(p => p.Strategy == StrategyEnum.AI))
			{
				var inst = p.AI.GetNightInstruction(MobilePlayers.Select(m => m.Id).ToList(), CurrentTurn().Id);
				var inst2 = new NightInstruction(Game.TheGame, inst);
				ProcessNightInstruction(inst2);
			}
		}

		public void GetDayInstructionsFromAIs()
		{
			foreach (var p in MobilePlayers.Where(p => p.Strategy == StrategyEnum.AI))
			{
				var inst = p.AI.GetDayInstruction(MobilePlayers.Select(m => m.Id).ToList(), CurrentTurn().Id);
				var inst2 = new DayInstruction(Game.TheGame, inst);
				ProcessDayInstruction(inst2);
			}
		}

		public void NewMayor()
		{
			var m = RandomPlayers().First();
			Hub.Announce("The town needs a new mayor.  Lots are drawn. It is " + m.NameSpan);
			m.IsMayor = true;
		}

		public void FinishDay()
		{
			Dictionary<string, double> stakeVotes = new Dictionary<string, double>();
			Dictionary<string, double> jailVotes = new Dictionary<string, double>();
			foreach (var p in MobilePlayers)
			{
				stakeVotes.Add(p.Id, 0);
				jailVotes.Add(p.Id, 0);
			}
			// nobody should be in jail at this point
			var votesNeeded = MobilePlayers.Count * 0.501;
			foreach (var d in CurrentTurn().DayInstructions)
			{
				var weighting = d.Actor.IsMayor ? 1.1 : 1;
				if (d.JailVote != null)
				{
					jailVotes[d.JailVote.Id] += weighting;
				}
				if (d.StakeVote != null)
				{
					stakeVotes[d.StakeVote.Id] += weighting;
					if (GetPlayer(d.StakeVote.Id).Strategy == StrategyEnum.AI)
					{
						GetPlayer(d.StakeVote.Id).AI.TellStakeVote(d.Actor.Id);
					}
				}
			}

			foreach (var p in MobilePlayers)
			{
				if (jailVotes[p.Id] >= votesNeeded)
				{
					var toJail = p;
					toJail.IsInJail = true;
					var mob = CurrentTurn().DayInstructions.Where(v => v.JailVote == toJail).Select(v => v.Actor);
					Hub.Announce(toJail.NameSpan + " has been put into protective custody for the night, by "
						+ PlayerListToString(mob)
						);
				}

				if (stakeVotes[p.Id] >= votesNeeded)
				{
					var tostake = p;
					var mob =
						PlayerListToString(CurrentTurn().DayInstructions.Where(v => v.StakeVote == tostake).Select(v => v.Actor));
					if (tostake.IsInJail)
					{
						Hub.Announce("Luckily, " + tostake.NameSpan + " was in protective custoy when any angry mob (" + mob + ") came to try and stake them.");
					}
					else
					{
						Hub.Announce(tostake.NameSpan + " has been staked by an angry mob (" + mob + ")!  I hope he/she really was a vampire....");
						tostake.IsDead = true;
						if (tostake.IsMayor)
						{
							NewMayor();
						}
					}
				}

			}

			if (!Players.Where(p => p.IsVampire && !p.IsDead).Any())
			{
				Hub.Announce("Well done! All the vampires are dead. The villagers live happily ever after.");
			}

			if (!Players.Where(p => !p.IsVampire && !p.IsDead).Any())
			{
				Hub.Announce("Well done!  Everyone left alive is a vampire. You live happily ever after.");
			}

			if (CurrentTurn().Id == 10)
			{
				Hub.Announce("After 10 days, the vampire hunter arrives to help. All remaining villagers are saved, and all remaining vampires are slain.");
				var hunams = Players.Where(p => !p.IsVampire && !p.IsDead);
				var list = PlayerListToString(hunams);
				Hub.Announce("Well done, " + list);
				//var vamplist = Players.Where(p => p.IsVampire && !p.IsDead);
			}

			StartTurn();
			Hub.SendStartNight(CurrentTurn().Id);
			GetNightInstructionsFromAIs();
		}



	}

	public class Turn
	{
		public int Id;
		public bool NightComplete { get; set; }
		public bool DayComplete { get; set; }

		public List<NightInstruction> NightInstructions { get; set; }
		public List<DayInstruction> DayInstructions { get; set; }
	}

	public class Player
	{
		public bool IsMayor { get; set; }
		public bool IsVampire { get; set; }
		public bool IsDead { get; set; }
		public bool IsInJail { get; set; }
		public int Bites { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }

		public string NameSpan
		{
			get
			{
				return "<strong style='color:" + Colour + "'>" + Name + "</strong>";
			}
		}
		public string Colour
		{
			get
			{
				string[] cols = { "Red", "Blue", "Purple", "Green", "Brown", "Teal" };
				var cid = Name.ToCharArray().Sum(x => (int)x) % cols.Count();
				return cols[cid];
			}
		}
		public string ConnectionId { get; set; }
		public StrategyEnum Strategy { get; set; }

		public AI AI { get; set; }
	}

	public class NightInstruction
	{
		public NightInstruction(Game game, NightFormModel model)
		{
			Action = model.Action;
			Actor = game.Players.Where(p => p.Id == model.ActorId).Single();
			if (Action != NightActionEnum.Sleep)
				Whom = game.Players.Where(p => p.Id == model.Whom).Single();
		}

		public NightActionEnum Action { get; set; }
		public Player Actor { get; set; }
		public Player Whom { get; set; }
	}


	public class DayInstruction
	{
		public DayInstruction(Game game, DayFormModel model)
		{
			Actor = game.Players.Where(p => p.Id == model.ActorId).Single();
			JailVote = game.Players.Where(p => p.Id == model.JailWhom).SingleOrDefault();
			StakeVote = game.Players.Where(p => p.Id == model.KillWhom).SingleOrDefault();
		}

		public Player Actor { get; set; }
		public Player JailVote { get; set; }
		public Player StakeVote { get; set; }
	}


	public enum NightActionEnum
	{
		Sleep = 1,
		Watch = 2,
		Bite = 3
	}

	public enum StrategyEnum
	{
		Human = 1,
		AI = 2
	}
}