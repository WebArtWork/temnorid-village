using System;
using System.Collections.Generic;
using System.Globalization;

namespace Temnorid.Village
{
	/// <summary>
	/// Turns a Lords save (<see cref="LordsProgress"/>) into <see cref="VillageData"/>: levels at one moment and everything in metres.
	/// <para>
	/// <b>Scale.</b> Lords draws the village on a 1800 x 1000 map and the farm on another 1800 x 1000 map where the village is only a
	/// small blob in the middle, so the two cannot share one scale. The village uses <see cref="MetresPerVillageUnit"/> (0.1: the
	/// 170-unit spacing of its places becomes 17 m, a village of about 120 x 70 m) and the farm uses <see cref="MetresPerFarmUnit"/>
	/// (0.25: the farm's ring of plots is about 125 x 50 m round the village). The village centre is the origin, +x is east and +z is
	/// north (Lords' y points down the screen, so z is minus y).
	/// </para>
	/// <para>
	/// <b>Centres.</b> The village centre is the middle of its ordinary places (Lords' own centre comes from a random generator that is not
	/// ported); the farm centre is the middle of the farm map, 900 x 500 units, which grows with the square root of the plots past 24 as
	/// in Lords (<c>farm.generator.ts</c>). Lords nudges the village a few dozen units off the farm's middle; that is ignored.
	/// Layout 1 villages are stretched 1.8 x 1.2 about their middle as Lords does when it redraws them in layout 2. Places a lord bought
	/// later have no saved spot: they are put on a ring just outside the others.
	/// </para>
	/// </summary>
	public static class VillageLayout
	{
		public const float MetresPerVillageUnit = 0.1f;
		public const float MetresPerFarmUnit = 0.25f;

		const double FarmMapWidth = 1800;
		const double FarmMapHeight = 1000;
		const int FarmPlotCount = 24;
		const double Layout1StretchX = 1.8;
		const double Layout1StretchY = 1.2;
		const float WallMargin = 10f;

		public static VillageData Build(LordsProgress progress, bool isLords, long now)
		{
			var data = new VillageData { IsLords = isLords, LordName = progress.LordName, Now = now };
			var village = progress.Village ?? new VillageSave();
			data.Seed = village.Seed;

			var slots = BuildSlots(village, data);
			var buildings = village.Buildings ?? new Dictionary<string, VillageBuilding>();
			data.Capacity = LordsRules.Capacity(buildings, now);

			foreach (var pair in buildings)
			{
				if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot)) continue;
				var building = pair.Value;
				var level = LordsRules.LevelAt(building, now);
				var running = LordsRules.RunningUpgrade(building, now);
				var working = running != null;
				var target = LordsRules.TargetLevel(building, now);

				// A building taken down completely leaves nothing (isGone in building.timing.ts).
				if (LordsRules.IsGone(building, now)) continue;

				if (building.Type == BuildingKinds.Wall || slot == BuildingKinds.WallPlace)
				{
					if (level > data.WallLevel) data.WallLevel = level;
					if (working) data.WallWorking = true;
					continue;
				}
				// Nothing built yet and no work started.
				if (level == 0 && !working) continue;
				if (slot != BuildingKinds.CenterPlace && data.SlotById(slot) == null) continue;
				if (data.BuildingAt(slot) != null) continue;
				data.Buildings.Add(new BuildingData { Slot = slot, Type = building.Type, Level = level, Target = target, Working = working });
			}

			// The wall marks the edge of the village; without it the same edge is just a little looser.
			var rx = 45f;
			var rz = 28f;
			foreach (var slot in slots)
			{
				rx = Math.Max(rx, Math.Abs(slot.X) + 9f);
				rz = Math.Max(rz, Math.Abs(slot.Z) + 9f);
			}
			data.VillageRx = rx + WallMargin;
			data.VillageRz = rz + WallMargin;

