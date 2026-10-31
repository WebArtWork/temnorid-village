using System.Collections.Generic;
using NUnit.Framework;

namespace Temnorid.Village.Tests
{
	/// <summary>
	/// Lords' rules ported to C#. The expected numbers were computed with node from the TypeScript formulas
	/// (economy.ts and building.timing.ts copied verbatim), not from this code.
	/// </summary>
	public class LordsRulesTests
	{
		const long Now = 1_000_000_000_000L;

		static WorkStep Step(int level, long startedAt, long endsAt, bool down = false) => new WorkStep { Level = level, StartedAt = startedAt, EndsAt = endsAt, Down = down };

		static VillageBuilding Building(string type, int level, WorkStep upgrade = null) => new VillageBuilding { Type = type, Level = level, Upgrade = upgrade };

		[Test]
		public void A_plot_yields_its_hourly_amount_by_level_and_keeps_growing_past_the_table()
		{
			var expected = new Dictionary<int, double> { { -1, 2 }, { 0, 2 }, { 1, 5 }, { 5, 33 }, { 10, 200 }, { 11, 250 }, { 12, 313 }, { 15, 610 }, { 20, 1863 } };
			foreach (var pair in expected) Assert.AreEqual(pair.Value, LordsRules.PlotProduction(pair.Key), "level " + pair.Key);
		}

		[Test]
		public void JavaScript_rounding_sends_halves_up()
		{
			Assert.AreEqual(3, LordsRules.JsRound(2.5));
			Assert.AreEqual(-2, LordsRules.JsRound(-2.5));
			Assert.AreEqual(1, LordsRules.JsRound(0.5));
		}

		[Test]
		public void A_level_counts_once_its_step_has_ended_and_the_last_finished_step_wins()
		{
			var work = new VillageBuilding { Type = "granary", Level = 2, Queue = new List<WorkStep> { Step(3, Now - 5000, Now - 1000) }, Upgrade = Step(4, Now - 1000, Now + 9000) };
			Assert.AreEqual(3, LordsRules.LevelAt(work, Now));
			Assert.AreEqual(2, LordsRules.LevelAt(work, Now - 2000), "the first step has not ended yet");
			Assert.AreEqual(2, LordsRules.LevelAt(work, Now - 9000));
			Assert.AreEqual(4, LordsRules.LevelAt(work, Now + 9000), "a step counts at the very moment it ends");
			Assert.AreEqual(5, LordsRules.LevelAt(new Construction { Level = 5 }, Now), "no work: the saved level");

			Assert.AreEqual(4, LordsRules.RunningUpgrade(work, Now).Level);
			Assert.AreEqual(3, LordsRules.RunningUpgrade(work, Now - 2000).Level);
			Assert.IsNull(LordsRules.RunningUpgrade(work, Now + 9000));
			Assert.AreEqual(Now + 9000, LordsRules.WorkEndsAt(work, Now));
			Assert.AreEqual(0, LordsRules.WorkEndsAt(work, Now + 9000));
			Assert.AreEqual(4, LordsRules.TargetLevel(work, Now), "the running step's level, the one it is heading for");
			Assert.AreEqual(4, LordsRules.TargetLevel(work, Now + 9001));
		}

		[Test]
		public void Taking_a_building_down_lowers_its_level_when_the_step_ends()
		{
			var work = new Construction { Level = 3, Upgrade = Step(2, Now - 10, Now + 10, true) };
			Assert.AreEqual(3, LordsRules.LevelAt(work, Now));
			Assert.AreEqual(2, LordsRules.LevelAt(work, Now + 10));
		}

		[Test]
		public void Capacity_adds_the_levels_of_every_store_of_a_kind_and_rounds_to_hundreds()
		{
			var buildings = new Dictionary<string, VillageBuilding>
			{
				{ "0", Building("lords-hall", 1) },
				{ "1", Building("warehouse", 3) },
				{ "2", Building("warehouse", 2, Step(3, Now - 10, Now + 10)) },
				{ "3", Building("granary", 1) },
				{ "4", Building("well", 0, Step(1, Now - 10, Now - 1)) },
			};
			var cap = LordsRules.Capacity(buildings, Now);
			foreach (var id in new[] { "wood", "stone", "iron", "clay" }) Assert.AreEqual(5100, cap[id], id + " (warehouse levels 3 + 2)");
			Assert.AreEqual(1200, cap["grain"], "granary level 1");
			Assert.AreEqual(1200, cap["water"], "the well's first level finished a moment ago");

			var later = LordsRules.Capacity(buildings, Now + 20);
			foreach (var id in new[] { "wood", "stone", "iron", "clay" }) Assert.AreEqual(7400, later[id], id + " (warehouse levels 3 + 3)");

			var bare = LordsRules.Capacity(new Dictionary<string, VillageBuilding>(), Now);
			foreach (var id in Res.All) Assert.AreEqual(800, bare[id], "no store built: the base capacity");
			Assert.AreEqual(800, LordsRules.Capacity(null, Now)["wood"]);
		}

