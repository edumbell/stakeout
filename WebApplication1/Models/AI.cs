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
		public double PublicSuspicion { get; set; }
		public double KnownBites { get; set; }
		public double GussedBites { get; set; }
		public bool KnownVampire { get; set; }
	}

	public class AI
	{
		public static double MIstrustToMult(double mistrust)
		{
			// -1 -> .66
			// 0 -> .5
			// +1 -> .33
			return 1 / (1 + Math.Pow(2, mistrust));
		}

		public string TraceLog = "";
		public Player Me { get; set; }
		public double SuspicionAgainstMe = 0;

		public List<Relation> Relations = new List<Relation>();

		private NightFormModel LastNightAction { get; set; }
		private IEnumerable<string> LastNightMet { get; set; }
		private List<string> LastNightAnnouncedSleep { get; set; }
		private List<string> LastNightKnownSlept { get; set; }
		private List<string> LastNightKnownWentOut { get; set; }

		// 0 = unknown; negative = probably slept
		private Dictionary<string, double> LastNightWentOutProbability { get; set; }

		private double WentOutProbabilityCapped(string pid)
		{
			return Math.Max(Math.Min(LastNightWentOutProbability[pid], 1), -1);
		}

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
					PublicSuspicion = SuspicionAgainstMe,
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
				if (x.DarkSideSuspicion < x.GussedBites * 3 - 4)
					x.DarkSideSuspicion = x.GussedBites * 3 - 4;
				if (x.GussedBites > 2)
				{
					x.DarkSideSuspicion += (x.GussedBites - 2) * .2;
				}
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
			Hub.AnnounceComms(Me.Game, comms);
		}

		public void TellStakeVote(string id)
		{
			Relation(id).Enmity += 1;
		}

		public void ReceiveWatchResult(bool stayedAtHome, IEnumerable<string> met)
		{
			LastNightMet = met;
			LastNightKnownWentOut.AddRange(met);
			foreach (var m in met)
			{
				LastNightWentOutProbability[m] = 10;
			}
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
					LastNightWentOutProbability[whomRelation.PlayerId] = -10;
				}
				else
				{
					LastNightKnownWentOut.Add(whomRelation.PlayerId);
					LastNightWentOutProbability[whomRelation.PlayerId] = 10;
					if (LastNightAnnouncedSleep.Contains(whomRelation.PlayerId))
					{
						// went out, and lied about it
						whomRelation.DarkSideSuspicion += 5;
						Trace("Detect lie sus +5");
					}
					else
					{
						// went out, but didn't lie about it
						whomRelation.DarkSideSuspicion += .2;
					}
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
					//Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(announceWhom).Name + " stayed at home");
					var comms = new CommsEvent(Me, CommsTypeEnum.Slept, lie, Me.Game.GetPlayer(announceWhom));
					Hub.AnnounceComms(Me.Game, comms);
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
						var comms = new CommsEvent(Me, CommsTypeEnum.WentOut, lie, Me.Game.GetPlayer(announceWhom));
						if (accuse)
						{
							Hub.AnnounceComms(Me.Game, comms, Me.Game.GetPlayer(announceWhom).NameSpan + " lied! They went out last night");
						}
						else
						{
							Hub.AnnounceComms(Me.Game, comms, Me.Game.GetPlayer(announceWhom).NameSpan + " went out last night");
						}



					}

				}
			}
		}

		// similar logic to vote-to-stake, but in this case we know one or other is a vampire (or very silly lying human)
		// slept/went-out  processed where, making use of extra information
		public void ProcessAccusation(string accuser, string accused)
		{
			// difference of 1,  66% 33%
			// difference of 4, 89% 11%
			var newAccusedSusp = MIstrustToMult(Relation(accused).DarkSideSuspicion - Relation(accuser).DarkSideSuspicion);
			var newAccuserSusp = 1 - newAccusedSusp;
			newAccuserSusp -= .15;
			newAccusedSusp -= .15;
			Relation(accused).DarkSideSuspicion += newAccusedSusp;
			Trace("Accused suspicion: " + newAccusedSusp.ToString("0.00"));
			Relation(accuser).DarkSideSuspicion += newAccuserSusp;
			Trace("Accuser suspicion: " + newAccuserSusp.ToString("0.00"));


			// public

			//var newPublicAccusedSusp = MIstrustToMult(Relation(accused).PublicSuspicion - Relation(accuser).PublicSuspicion);
			//var newPublicAccuserSusp = 0 - newPublicAccusedSusp;
			//newAccuserSusp -= .15;
			//newAccusedSusp -= .15;
			//Relation(accused).PublicSuspicion += newPublicAccusedSusp;
			//Trace("Accused Public suspicion: " + newPublicAccusedSusp.ToString("0.00"));
			//Relation(accuser).PublicSuspicion += newPublicAccuserSusp;
			//Trace("Accuser Public suspicion: " + newPublicAccuserSusp.ToString("0.00"));

		}

		public void ProcessVotingEvent(EventTypeEnum type, Player subject, Player whom)
		{
			if (subject == Me)
				return;

			//var mistrustOfSubject = .5 + Relation(subject.Id).DarkSideSuspicion;
			//if (mistrustOfSubject < 1)
			//	mistrustOfSubject = 1;
			var relativeTrustOfSubject = -1.0;
			var relativeTrustOfWhom = -1.0;
			var relativePublicTrustOfSubject = -1.0;
			var relativePublicTrustOfWhom = -1.0;
			if (whom != null)
			{
				relativeTrustOfWhom = MIstrustToMult(Relation(whom.Id).DarkSideSuspicion - Relation(subject.Id).DarkSideSuspicion / 2);
				relativeTrustOfSubject = MIstrustToMult(-Relation(whom.Id).DarkSideSuspicion / 2 + Relation(subject.Id).DarkSideSuspicion);
				relativePublicTrustOfWhom = MIstrustToMult(Relation(whom.Id).PublicSuspicion - Relation(subject.Id).PublicSuspicion / 2);
				relativePublicTrustOfSubject = MIstrustToMult(-Relation(whom.Id).PublicSuspicion / 2 + Relation(subject.Id).PublicSuspicion);
			}
			switch (type)
			{
				case EventTypeEnum.VoteStake:
					// staking *me* enmity processed elsehwere

					// if we trust the voter more than the stake-ee, then we start to share their suspicion
					// (but if not, then the stake-ee  *might* be innocent victim)
					var susp1 = relativeTrustOfSubject*.5;
					var susp2 = relativeTrustOfWhom*.5;
					Relation(whom.Id).DarkSideSuspicion += susp1;
					Relation(whom.Id).Enmity += susp1 * 2;
					Trace("Stake-votee suspicion: " + susp1.ToString("0.00"));
					// if we trust the stake-ee more, then the voter might be a vampire
					
					Relation(subject.Id).DarkSideSuspicion += susp2;
					Relation(subject.Id).Enmity += susp2 * 2;
					Trace("Stake-voter suspicion: " + (susp2).ToString("0.00"));
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
			innocents.Add(Me.Id);
			innocents.Add(whom);
			var suspects = Relations.Where(x => !innocents.Contains(x.PlayerId));


			if (LastNightAction.Whom == whom)
			{
				if (LastNightMet != null && LastNightMet.Any())
				{
					suspects = Relations.Where(x => LastNightMet.Contains(x.PlayerId));
				}
			}
			else
			{
				if (LastNightMet != null && LastNightMet.Any())
				{
					suspects = suspects.Where(x => !LastNightMet.Contains(x.PlayerId));
				}
			}

			if (suspects.Any())
			{
				var mostSuspectSuspect = 0d;
				var totalNewSuspicion = 0d;
				foreach (var rel in suspects)
				{
					// can be slightly negative, if we have multiple trusted reports of stay-homeness totalling > .5... that's ok:
					totalNewSuspicion += MIstrustToMult(0-LastNightWentOutProbability[rel.PlayerId] * 3);
					var thisSuspect = 1 - MIstrustToMult(LastNightWentOutProbability[rel.PlayerId] * 3) - MIstrustToMult(rel.DarkSideSuspicion);
					if (thisSuspect > mostSuspectSuspect)
						mostSuspectSuspect = thisSuspect;
				}

				foreach (var rel in suspects)
				{
					var amountSuspicion = MIstrustToMult(0-LastNightWentOutProbability[rel.PlayerId] * 3) / totalNewSuspicion;
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
			Relation(lier).DarkSideSuspicion += 5;
			Trace("Detect lie +5");

			// can rat out a fellow vampire we don't like
			if (!Me.IsVampire || Relation(lier).Enmity > r.NextDouble() * 2)
			{
				var comms = new CommsEvent(Me, topic, false, Me.Game.GetPlayer(lier));
				if (topic != CommsTypeEnum.LiedAboutSleeping)
				{
					// don't announce lied-about-sleeping because  processed elsewhere
					Me.Game.AddToLog(comms);
					Me.Game.Hub.AnnounceComms(Me.Game, comms, Me.Game.GetPlayer(lier).NameSpan + " lied! " + msg);
				}
			}
		}

		public void HearComms(CommsTypeEnum type, Player subject, Player whom)
		{
			if (subject == Me)
				return;
			// start mistrusting at suspicion > 0.5
			var trust = MIstrustToMult(Relation(subject.Id).DarkSideSuspicion);

			switch (type)
			{
				case CommsTypeEnum.IWasBitten:
					// first, process public effect
					if (Me.IsInJail)
					{
						SuspicionAgainstMe -= trust * .5; // assume others have similar mistrust
					}

					// next process private conclusions
					// (irrelevant if we did the biting!  except maybe lie about it)
					if (LastNightAction.Whom == subject.Id && LastNightAction.Action == NightActionEnum.Bite)
					{
						bool theyTrustMe = Relation(subject.Id).DarkSideSuspicion + r.NextDouble() - r.NextDouble() > SuspicionAgainstMe * 2;

						if (theyTrustMe && !LastNightMet.Any() && r.NextDouble() > .5)
						{
							var falseAccuse = new CommsEvent(Me, CommsTypeEnum.LiedAboutBeingBitten, true, subject);
							Me.Game.Hub.AnnounceComms(Me.Game, falseAccuse, "No possible suspects!");
						}
						return;
					}

					var worstSuspect = ProcessBiteInfo(subject.Id, trust);
					// no more cryWolf at approx worst suspect = .3

					var cryWolfSusp = 2 / (worstSuspect + 1.2) - 1.35;
					if (cryWolfSusp < 0)
						cryWolfSusp = 0;
					Relation(subject.Id).DarkSideSuspicion += cryWolfSusp;
					Trace("Cry-wolf addSusp: " + cryWolfSusp.ToString("0.00"));

					break;
				case CommsTypeEnum.Slept:
					if (LastNightMet != null && LastNightMet.Contains(whom.Id))
					{
						ProcessLie(subject.Id, CommsTypeEnum.GenericLie, whom + " went out!");
					}
					else if (whom.Id != Me.Id)
					{
						LastNightWentOutProbability[whom.Id] -= trust * .5;
						Relation(whom.Id).DarkSideSuspicion -= trust * .2;
					}
					break;
				case CommsTypeEnum.WentOut:
					if (whom.Id != Me.Id)
					{
						// if contrary evidence, don't trust the report

						var trustThisReport = trust - MIstrustToMult(0- LastNightWentOutProbability[whom.Id]) + .5;
						if (LastNightKnownSlept.Contains(whom.Id))
						{
							ProcessLie(subject.Id, CommsTypeEnum.GenericLie, whom + " slept all night!");
						}
						else if (LastNightAnnouncedSleep.Contains(whom.Id))
						{
							var accusedTrust = MIstrustToMult(Relation(whom.Id).DarkSideSuspicion);
							// this is insetad of using CommsTypeEnum.LiedAboutSleeping:
							LastNightWentOutProbability[whom.Id] += MIstrustToMult(Relation(subject.Id).DarkSideSuspicion - Relation(whom.Id).DarkSideSuspicion * .5) * .5;
							Relation(whom.Id).DarkSideSuspicion += trustThisReport - .1;
							var accuserAddSusp = accusedTrust * .75 + MIstrustToMult(0- LastNightWentOutProbability[whom.Id])+.5;
							Relation(subject.Id).DarkSideSuspicion += accuserAddSusp;
							Trace("Accused of sleep/went-out lying addSusp:" + (trustThisReport - .1).ToString("0.00"));
							Trace("Accuser of sleep/went-out lying addSusp:" + accuserAddSusp.ToString("0.00"));
						}
						else
						{
							LastNightWentOutProbability[whom.Id] += trust * .5;
							var accusedAddSusp = trustThisReport * .2;
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
					ProcessAccusation(subject.Id, whom.Id);
					break;
				case CommsTypeEnum.GenericLie:
					ProcessAccusation(subject.Id, whom.Id);
					break;
			}
		}

		public string PickEnemy(double threshHold)
		{
			var max = threshHold;

			string who = null;

			foreach (var x in Relations)
			{
				double t = 0;
				if (Me.Game.GetPlayer(x.PlayerId).IsMayor)
				{
					// only hate on the mayor if the mayor has power
					if (Math.Floor(Relations.Count / 2.0) == Relations.Count / 2.0)
						t += .2;
				}

				t += x.Enmity*.5;

				if (Me.IsVampire)
				{
					// stake anyone unless they're probably a vamp
					// todo: careful of staking publicly trusted
					t += r.NextDouble() - MIstrustToMult(5 - x.DarkSideSuspicion) - MIstrustToMult(x.DarkSideSuspicion+2);
				}
				else
				{
					t += MIstrustToMult(2 - x.DarkSideSuspicion) + x.DarkSideSuspicion * .05;
				}

				t = t * r.NextDouble() * r.NextDouble() + t * r.NextDouble() * r.NextDouble();
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
			LastNightKnownWentOut = new List<string>();
			LastNightMet = new List<string>();
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
						t -= MIstrustToMult(0 - re.DarkSideSuspicion);

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
						Relation(d.Whom).Enmity -= 2;
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
				Hub.AnnounceComms(Me.Game, comms);
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