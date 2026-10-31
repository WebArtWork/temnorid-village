using System.Collections.Generic;
using Newtonsoft.Json;

namespace Temnorid.Village
{
	/// <summary>A building place, in metres from the village centre (+x east, +z north).</summary>
	public sealed class SlotData
	{
		[JsonProperty("id")] public int Id;
		[JsonProperty("x")] public float X;
		[JsonProperty("z")] public float Z;
		[JsonProperty("extra")] public bool Extra;
	}

	/// <summary>A building at a moment: its level then, and whether work is running on it (a scaffold).</summary>
	public sealed class BuildingData
	{
		/// <summary>The place it stands on; 0 is the centre.</summary>
		[JsonProperty("slot")] public int Slot;
		[JsonProperty("type")] public string Type = "";
		[JsonProperty("level")] public int Level;
		/// <summary>The level the running work ends at; the same as <see cref="Level"/> when nothing is running.</summary>
		[JsonProperty("target")] public int Target;
		[JsonProperty("working")] public bool Working;
	}

	/// <summary>A farm plot at a moment, in metres from the village centre.</summary>
	public sealed class PlotData
	{
		[JsonProperty("id")] public int Id;
		[JsonProperty("resource")] public string Resource = Res.Wood;
		[JsonProperty("x")] public float X;
		[JsonProperty("z")] public float Z;
		[JsonProperty("level")] public int Level;
		[JsonProperty("target")] public int Target;
		[JsonProperty("working")] public bool Working;
		[JsonProperty("patch")] public int Patch;
	}

	/// <summary>
	/// Everything <see cref="VillageView"/> needs to draw a lord's village and farm: places, buildings and plots with their levels at one
	/// moment, already in metres. It is plain JSON, so an online host can send it to guests (<see cref="ToJson"/>, <see cref="FromJson"/>)
	/// and guests draw the same land without a save of their own. Build it with <see cref="VillageLayout.Build"/>.
	/// </summary>
	public sealed class VillageData
	{
		/// <summary>True when this is a Lords player's own village; false for the standard default village.</summary>
		[JsonProperty("lords")] public bool IsLords;
		[JsonProperty("lord")] public string LordName = "";
		/// <summary>Scatters the trees and rocks; the same seed gives the same scenery.</summary>
		[JsonProperty("seed")] public double Seed;
		/// <summary>Epoch milliseconds the levels are true at.</summary>
		[JsonProperty("now")] public long Now;
		[JsonProperty("slots")] public List<SlotData> Slots = new List<SlotData>();
		[JsonProperty("buildings")] public List<BuildingData> Buildings = new List<BuildingData>();
		/// <summary>The level of the wall round the village, 0 when there is none yet.</summary>
		[JsonProperty("wall")] public int WallLevel;
		[JsonProperty("wallWorking")] public bool WallWorking;
		[JsonProperty("plots")] public List<PlotData> Plots = new List<PlotData>();
		/// <summary>What the stores hold at most, per resource, as the buildings stand.</summary>
		[JsonProperty("capacity")] public Amounts Capacity = new Amounts();
		/// <summary>Half the width and depth of the village (inside its wall), in metres.</summary>
		[JsonProperty("villageRx")] public float VillageRx = 20f;
		[JsonProperty("villageRz")] public float VillageRz = 12f;
		/// <summary>Half the width and depth of the farm, in metres.</summary>
		[JsonProperty("farmRx")] public float FarmRx = 90f;
		[JsonProperty("farmRz")] public float FarmRz = 50f;
		/// <summary>The building that is the lord's stores: the warehouse, else the hall, else the centre.</summary>
		[JsonProperty("storeSlot")] public int StoreSlot;
		[JsonProperty("spawnX")] public float SpawnX;
		[JsonProperty("spawnZ")] public float SpawnZ;

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

		/// <summary>Reads data sent by a host; null when it is not valid.</summary>
		public static VillageData FromJson(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try
			{
				var data = JsonConvert.DeserializeObject<VillageData>(json);
				if (data == null) return null;
				if (data.Slots == null) data.Slots = new List<SlotData>();
				if (data.Buildings == null) data.Buildings = new List<BuildingData>();
				if (data.Plots == null) data.Plots = new List<PlotData>();
				if (data.Capacity == null) data.Capacity = new Amounts();
				data.Buildings.RemoveAll(b => b == null || string.IsNullOrEmpty(b.Type));
				data.Plots.RemoveAll(p => p == null || !Res.IsKnown(p.Resource));
				data.Slots.RemoveAll(s => s == null);
				return data;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		public SlotData SlotById(int id)
		{
			foreach (var slot in Slots) if (slot.Id == id) return slot;
			return null;
		}

		public BuildingData BuildingAt(int slot)
		{
			foreach (var building in Buildings) if (building.Slot == slot) return building;
			return null;
		}

		public PlotData PlotById(int id)
		{
			foreach (var plot in Plots) if (plot.Id == id) return plot;
			return null;
		}
	}
}
