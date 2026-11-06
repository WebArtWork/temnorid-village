using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Temnorid.Village.Tests
{
	/// <summary>The hero's rules, with the numbers Lords has (hero.ts, dungeon.data.ts).</summary>
	public class HeroRulesTests
	{
		const long Hour = 3_600_000;
		const long T0 = 1_800_000_000_000;
		static readonly IDictionary<string, VillageBuilding> NoBuildings = new Dictionary<string, VillageBuilding>();

		static DungeonDef Bakota => DungeonCatalog.ById("bakota-caves");

		[Test]
		public void The_six_dungeons_are_Lords_dungeons()
		{
			Assert.AreEqual(6, DungeonCatalog.All.Length);
			Assert.AreEqual(5, Bakota.Minutes);
			Assert.AreEqual(5, Bakota.DamageMin);
			Assert.AreEqual(12, Bakota.DamageMax);
			Assert.AreEqual(4, DungeonCatalog.ById("optymistychna").Loot.Length);
		}

		[Test]
		public void A_new_hero_is_home_with_full_health_and_heals_ten_an_hour()
		{
			var hero = HeroSave.New(T0);
			Assert.AreEqual(HeroStatus.Home, HeroRules.StatusNow(hero, T0, NoBuildings));
			Assert.AreEqual(100, HeroRules.HealthNow(hero, T0, NoBuildings), 1e-9);
			hero.Health = 50;
			Assert.AreEqual(70, HeroRules.HealthNow(hero, T0 + 2 * Hour, NoBuildings), 1e-9);
			Assert.AreEqual(100, HeroRules.HealthNow(hero, T0 + 20 * Hour, NoBuildings), 1e-9, "never above the maximum");
		}

		[Test]
		public void Sending_the_hero_writes_a_trip_Lords_reads_as_away()
		{
			var hero = HeroSave.New(T0);
			Assert.AreEqual(CannotGo.None, HeroRules.WhyNot(hero, Bakota, T0, NoBuildings));
			var sent = HeroRules.Send(hero, Bakota, T0, NoBuildings, new Random(3));

			Assert.AreEqual(HeroStatus.Away, HeroRules.StatusNow(sent, T0 + 60_000, NoBuildings));
			Assert.AreEqual("bakota-caves", sent.Expedition.Dungeon);
			Assert.AreEqual(T0 + 5 * 60_000, sent.Expedition.EndsAt);
			Assert.That(sent.Expedition.Damage, Is.InRange(5.0, 12.0));
			Assert.That(sent.Expedition.Loot.Keys, Is.EquivalentTo(new[] { "grain", "clay" }));
			Assert.AreEqual(CannotGo.Busy, HeroRules.WhyNot(sent, Bakota, T0 + 60_000, NoBuildings), "away: busy");

			// The saved JSON has the shape Lords reads.
			var json = sent.ToJson();
			Assert.AreEqual("bakota-caves", (string)json["expedition"]["dungeon"]);
			Assert.IsTrue(json["expedition"]["loot"] is JObject);
		}

		[Test]
		public void Ending_the_trip_early_makes_the_hero_back_with_the_loot_or_in_the_infirmary_when_it_fell()
		{
			var sent = HeroRules.Send(HeroSave.New(T0), Bakota, T0, NoBuildings, new Random(1));
			var now = T0 + 60_000;

			var won = HeroRules.EndTripNow(sent, now, false);
			Assert.AreEqual(HeroStatus.Back, HeroRules.StatusNow(won, now, NoBuildings));
			Assert.AreEqual(sent.Expedition.Loot.Count, won.Expedition.Loot.Count);

			var fell = HeroRules.EndTripNow(sent, now, true);
			Assert.AreEqual(HeroStatus.Infirmary, HeroRules.StatusNow(fell, now, NoBuildings));
			Assert.AreEqual(0, HeroRules.HealthNow(fell, now, NoBuildings), 1e-9);
			Assert.AreEqual(CannotGo.Infirmary, HeroRules.WhyNot(fell, Bakota, now, NoBuildings));
			// With no infirmary the recovery takes 24 hours, then the hero comes home with a little health.
			Assert.AreEqual(HeroStatus.Home, HeroRules.StatusNow(fell, now + 25 * Hour, NoBuildings));
			Assert.AreEqual(HeroRules.ReviveHealth + HeroRules.HealPerHour, HeroRules.HealthNow(fell, now + 25 * Hour, NoBuildings), 1e-6);
		}

		[Test]
		public void A_weak_hero_cannot_go()
		{
			var hero = new HeroSave { Health = 5, At = T0 };
			Assert.AreEqual(CannotGo.Weak, HeroRules.WhyNot(hero, Bakota, T0, NoBuildings));
			Assert.AreEqual(CannotGo.None, HeroRules.WhyNot(hero, Bakota, T0 + Hour, NoBuildings), "ten more health an hour later");
		}
	}
}
