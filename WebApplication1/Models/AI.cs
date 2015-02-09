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
		//public double Trust { get; set; }
		public double Bites { get; set; }
		public bool KnownVampire { get; set; }
	}

	public class AI
	{
		public Player Me { get; set; }
		public double SuspicionAgainstMe = 0;

		private List<Relation> Relations = new List<Relation>();

		private NightFormModel LastNightAction { get; set; }
		//private NightFormModel LastNightAction { get; set; }

		public Hubs.StakeHub Hub { get; set; }

		private Random r = new Random();

		private Relation Relation(string id)
		{
			return Relations.Where(x => x.PlayerId == id).Single();
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
			if (r.Next() * 1.5 - .5 > Me.Bites - 1 && SuspicionAgainstMe < 2)
			{
				Hub.Send(Me.Id, "I've been bitten!");
				SuspicionAgainstMe += 1;
			}
		}

		public void TellStakeVote(string id)
		{
			Relation(id).Enmity++;
		}

		public void TellWatchResult(bool stayedAtHome, bool metAny)
		{
			if (LastNightAction.Action == NightActionEnum.Watch)
			{
				bool lie = false;
				if (Me.IsVampire && ! metAny)
				{
					if (r.Next(10) > 5)
						return;
					if (r.Next(10) > 7)
						lie = true;
				}
				if (lie)
					stayedAtHome = !stayedAtHome;
				if (stayedAtHome)
				{
					Hub.Send(Me.Id, Game.GetPlayer(LastNightAction.Whom).Name + " stayed at home");
				}
				else
				{
					Hub.Send(Me.Id, Game.GetPlayer(LastNightAction.Whom).Name + " went out last night");
				}
			}
		}

		public string PickEnemy(List<string> ids)
		{
			var max = 0d;
			string who = RandomId(ids);
			foreach (var x in Relations.Where(x => !x.KnownVampire))
			{
				var t = x.Enmity * new Random().Next()  + new Random().Next()*.5;
				if (t > max)
				{
					max = t;
					who = x.PlayerId;
				}
			}
			return who;
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
				d.JailWhom = RandomId(otherIds);
				d.KillWhom = PickEnemy(otherIds);
			}

			if (r.Next() * 10 < 5 - (turnId / 2))
			{
				d.KillWhom = null;
			}

			if (r.Next() * 10 - 1 < SuspicionAgainstMe)
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
					if (r.Next() * 10 - r.Next() * 5 < turnId)
					{
						d.Action = NightActionEnum.Bite;
					}
					else if (r.Next() * 10 < 5)
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
					if (r.Next() * 10 < 5)
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
			if (d.Action == NightActionEnum.Sleep
				|| (
				d.Action == NightActionEnum.Bite
				&& r.Next() * 5 - r.Next() * 5 > SuspicionAgainstMe
				)
				)
			{
				Hub.Send(Me.Id, "I'm staying at home");
			}
			LastNightAction = d;
			return d;

		}
	}
}