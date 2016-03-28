using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

// todo: conflicting "was where"'s should be processed by comms, not by announce-generic-lie
// todo: ... then it's ok if vampires just announce their lies, without knowing whether to accuse?



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

		public double SuspicionThisTurn { get; set; }
		public double ContradictionsThisTurn { get; set; }
		public double GuessedBitesThisTurn { get; set; }
		public double KnownBitesThisTurn { get; set; }

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

		public double TrustThisTurn(string pid)
		{
			// only use after preprocessing contradictions
			var trust =  MIstrustToMult(Relation(pid).DarkSideSuspicion)
					- Relation(pid).ContradictionsThisTurn;
			if (trust < 0)
				trust = 0;
			return trust;
		}

		public string TraceLog = "";
		public string ThisTurnTraceLog = "";
		public Player Me { get; set; }
		public double SuspicionAgainstMe = 0;
		public double SuspicionAgainstMeThisTurn = 0;

		public List<Relation> Relations = new List<Relation>();

		//public List<Relation> CurrentTurnRelationChanges = new List<Relation>();

		public List<CommsEvent> CurrentTurnComms = new List<CommsEvent>();
		public List<CommsEvent> CurrentTurnAlreadyProcessedComms = new List<CommsEvent>();

		public NightFormModel LastNightAction { get; set; }
		private IEnumerable<string> LastNightMet { get; set; }
		private List<string> LastNightAnnouncedSleep { get; set; }
		private List<string> LastNightKnownSlept { get; set; }
		private List<string> LastNightKnownWentOut { get; set; }
		public int MeBittenLastNight { get; set; }

		// 0 = unknown; negative = probably slept
		private Dictionary<string, double> LastNightWentOutProbability { get; set; }

		private List<CommsEvent> KnownTruths { get; set; }
		private List<CommsEvent> KnownLies { get; set; }

		private double WentOutProbabilityCapped(string pid)
		{
			return Math.Max(Math.Min(LastNightWentOutProbability[pid], 1), -1);
		}

		//private NightFormModel LastNightAction { get; set; }

		public Hubs.StakeHub Hub { get; set; }

		private Random r = new Random();

		private void Trace(bool reprocessable, string msg)
		{
			if (reprocessable)
			{
				ThisTurnTraceLog += "<br/>" + msg;
			}
			else
			{
				TraceLog += "<br/>" + msg;
			}
		}

		private Relation Relation(string id)
		{
			if (id == Me.Id)
			{
				return new Relation()
				{
					Enmity = -100,
					GussedBites = Me.Bites,
					DarkSideSuspicion = Me.IsVampire ? 100 : -100,
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
			MeBittenLastNight++;
			if (r.NextDouble() * 1.8 > Me.AnnouncedBites + .5 && SuspicionAgainstMe < 2)
			{
				if (Me.Strategy == StrategyEnum.AI)
				{
					AnnounceMeBitten(true);
				}
			}

		}

		public void AnnounceMeBitten(bool isTrue)
		{
			SuspicionAgainstMe += .2;
			Me.AnnouncedBites += 1;
			var comms = new CommsEvent(Me, CommsTypeEnum.IWasBitten, !isTrue);
			Hub.AnnounceComms(Me.Game, comms);
		}

		public void TellStakeVote(string id)
		{
			SuspicionAgainstMe += .1;
			Relation(id).Enmity += 1;
		}

		public void DecideWhetherAnnounceWentOut(CommsEvent comms)
		{
			if (Me.Strategy == StrategyEnum.AI)
			{

				// covers both lies and truths

				var keepMouthShut = false;
				var accuse = false;

				if (comms.Whom !=  null && LastNightAnnouncedSleep.Contains(comms.Whom.Id))
				{
					// they went out, and lied about it [or we're claiming they lied about it] - do we accuse?
					// But careful not to rat out (or falsely accuse) an accomplice
					if (Me.IsVampire)
					{
						if (Relation(comms.Whom.Id).KnownVampire)
						{
							keepMouthShut = true;
						}
						else if (Relation(comms.Whom.Id).Enmity > 1 + 3 * r.NextDouble())
						{
							accuse = true;
						}
						else if (Relation(comms.Whom.Id).GussedBites > 1.5 + 1 * r.NextDouble())
						{
							// also don't rat out a probable vampire
							keepMouthShut = true;
						}
						else
							accuse = true;
					}
					else
					{
						accuse = true;
						// 'lied' comms redundant, as AI's detect discrepancy elsewhere

						//var comms = new CommsEvent(Me, CommsTypeEnum.LiedAboutSleeping, lie, Me.Game.GetPlayer(announceWhom));
						//Me.Game.AnnounceToAIs(comms);  redundant
						//Me.Game.AddToLog(comms);
					}
				}

				if (!keepMouthShut)
				{
					if (comms.Where == null)
					{
						if (accuse)
						{
							Hub.AnnounceComms(Me.Game, comms, true);
						}
						else
						{
							SuspicionAgainstMeThisTurn -= .1;
							Hub.AnnounceComms(Me.Game, comms);
						}
					}
					else
					{
						if (accuse)
						{
							Hub.AnnounceComms(Me.Game, comms, true);
						}
						else
						{
							SuspicionAgainstMeThisTurn -= .1;
							Hub.AnnounceComms(Me.Game, comms);
						}
					}

				}
			}
		}

		public void ReceiveWatchResult(bool stayedAtHome, IEnumerable<string> met)
		{
			LastNightMet = met;
			LastNightKnownWentOut.AddRange(met);
			foreach (var m in met)
			{
				LastNightWentOutProbability[m] = 100;
				if (LastNightAnnouncedSleep.Contains(m))
				{
					ProcessLie(m, CommsTypeEnum.LiedAboutSleeping, null, null);
				}
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
					LastNightWentOutProbability[whomRelation.PlayerId] = -100;
				}
				else
				{
					LastNightKnownWentOut.Add(whomRelation.PlayerId);
					LastNightWentOutProbability[whomRelation.PlayerId] = 100;
					if (LastNightAnnouncedSleep.Contains(whomRelation.PlayerId))
					{
						// went out, and lied about it
						whomRelation.DarkSideSuspicion += 5;
						Trace(false, "Detect lie sus +5");
					}
					else
					{
						// went out, but didn't lie about it
						whomRelation.DarkSideSuspicion += .2;
					}
				}
				//}


				if (Me.Strategy == StrategyEnum.AI)
				{

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
					string alsoAnnounceWhom = null;


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

						if (r.NextDouble() * 15 < Me.Game.CurrentTurn().Id + 5 - SuspicionAgainstMe)
						{
							alsoAnnounceWhom = PickEnemy(0);
							if (alsoAnnounceWhom == announceWhom)
								alsoAnnounceWhom = null;
							if (Me.Game.JailedPlayer != null)
							{
								if (alsoAnnounceWhom == Me.Game.JailedPlayer.Id)
									alsoAnnounceWhom = null;
							}
						}
						if (alsoAnnounceWhom != null)
						{
							var comms = new CommsEvent(Me, CommsTypeEnum.WentOut, true, Me.Game.GetPlayer(alsoAnnounceWhom), Me.Game.GetPlayer(LastNightAction.Whom));
							DecideWhetherAnnounceWentOut(comms);
						}
						
					}
					else
					{
						// not lying, so announce who we met
						foreach (var m in met)
						{

							var comms = new CommsEvent(Me, CommsTypeEnum.WentOut, false, Me.Game.GetPlayer(m), Me.Game.GetPlayer(LastNightAction.Whom));
							DecideWhetherAnnounceWentOut(comms);
							//Hub.AnnounceComms(Me.Game, comms);
						}
					}
					if (stayedAtHome)
					{
						//Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(announceWhom).Name + " stayed at home");
						var comms = new CommsEvent(Me, CommsTypeEnum.Slept, lie, Me.Game.GetPlayer(announceWhom));
						if (!lie)
							SuspicionAgainstMeThisTurn -= .1;
						Hub.AnnounceComms(Me.Game, comms);
					}
					else
					{
						var comms = new CommsEvent(Me, CommsTypeEnum.WentOut, lie, Me.Game.GetPlayer(announceWhom), null);
						DecideWhetherAnnounceWentOut(comms);
					}
				}
			}
		}


		// can be re-processed
		// similar logic to vote-to-stake, but in this case we know one or other is a vampire (or very silly lying human)
		// slept/went-out  processed elsewhere, making use of extra information
		public void ProcessAccusation(string accuser, string accused)
		{
			// difference of 1,  66% 33%
			// difference of 4, 89% 11%
			var newAccusedSusp = MIstrustToMult(Relation(accuser).DarkSideSuspicion - Relation(accused).DarkSideSuspicion);
			var newAccuserSusp = 1 - newAccusedSusp;
			newAccuserSusp -= .15;
			newAccusedSusp -= .15;
			Relation(accused).SuspicionThisTurn += newAccusedSusp;
			Trace(true,"Accused suspicion: " + newAccusedSusp.ToString("0.00"));
			Relation(accuser).SuspicionThisTurn += newAccuserSusp;
			Trace(true,"Accuser suspicion: " + newAccuserSusp.ToString("0.00"));


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
			{
				// processed elsewhere
				return;
			}

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
					var susp1 = relativeTrustOfSubject * .8 - .3; // being staked by shady character can make you look good
					var susp2 = relativeTrustOfWhom * .6 - .1;
					Relation(whom.Id).DarkSideSuspicion += susp1;
					if (!Me.IsVampire)
					{
						Relation(whom.Id).Enmity += susp1 * 2;
					}
					Trace(false, "Stake-votee suspicion: " + susp1.ToString("0.00"));
					// if we trust the stake-ee more, then the voter might be a vampire

					Relation(subject.Id).DarkSideSuspicion += susp2;
					if (!Me.IsVampire)
					{
						Relation(subject.Id).Enmity += susp2 * 2;
					}
					Trace(false, "Stake-voter suspicion: " + (susp2).ToString("0.00"));
					break;
				case EventTypeEnum.WasJailed:
					Relation(subject.Id).GussedBites -= .1;
					break;
			}
		}

		
		private double ProcessBiteInfo(string whom, double probabilityIsBite, CommsEvent comms)
		{
			// got here if someone was bitten (including self) and self didn't do the biting
			bool reprocess = probabilityIsBite < 1;

			Trace(reprocess, "Someone bitten (" + probabilityIsBite.ToString("0.00") + "):");

			Relation(whom).GuessedBitesThisTurn += probabilityIsBite;
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
					totalNewSuspicion += MIstrustToMult(0 - LastNightWentOutProbability[rel.PlayerId] * 3);
					var thisSuspect = 1 - MIstrustToMult(LastNightWentOutProbability[rel.PlayerId] * 3 + .3) - MIstrustToMult(rel.DarkSideSuspicion + .3);
					if (thisSuspect > mostSuspectSuspect)
						mostSuspectSuspect = thisSuspect;
				}

				if (probabilityIsBite < 1)
				{
					probabilityIsBite = probabilityIsBite * mostSuspectSuspect;
				}
				Trace(reprocess, "probabilityIsBite reduce to: " + probabilityIsBite.ToString("0.00"));

				foreach (var rel in suspects)
				{
					var amountSuspicion = MIstrustToMult(0 - LastNightWentOutProbability[rel.PlayerId] * 3) / totalNewSuspicion;
					var addSusp = 3 * amountSuspicion * probabilityIsBite;
					if (reprocess)
					{
						rel.SuspicionThisTurn += addSusp;
					}
					else
					{
						rel.DarkSideSuspicion += addSusp;
					}
					Trace(reprocess, "LastNightWentOut: " + LastNightWentOutProbability[rel.PlayerId] + " addSusp:" + addSusp.ToString("0.00"));
				}
				return mostSuspectSuspect;
			}
			else
			{
				// if no possible suspects, and self didn't do the biting, then we know somebody lied

				ProcessLie(whom, CommsTypeEnum.LiedAboutBeingBitten, "No possible suspects!", comms);

				return -10;
			}

		}

		public void ProcessTruth(CommsEvent comm)
		{
			if (comm == null || !CurrentTurnAlreadyProcessedComms.Contains(comm))
			{
				CurrentTurnAlreadyProcessedComms.Add(comm);
			}
			else
			{
				return;
			}
			KnownTruths.Add(comm);
			Relation(comm.Speaker.Id).SuspicionThisTurn -= .3;
		}

		// will skip if already processed
		public void ProcessLie(string lier, CommsTypeEnum topic, string msg, CommsEvent fromComms)
		{
			
			if (fromComms == null || !CurrentTurnAlreadyProcessedComms.Contains(fromComms))
			{
				CurrentTurnAlreadyProcessedComms.Add(fromComms);
			}
			else
			{
				return;
			}
			KnownLies.Add(fromComms);

			Relation(lier).DarkSideSuspicion += 5;
			Trace(false,"Detect lie +5");

			// can rat out a fellow vampire we don't like
			if (!Me.IsVampire || Relation(lier).Enmity > r.NextDouble() * 2)
			{
				if (Me.Strategy == StrategyEnum.AI)
				{
					var comms = new CommsEvent(Me, topic, false, Me.Game.GetPlayer(lier));
					if (topic != CommsTypeEnum.LiedAboutSleeping)
					{
						// don't announce lied-about-sleeping because  processed elsewhere
						//Me.Game.AddToLog(comms);
						Me.Game.Hub.AnnounceComms(Me.Game, comms, true, Me.Game.GetPlayer(lier).NameSpan + " lied! " + msg);
					}
				}
			}
		}

		public void HearComms(CommsEvent comm) //CommsTypeEnum type, Player subject, Player whom, Player where)
		{
			CurrentTurnComms.Add(comm);
			ReprocessAllComms();
		}

		public bool IsSleepDeclaration(CommsTypeEnum c1)
		{
			return (c1 == CommsTypeEnum.Slept || c1 == CommsTypeEnum.WillSleep);
		}

		public bool IsLieAccusation(CommsTypeEnum c1)
		{
			return c1 == CommsTypeEnum.GenericLie
		|| c1 == CommsTypeEnum.LiedAboutBeingBitten
		|| c1 == CommsTypeEnum.LiedAboutSleeping;
		}

		public bool AreEquivalent(CommsTypeEnum c1, CommsTypeEnum c2)
		{
			if (c1 == c2)
				return true;
			if (IsSleepDeclaration(c1) && IsSleepDeclaration(c2))
				return true;
			if (IsLieAccusation(c1) && IsLieAccusation(c2))
				return true;
			return false;
		}

		public bool AreMutuallyExclusive(CommsTypeEnum c1, CommsTypeEnum c2)
		{
			if (IsSleepDeclaration(c1) && c2 == CommsTypeEnum.WentOut)
				return true;
			if (IsSleepDeclaration(c2) && c1 == CommsTypeEnum.WentOut)
				return true;
			return false;
		}


		public void PreprocessAllComms()
		{
			// calculates LastNightWentOutProbability and detects contradictions
			
			foreach (var comm in CurrentTurnComms.Where(x => x.Speaker != Me ))
			{
				var thisRelation = Relation(comm.Speaker.Id);


				var aboutSamePerson = CurrentTurnComms.Where(x => x.Whom == comm.Whom &&  x != comm);
				if (aboutSamePerson.Any())
				{

						foreach (var a in aboutSamePerson)
						{
							var otherTrust = MIstrustToMult(Relation(a.Speaker.Id).DarkSideSuspicion);
							if (KnownTruths.Contains(a))
								otherTrust = 100;
							if (KnownLies.Contains(a))
								otherTrust -= 100;
							if (AreMutuallyExclusive(a.EventType, comm.EventType)
								)
							{
								thisRelation.ContradictionsThisTurn += otherTrust;
								thisRelation.SuspicionThisTurn += otherTrust * .5;
							}
							else if (AreEquivalent(a.EventType, comm.EventType))
							{
								if (a.Where == comm.Where || a.Where == null || comm.Where == null) // could be null - that's fine
								{
									thisRelation.ContradictionsThisTurn -= otherTrust * .5;
									thisRelation.SuspicionThisTurn -= otherTrust * .2;
								}
								else if (a.Where != comm.Where)
								{
									thisRelation.ContradictionsThisTurn += otherTrust ;
									thisRelation.SuspicionThisTurn += otherTrust * .5;
								}
							}
							// else the two assertions are orthoganal
						}

				}

			}
			foreach (var comm in CurrentTurnComms.Where(x => x.Speaker != Me))
			{
				var thisRelation = Relation(comm.Speaker.Id);
				var trust = TrustThisTurn(comm.Speaker.Id);

				switch (comm.EventType)
				{
					case CommsTypeEnum.Slept:
					case CommsTypeEnum.WillSleep:
						if (comm.Whom.Id != Me.Id)
						{
							LastNightWentOutProbability[comm.Whom.Id] -= trust * .5;
						}
						break;
					case CommsTypeEnum.WentOut:
						if (comm.Whom.Id != Me.Id)
						{
							LastNightWentOutProbability[comm.Whom.Id] += trust * .5;
						}
						break;
				}
			}
		}

		public void ProcessComms(CommsEvent comm)
		{
			if (comm.Speaker == Me)
			{
				return;
			}
			// start mistrusting at suspicion > 0.5
			var trust = TrustThisTurn(comm.Speaker.Id);

			var whom = comm.Whom;
			var where = comm.Where;
			var subject = comm.Speaker;

			switch (comm.EventType)
			{
				case CommsTypeEnum.IWasBitten:
					// first, process public effect
					if (Me.IsInJail)
					{
						SuspicionAgainstMeThisTurn -= trust * .5; // assume others have similar mistrust
					}

					// next process private conclusions


					if (LastNightAction.Whom == comm.Speaker.Id && LastNightAction.Action == NightActionEnum.Bite)
					{
						// (irrelevant if we did the biting!  except maybe lie about it)
						// don't try and accuse if we're already more under suspicion than the bite-ee
						bool theyTrustMe = Relation(comm.Speaker.Id).DarkSideSuspicion + r.NextDouble() * 2 - r.NextDouble() * 3 > SuspicionAgainstMe;

						// don't try and accuse if there was another observer who'd know that we can't possibly know
						if (theyTrustMe && !LastNightMet.Any() && r.NextDouble() > .5)
						{
							if (Me.Strategy == StrategyEnum.AI)
							{

								var falseAccuse = new CommsEvent(Me, CommsTypeEnum.LiedAboutBeingBitten, true, subject);
								Me.Game.Hub.AnnounceComms(Me.Game, falseAccuse, true, "No possible suspects!");
							}
						}
						return;
					}

					var worstSuspect = ProcessBiteInfo(comm.Speaker.Id, trust, comm);
					Trace(true, "Worst suspect divisor: " + worstSuspect.ToString("0.00"));
					// no more cryWolf at approx worst suspect = .7

					var cryWolfSusp = 3 / (worstSuspect + .8) - 2;
					if (cryWolfSusp < -.2)
						cryWolfSusp = -.2;
					Relation(comm.Speaker.Id).DarkSideSuspicion += cryWolfSusp;
					Trace(true,"Cry-wolf addSusp: " + cryWolfSusp.ToString("0.00"));

					break;
				case CommsTypeEnum.Slept:
					if (LastNightMet != null && LastNightMet.Contains(comm.Whom.Id))
					{
						ProcessLie(comm.Speaker.Id, CommsTypeEnum.GenericLie, comm.Whom + " went out!", comm);
					}
					else if (comm.Whom.Id != Me.Id)
					{
						Relation(comm.Speaker.Id).SuspicionThisTurn -= .1; // trust for not-contradicted information

						Relation(comm.Whom.Id).SuspicionThisTurn -= trust * .2;
					}
					else if (comm.Whom.Id == Me.Id)
					{
						if (LastNightAction.Action != NightActionEnum.Sleep)
						{
							ProcessLie(comm.Speaker.Id, CommsTypeEnum.GenericLie, "I slept last night", comm);
							// todo: one-per-turn negative enmity
						}
						else
							Relation(subject.Id).SuspicionThisTurn -= .15; // trust for correct information
					}
					else if (LastNightKnownSlept.Contains(comm.Whom.Id))
					{
						ProcessTruth(comm);
					}
					break;
				case CommsTypeEnum.WentOut:
					if (comm.Whom.Id != Me.Id)
					{
						// if contrary evidence, don't trust the report

						var trustThisReport = trust - MIstrustToMult(0 - LastNightWentOutProbability[comm.Speaker.Id]) + .5;
						if (LastNightKnownSlept.Contains(comm.Whom.Id))
						{
							ProcessLie(comm.Speaker.Id, CommsTypeEnum.GenericLie, comm.Whom.NameSpan + " slept all night!", comm);
						}
						else if (LastNightMet.Contains(whom.Id) && where != null)
						{
							// we have hard evidence
							if (LastNightAction.Whom == where.Id)
							{
								// truth
								ProcessTruth(comm);
							}
							else
							{
								// lie
								ProcessLie(subject.Id, CommsTypeEnum.GenericLie, subject.NameSpan + " lied! " + whom.NameSpan + " was at " + where.NameSpan + "'s", comm);
							}
						}
						else if (LastNightAnnouncedSleep.Contains(whom.Id))
						{
							var accusedTrust = MIstrustToMult(Relation(whom.Id).DarkSideSuspicion);
							// this is insetad of using CommsTypeEnum.LiedAboutSleeping:
							LastNightWentOutProbability[whom.Id] += MIstrustToMult(Relation(subject.Id).DarkSideSuspicion - Relation(whom.Id).DarkSideSuspicion * .5) * .5;
							Relation(whom.Id).SuspicionThisTurn += trustThisReport - .1;
							var accuserAddSusp = accusedTrust * .75 * ( MIstrustToMult( LastNightWentOutProbability[whom.Id]));
							Relation(subject.Id).SuspicionThisTurn += accuserAddSusp;
							Trace(true,"Accused of sleep/went-out lying addSusp:" + (trustThisReport - .1).ToString("0.00"));
							Trace(true, "Accuser of sleep/went-out lying addSusp:" + accuserAddSusp.ToString("0.00"));
						}
						else
						{
							Relation(subject.Id).SuspicionThisTurn -= .1; // trust for not-yet-contradicted information
							LastNightWentOutProbability[whom.Id] += trust * .5;
							var whomAddSusp = trustThisReport * .2;
							Relation(whom.Id).SuspicionThisTurn += whomAddSusp;
							Trace(true, "Heard went-out addSusp:" + whomAddSusp.ToString("0.00"));
						}
					}
					else
					{
						// claimed that I went out
						if (LastNightAction.Action == NightActionEnum.Sleep)
							ProcessLie(subject.Id, CommsTypeEnum.GenericLie, "I slept all night!", comm);
						else
							Relation(subject.Id).SuspicionThisTurn -= .15; // trust for correct information
					}
					break;
				case CommsTypeEnum.WillSleep:
					Relation(subject.Id).SuspicionThisTurn -= .1; // trust for not-yet-contracted information
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

		public string PickJailee(double threshHold)
		{
			var max = threshHold;

			string who = null;

			foreach (var x in Relations)
			{
				double t = 1;

				if (x.PlayerId == Me.Id)
				{
					t += MIstrustToMult(0 - SuspicionAgainstMe);
				}
				else
				{
					t += MIstrustToMult(0 - x.DarkSideSuspicion);
				}
				if (x.GussedBites < 3)
				{
					t += x.GussedBites;
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


		/// <summary>
		/// NB can pick player in jail
		/// </summary>
		/// <param name="threshHold"></param>
		/// <returns></returns>
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

				t += x.Enmity * .2;

				if (Me.IsVampire)
				{
					// stake anyone unless they're probably a vamp
					// todo: careful of staking publicly trusted
					// prefer anyone between 0 and 5 susp
					t += r.NextDouble() * 2 - MIstrustToMult(5 - x.DarkSideSuspicion) - MIstrustToMult(x.DarkSideSuspicion + 2);

					var numAllies = Relations.Sum(rr => (rr.KnownVampire || rr.GussedBites > 3) ? 1 : 0) + 1;
					var numEnemies = Relations.Count() + 1 - numAllies;
					// if vampires prob in majority, go stake crazy
					if (numAllies > numEnemies
						|| Me.IsMayor && numAllies == numEnemies
						)
						t = t * 2 + 1;
				}
				else
				{
					t += MIstrustToMult(2 - x.DarkSideSuspicion) * 1.5 + x.DarkSideSuspicion * .05;
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
					if (Me.Strategy == StrategyEnum.AI)
					{

						AnnounceMeBitten(false);
						SuspicionAgainstMe += 1;
					}
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
				d.KillWhom = PickEnemy(.9);
			}

			//if (r.Next(10)  < 5 - (turnId / 2))
			//{
			//	d.KillWhom = null;
			//}

			if (r.NextDouble() * 10 - 1 < SuspicionAgainstMe)
			{
				d.JailWhom = Me.Id;
			};
			if (d.KillWhom != null)
			{
				SuspicionAgainstMe += .1;
			}
			return d;
		}

		public void ReprocessAllComms()
		{
			SuspicionAgainstMeThisTurn = 0;
			var keys = LastNightWentOutProbability.Keys.ToList();
			foreach (var k in keys)
			{
				if (LastNightWentOutProbability[k] < -10 || LastNightWentOutProbability[k] > 10)
				{
					// we have knowledge.  leave alone
				}
				else
				{
					// we're relying on comms, so reset to zero before reprocessing comms
					LastNightWentOutProbability[k] = 0;
				}
			}
			foreach (var r in Relations)
			{
				r.GuessedBitesThisTurn = 0;
				r.KnownBitesThisTurn = 0;
				r.SuspicionThisTurn = 0;
				r.ContradictionsThisTurn = 0;
			}
			PreprocessAllComms();
			foreach (var c in CurrentTurnComms.ToList())
			{
				ProcessComms(c);
			}
			if (MeBittenLastNight > 0)
				ProcessBiteInfo(Me.Id, MeBittenLastNight, null);
		}

		public void ResetBeforeNight(List<string> ids)
		{
			LastNightAnnouncedSleep = new List<string>();
			LastNightKnownSlept = new List<string>();
			LastNightKnownWentOut = new List<string>();
			LastNightMet = new List<string>();
			MeBittenLastNight = 0;
			TraceLog += ThisTurnTraceLog;
			ThisTurnTraceLog = "";

			LastNightWentOutProbability = new Dictionary<string, double>();
			foreach (var id in ids)
			{
				LastNightWentOutProbability.Add(id, 0);
			}
			KnownLies = new List<CommsEvent>();
			KnownTruths = new List<CommsEvent>();
			CurrentTurnComms = new List<CommsEvent>();
			CurrentTurnAlreadyProcessedComms = new List<CommsEvent>();
		}

		public void OnDayEnding()
		{
			//ReprocessAllComms();
			// apply values
			SuspicionAgainstMe += SuspicionAgainstMeThisTurn;
			SuspicionAgainstMeThisTurn = 0;
			foreach (var r in Relations)
			{
				r.GussedBites += r.GuessedBitesThisTurn;
				r.KnownBites += r.KnownBitesThisTurn;
				r.DarkSideSuspicion += r.SuspicionThisTurn;
				r.PublicSuspicion += r.SuspicionThisTurn; // fudge... all suspicion from comms, not from observations
			}
		}

		public NightFormModel GetNightInstruction(List<string> ids, int turnId)
		{
			var otherIds = ids.Where(x => x != Me.Id).ToList();
			// if it's the first night, ensure we have a relation object for each other player
			foreach (var id in otherIds)
			{
				var dummy = Relation(id);
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
				if (Me.Strategy == StrategyEnum.AI)
				{
					var isTrue = d.Action == NightActionEnum.Sleep;
					var comms = new CommsEvent(Me, CommsTypeEnum.WillSleep, !isTrue, Me);
					SuspicionAgainstMe -= .1;
					Hub.AnnounceComms(Me.Game, comms);
					LastNightAnnouncedSleep.Add(Me.Id);
				}
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