		static (List<FarmPlot>, Dictionary<string, Construction>) Farm()
		{
			var plots = new List<FarmPlot>
			{
				new FarmPlot { Id = 1, Resource = "wood" }, new FarmPlot { Id = 2, Resource = "wood" }, new FarmPlot { Id = 3, Resource = "grain" },
				new FarmPlot { Id = 4, Resource = "clay" }, new FarmPlot { Id = 5, Resource = "water" },
			};
			var levels = new Dictionary<string, Construction>
			{
				{ "1", new Construction { Level = 3 } },
				{ "2", new Construction { Level = 1, Upgrade = Step(2, Now - 1, Now + 1) } },
				{ "3", new Construction { Level = 12 } },
				{ "5", new Construction { Level = 0, Upgrade = Step(1, Now - 9, Now - 5) } },
			};
			return (plots, levels);
		}

		[Test]
		public void The_farms_hourly_production_sums_its_plots_at_their_levels_and_unlisted_plots_are_level_0()
		{
			var (plots, levels) = Farm();
			var now = LordsRules.ProductionPerHour(plots, levels, Now);
			Assert.AreEqual(20, now["wood"], "level 3 (15) + level 1 (5), the upgrade is still running");
			Assert.AreEqual(0, now["stone"]);
			Assert.AreEqual(0, now["iron"]);
			Assert.AreEqual(5, now["water"], "the first level finished");
			Assert.AreEqual(313, now["grain"], "level 12 is past the table");
			Assert.AreEqual(2, now["clay"], "no saved work: level 0 yields 2");
			Assert.AreEqual(24, LordsRules.ProductionPerHour(plots, levels, Now + 2)["wood"], "the upgrade finishes: 15 + 9");
		}

		[Test]
		public void The_stock_grows_with_production_but_never_past_the_capacity()
		{
			var (plots, levels) = Farm();
			var buildings = new Dictionary<string, VillageBuilding>
			{
				{ "0", Building("lords-hall", 1) }, { "1", Building("warehouse", 3) }, { "2", Building("warehouse", 2, Step(3, Now - 10, Now + 10)) },
				{ "3", Building("granary", 1) }, { "4", Building("well", 0, Step(1, Now - 10, Now - 1)) },
			};
			var stock = new StockSave(new Amounts { ["wood"] = 750, ["stone"] = 750, ["iron"] = 100.5, ["water"] = 700, ["grain"] = 1000, ["clay"] = 795 }, Now - 2 * 3_600_000L - 1_800_000L);
			var at = LordsRules.StockAt(stock, plots, levels, buildings, Now);
			Assert.AreEqual(800, at["wood"], 1e-9, "750 + 20/h * 2.5h");
			Assert.AreEqual(750, at["stone"], 1e-9);
			Assert.AreEqual(100.5, at["iron"], 1e-9);
			Assert.AreEqual(712.5, at["water"], 1e-9);
			Assert.AreEqual(1200, at["grain"], 1e-9, "1000 + 313 * 2.5 is capped at the granary's 1200");
			Assert.AreEqual(800, at["clay"], 1e-9, "795 + 2 * 2.5 = 800");

			var over = LordsRules.StockAt(new StockSave(new Amounts { ["wood"] = 3000, ["stone"] = 750, ["iron"] = 750, ["water"] = 750, ["grain"] = 750, ["clay"] = 750 }, Now - 3_600_000L), plots, levels, buildings, Now);
			Assert.AreEqual(3020, over["wood"], 1e-9, "3000 + 20 an hour for one hour, still under the 5100 cap");
			Assert.AreEqual(1063, over["grain"], 1e-9);
		}

		[Test]
		public void A_stock_saved_in_the_future_does_not_produce_or_shrink()
		{
			var (plots, levels) = Farm();
			var stock = new StockSave(new Amounts { ["wood"] = 1, ["stone"] = 2, ["iron"] = 3, ["water"] = 4, ["grain"] = 5, ["clay"] = 6 }, Now + 99_999);
			var at = LordsRules.StockAt(stock, plots, levels, new Dictionary<string, VillageBuilding>(), Now);
			Assert.AreEqual(1, at["wood"]);
			Assert.AreEqual(6, at["clay"]);
		}
	}
}
