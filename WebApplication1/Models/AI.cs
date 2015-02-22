using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApplication1.Models
{
	public class Relation
	{
		public string PlayerId { get; set; }
		public double Enmity { get; set; }
		public double DarkSideSuspicion { get; set; }
		public double KnownBites { get; set; }
		public double GussedBites { get; set; }
		public bool KnownVampire { get; set; }
	}

	public class AI
	{
		public string TraceLog = "";
		public Player Me { get; set; }
		public double SuspicionAgainstMe = 0;

		public List<Relation> Relations = new List<Relation>();

		private NightFormModel LastNightAction { get; set; }
		private List<string> LastNightAnnouncedSleep { get; set; }
		private List<string> LastNightKnownSlept { get; set; }

		// 0 = unknown; negative = probably slept
		private Dictionary<string, double> LastNightWentOutProbability { get; set; }
		//private NightFormModel LastNightAction { get; set; }

		public Hubs.StakeHub Hub { get; set; }

		private Random r = new Random();

		private void Trace(string msg)
		{
			TraceLog += "<br/>" + msg;
		}

		private Relation Relation(string id)
		{
			if (id == Me.Id)
			{
				return new Relation()
				{
					Enmity = -100,
					GussedBites = Me.Bites,
					DarkSideSuspicion = Me.IsVampire ? 10 : -10,
					PlayerId = Me.Id
				};
			}
			var result = Relations.Where(x => x.PlayerId == id).FirstOrDefault();
			if (result == null)
			{
				result = new Relation() { PlayerId = id };
				Relations.Add(result);
			}
			return result;


		}


		private string RandomNotMe()
		{
			return RandomId(
				Relations.Select(x => x.PlayerId).ToList()
				);
		}

		private string RandomId(List<string> otherIds)
		{
			return otherIds.OrderBy(x => r.NextDouble()).First();
		}

		private void Daily()
		{
			foreach (var x in Relations)
			{
				x.Enmity = x.Enmity * .7 - .1;
				x.GussedBites = x.GussedBites + .1;
				if (x.DarkSideSuspicion < x.GussedBites*2 - 3)
					x.DarkSideSuspicion = x.GussedBites*2- 3;
			}
		}


		public void TellFellowVampire(string pid)
		{
			Relation(pid).KnownVampire = true;
			Relation(pid).Enmity = -1;
			Relation(pid).DarkSideSuspicion += 10;
		}

		public void OnMeBitten()
		{
			if (r.NextDouble() * 1.5 - .5 > Me.Bites - 1 && SuspicionAgainstMe < 2)
			{
				AnnounceMeBitten(true);
				SuspicionAgainstMe += 1;
			}
			// todo: figure out who we know was at home - requires remembering all night's events
			ProcessBiteInfo(Me.Id, 1);

		}

		public void AnnounceMeBitten(bool isTrue)
		{
			var comms = new CommsEvent(Me, CommsTypeEnum.IWasBitten, !isTrue);
			Me.Game.AnnounceToAIs(comms);
			Hub.Send(Me.Game.GameId, Me.Id, "I've been bitten!");
			Me.Game.AddToLog(comms);
		}

		public void TellStakeVote(string id)
		{
			Relation(id).Enmity += 1;
		}

		public void ReceiveWatchResult(bool stayedAtHome, IEnumerable<string> met)
		{

			if (LastNightAction.Action == NightActionEnum.Watch || LastNightAction.Action == NightActionEnum.Bite)
			{
				var whomRelation = Relation(LastNightAction.Whom);

				// set suspicion based on observation
				//if (Me.IsVampire)
				//{
				if (stayedAtHome)
				{
					whomRelation.DarkSideSuspicion -= .3;
					LastNightKnownSlept.Add(LastNightAction.Whom);
				}
				else if (LastNightAnnouncedSleep.Contains(whomRelation.PlayerId))
				{
					// went out, and lied about it
					whomRelation.DarkSideSuspicion += 5;
					Trace("Detect lie sus +1");
				}
				else
				{
					// went out, but didn't lie about it
					whomRelation.DarkSideSuspicion += .2;
				}
				//}


				// before going any futher:  never claim to know anything if we claimed we were staying home!
				if (LastNightAnnouncedSleep.Contains(Me.Id))
					return;

				// work out whether/what to announce

				bool lie = false;
				if (Me.IsVampire && !met.Any())
				{
					if (r.Next(10) >= 5)
						return;

					if (r.Next(10) >= 7 || LastNightAction.Action == NightActionEnum.Bite)
						lie = true;
				}

				var announceWhom = LastNightAction.Whom;

				if (lie)
				{
					if (r.NextDouble() > .9)
					{
						stayedAtHome = false;
						announceWhom = PickEnemy(-100);
						if (announceWhom == null)
							announceWhom = RandomNotMe();
					}
					else if (r.NextDouble() > .95 || LastNightAction.Action == NightActionEnum.Bite)
					{
						stayedAtHome = false;
						announceWhom = RandomNotMe();
					}
					else
					{
						stayedAtHome = !stayedAtHome;
					}
				}

				if (stayedAtHome)
				{
					Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(announceWhom).Name + " stayed at home");
					var comms = new CommsEvent(Me, CommsTypeEnum.Slept, lie, Me.Game.GetPlayer(announceWhom));
					Me.Game.AnnounceToAIs(comms);
					Me.Game.AddToLog(comms);
				}
				else
				{
					var keepMouthShut = false;
					var accuse = false;
					// they went out, and lied about it - do we accuse?
					if (LastNightAnnouncedSleep.Contains(announceWhom))
					{

						// But careful not to rat out (or falsely accuse) an accomplice
						if (Me.IsVampire)
						{
							if (Relation(announceWhom).KnownVampire)
							{
								keepMouthShut = true;
							}
							else if (Relation(announceWhom).Enmity > 1 + 3 * r.NextDouble())
							{
								accuse = true;
							}
							else if (Relation(announceWhom).GussedBites > 1.5 + 1 * r.NextDouble())
							{
								keepMouthShut = true;
							}
							else
								accuse = true;
						}
						else
						{
							accuse = true;
							// comms redundant, as AI's detect discrepancy elsewhere

							//var comms = new CommsEvent(Me, CommsTypeEnum.LiedAboutSleeping, lie, Me.Game.GetPlayer(announceWhom));
							//Me.Game.AnnounceToAIs(comms);  redundant
							//Me.Game.AddToLog(comms);
						}
					}

					if (!keepMouthShut)
					{
						if (accuse)
						{
							Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(announceWhom).Name + " lied! They went out last night");
						}
						else
						{
							Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(announceWhom).Name + " went out last night");
						}
						var comms = new CommsEvent(Me, CommsTypeEnum.WentOut, lie, Me.Game.GetPlayer(announceWhom));
						Me.Game.AnnounceToAIs(comms);
						Me.Game.AddToLog(comms);
					}

				}
			}
		}

		public void ProcessVotingEvent(EventTypeEnum type, Player subject, Player whom)
		{
			if (subject == Me)
				return;

			var mistrustOfSubject = .5 + Relation(subject.Id).DarkSideSuspicion;
			if (mistrustOfSubject < 1)
				mistrustOfSubject = 1;
			var relativeTrustOfSubject = -1.0;
			var relativeTrustOfWhom = -1.0;
			if (whom != null)
			{
				relativeTrustOfSubject = Relation(whom.Id).DarkSideSuspicion - Relation(subject.Id).DarkSideSuspicion;
				relativeTrustOfWhom = 0 - Relation(whom.Id).DarkSideSuspicion + Relation(subject.Id).DarkSideSuspicion;
			}
			switch (type)
			{
				case EventTypeEnum.VoteStake:
					// staking *me* enmity processed elsehwere

					// if we trust the voter more than the stake-ee, then we start to share their suspicion
					// (but if not, then the stake-ee is *might* be innocent victim)
					var susp1 = relativeTrustOfSubject * .3;
					if (susp1 < 0) susp1 = susp1 * .5;
					Relation(whom.Id).DarkSideSuspicion += susp1;
					Trace("Stake-votee suspicion: " + susp1.ToString("0.00"));
					// if we trust the stake-ee more, then the voter might be a vampire
					var susp2 = relativeTrustOfWhom * .2;
					if (susp2 < 0) susp2 = susp2 * .5;
					Relation(subject.Id).DarkSideSuspicion += susp2;
					Trace("Stake-voter suspicion: " + susp2.ToString("0.00"));
					break;
				case EventTypeEnum.WasJailed:
					Relation(subject.Id).GussedBites -= .1;
					break;
			}
		}

		private double ProcessBiteInfo(string whom, double probabilityIsBite)
		{
			// got here if someone was bitten (including self) and self didn't do the biting
			Trace("Someone bitten (" + probabilityIsBite.ToString("0.00") + "):");
			Relation(whom).GussedBites += probabilityIsBite;
			var innocents = LastNightKnownSlept;
			if (Me.Game.JailedPlayer != null)
			{
				innocents.Add(Me.Game.JailedPlayer.Id);
			}
			innocents.Add(whom);
			var suspects = Relations.Where(x => !innocents.Contains(x.PlayerId));
			if (suspects.Any())
			{
				var mostSuspectSuspect = 0d;
				var totalSuspicion = 0d;
				foreach (var rel in suspects)
				{
					// can be slightly negative, if we have multiple trusted reports of stay-homeness totalling > .5... that's ok:
					totalSuspicion += (LastNightWentOutProbability[rel.PlayerId] + .5);
					var thisSuspect = LastNightWentOutProbability[rel.PlayerId] + rel.DarkSideSuspicion * .2;
					if (thisSuspect > mostSuspectSuspect)
						mostSuspectSuspect = thisSuspect;
				}
				if (totalSuspicion < .1)
					totalSuspicion = .1;
				foreach (var rel in suspects)
				{
					// can be slightly negative, if we have multiple trusted reports of stay-homeness totalling > .5... that's ok:
					var amountSuspicion = (LastNightWentOutProbability[rel.PlayerId] + .5) / totalSuspicion;
					var addSusp = 4 * amountSuspicion * probabilityIsBite;
					rel.DarkSideSuspicion += addSusp;
					Trace("LastNightWentOut: " + LastNightWentOutProbability[rel.PlayerId] + " addSusp:" + addSusp.ToString("0.00"));
				}
				return mostSuspectSuspect;
			}
			else
			{
				// if no possible suspects, and self didn't do the biting, then we know somebody lied
				ProcessLie(whom, CommsTypeEnum.LiedAboutBeingBitten, "No possible suspects!");
				return -1;
			}

		}

		public void ProcessLie(string lier, CommsTypeEnum topic, string msg = null)
		{
			Relation(lier).DarkSideSuspicion += 10;
			var comms = new CommsEvent(Me, topic, false, Me.Game.GetPlayer(lier));
			Me.Game.AddToLog(comms);
			if (topic != CommsTypeEnum.LiedAboutSleeping)
			{
				// sleeping lie is processed elsewhere
				Me.Game.AnnounceToAIs(comms);
				if (msg != null)
					Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(lier).Name + " lied! " + msg);
			}
		}

		public void HearComms(CommsTypeEnum type, Player subject, Player whom)
		{
			if (subject == Me)
				return;
			// start mistrusting at suspicion > 0.5
			var mistrust = .5 + Relation(subject.Id).DarkSideSuspicion;
			if (mistrust < 1)
				mistrust = 1;

			switch (type)
			{
				case CommsTypeEnum.IWasBitten:
					// first, process public effect
					if (Me.IsInJail)
					{
						SuspicionAgainstMe -= .1;
						SuspicionAgainstMe += .2 / mistrust; // assume others have similar mistrust
					}

					// next process private conclusions
					// (irrelevant if we did the biting!)
					if (LastNightAction.Whom == subject.Id && LastNightAction.Action == NightActionEnum.Bite)
						return;

					var worstSuspect = ProcessBiteInfo(subject.Id, .8 / mistrust);
					// worstsuspect = probWentOut + darkSide*.2
					// no more cryWolf at approx worst suspect = .3
					if (worstSuspect < -1)
						worstSuspect = -1;
					var cryWolfSusp = 2 / (worstSuspect + 1.2) - 1.35;
					if (cryWolfSusp < 0)
						cryWolfSusp = 0;
					Relation(subject.Id).DarkSideSuspicion += cryWolfSusp;
					Trace("Cry-wolf addSusp: " + cryWolfSusp.ToString("0.00"));

					break;
				case CommsTypeEnum.Slept:
					if (whom.Id != Me.Id)
					{
						LastNightWentOutProbability[whom.Id] -= .5 / mistrust;
						Relation(whom.Id).DarkSideSuspicion -= .2 / mistrust;
					}
					break;
				case CommsTypeEnum.WentOut:
					if (whom.Id != Me.Id)
					{
						LastNightWentOutProbability[whom.Id] += .5 / mistrust;
						if (LastNightAnnouncedSleep.Contains(whom.Id))
						{
							// this is same as case CommsTypeEnum.LiedAboutSleeping:
							var accusedAddSusp = 2 / mistrust;
							Relation(whom.Id).DarkSideSuspicion += accusedAddSusp;
							Trace("Heard accused of sleep/went-out lying addSusp:" + accusedAddSusp.ToString("0.00"));
						}
						else
						{
							var accusedAddSusp = .2 / mistrust;
							Relation(whom.Id).DarkSideSuspicion += accusedAddSusp;
							Trace("Heard went-out addSusp:" + accusedAddSusp.ToString("0.00"));
						}
					}
					else
					{
						// claimed that I went out
						if (LastNightAction.Action == NightActionEnum.Sleep)
							ProcessLie(subject.Id, CommsTypeEnum.GenericLie, "I slept all night!");
					}
					break;
				case CommsTypeEnum.WillSleep:
					LastNightAnnouncedSleep.Add(subject.Id);
					break;
				case CommsTypeEnum.LiedAboutSleeping:
					// processed elsewhere
					break;
				case CommsTypeEnum.LiedAboutBeingBitten:
					var accusedAddSusp2 = 2 / mistrust;
					Relation(whom.Id).DarkSideSuspicion += accusedAddSusp2;
					Trace("Heard accused of lying about being bitten:" + accusedAddSusp2.ToString("0.00"));
					break;
				case CommsTypeEnum.GenericLie:
					var accusedAddSusp3 = 2 / mistrust;
					Relation(whom.Id).DarkSideSuspicion += accusedAddSusp3;
					Trace("Heard accused of generic lie addSusp:" + accusedAddSusp3.ToString("0.00"));
					break;
			}
		}

		public string PickEnemy(double threshHold)
		{
			var max = threshHold;
			string who = null;

			foreach (var x in Relations)
			{
				double t = r.NextDouble() * .5;
				t += x.Enmity;

				if (Me.IsVampire && x.KnownVampire)
				{
					t -= 3;
				}

				if (Me.IsVampire)
				{
					t -= x.DarkSideSuspicion;
				}
				else
				{
					t += x.DarkSideSuspicion;
				}

				t = t * r.NextDouble() + t * r.NextDouble() * r.NextDouble();
				if (t > max)
				{
					max = t;
					who = x.PlayerId;
				}
			}
			return who;
		}

		public void MakeMorningAnnouncements()
		{
			if (Me.IsVampire)
			{
				if (r.NextDouble() * 10 > SuspicionAgainstMe * r.NextDouble() + 9)
				{
					AnnounceMeBitten(false);
					SuspicionAgainstMe += 1;
				}
			}
		}

		public DayFormModel GetDayInstruction(List<string> ids, int turnId)
		{
			Daily();
			var otherIds = ids.Where(x => x != Me.Id).ToList();
			var d = new DayFormModel()
			{
				ActorId = Me.Id,
				TurnId = turnId
			};
			if (otherIds.Any())
			{
				d.JailWhom = RandomId(ids);
				d.KillWhom = PickEnemy(.8);
			}

			//if (r.Next(10)  < 5 - (turnId / 2))
			//{
			//	d.KillWhom = null;
			//}

			if (r.NextDouble() * 10 - 1 < SuspicionAgainstMe)
			{
				d.JailWhom = Me.Id;
			};
			return d;
		}

		public void OnDayEnding()
		{
			LastNightAnnouncedSleep = new List<string>();
			LastNightKnownSlept = new List<string>();

		}

		public NightFormModel GetNightInstruction(List<string> ids, int turnId)
		{
			var otherIds = ids.Where(x => x != Me.Id).ToList();
			// if it's the first night, ensure we have a relation object for each other player
			foreach (var id in otherIds)
			{
				var dummy = Relation(id);
			}

			LastNightWentOutProbability = new Dictionary<string, double>();
			foreach (var rel in Relations)
			{
				LastNightWentOutProbability.Add(rel.PlayerId, 0);
			}

			var d = new NightFormModel()
			{
				ActorId = Me.Id
			};

			if (otherIds.Any())
			{
				d.Whom = RandomId(otherIds);

				if (Me.IsVampire)
				{
					if (r.NextDouble() * 10 - r.NextDouble() * 5 < turnId)
					{
						d.Action = NightActionEnum.Bite;
					}
					else if (r.NextDouble() * 10 < 3)
					{
						d.Action = NightActionEnum.Watch;
					}
					else
					{
						d.Action = NightActionEnum.Sleep;
					}
				}
				else
				{
					if (r.NextDouble() * 10 < 5)
					{
						d.Action = NightActionEnum.Watch;
					}
					else
					{
						d.Action = NightActionEnum.Sleep;
					}
				}

				if (d.Action == NightActionEnum.Bite)
				{
					var maxTastiness = 0d;
					foreach (var re in Relations.Where(x => otherIds.Contains(x.PlayerId)).Where(x => !x.KnownVampire))
					{
						var t = re.GussedBites;
						if (t > 2.5)
							t = 2.5;
						t = t + r.NextDouble() * 2;

						// if we think someone may already be a vampire, don't bite them
						// todo: but it helps to know for sure they are.....
						t -= (re.DarkSideSuspicion * .3);

						if (t > maxTastiness)
						{
							maxTastiness = t;
							d.Whom = re.PlayerId;
						}
					}
					Relation(d.Whom).KnownBites++;
					Relation(d.Whom).GussedBites++;
					if (Relation(d.Whom).KnownBites >= 3)
					{
						Relation(d.Whom).KnownVampire = true;
						Relation(d.Whom).Enmity = -1;
						Relation(d.Whom).DarkSideSuspicion += 10;
					}
				}
			}
			else
			{
				d.Action = NightActionEnum.Sleep;
			}

			if (d.Action == NightActionEnum.Sleep
				|| (
				d.Action == NightActionEnum.Bite
				&& r.NextDouble() * 5 - r.NextDouble() * 5 > SuspicionAgainstMe
				)
				)
			{
				var isTrue = d.Action == NightActionEnum.Sleep;
				var comms = new CommsEvent(Me, CommsTypeEnum.WillSleep, !isTrue);
				Me.Game.AddToLog(comms);
				Me.Game.AnnounceToAIs(comms);
				Hub.Send(Me.Game.GameId, Me.Id, "I'm staying at home");
				LastNightAnnouncedSleep.Add(Me.Id);
			}
			LastNightAction = d;

			if (d.Action != NightActionEnum.Sleep)
			{
				// assuming someone might observe
				SuspicionAgainstMe += .2;
			}


			return d;

		}
	}
}