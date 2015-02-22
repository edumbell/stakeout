using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApplication1.Models
{
	public class GameLog
	{
	}

	public enum CommsTypeEnum
	{
		WillSleep = 1, // broadcast to AI
		IWasBitten = 2, // broadcast to AI
		Slept = 3, // broadcast to AI
		WentOut = 4, // broadcast to AI
		LiedAboutSleeping = 4, // broadcast to AI
		LiedAboutBeingBitten = 5
	}

	public enum EventTypeEnum
	{
		VoteStake = 1,
		VoteJail = 2,
		WasJailed = 6,
		WasStaked = 7,
		Sleep = 3,// logged
		Watch = 4,// logged
		Bite = 5,// logged
		MetVampire = 10,
		Met = 8,
		WasBitten = 9
	}

	public abstract class LogItem
	{
		public string Turn { get; set; }
		public Player Subject { get; set; }
		public Player Whom { get; set; }
	}
	public class GameEvent : LogItem
	{
		public EventTypeEnum EventType { get; set; }
		public Player Where { get; set; }
		public GameEvent()
		{

		}
		public GameEvent(NightInstruction ni)
		{
			Subject = ni.Actor;
			Whom = ni.Whom;
			switch (ni.Action)
			{
				case NightActionEnum.Bite:
					EventType = EventTypeEnum.Bite;
					break;
				case NightActionEnum.Sleep:
					EventType = EventTypeEnum.Sleep;
					break;
				case NightActionEnum.Watch:
					EventType = EventTypeEnum.Watch;
					break;
				default:
					throw new Exception("Unsupported Night Instruction enum");
			}
		}


		public override string ToString()
		{
			var result = Subject.NameSpan + " " + EventType;
			if (Whom != null)
			{
				result += " " + Whom.NameSpan;
			}
			if (Where != null)
			{
				result += " at " + Where.Name + "'s";
			}
			return result;
		}
	}

	public class CommsEvent : LogItem
	{
		public CommsTypeEnum EventType { get; set; }

		public bool Lied { get; set; }

		public CommsEvent(Player subject, CommsTypeEnum type, bool lied, Player whom = null)
		{
			EventType = type;
			Subject = subject;
			Whom = whom;
			Lied = lied;
		}

		public override string ToString()
		{
			string verb = "said";
			if (Lied)
				verb = "lied";
			var result = Subject.NameSpan + " " + verb;
			if (Whom != null)
			{
				result += " " + Whom.NameSpan;
			}
			result += " " + EventType;
			if (Lied)
			{
				result += " *";
			}
			return  result;
		}

	}


}