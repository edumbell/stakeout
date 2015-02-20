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
		public Player Me { get; set; }
		public double SuspicionAgainstMe = 0;

		public List<Relation> Relations = new List<Relation>();

		private NightFormModel LastNightAction { get; set; }
		private List<string> LastNightAnnouncedSleep { get; set; }
		//private NightFormModel LastNightAction { get; set; }

		public Hubs.StakeHub Hub { get; set; }

		private Random r = new Random();

		private Relation Relation(string id)
		{
			if (id == Me.Id)
			{
				return new Relation()
				{
					Enmity = -100,
					GussedBites = Me.Bites,
					DarkSideSuspicion = Me.IsVampire ? 1 : -1,
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
				x.Enmity = x.Enmity * .8;
				x.GussedBites = x.GussedBites + .1;
				if (x.DarkSideSuspicion < x.GussedBites-1)
					x.DarkSideSuspicion = x.GussedBites-1;
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
			foreach (var rel in Relations)
			{
				if (Me.Game.JailedPlayer == null || Me.Game.JailedPlayer.Id != rel.PlayerId)
				{
					// player was active - 
					rel.DarkSideSuspicion += .5;					
				}
			}
			
		}

		public void AnnounceMeBitten(bool isTrue)
		{
			var comms = new CommsEvent(Me, CommsTypeEnum.IWasBitten, ! isTrue);
			Me.Game.AnnounceToAIs(comms);
			Hub.Send(Me.Game.GameId, Me.Id, "I've been bitten!");
			Me.Game.AddToLog(comms);
		}

		public void TellStakeVote(string id)
		{
			Relation(id).Enmity++;
		}

		public void ReceiveWatchResult(bool stayedAtHome, bool metAny)
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
				}
				else if (LastNightAnnouncedSleep.Contains(whomRelation.PlayerId))
				{
					// went out, and lied about it
					whomRelation.DarkSideSuspicion += 1;
				}
				else
				{
					// went out, but didn't lie about it
					whomRelation.DarkSideSuspicion += .3;
				}
				//}


				// before going any futher:  never claim to know anything if we claimed we were staying home!
				if (LastNightAnnouncedSleep.Contains(Me.Id))
					return;

				// work out whether/what to announce

				bool lie = false;
				if (Me.IsVampire && !metAny)
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
					if ( LastNightAnnouncedSleep.Contains(announceWhom))
					{
						// ok we're about to accuse someone of lying.  But careful not to rat out (or falsely accuse) an accomplice
						if (Me.IsVampire)
						{
							if (Relation(announceWhom).KnownVampire || Relation(announceWhom).GussedBites > 3 * r.NextDouble())
								keepMouthShut = true;
						}
						else
						{
							accuse = true;
							var comms = new CommsEvent(Me, CommsTypeEnum.LiedAboutSleeping, lie, Me.Game.GetPlayer(announceWhom));
							//Me.Game.AnnounceToAIs(comms);  redundant
							Me.Game.AddToLog(comms);
						}
					}

					if (! keepMouthShut)
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
			var mistrustOfWhom = -1.0;
			if (whom != null)
			{
				 mistrustOfWhom = .5 + Relation(whom.Id).DarkSideSuspicion;
				 if (mistrustOfWhom < 1)
					 mistrustOfWhom = 1;
			}
			switch (type)
			{
				case EventTypeEnum.VoteStake:
					// staking *me* enmity processed elsehwere
					// if we trust the voter, then we take their word for it
					// (but if not, then the stake-ee is probably an be innocent victim)
					Relation(whom.Id).DarkSideSuspicion += .5 / mistrustOfSubject -.2;
					// if we trust the stake-ee, then the voter might be a vampire
					
					Relation(subject.Id).DarkSideSuspicion += .4 / mistrustOfWhom - .2;
					break;
				case EventTypeEnum.WasJailed:
					Relation(subject.Id).GussedBites -= .1;
					break;
			}
		}

		public void HearComms(CommsTypeEnum type, Player subject, Player whom)
		{
			if (subject == Me)
				return;
			var mistrust = .5 + Relation(subject.Id).DarkSideSuspicion * .5;
			if (mistrust < 1)
				mistrust = 1;

			switch (type)
			{
				case CommsTypeEnum.IWasBitten:
					Relation(subject.Id).GussedBites += .6 / mistrust;
					if (Me.Game.JailedPlayer != null)
					{
						// rather than distrust all active players, trust the jailed player?
						Relation(Me.Game.JailedPlayer.Id).DarkSideSuspicion -= .3 / mistrust;
					}
					if (Me.IsInJail)
					{
						SuspicionAgainstMe -= .3 / mistrust;
					}
					break;
				case CommsTypeEnum.Slept:
					Relation(whom.Id).DarkSideSuspicion -= .2 / mistrust;
					break;
				case CommsTypeEnum.WentOut:
					if (LastNightAnnouncedSleep.Contains(whom.Id))
					{
						// this is same as case CommsTypeEnum.LiedAboutSleeping:
						Relation(whom.Id).DarkSideSuspicion += 1 / mistrust;
					}
					else
					{
						Relation(whom.Id).DarkSideSuspicion += .2 / mistrust;
					}
					break;
				case CommsTypeEnum.WillSleep:
					LastNightAnnouncedSleep.Add(subject.Id);
					break;
			}
		}

		public string PickEnemy(double threshHold)
		{
			var max = threshHold;
			string who = null;

			foreach (var x in Relations)
			{
				double t = r.NextDouble() - r.NextDouble();
				t += x.Enmity * r.NextDouble();

				if (Me.IsVampire && x.KnownVampire)
				{
					t -= 3;
				}

				if (Me.IsVampire)
				{
					t -= x.DarkSideSuspicion * r.NextDouble();
				}
				else
				{
					t += x.DarkSideSuspicion * r.NextDouble();
				}


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
				if (r.NextDouble() > SuspicionAgainstMe * 4 + .9)
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
		}

		public NightFormModel GetNightInstruction(List<string> ids, int turnId)
		{
			var otherIds = ids.Where(x => x != Me.Id).ToList();

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