using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static Temnorid.Village.Tests.TestKit;

namespace Temnorid.Village.Tests
{
	public class VillageDataTests
	{
		static VillageData DefaultData() => VillageLayout.Build(DefaultVillage.Create(T0), false, T0);

		// ---- the default village ----

		[Test]
		public void The_default_village_has_twelve_places_a_hall_in_the_centre_and_no_overlap()
		{
			var progress = DefaultVillage.Create(T0);
			Assert.AreEqual(12, progress.Village.Slots.Count);
			CollectionAssert.AreEqual(Enumerable.Range(1, 12), progress.Village.Slots.Select(s => s.Id), "ids 1 to 12, clockwise from the north");
			Assert.AreEqual("lords-hall", progress.Village.Buildings["0"].Type, "the hall is in the centre");
			// Lords keeps its places at least 170 map units apart.
			foreach (var a in progress.Village.Slots)
				foreach (var b in progress.Village.Slots.Where(s => s.Id > a.Id))
					Assert.GreaterOrEqual(Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)), 170, a.Id + " and " + b.Id);

			var data = DefaultData();
			Assert.AreEqual(12, data.Slots.Count);
			foreach (var building in progress.Village.Buildings)
			{
				var slot = int.Parse(building.Key);
				Assert.IsTrue(slot == 0 || progress.Village.Slots.Any(s => s.Id == slot), "building " + building.Value.Type + " is on a valid place");
				Assert.GreaterOrEqual(building.Value.Level, 1);
				Assert.LessOrEqual(building.Value.Level, 3);
			}
			Assert.AreEqual(progress.Village.Buildings.Count, data.Buildings.Count, "every building is drawn");

