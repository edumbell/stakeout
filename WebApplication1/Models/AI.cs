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
		public double Bites { get; set; }
		public bool KnownVampire { get; set; }
	}

	public class AI
	{
		public Player Me { get; set; }
		public double SuspicionAgainstMe = 0;

		private List<Relation> Relations = new List<Relation>();

		private NightFormModel LastNightAction { get; set; }
		private bool LastNightAnnouncedSleep { get; set; }
		//private NightFormModel LastNightAction { get; set; }

		public Hubs.StakeHub Hub { get; set; }

		private Random r = new Random();

		private Relation Relation(string id)
		{
			return Relations.Where(x => x.PlayerId == id).Single();
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
			}
		}

		public void TellFellowVampire(string pid)
		{
			Relation(pid).KnownVampire = true;
			Relation(pid).Enmity = -3;
		}

		public void TellBitten()
		{
			if (r.NextDouble() * 1.5 - .5 > Me.Bites - 1 && SuspicionAgainstMe < 2)
			{
				AnnounceBitten(true);
				SuspicionAgainstMe += 1;
			}
		}

		public void AnnounceBitten(bool isTrue)
		{
			Hub.Send(Me.Game.GameId, Me.Id, "I've been bitten!");
			Me.Game.AddToLog(Me, CommsTypeEnum.IWasBitten, isTrue);
		}

		public void TellStakeVote(string id)
		{
			Relation(id).Enmity++;
		}

		public void TellWatchResult(bool stayedAtHome, bool metAny)
		{
			
			if (LastNightAction.Action == NightActionEnum.Watch || LastNightAction.Action == NightActionEnum.Bite)
			{
				var whomRelation = Relations.Where(x => x.PlayerId == LastNightAction.Whom).Single();

				// set suspicion based on observation
				if (Me.IsVampire)
				{
					if (stayedAtHome)
						whomRelation.DarkSideSuspicion -= .3;
					else
						whomRelation.DarkSideSuspicion += .4;
				}


				// before going any futher:  never claim to know anything if we claimed we were staying home!
				if (LastNightAnnouncedSleep)
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
					Me.Game.AddToLog(Me, CommsTypeEnum.Slept, !lie, Me.Game.GetPlayer(announceWhom));
				}
				else
				{
					Hub.Send(Me.Game.GameId, Me.Id, Me.Game.GetPlayer(announceWhom).Name + " went out last night");
					Me.Game.AddToLog(Me, CommsTypeEnum.WentOut, !lie, Me.Game.GetPlayer(announceWhom));
				}
			}
		}

		public string PickEnemy(double threshHold)
		{
			var max = threshHold;
			string who = RandomNotMe();

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
					AnnounceBitten(false);
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


		public NightFormModel GetNightInstruction(List<string> ids, int turnId)
		{
			var otherIds = ids.Where(x => x != Me.Id).ToList();

			if (!Relations.Any())
			{
				// must be start of game.
				Relations.AddRange(otherIds.Select(x => new Relation() { PlayerId = x }));
			}

			SuspicionAgainstMe += .1;

			if (SuspicionAgainstMe > 1)
			{
				SuspicionAgainstMe += .2;
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
					if (r.NextDouble() * 10 - r.NextDouble() * 5 < turnId )
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
					foreach (var re in Relations.Where(x => !x.KnownVampire))
					{
						var t = re.Bites + new Random().Next() * 2;
						if (t > maxTastiness)
						{
							maxTastiness = t;
							d.Whom = re.PlayerId;
						}
					}
					Relation(d.Whom).Bites++;
					Relation(d.Whom).DarkSideSuspicion += .5;
					if (Relation(d.Whom).Bites >= 3)
					{
						Relation(d.Whom).KnownVampire = true;
					}
				}
			}
			else
			{
				d.Action = NightActionEnum.Sleep;
			}
			LastNightAnnouncedSleep = false;
			if (d.Action == NightActionEnum.Sleep
				|| (
				d.Action == NightActionEnum.Bite
				&& r.NextDouble() * 5 - r.NextDouble() * 5 > SuspicionAgainstMe
				)
				)
			{
				var isTrue = d.Action == NightActionEnum.Sleep;
				Me.Game.AddToLog(Me, CommsTypeEnum.WillSleep, isTrue);
				Hub.Send(Me.Game.GameId, Me.Id, "I'm staying at home");
				LastNightAnnouncedSleep = true;
			}
			LastNightAction = d;
			return d;

		}
	}
}