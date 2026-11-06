using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Temnorid.Village
{
	/// <summary>A trip into a dungeon (Lords' <c>HeroExpedition</c>). The loot and the wounds are rolled when the hero sets out.</summary>
	public sealed class HeroExpedition
	{
		[JsonProperty("dungeon")] public string Dungeon = "";
		[JsonProperty("startedAt")] public long StartedAt;
		[JsonProperty("endsAt")] public long EndsAt;
		/// <summary>What the hero brings back, by resource id.</summary>
		[JsonProperty("loot")] public Dictionary<string, double> Loot = new Dictionary<string, double>();
		/// <summary>Health lost, taken when the hero is back. Wounds deeper than the health the hero has then mean the hero falls.</summary>
		[JsonProperty("damage")] public double Damage;
	}

	/// <summary>The lord's hero as Lords saves it in <c>hero</c> (Lords' <c>HeroSave</c>).</summary>
	public sealed class HeroSave
	{
		/// <summary>Health at <see cref="At"/>.</summary>
		[JsonProperty("health")] public double Health = HeroRules.MaxHealth;
		[JsonProperty("at")] public long At;
		[JsonProperty("expedition", NullValueHandling = NullValueHandling.Include)] public HeroExpedition Expedition;

		public static HeroSave New(long now) => new HeroSave { Health = HeroRules.MaxHealth, At = now };

		public JObject ToJson() => JObject.FromObject(this);
	}

	/// <summary>home: free to go. away: in a dungeon. back: home with loot to hand over. infirmary: fell, recovering.</summary>
	public enum HeroStatus
	{
		Home,
		Away,
		Back,
		Infirmary,
	}

	/// <summary>Why the hero cannot go into a dungeon now.</summary>
	public enum CannotGo
	{
		None,
		Busy,
		Infirmary,
		Weak,
	}

	/// <summary>One of the caves and ruins of Podillia (Lords' <c>Dungeon</c>, dungeon.data.ts), easiest first.</summary>
	public sealed class DungeonDef
	{
		public string Id;
		/// <summary>The name shown to the player.</summary>
		public string Name;
		public string Description;
		/// <summary>1 easy, 2 hard, 3 deadly.</summary>
		public int Danger;
		/// <summary>Kilometres.</summary>
		public int Distance;
		public int Minutes;
		public int DamageMin, DamageMax;
		/// <summary>Resource id, min, max.</summary>
		public (string Resource, double Min, double Max)[] Loot;

		public string LootText()
		{
			var parts = new List<string>();
			foreach (var item in Loot) parts.Add(Res.Name(item.Resource));
			return string.Join(", ", parts);
		}
	}

	/// <summary>The six dungeons, with the same ids and numbers as Lords.</summary>
	public static class DungeonCatalog
	{
		public static readonly DungeonDef[] All =
		{
			new DungeonDef { Id = "bakota-caves", Name = "Bakota Caves", Description = "The abandoned cells of a cliff monastery over the Dniester.", Danger = 1, Distance = 12, Minutes = 5, DamageMin = 5, DamageMax = 12,
				Loot = new[] { (Res.Grain, 40d, 80d), (Res.Clay, 40d, 80d) } },
			new DungeonDef { Id = "atlantida", Name = "Atlantida Cave", Description = "Gypsum halls near Zavallia where the walls glitter with crystals.", Danger = 1, Distance = 20, Minutes = 8, DamageMin = 8, DamageMax = 16,
				Loot = new[] { (Res.Stone, 40d, 80d), (Res.Water, 30d, 70d) } },
			new DungeonDef { Id = "mlynky", Name = "Mlynky Cave", Description = "Narrow passages under Chortkiv where people fled from raids.", Danger = 2, Distance = 30, Minutes = 12, DamageMin = 14, DamageMax = 26,
				Loot = new[] { (Res.Wood, 60d, 100d), (Res.Iron, 70d, 120d), (Res.Clay, 80d, 140d) } },
			new DungeonDef { Id = "kryshtaleva", Name = "Crystal Cave", Description = "Above the Zbruch: torchlight breaks into a thousand crystals.", Danger = 2, Distance = 45, Minutes = 18, DamageMin = 20, DamageMax = 36,
				Loot = new[] { (Res.Stone, 80d, 140d), (Res.Iron, 70d, 120d), (Res.Grain, 80d, 140d) } },
			new DungeonDef { Id = "ozerna", Name = "Lake Cave", Description = "Underground lakes where the water has not stirred for centuries.", Danger = 3, Distance = 60, Minutes = 25, DamageMin = 28, DamageMax = 48,
				Loot = new[] { (Res.Water, 120d, 200d), (Res.Wood, 110d, 180d), (Res.Iron, 130d, 230d), (Res.Grain, 150d, 250d) } },
			new DungeonDef { Id = "optymistychna", Name = "Optymistychna Cave", Description = "The longest maze under Podillia. Who knows the way back returns rich.", Danger = 3, Distance = 85, Minutes = 35, DamageMin = 36, DamageMax = 60,
				Loot = new[] { (Res.Wood, 110d, 180d), (Res.Stone, 150d, 250d), (Res.Clay, 150d, 250d), (Res.Water, 120d, 200d) } },
		};

		public static DungeonDef ById(string id)
		{
			foreach (var dungeon in All) if (dungeon.Id == id) return dungeon;
			return null;
		}
	}

	/// <summary>
	/// The hero's rules, ported from Lords (hero.ts): the hero heals over time, is hurt in the dungeon (the wounds are rolled when they set out and counted
	/// from the moment they are back), and a hero whose wounds are deeper than their health falls and recovers in the infirmary.
	/// </summary>
	public static class HeroRules
	{
		public const double MaxHealth = 100;
		public const double HealPerHour = 10;
		public const double ReviveHealth = 25;
		const double RecoveryHoursWithoutInfirmary = 24;
		const double RecoveryHoursInfirmary = 8;
		const double RecoverySpeedup = 0.9;
		const double RecoveryHoursMin = 1;
		const double HourMs = 3_600_000;

		/// <summary>The highest level of a kind of building by <paramref name="now"/>, 0 when none stands.</summary>
		public static int BuildingLevel(IDictionary<string, VillageBuilding> buildings, string type, long now)
		{
			var best = 0;
			if (buildings == null) return 0;
			foreach (var building in buildings.Values)
				if (building != null && building.Type == type) best = Math.Max(best, LordsRules.LevelAt(building, now));
			return best;
		}

		/// <summary>Hours a fallen hero spends recovering, with an infirmary at <paramref name="level"/> (0 for none).</summary>
		public static double RecoveryHours(int level)
		{
			if (level <= 0) return RecoveryHoursWithoutInfirmary;
			return Math.Max(RecoveryHoursMin, RecoveryHoursInfirmary * Math.Pow(RecoverySpeedup, level - 1));
		}

		static double Healed(double health, long from, long to)
		{
			var hours = Math.Max(0, to - from) / HourMs;
			return Math.Min(MaxHealth, health + HealPerHour * hours);
		}

		/// <summary>How the last trip ended, once it has: whether the hero fell, and when they leave the infirmary. Null when there was no trip with wounds.</summary>
		public static (bool Fell, long RecoveredAt)? Fate(HeroSave hero, IDictionary<string, VillageBuilding> buildings)
		{
			var trip = hero.Expedition;
			if (trip == null || trip.Damage <= 0) return null;
			var onReturn = Healed(hero.Health, hero.At, trip.EndsAt) - trip.Damage;
			if (onReturn > 0) return (false, trip.EndsAt);
			var level = BuildingLevel(buildings, "infirmary", trip.EndsAt);
			return (true, trip.EndsAt + (long)(RecoveryHours(level) * HourMs));
		}

		public static double HealthNow(HeroSave hero, long now, IDictionary<string, VillageBuilding> buildings)
		{
			var trip = hero.Expedition;
			var fate = Fate(hero, buildings);
			if (trip == null || fate == null || now < trip.EndsAt) return Healed(hero.Health, hero.At, now);
			if (fate.Value.Fell) return now < fate.Value.RecoveredAt ? 0 : Healed(ReviveHealth, fate.Value.RecoveredAt, now);
			var onReturn = Healed(hero.Health, hero.At, trip.EndsAt) - trip.Damage;
			return Healed(onReturn, trip.EndsAt, now);
		}

		public static HeroStatus StatusNow(HeroSave hero, long now, IDictionary<string, VillageBuilding> buildings)
		{
			var trip = hero.Expedition;
			if (trip == null) return HeroStatus.Home;
			if (trip.EndsAt > now) return HeroStatus.Away;
			var fate = Fate(hero, buildings);
			if (fate != null && fate.Value.Fell) return now < fate.Value.RecoveredAt ? HeroStatus.Infirmary : HeroStatus.Home;
			return HeroStatus.Back;
		}

		public static CannotGo WhyNot(HeroSave hero, DungeonDef dungeon, long now, IDictionary<string, VillageBuilding> buildings)
		{
			var status = StatusNow(hero, now, buildings);
			if (status == HeroStatus.Infirmary) return CannotGo.Infirmary;
			if (status != HeroStatus.Home) return CannotGo.Busy;
			return HealthNow(hero, now, buildings) <= dungeon.DamageMin ? CannotGo.Weak : CannotGo.None;
		}

		/// <summary>Sends the hero out: a new save with the trip, its wounds and its loot rolled now. The caller checks <see cref="WhyNot"/> first.</summary>
		public static HeroSave Send(HeroSave hero, DungeonDef dungeon, long now, IDictionary<string, VillageBuilding> buildings, Random random)
		{
			var loot = new Dictionary<string, double>();
			foreach (var item in dungeon.Loot) loot[item.Resource] = Math.Round((item.Min + random.NextDouble() * (item.Max - item.Min)) / 10) * 10;
			return new HeroSave
			{
				Health = HealthNow(hero, now, buildings),
				At = now,
				Expedition = new HeroExpedition
				{
					Dungeon = dungeon.Id,
					StartedAt = now,
					EndsAt = now + dungeon.Minutes * 60_000L,
					Loot = loot,
					Damage = dungeon.DamageMin + Math.Floor(random.NextDouble() * (dungeon.DamageMax - dungeon.DamageMin + 1)),
				},
			};
		}

		/// <summary>
		/// The hero is out of the dungeon now, early: the trip ends at <paramref name="now"/> and its loot waits in Lords. When the hero fell in the
		/// dungeon (<paramref name="fell"/>) the wounds are made deeper than any health, so Lords sends them to the infirmary.
		/// </summary>
		public static HeroSave EndTripNow(HeroSave hero, long now, bool fell)
		{
			var trip = hero.Expedition;
			if (trip == null) return hero;
			var ended = new HeroExpedition
			{
				Dungeon = trip.Dungeon,
				StartedAt = trip.StartedAt,
				EndsAt = Math.Min(trip.EndsAt, now),
				Loot = fell ? new Dictionary<string, double>() : trip.Loot,
				Damage = fell ? MaxHealth * 10 : trip.Damage,
			};
			return new HeroSave { Health = hero.Health, At = hero.At, Expedition = ended };
		}
	}
}