			BuildPlots(progress.Farm ?? new FarmSave(), data, now);
			data.StoreSlot = FindStore(data);
			var spawn = FindSpawn(data);
			data.SpawnX = spawn.x;
			data.SpawnZ = spawn.z;
			return data;
		}

		static List<SlotData> BuildSlots(VillageSave village, VillageData data)
		{
			var ordinary = new List<VillageSlot>();
			var extras = new List<VillageSlot>();
			foreach (var slot in village.Slots)
			{
				if (slot.Id == BuildingKinds.CenterPlace || slot.Id == BuildingKinds.WallPlace) continue;
				if (slot.Extra && slot.X == 0 && slot.Y == 0) extras.Add(slot);
				else ordinary.Add(slot);
			}

			double cx = 0, cy = 0;
			foreach (var slot in ordinary) { cx += slot.X; cy += slot.Y; }
			if (ordinary.Count > 0) { cx /= ordinary.Count; cy /= ordinary.Count; }
			var stretchX = village.Layout == 1 ? Layout1StretchX : 1.0;
			var stretchY = village.Layout == 1 ? Layout1StretchY : 1.0;

			foreach (var slot in ordinary)
			{
				data.Slots.Add(new SlotData
				{
					Id = slot.Id,
					Extra = slot.Extra,
					X = (float)((slot.X - cx) * stretchX * MetresPerVillageUnit),
					Z = (float)(-(slot.Y - cy) * stretchY * MetresPerVillageUnit),
				});
			}

			// Bought places have no saved spot: a ring a little outside the others, spread by the golden angle.
			float maxX = 30f, maxZ = 20f;
			foreach (var slot in data.Slots) { maxX = Math.Max(maxX, Math.Abs(slot.X)); maxZ = Math.Max(maxZ, Math.Abs(slot.Z)); }
			var index = 0;
			extras.Sort((a, b) => a.Id.CompareTo(b.Id));
			foreach (var slot in extras)
			{
				var angle = (float)(index++ * 2.399963 + 0.6);
				data.Slots.Add(new SlotData { Id = slot.Id, Extra = true, X = (maxX + 15f) * (float)Math.Cos(angle), Z = (maxZ + 12f) * (float)Math.Sin(angle) });
			}
			return data.Slots;
		}

		static void BuildPlots(FarmSave farm, VillageData data, long now)
		{
			// The drawn land grows with the plots past 24, as in Lords (_dims in farm.generator.ts).
			var scale = Math.Max(1.0, Math.Sqrt(farm.Plots.Count / (double)FarmPlotCount));
			var centreX = FarmMapWidth * scale / 2;
			var centreY = FarmMapHeight * scale / 2;
			data.FarmRx = (float)(centreX * MetresPerFarmUnit);
			data.FarmRz = (float)(centreY * MetresPerFarmUnit);

			var plots = new List<FarmPlot>(farm.Plots);
			plots.Sort((a, b) => a.Id.CompareTo(b.Id));
			var seen = new HashSet<int>();
			foreach (var plot in plots)
			{
				if (!seen.Add(plot.Id)) continue;
				farm.Levels.TryGetValue(plot.Id.ToString(CultureInfo.InvariantCulture), out var work);
				var level = work != null ? LordsRules.LevelAt(work, now) : 0;
				data.Plots.Add(new PlotData
				{
					Id = plot.Id,
					Resource = plot.Resource,
					X = (float)((plot.X - centreX) * MetresPerFarmUnit),
					Z = (float)(-(plot.Y - centreY) * MetresPerFarmUnit),
					Level = level,
					Target = work != null ? LordsRules.TargetLevel(work, now) : 0,
					Working = work != null && LordsRules.RunningUpgrade(work, now) != null,
					Patch = plot.Patch,
				});
			}
		}

		/// <summary>The place where a game stores what it carries: the first warehouse, else the lord's hall, else the centre.</summary>
		static int FindStore(VillageData data)
		{
			var best = int.MaxValue;
			foreach (var building in data.Buildings) if (building.Type == BuildingKinds.Warehouse && building.Level > 0 && building.Slot < best) best = building.Slot;
			if (best != int.MaxValue) return best;
			foreach (var building in data.Buildings) if (building.Type == BuildingKinds.LordsHall) return building.Slot;
			return BuildingKinds.CenterPlace;
		}

		/// <summary>Where a building's box stands, in metres.</summary>
		public static (float x, float z) PositionOf(VillageData data, int slot)
		{
			if (slot == BuildingKinds.CenterPlace) return (0f, 0f);
			var place = data.SlotById(slot);
			return place != null ? (place.X, place.Z) : (0f, 0f);
		}

		/// <summary>True when a point is inside the box of a building (grown by <paramref name="margin"/>).</summary>
		public static bool IsInsideBuilding(VillageData data, float x, float z, float margin)
		{
			foreach (var building in data.Buildings)
			{
				var level = Math.Max(1, Math.Max(building.Level, building.Target));
				var (bx, bz) = PositionOf(data, building.Slot);
				if (Math.Abs(x - bx) < BuildingKinds.Width(building.Type, level) / 2f + margin && Math.Abs(z - bz) < BuildingKinds.Depth(building.Type, level) / 2f + margin) return true;
			}
			return false;
		}

		/// <summary>The free spot nearest to the lord's stores, where a player starts: not inside or against any building.</summary>
		static (float x, float z) FindSpawn(VillageData data)
		{
			var (sx, sz) = PositionOf(data, data.StoreSlot);
			var best = (x: 0f, z: -data.VillageRz - 2f);
			var bestDistance = float.MaxValue;
			for (var x = -data.VillageRx; x <= data.VillageRx; x += 2f)
			{
				for (var z = -data.VillageRz; z <= data.VillageRz; z += 2f)
				{
					if (IsInsideBuilding(data, x, z, 2.5f)) continue;
					var distance = (x - sx) * (x - sx) + (z - sz) * (z - sz);
					if (distance < bestDistance) { bestDistance = distance; best = (x, z); }
				}
			}
			return best;
		}
	}
}
