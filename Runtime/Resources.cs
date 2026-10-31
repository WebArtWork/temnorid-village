using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Temnorid.Village
{
	/// <summary>The six resources of Lords (resource.data.ts), in Lords' order, with their ids, English names and colours.</summary>
	public static class Res
	{
		public const string Wood = "wood";
		public const string Stone = "stone";
		public const string Iron = "iron";
		public const string Water = "water";
		public const string Grain = "grain";
		public const string Clay = "clay";

		public static readonly string[] All = { Wood, Stone, Iron, Water, Grain, Clay };

		static readonly string[] Names = { "Wood", "Stone", "Iron", "Water", "Grain", "Clay" };
		/// <summary>Lords' resource colours.</summary>
		static readonly string[] Hex = { "#3f6b2f", "#7a746b", "#8a4b33", "#2f78a0", "#b8892a", "#a0613a" };

		public static int IndexOf(string id) => Array.IndexOf(All, id);
		public static bool IsKnown(string id) => IndexOf(id) >= 0;

		public static string Name(string id)
		{
			var index = IndexOf(id);
			return index >= 0 ? Names[index] : id;
		}

		public static string HexOf(string id)
		{
			var index = IndexOf(id);
			return index >= 0 ? Hex[index] : "#808080";
		}

		public static Color ColorOf(string id)
		{
			ColorUtility.TryParseHtmlString(HexOf(id), out var colour);
			return colour;
		}
	}

	/// <summary>An amount of each of the six resources (Lords' <c>Amounts</c>). Fractions are kept: production is fractional.</summary>
	[JsonConverter(typeof(AmountsJson))]
	public sealed class Amounts
	{
		readonly double[] _values = new double[Res.All.Length];

		public double this[string id]
		{
			get { var index = Res.IndexOf(id); return index >= 0 ? _values[index] : 0d; }
			set { var index = Res.IndexOf(id); if (index >= 0) _values[index] = value; }
		}

		public double this[int index]
		{
			get => _values[index];
			set => _values[index] = value;
		}

		public static Amounts Filled(double value)
		{
			var amounts = new Amounts();
			for (var i = 0; i < amounts._values.Length; i++) amounts._values[i] = value;
			return amounts;
		}

		public Amounts Clone()
		{
			var copy = new Amounts();
			Array.Copy(_values, copy._values, _values.Length);
			return copy;
		}

		public bool IsZero()
		{
			foreach (var value in _values) if (value != 0d) return false;
			return true;
		}

		public bool SameAs(Amounts other, double tolerance = 1e-9)
		{
			if (other == null) return false;
			for (var i = 0; i < _values.Length; i++) if (Math.Abs(_values[i] - other._values[i]) > tolerance) return false;
			return true;
		}

		/// <summary>A JSON object with the six ids, as Lords saves it.</summary>
		public JObject ToJson()
		{
			var json = new JObject();
			for (var i = 0; i < Res.All.Length; i++) json[Res.All[i]] = _values[i];
			return json;
		}

		/// <summary>Reads an object of numbers; unknown ids and non-numbers are ignored, missing ids are 0.</summary>
		public static Amounts FromJson(JToken token)
		{
			var amounts = new Amounts();
			if (token is JObject obj)
			{
				foreach (var property in obj.Properties())
				{
					var index = Res.IndexOf(property.Name);
					if (index < 0) continue;
					if (property.Value.Type == JTokenType.Integer || property.Value.Type == JTokenType.Float) amounts._values[index] = property.Value.Value<double>();
				}
			}
			return amounts;
		}

		/// <summary>True when the token is an object whose six resources are all numbers: the shape Lords writes.</summary>
		public static bool IsComplete(JToken token)
		{
			if (!(token is JObject obj)) return false;
			foreach (var id in Res.All)
			{
				var value = obj[id];
				if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return false;
				var number = value.Value<double>();
				if (double.IsNaN(number) || double.IsInfinity(number)) return false;
			}
			return true;
		}

		public override string ToString()
		{
			var parts = new List<string>();
			for (var i = 0; i < Res.All.Length; i++) parts.Add(Res.All[i] + "=" + _values[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
			return string.Join(" ", parts);
		}
	}

	sealed class AmountsJson : JsonConverter
	{
		public override bool CanConvert(Type objectType) => objectType == typeof(Amounts);

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null) return null;
			return Amounts.FromJson(JToken.Load(reader));
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			((Amounts)value).ToJson().WriteTo(writer);
		}
	}
}
