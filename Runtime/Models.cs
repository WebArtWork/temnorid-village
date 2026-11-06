using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Temnorid.Village
{
	// These classes mirror the Firestore document users/{uid}/progress/lords as the Lords app saves it (progress.service.ts and the
	// files it names). Fields this game does not need are not modelled: they are ignored when reading, and never written back.

	/// <summary>The lord's stores at one moment (Lords' <c>StockSave</c>).</summary>
	public sealed class StockSave
	{
		[JsonProperty("amounts")] public Amounts Amounts = new Amounts();
		/// <summary>Epoch milliseconds the amounts were true at.</summary>
		[JsonProperty("at")] public long At;

		public StockSave() { }

		public StockSave(Amounts amounts, long at)
		{
			Amounts = amounts;
			At = at;
		}
	}

	/// <summary>A place where a building can stand. Coordinates are Lords map units (the village map is 1800 x 1000).</summary>
	public sealed class VillageSlot
	{
		[JsonProperty("id")] public int Id;
		[JsonProperty("x")] public double X;
		[JsonProperty("y")] public double Y;
		/// <summary>Bought later; Lords draws the spot, it is not saved.</summary>
		[JsonProperty("extra")] public bool Extra;
	}

	/// <summary>One level of work (Lords' <c>WorkStep</c>): it counts as done once <see cref="EndsAt"/> has passed.</summary>
	public sealed class WorkStep
	{
		[JsonProperty("level")] public int Level;
		[JsonProperty("startedAt")] public long StartedAt;
		[JsonProperty("endsAt")] public long EndsAt;
		/// <summary>True while a building is being taken down: <see cref="Level"/> is then the lower level it ends at.</summary>
		[JsonProperty("down")] public bool Down;
	}

	/// <summary>Something raised level by level over time, a building or a farm plot (Lords' <c>Construction</c>).</summary>
	public class Construction
	{
		/// <summary>The last finished level when the work was saved.</summary>
		[JsonProperty("level")] public int Level;
		[JsonProperty("queue")] public List<WorkStep> Queue;
		[JsonProperty("upgrade")] public WorkStep Upgrade;
	}

	/// <summary>A building on a village place, saved by place id (Lords' <c>VillageBuilding</c>).</summary>
	public sealed class VillageBuilding : Construction
	{
		[JsonProperty("type")] public string Type = "";
	}

	public sealed class VillageSave
	{
		[JsonProperty("seed")] public double Seed;
		[JsonProperty("layout")] public int Layout = 2;
		[JsonProperty("slots")] public List<VillageSlot> Slots = new List<VillageSlot>();
		/// <summary>What stands on each place, by place id: 0 is the centre, -1 the wall, 1 and up the ordinary places.</summary>
		[JsonProperty("buildings")] public Dictionary<string, VillageBuilding> Buildings = new Dictionary<string, VillageBuilding>();
	}

	/// <summary>A plot of land outside the village that yields one resource (Lords' <c>FarmPlot</c>), at farm map units.</summary>
	public sealed class FarmPlot
	{
		[JsonProperty("id")] public int Id;
		[JsonProperty("resource")] public string Resource = Res.Wood;
		[JsonProperty("x")] public double X;
		[JsonProperty("y")] public double Y;
		[JsonProperty("extra")] public bool Extra;
		[JsonProperty("patch")] public int Patch;
	}

	public sealed class FarmSave
	{
		[JsonProperty("seed")] public double Seed;
		[JsonProperty("layout")] public int Layout = 2;
		[JsonProperty("plots")] public List<FarmPlot> Plots = new List<FarmPlot>();
		/// <summary>How far each plot has been worked, by plot id. Plots not listed are at level 0.</summary>
		[JsonProperty("levels")] public Dictionary<string, Construction> Levels = new Dictionary<string, Construction>();
	}

	public sealed class LordInfo
	{
		[JsonProperty("name")] public string Name = "";
	}

	/// <summary>A player's Lords save, the parts Craft and other games read.</summary>
	public sealed class LordsProgress
	{
		[JsonProperty("lord")] public LordInfo Lord = new LordInfo();
		[JsonProperty("stock")] public StockSave Stock;
		[JsonProperty("village")] public VillageSave Village;
		[JsonProperty("farm")] public FarmSave Farm;
		/// <summary>The lord's hero, created by Lords the first time it opens; null before that.</summary>
		[JsonProperty("hero")] public HeroSave Hero;

		[JsonIgnore] public string LordName => Lord != null && !string.IsNullOrWhiteSpace(Lord.Name) ? Lord.Name : "";

		/// <summary>Reads a document's fields. Unknown fields are ignored and missing ones tolerated; a field of the wrong type makes it fail.</summary>
		public static bool TryParse(JObject data, out LordsProgress progress, out string error)
		{
			progress = null;
			error = "";
			if (data == null) { error = "No data."; return false; }
			try
			{
				progress = data.ToObject<LordsProgress>() ?? new LordsProgress();
				progress.Normalize();
				return true;
			}
			catch (JsonException exception)
			{
				// Only the kind of problem, not the data: it may hold a uid.
				error = "The save has an unexpected shape (" + exception.GetType().Name + ").";
				return false;
			}
		}

		/// <summary>Replaces nulls (a JSON null or a missing list) with empty collections so nothing downstream checks twice.</summary>
		public void Normalize()
		{
			if (Lord == null) Lord = new LordInfo();
			if (Village != null)
			{
				if (Village.Slots == null) Village.Slots = new List<VillageSlot>();
				if (Village.Buildings == null) Village.Buildings = new Dictionary<string, VillageBuilding>();
				var broken = new List<string>();
				foreach (var pair in Village.Buildings) if (pair.Value == null || string.IsNullOrEmpty(pair.Value.Type)) broken.Add(pair.Key);
				foreach (var key in broken) Village.Buildings.Remove(key);
				foreach (var slot in Village.Slots.ToArray()) if (slot == null) Village.Slots.Remove(slot);
			}
			if (Farm != null)
			{
				if (Farm.Plots == null) Farm.Plots = new List<FarmPlot>();
				if (Farm.Levels == null) Farm.Levels = new Dictionary<string, Construction>();
				Farm.Plots.RemoveAll(plot => plot == null || !Res.IsKnown(plot.Resource));
				var empty = new List<string>();
				foreach (var pair in Farm.Levels) if (pair.Value == null) empty.Add(pair.Key);
				foreach (var key in empty) Farm.Levels.Remove(key);
			}
		}

		/// <summary>True when there is a village with places and a farm with plots: enough to show the lord's land.</summary>
		[JsonIgnore] public bool HasMaps => Village != null && Village.Slots.Count > 0 && Farm != null && Farm.Plots.Count > 0;
	}
}