			// No two boxes overlap, at the size they are drawn.
			for (var i = 0; i < data.Buildings.Count; i++)
			{
				for (var j = i + 1; j < data.Buildings.Count; j++)
				{
					var a = data.Buildings[i];
					var b = data.Buildings[j];
					var (ax, az) = VillageLayout.PositionOf(data, a.Slot);
					var (bx, bz) = VillageLayout.PositionOf(data, b.Slot);
					var overlapX = Math.Abs(ax - bx) < (BuildingKinds.Width(a.Type, a.Level) + BuildingKinds.Width(b.Type, b.Level)) / 2f;
					var overlapZ = Math.Abs(az - bz) < (BuildingKinds.Depth(a.Type, a.Level) + BuildingKinds.Depth(b.Type, b.Level)) / 2f;
					Assert.IsFalse(overlapX && overlapZ, a.Type + " overlaps " + b.Type);
				}
			}
		}

		[Test]
		public void The_default_village_has_a_warehouse_a_granary_and_a_well_and_the_capacity_follows_the_Lords_formula()
		{
			var data = DefaultData();
			Assert.AreEqual("warehouse", data.BuildingAt(1).Type);
			Assert.AreEqual(3, data.BuildingAt(1).Level);
			Assert.AreEqual(1, data.StoreSlot, "the warehouse is the stores");
			Assert.IsTrue(data.Buildings.Any(b => b.Type == "granary"));
			Assert.IsTrue(data.Buildings.Any(b => b.Type == "well"));
			Assert.AreEqual(2400, data.Capacity["wood"]);
			Assert.AreEqual(1700, data.Capacity["grain"], "granary level 2: 800 * 1.45^2 = 1682");
			Assert.AreEqual(1200, data.Capacity["water"]);
			Assert.IsFalse(data.IsLords);
			Assert.AreEqual(0, data.WallLevel);
			Assert.IsFalse(data.Buildings.Any(b => b.Working), "nothing is under construction");
		}

		[Test]
		public void The_default_stock_is_the_Lords_start_stock_of_all_six_resources()
		{
			var progress = DefaultVillage.Create(T0);
			Assert.AreEqual(6, Res.All.Length);
			foreach (var id in Res.All) Assert.AreEqual(750, progress.Stock.Amounts[id], id);
			Assert.AreEqual(T0, progress.Stock.At);
			Assert.AreEqual(750, LordsRules.StartStock);
			CollectionAssert.AreEqual(new[] { "wood", "stone", "iron", "water", "grain", "clay" }, Res.All);
		}

		[Test]
		public void The_default_farm_has_two_plots_per_resource_at_levels_one_and_two_clear_of_each_other_and_of_the_village()
		{
			var data = DefaultData();
			Assert.AreEqual(12, data.Plots.Count);
			foreach (var id in Res.All)
			{
				var own = data.Plots.Where(p => p.Resource == id).ToList();
				Assert.AreEqual(2, own.Count, id);
				CollectionAssert.AreEquivalent(new[] { 1, 2 }, own.Select(p => p.Level), id + " levels");
			}
			for (var i = 0; i < data.Plots.Count; i++)
			{
				var a = data.Plots[i];
				var vx = a.X / data.VillageRx;
				var vz = a.Z / data.VillageRz;
				Assert.Greater(vx * vx + vz * vz, 1f, "plot " + a.Id + " is outside the village");
				for (var j = i + 1; j < data.Plots.Count; j++)
				{
					var b = data.Plots[j];
					Assert.GreaterOrEqual(Mathf.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)), 7f, "plots " + a.Id + " and " + b.Id + " (patches are 6 m wide)");
				}
			}
		}

		[Test]
		public void The_default_village_is_the_same_every_time_and_the_spawn_is_in_the_open()
		{
			Assert.AreEqual(DefaultData().ToJson(), DefaultData().ToJson());
			var data = DefaultData();
			Assert.IsFalse(VillageLayout.IsInsideBuilding(data, data.SpawnX, data.SpawnZ, 1.5f));
			var (sx, sz) = VillageLayout.PositionOf(data, data.StoreSlot);
			Assert.Less(Mathf.Sqrt((data.SpawnX - sx) * (data.SpawnX - sx) + (data.SpawnZ - sz) * (data.SpawnZ - sz)), 8f, "close to the stores");
		}

		// ---- a Lords save ----

		static LordsProgress LordsLike()
		{
			var progress = DefaultVillage.Create(T0);
			progress.Village.Layout = 2;
			// A hall level 1 being raised to 2, a granary still being built, a well taken down completely, a stray building and a wall.
			progress.Village.Buildings["0"] = new VillageBuilding { Type = "lords-hall", Level = 1, Upgrade = new WorkStep { Level = 2, StartedAt = T0 - 1000, EndsAt = T0 + 5000 } };
			progress.Village.Buildings["4"] = new VillageBuilding { Type = "granary", Level = 0, Upgrade = new WorkStep { Level = 1, StartedAt = T0 - 1000, EndsAt = T0 + 5000 } };
			progress.Village.Buildings["6"] = new VillageBuilding { Type = "well", Level = 1, Upgrade = new WorkStep { Level = 0, StartedAt = T0 - 2000, EndsAt = T0 - 1000, Down = true } };
			progress.Village.Buildings["99"] = new VillageBuilding { Type = "smithy", Level = 1 };
			progress.Village.Buildings["-1"] = new VillageBuilding { Type = "wall", Level = 2 };
			progress.Farm.Levels["1"] = new Construction { Level = 3, Upgrade = new WorkStep { Level = 4, StartedAt = T0 - 1, EndsAt = T0 + 1000 } };
			return progress;
		}

		[Test]
		public void Levels_are_taken_at_the_moment_scaffolds_show_running_work_and_what_is_gone_or_misplaced_is_left_out()
		{
			var data = VillageLayout.Build(LordsLike(), true, T0);
			Assert.IsTrue(data.IsLords);
			var hall = data.BuildingAt(0);
			Assert.AreEqual(1, hall.Level);
			Assert.AreEqual(2, hall.Target);
			Assert.IsTrue(hall.Working);
			var granary = data.BuildingAt(4);
			Assert.AreEqual(0, granary.Level);
			Assert.IsTrue(granary.Working, "a first level under construction is a scaffold");
			Assert.IsNull(data.BuildingAt(6), "a well torn down completely leaves nothing");
			Assert.IsNull(data.BuildingAt(99), "a building on a place that does not exist is not drawn");
			Assert.AreEqual(2, data.WallLevel);
			Assert.IsFalse(data.WallWorking);
			Assert.IsNull(data.Buildings.FirstOrDefault(b => b.Type == "wall"), "the wall is not a box on a place");
			var plot = data.PlotById(1);
			Assert.AreEqual(3, plot.Level);
			Assert.AreEqual(4, plot.Target);
			Assert.IsTrue(plot.Working);

			var later = VillageLayout.Build(LordsLike(), true, T0 + 10_000);
			Assert.AreEqual(2, later.BuildingAt(0).Level, "the work has ended");
			Assert.IsFalse(later.BuildingAt(0).Working);
			Assert.AreEqual(1, later.BuildingAt(4).Level);
			Assert.AreEqual(4, later.PlotById(1).Level);
			Assert.AreEqual(LordsRules.Capacity(LordsLike().Village.Buildings, T0 + 10_000)["wood"], later.Capacity["wood"], 1e-9);
		}

		[Test]
		public void Map_units_become_metres_with_the_village_centre_at_the_origin_and_north_up()
		{
			var progress = new LordsProgress { Lord = new LordInfo { Name = "A" }, Stock = new StockSave(Amounts.Filled(1), T0) };
			progress.Village = new VillageSave { Slots = { new VillageSlot { Id = 1, X = 800, Y = 500 }, new VillageSlot { Id = 2, X = 1000, Y = 400 } } };
			progress.Farm = new FarmSave { Plots = { new FarmPlot { Id = 1, Resource = "wood", X = 900, Y = 500 }, new FarmPlot { Id = 2, Resource = "clay", X = 1000, Y = 400 } } };
			var data = VillageLayout.Build(progress, true, T0);
			// The middle of the two places is (900, 450): 100 units west and east, 50 units south and north.
			Assert.AreEqual(-4f, data.SlotById(1).X, 1e-4f, "100 units * 0.04 m");
			Assert.AreEqual(-2f, data.SlotById(1).Z, 1e-4f, "south of the centre is -z");
			Assert.AreEqual(4f, data.SlotById(2).X, 1e-4f);
			Assert.AreEqual(2f, data.SlotById(2).Z, 1e-4f);
			// The farm map's middle is (900, 500), at 0.1 m a unit.
			Assert.AreEqual(0f, data.PlotById(1).X, 1e-4f);
			Assert.AreEqual(0f, data.PlotById(1).Z, 1e-4f);
			Assert.AreEqual(10f, data.PlotById(2).X, 1e-4f);
			Assert.AreEqual(10f, data.PlotById(2).Z, 1e-4f);
			Assert.AreEqual(90f, data.FarmRx, 1e-3f, "1800 units * 0.1 / 2");
			Assert.AreEqual(50f, data.FarmRz, 1e-3f);
		}

		[Test]
		public void A_grown_farm_is_drawn_bigger_and_bought_places_get_a_spot_outside_the_others()
		{
			var progress = LordsLike();
			for (var i = 0; i < 24; i++) progress.Farm.Plots.Add(new FarmPlot { Id = 100 + i, Resource = "wood", X = 900, Y = 500, Extra = true });
			progress.Village.Slots.Add(new VillageSlot { Id = 13, Extra = true });
			progress.Village.Slots.Add(new VillageSlot { Id = 14, Extra = true });
			var data = VillageLayout.Build(progress, true, T0);
			Assert.AreEqual(90f * Mathf.Sqrt(36f / 24f), data.FarmRx, 1e-2f, "36 plots: the land grows with the square root of 36 / 24, as in Lords");
			var extra = data.SlotById(13);
			Assert.IsTrue(extra.Extra);
			Assert.AreNotEqual(data.SlotById(14).X, extra.X);
			var maxOrdinary = data.Slots.Where(s => !s.Extra).Max(s => Mathf.Abs(s.X));
			Assert.Greater(Mathf.Abs(extra.X), maxOrdinary * 0.5f);
		}

		[Test]
		public void Village_data_travels_as_json_to_an_online_guest_and_a_bad_message_is_refused()
		{
			var data = VillageLayout.Build(LordsLike(), true, T0);
			var json = data.ToJson();
			Assert.Less(json.Length, 20000, "one event");
			var back = VillageData.FromJson(json);
			Assert.AreEqual(json, back.ToJson());
			Assert.AreEqual(2400, back.Capacity["wood"], 1e-9);
			Assert.IsNull(VillageData.FromJson(""));
			Assert.IsNull(VillageData.FromJson("not json"));
			Assert.IsNotNull(VillageData.FromJson("{}"), "an empty village is still valid");
			Assert.AreEqual(0, VillageData.FromJson("{\"plots\":[null,{\"id\":1,\"resource\":\"gold\"}]}").Plots.Count, "unknown resources are dropped");
		}

		// ---- reading a Lords document ----

		[Test]
		public void A_lords_document_is_read_with_unknown_fields_ignored_and_missing_ones_tolerated()
		{
			var doc = LordsDoc();
			var read = ProgressDocument.Found(doc, "t1");
			Assert.IsNotNull(read.Progress);
			Assert.AreEqual("Ann", read.Progress.LordName);
			Assert.IsTrue(read.HasValidStock);
			Assert.IsTrue(read.IsUsableLordsSave);
			Assert.AreEqual(12, read.Progress.Farm.Plots.Count);
			Assert.AreEqual(3, read.Progress.Village.Buildings["1"].Level);
			Assert.AreEqual(T0, read.Progress.Stock.At);

			var sparse = ProgressDocument.Found(JObject.Parse("{\"lord\":{\"name\":\"Bo\"},\"village\":{\"slots\":[{\"id\":1,\"x\":1,\"y\":2}]},\"farm\":{\"plots\":null,\"levels\":null}}"), "t");
			Assert.IsNotNull(sparse.Progress);
			Assert.AreEqual(0, sparse.Progress.Farm.Plots.Count);
			Assert.AreEqual(0, sparse.Progress.Village.Buildings.Count);
			Assert.IsFalse(sparse.HasValidStock);
			Assert.IsFalse(sparse.IsUsableLordsSave);

			// Whole numbers saved as doubles and fractions are both fine.
			var mixed = JObject.Parse("{\"stock\":{\"amounts\":{\"wood\":750,\"stone\":750.5,\"iron\":1,\"water\":2,\"grain\":3,\"clay\":4},\"at\":1.79e12}}");
			Assert.IsTrue(ProgressDocument.Found(mixed, "t").HasValidStock);
			Assert.AreEqual(1_790_000_000_000L, ProgressDocument.Found(mixed, "t").Progress.Stock.At);

			Assert.IsNull(ProgressDocument.Found(JObject.Parse("{\"stock\":5}"), "t").Progress);
			Assert.IsFalse(ProgressDocument.Found(JObject.Parse("{\"stock\":{\"amounts\":{\"wood\":\"many\"},\"at\":5}}"), "t").HasValidStock);
		}
	}
}
