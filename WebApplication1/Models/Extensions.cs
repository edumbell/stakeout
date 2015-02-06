using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using System.Reflection;
using System.Dynamic;
using System.Globalization;
using System.ComponentModel;
namespace WebApplication1.Models
{
	public static class Extensions
	{
		public static IEnumerable<SelectListItem> ToSelectList(this IEnumerable<Player> players)
		{
			return players.Select(p => new SelectListItem() { Text = p.Name, Value = p.Id });
		}

		public static IEnumerable<Player> Exclude(this IEnumerable<Player> players, string pid)
		{
			return players.Where(p => p.Id != pid).ToList();
		}


		public static string GetDescription<T>(this T enumeration) where T : struct, IConvertible
		{
			var attribute = enumeration.GetType().GetMember(enumeration.ToString())[0].GetCustomAttributes(typeof(DescriptionAttribute), false).Cast<DescriptionAttribute>().SingleOrDefault();

			if (attribute == null)
				return enumeration.ToString();
			return attribute.Description;
		}

		public static IEnumerable<SelectListItem> GetDropDownValues<T>(T? defaultValue = null, bool includeNull = false) where T : struct, IConvertible
		{
			var results = new List<SelectListItem>();
			var culture = CultureInfo.CurrentCulture.NumberFormat;
			if (includeNull)
				results.Add(new SelectListItem() { Text = "", Value = "-1", Selected = defaultValue == null });
			foreach (T value in Enum.GetValues(typeof(T)))
			{
				results.Add(new SelectListItem() { Text = value.GetDescription(), Value = value.ToInt16(culture).ToString(), Selected = value.Equals(defaultValue) });
			}
			return results;
		}


	}
}