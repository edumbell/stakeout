using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApplication1.Models
{

	public class Game
	{
		public string Error { get; set; }
		public bool DebugAllowed { get; set; }
		public string OverMessage { get; set; }

		public bool GameOver
		{
			get
			{
				return OverMessage != null;
			}
		}

		public static List<Game> GameList = new List<Game>();

		public List<LogItem> Log = new List<LogItem>();

		public static Game GetGame(string id)
		{
			return GameList.Where(g => g.GameId == id).Single();
		}

		public Game()
		{
			Turns = new Dictionary<int, Turn>();
			Players = new List<Player>();
			//Turns.Add(0, new Turn());
		}

		public void LogAndAnnounceVotes(DayInstruction di)
		{
			var subject = di.Actor;
			var stakeWhom = di.StakeVote;
			if (stakeWhom != null)
			{
				var sv = new GameEvent()
				{
					EventType = EventTypeEnum.VoteStake,
					Subject = subject,
					Whom = stakeWhom
				};
				AddToLog(sv);
				AnnounceEventToAIs(sv);
			}
			var jailWhom = di.JailVote;
			if (jailWhom != null)
			{
				var jv = new GameEvent()
				{
					EventType = EventTypeEnum.VoteJail,
					Subject = subject,
					Whom = jailWhom
				};
				AddToLog(jv);
				AnnounceEventToAIs(jv); // currently they don't care
			}
		}



		public void AddToLog(LogItem item)
		{
			string turn = "<span class='debug-heading'>turn " + CurrentTurn().Id.ToString() + ", "
				+ (CurrentTurn().NightComplete ? "Morning" : "Night") + "</span>";
			item.Turn = turn;
			Log.Add(item);
		}

		public void Announce(string msg)
		{
			Hub.Announce(this, msg);
		}

		public WebApplication1.Hubs.StakeHub Hub { get; set; }

		public string Name { get; set; }
		public string GameId { get; set; }

		public List<Player> Players { get; set; }
		public Dictionary<int, Turn> Turns { get; set; }

		public Player GetPlayer(string id)
		{
			return Players.Where(p => p.Id == id).SingleOrDefault();
		}

		public int GetNumHumans()
		{
			return Players.Where(p => p.Strategy == StrategyEnum.Human).Count();
		}
		public int GetNumBots()
		{
			return Players.Where(p => p.Strategy == StrategyEnum.AI).Count();
		}

		public List<Player> LivingPlayers
		{
			get
			{
				return Players.Where(p => !p.IsDead).ToList();
			}
		}

		public List<Player> MobilePlayers
		{
			get
			{
				return Players.Where(p => !p.IsDead && !p.IsInJail).ToList();
			}
		}

		public Player JailedPlayer
		{
			get
			{
				return Players.Where(p => p.IsInJail).FirstOrDefault();
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
				if (r.NextDouble()*100 <  n*1.5 )
				{
					p.IsVampire = true;
					nvampires++;
				}
			}
			StartTurn();
		}

		public void StartTurn()
		{
			var ais = Players.Where(p => p.Strategy == StrategyEnum.AI);
			foreach (var ai in ais)
			{
				ai.AI.OnDayEnding();
			}
			var i = Turns.Keys.Any() ? Turns.Keys.Max() + 1 : 1;
			var t = new Turn()
			{
				Id = i,
				NightInstructions = new List<NightInstruction>(),
				DayInstructions = new List<DayInstruction>()
			};
			Turns.Add(i, t);
			Hub.SendStartNight(this, CurrentTurn().Id);
			GetNightInstructionsFromAIs();

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
			var logEvent = new GameEvent(i);
			AddToLog(logEvent);
			if (CurrentTurn().NightInstructions.Any())
			{
				var existing = CurrentTurn().NightInstructions.Where(x => x.Actor == i.Actor).FirstOrDefault();
				if (existing != null)
				{
					throw new Exception("Night instructions already given for player & turn.");
				}
			}
			CurrentTurn().NightInstructions.Add(i);
			Hub.DisplayNightActionsEntered(this);
			if (CurrentTurn().NightInstructions.Count() == MobilePlayers.Count())
			{
				FinishNight();
			}
		}

		public void ProcessDayInstruction(DayInstruction i)
		{
			var logevent = new GameEvent();
			if (CurrentTurn().DayInstructions.Any())
			{
				var existing = CurrentTurn().DayInstructions.Where(x => x.Actor == i.Actor).FirstOrDefault();
				if (existing != null)
				{
					throw new Exception("Day instructions already given for player & turn.");
				}
			}
			CurrentTurn().DayInstructions.Add(i);
			Hub.DisplayVotes(this, VotesNeeded);

			if (CurrentTurn().DayInstructions.Count() == MobilePlayers
				.Where(m => m.Strategy == StrategyEnum.Human)
				.Count())
			{
				// all human orders entered
				GetDayInstructionsFromAIs();
			}

			if (CurrentTurn().DayInstructions.Count() == LivingPlayers.Count())
			{
				FinishDay();
			}
		}

		public static string PlayerListToString(IEnumerable<Player> players, string separator = null)
		{
			if (separator == null)
				separator = " and ";
			if (players.Count() < 3)
				separator = " and ";
			return string.Join(separator, players.Select(p => p.NameSpan).ToArray());
		}


		public void ProcessNightInsruction(NightInstruction i)
		{

		}
		public void FinishNight()
		{

			foreach (var i in CurrentTurn().NightInstructions)
			{
				var p = i.Actor;
				if (p.IsInJail)
					throw new Exception("Jailed actor submitted action?");


				// NB first send results of watching/biting, so AI's know what they observed by the time they're told they were bitten
				if (i.Action == NightActionEnum.Watch || i.Action == NightActionEnum.Bite)
				{
					var watcheeInsruction = CurrentTurn().NightInstructions.Where(x => x.Actor == i.Whom).SingleOrDefault();
					if (watcheeInsruction == null)
						throw new Exception("Missing night instruction for " + i.Whom.Name);
					var watcheeAction = watcheeInsruction.Action;
					if (watcheeAction == NightActionEnum.Sleep)
					{
						Hub.SendPrivate(p, i.Whom.NameSpan + " spent the night at home.");
					}
					else
					{
						Hub.SendPrivate(p, i.Whom.NameSpan + " snuck out of the house in the middle of the night!");
					}

					var met = CurrentTurn().NightInstructions.Where(x => x.Whom == i.Whom && x != i).Select(x => x.Actor);

					if (i.Actor.Strategy == StrategyEnum.AI)
					{
						i.Actor.AI.ReceiveWatchResult(watcheeAction == NightActionEnum.Sleep, met.Select(x => x.Id));
					}

					// send 'met' messages

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
			foreach (var i in CurrentTurn().NightInstructions)
			{
				var p = i.Actor;

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
						if (i.Whom.Strategy == StrategyEnum.AI)
						{
							i.Whom.AI.TellFellowVampire(i.Actor.Id);
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
							i.Whom.AI.OnMeBitten();
						}

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
			foreach (var p in MobilePlayers.Where(p => p.Strategy == StrategyEnum.AI))
			{
				p.AI.MakeMorningAnnouncements();
			}

			EndOfGameCheck();
			if (!GameOver)
			{
				CurrentTurn().NightComplete = true;
				Hub.SendStartDay(this, CurrentTurn().Id);
			}
		}

		public void GetNightInstructionsFromAIs()
		{
			var anyHumansToWaitFor = MobilePlayers.Where(p => p.Strategy == StrategyEnum.Human).Any();
			if (!anyHumansToWaitFor)
			{
				System.Threading.Thread.Sleep(5000);
			}

			foreach (var p in MobilePlayers.Where(p => p.Strategy == StrategyEnum.AI))
			{
				var inst = p.AI.GetNightInstruction(MobilePlayers.Select(m => m.Id).ToList(), CurrentTurn().Id);
				var inst2 = new NightInstruction(this, inst);
				ProcessNightInstruction(inst2);
			}
		}

		public void GetDayInstructionsFromAIs()
		{
			foreach (var p in MobilePlayers.Where(p => p.Strategy == StrategyEnum.AI))
			{
				var inst = p.AI.GetDayInstruction(MobilePlayers.Select(m => m.Id).ToList(), CurrentTurn().Id);
				var inst2 = new DayInstruction(this, inst);
				ProcessDayInstruction(inst2);
			}
		}

		public void NewMayor()
		{
			var m = RandomPlayers().First();
			Announce("The town needs a new mayor.  Lots are drawn. It is " + m.NameSpan);
			m.IsMayor = true;
		}

		public double VotesNeeded
		{
			get
			{
				var result = LivingPlayers.Count * 0.501;
				if (result < 2)
					result = 2;
				return result;
			}
		}

		public void FinishDay()
		{
			foreach (var di in CurrentTurn().DayInstructions)
			{
				LogAndAnnounceVotes(di);
			}
			Dictionary<string, double> stakeVotes = new Dictionary<string, double>();
			Dictionary<string, double> jailVotes = new Dictionary<string, double>();
			foreach (var p in MobilePlayers)
			{
				stakeVotes.Add(p.Id, 0);
				jailVotes.Add(p.Id, 0);
			}
			// nobody should be in jail at this point
			var votesNeeded = VotesNeeded;
			foreach (var d in CurrentTurn().DayInstructions)
			{
				if (d.JailVote != null)
				{
					jailVotes[d.JailVote.Id] += d.Weight;
				}
				if (d.StakeVote != null)
				{
					stakeVotes[d.StakeVote.Id] += d.Weight;
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
					Announce(toJail.NameSpan + " has been put into protective custody for the night, by "
						+ PlayerListToString(mob)
						);
					var jailEvent = new GameEvent()
						{
							EventType = EventTypeEnum.WasJailed,
							Subject = toJail
						};
					AnnounceEventToAIs(jailEvent);
					AddToLog(jailEvent);
				}

				if (stakeVotes[p.Id] >= votesNeeded)
				{
					var tostake = p;
					var mob =
						PlayerListToString(CurrentTurn().DayInstructions.Where(v => v.StakeVote == tostake).Select(v => v.Actor));
					if (tostake.IsInJail)
					{
						Announce("Luckily, " + tostake.NameSpan + " was in protective custoy when any angry mob (" + mob + ") came to try and stake them.");
					}
					else
					{
						Announce(tostake.NameSpan + " has been staked by an angry mob (" + mob + ")!  I hope he/she really was a vampire....");
						tostake.IsDead = true;
						if (tostake.IsMayor)
						{
							NewMayor();
						}
					}
				}
			}
			EndOfGameCheck();
			CurrentTurn().DayComplete = true;
			if (!GameOver)
			{
				StartTurn();
			}
		}

		private void EndOfGameCheck()
		{
			if (!Players.Where(p => p.IsVampire && !p.IsDead).Any())
			{
				Announce("Well done! All the vampires are dead. The villagers live happily ever after.");
				OverMessage = "Villagers staked all the vampires";
			}

			if (!Players.Where(p => !p.IsVampire && !p.IsDead).Any())
			{
				Announce("Well done!  Everyone left alive is a vampire. You live happily ever after.");
				OverMessage = "Vampires won";
			}

			if (CurrentTurn().Id == 10)
			{
				Announce("After 10 days, the vampire hunter arrives to help. All remaining villagers are saved, and all remaining vampires are slain.");
				OverMessage = "Villagers survived 10 days";
				var hunams = Players.Where(p => !p.IsVampire && !p.IsDead);
				var list = PlayerListToString(hunams);
				Announce("Well done, " + list);
				//var vamplist = Players.Where(p => p.IsVampire && !p.IsDead);
			}
		}

		public void AnnounceToAIs(CommsEvent comms)
		{
			var ais = Players.Where(p => p.Strategy == StrategyEnum.AI);
			foreach (var ai in ais)
			{
				ai.AI.HearComms(comms.EventType, comms.Subject, comms.Whom);
			}
		}

		public void AnnounceEventToAIs(GameEvent ev)
		{
			var ais = Players.Where(p => p.Strategy == StrategyEnum.AI);
			foreach (var ai in ais)
			{
				ai.AI.ProcessVotingEvent(ev.EventType, ev.Subject, ev.Whom);
			}
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
		public string Colour { get; set; }
		public Game Game { get; set; }
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
		public void SetColour()
		{
			string[] cols = { "#7766ff","#0099ff", "#bb00ff", "#f000aa", "#ff4444", "#ff9900", "#99c000", "#00c0a4", "#00c600", "#aaaaaa" };
			var cid = Name.ToCharArray().Sum(x => (int)x) % cols.Count();
			Colour = cols[cid];
			if (Game.Players.Select(p => p.Colour).Contains(Colour))
			{
				var remainingCols = cols.Except(Game.Players.Select(p => p.Colour)).ToArray();
				if (remainingCols.Any())
				{
					var cidAlt = Name.ToCharArray().Sum(x => (int)x) % remainingCols.Count();
					Colour = remainingCols[cidAlt];
				}
			}
			// availCols.Except(Game.Players.Select(Colour));
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
			Weight = Actor.IsMayor ? 1.1 : 1;
		}

		public Player Actor { get; set; }
		public Player JailVote { get; set; }
		public Player StakeVote { get; set; }

		public double Weight { get; set; }
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