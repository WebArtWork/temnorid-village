using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static Temnorid.Village.Tests.TestKit;

namespace Temnorid.Village.Tests
{
	public class VillageViewTests
	{
		GameObject _host;
		VillageView _view;

		[SetUp]
		public void SetUp()
		{
			_host = new GameObject("VillageHost");
			_view = _host.AddComponent<VillageView>();
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(_host);
		}

		[Test]
		public void The_default_village_is_drawn_with_a_plot_for_every_farm_plot_and_a_box_for_every_building()
		{
			var data = VillageLayout.Build(DefaultVillage.Create(T0), false, T0);
			_view.Build(data);

			Assert.AreEqual(12, _view.Plots.Count);
			for (var id = 1; id <= 12; id++)
			{
				var plot = _view.PlotById(id);
				Assert.IsNotNull(plot, "plot " + id);
				Assert.IsNotNull(plot.Visual);
				Assert.IsTrue(plot.Root.GetComponent<BoxCollider>().isTrigger, "a plot has a trigger body to measure from");
			}
			Assert.IsNull(_view.PlotById(99));
			foreach (var building in data.Buildings)
			{
				var box = _view.BuildingBySlot(building.Slot);
				Assert.IsNotNull(box, building.Type);
				Assert.IsNotNull(box.GetComponent<BoxCollider>());
				var (x, z) = VillageLayout.PositionOf(data, building.Slot);
				Assert.AreEqual(x, box.localPosition.x, 1e-4f);
				Assert.AreEqual(z, box.localPosition.z, 1e-4f);
			}
			Assert.IsNull(_view.BuildingBySlot(9), "a free place has no building");
			Assert.AreEqual("Building_1_warehouse", _view.StoreBuilding.name);
			Assert.AreEqual(_view.StoreBuilding.position, _view.StorePosition);
			Assert.IsFalse(VillageLayout.IsInsideBuilding(data, _view.SpawnPosition.x, _view.SpawnPosition.z, 1f));
		}

		[Test]
		public void Plots_take_the_Lords_colour_of_their_resource_and_show_level_markers()
		{
			var data = VillageLayout.Build(DefaultVillage.Create(T0), false, T0);
			_view.Build(data);
			var plot = _view.PlotById(1);
			Assert.AreEqual("wood", plot.Data.Resource);
			var markers = plot.Root.Cast<Transform>().Count(child => child.name.StartsWith("Level"));
			Assert.AreEqual(plot.Data.Level, markers, "one marker for each level");
			Assert.AreEqual(Res.ColorOf("wood"), new Color(0x3f / 255f, 0x6b / 255f, 0x2f / 255f, 1f));
			Assert.AreEqual(Res.ColorOf("clay"), new Color(0xa0 / 255f, 0x61 / 255f, 0x3a / 255f, 1f));
		}

		[Test]
		public void Buildings_under_construction_get_a_scaffold_and_labels_name_and_level()
		{
			var progress = DefaultVillage.Create(T0);
			progress.Village.Buildings["2"] = new VillageBuilding { Type = "sawmill", Level = 0, Upgrade = new WorkStep { Level = 1, StartedAt = T0, EndsAt = T0 + 60_000 } };
			progress.Village.Buildings["-1"] = new VillageBuilding { Type = "wall", Level = 1 };
			var data = VillageLayout.Build(progress, true, T0 + 1);
			_view.Build(data);

			var sawmill = _view.BuildingBySlot(2);
			Assert.IsNotNull(sawmill.Find("Visual/Foundation"), "not built yet: a foundation");
			Assert.IsNull(sawmill.Find("Visual/Walls"));
			Assert.IsNotNull(sawmill.Find("Visual/Pole"), "and a scaffold");
			Assert.IsNull(_view.BuildingBySlot(1).Find("Visual/Pole"), "a finished building has none");
			StringAssert.Contains("Sawmill", sawmill.Find("Label").GetComponent<TextMesh>().text);
			StringAssert.Contains("Building to level 1", sawmill.Find("Label").GetComponent<TextMesh>().text);
			var warehouse = _view.BuildingBySlot(1).Find("Label").GetComponent<TextMesh>().text;
			StringAssert.Contains("Warehouse", warehouse);
			StringAssert.Contains("Level 3", warehouse);
			Assert.IsNotNull(_host.transform.Find("VillageRoot/Wall"), "the wall is drawn when the save has one");
		}

		[Test]
		public void A_building_grows_with_its_level()
		{
			Assert.Less(BuildingKinds.Width("warehouse", 1), BuildingKinds.Width("warehouse", 5));
			Assert.Less(BuildingKinds.Height("warehouse", 1), BuildingKinds.Height("warehouse", 5));
			Assert.AreEqual(BuildingKinds.Width("warehouse", 8), BuildingKinds.Width("warehouse", 20), "the box stops growing so places never overlap");
			Assert.Less(BuildingKinds.Width("warehouse", 3), BuildingKinds.Width("lords-hall", 3));
			Assert.AreEqual("Lord's hall", BuildingKinds.Name("lords-hall"));
			Assert.AreEqual("mystery", BuildingKinds.Name("mystery"));
		}

		[Test]
		public void Building_again_replaces_the_old_village_and_clearing_removes_it()
		{
			var data = VillageLayout.Build(DefaultVillage.Create(T0), false, T0);
			_view.Build(data);
			_view.Build(data);
			Assert.AreEqual(1, _host.transform.Cast<Transform>().Count(t => t.name == "VillageRoot" && t.gameObject.activeSelf), "after a rebuild only one root is alive (the old one is destroyed)");
			_view.Clear();
			Assert.AreEqual(0, _view.Plots.Count);
			Assert.IsNull(_view.StoreBuilding);
		}
	}
}
