using System;
using System.Collections.Generic;

namespace Temnorid.Village
{
	/// <summary>
	/// The rules of Lords' economy, ported exactly from the Lords app so that Craft and Lords agree on every number:
	/// <c>building.timing.ts</c> (<see cref="LevelAt"/>, <see cref="RunningUpgrade"/>, <see cref="WorkEndsAt"/>, <see cref="TargetLevel"/>) and
	/// <c>economy.ts</c> (<see cref="PlotProduction"/>, <see cref="ProductionPerHour"/>, <see cref="Capacity"/>, <see cref="StockAt"/>).
	/// Time is epoch milliseconds, as in Lords. Pure C#, no Unity.
	/// </summary>
	public static class LordsRules
	{
		/// <summary>Each lord starts with this much of each resource (<c>START_STOCK</c>).</summary>
		public const double StartStock = 750;
		/// <summary>Each resource holds this much with no store built (<c>BASE_CAPACITY</c>).</summary>
		public const double BaseCapacity = 800;
		/// <summary>Room grows by this factor with every level of a resource's store (<c>CAPACITY_GROWTH</c>).</summary>
		public const double CapacityGrowth = 1.45;
		/// <summary>Hourly yield of one plot by level (<c>PLOT_PRODUCTION</c>).</summary>
		public static readonly double[] PlotProductionTable = { 2, 5, 9, 15, 22, 33, 50, 70, 100, 145, 200 };
		/// <summary>Past the table, each level yields this much more than the one before (<c>PLOT_GROWTH</c>).</summary>
		public const double PlotGrowth = 1.25;

		public const string StoreWarehouse = "warehouse";
		public const string StoreGranary = "granary";
		public const string StoreWell = "well";

		/// <summary>Which building stores each resource (<c>STORE</c> in economy.ts).</summary>
		public static string StoreOf(string resource)
		{
			switch (resource)
			{
				case Res.Grain: return StoreGranary;
				case Res.Water: return StoreWell;
				default: return StoreWarehouse;
			}
		}

		/// <summary>JavaScript's <c>Math.round</c>: halves go up (C#'s default rounds them to even).</summary>
		public static double JsRound(double value) => Math.Floor(value + 0.5);

		// ---- building.timing.ts ----

		static IEnumerable<WorkStep> Steps(Construction work)
		{
			if (work.Queue != null) foreach (var step in work.Queue) if (step != null) yield return step;
			if (work.Upgrade != null) yield return work.Upgrade;
		}

		/// <summary>The level reached by <paramref name="now"/>: finished steps count, a running one does not.</summary>
		public static int LevelAt(Construction work, long now)
		{
			var level = work.Level;
			foreach (var step in Steps(work)) if (step.EndsAt <= now) level = step.Level;
			return level;
		}

		/// <summary>The step being worked on at <paramref name="now"/>, or null.</summary>
		public static WorkStep RunningUpgrade(Construction work, long now)
		{
			foreach (var step in Steps(work)) if (step.EndsAt > now) return step;
			return null;
		}

		/// <summary>When all the work will be done, 0 once it is.</summary>
		public static long WorkEndsAt(Construction work, long now)
		{
			WorkStep last = null;
			foreach (var step in Steps(work)) last = step;
			return last != null && last.EndsAt > now ? last.EndsAt : 0;
		}

		/// <summary>The level the work ends at: running and waiting steps count.</summary>
		public static int TargetLevel(Construction work, long now)
		{
			WorkStep last = null;
			foreach (var step in Steps(work)) last = step;
			return last != null && last.EndsAt > now ? last.Level : LevelAt(work, now);
		}

		/// <summary>A building whose dismantling is over and nothing is left of it (<c>isGone</c>).</summary>
		public static bool IsGone(Construction work, long now)
		{
			var step = work.Upgrade;
			return step != null && step.Down && step.Level == 0 && step.EndsAt <= now;
		}

		// ---- economy.ts ----

		/// <summary>What one plot yields per hour at a level.</summary>
		public static double PlotProduction(int level)
		{
			var last = PlotProductionTable.Length - 1;
			var known = PlotProductionTable[Math.Max(0, Math.Min(last, level))];
			return level > last ? JsRound(known * Math.Pow(PlotGrowth, level - last)) : known;
		}

		/// <summary>The level of a farm plot at <paramref name="now"/>: 0 when it has no saved work.</summary>
		public static int PlotLevel(FarmPlot plot, IDictionary<string, Construction> levels, long now)
		{
			return levels != null && levels.TryGetValue(plot.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var saved) && saved != null ? LevelAt(saved, now) : 0;
		}

		/// <summary>Hourly yield of the whole farm at <paramref name="now"/>, per resource.</summary>
		public static Amounts ProductionPerHour(IEnumerable<FarmPlot> plots, IDictionary<string, Construction> levels, long now)
		{
			var perHour = new Amounts();
			foreach (var plot in plots) perHour[plot.Resource] += PlotProduction(PlotLevel(plot, levels, now));
			return perHour;
		}

		/// <summary>How much of each resource the stores hold at most at <paramref name="now"/>.</summary>
		public static Amounts Capacity(IDictionary<string, VillageBuilding> buildings, long now)
		{
			var levels = new Dictionary<string, int>();
			if (buildings != null)
			{
				foreach (var building in buildings.Values)
				{
					if (building == null || string.IsNullOrEmpty(building.Type)) continue;
					levels.TryGetValue(building.Type, out var sum);
					levels[building.Type] = sum + LevelAt(building, now);
				}
			}

			var capacity = new Amounts();
			foreach (var id in Res.All)
			{
				levels.TryGetValue(StoreOf(id), out var level);
				capacity[id] = JsRound(BaseCapacity * Math.Pow(CapacityGrowth, level) / 100) * 100;
			}
			return capacity;
		}

		/// <summary>
		/// The stock at <paramref name="now"/>: the saved amounts plus what the farm made since, never above the stores' capacity.
		/// What is already over the cap is kept, just not added to.
		/// </summary>
		public static Amounts StockAt(StockSave stock, IEnumerable<FarmPlot> plots, IDictionary<string, Construction> levels, IDictionary<string, VillageBuilding> buildings, long now)
		{
			var hours = Math.Max(0L, now - stock.At) / 3_600_000d;
			var perHour = ProductionPerHour(plots, levels, now);
			var cap = Capacity(buildings, now);
			var result = new Amounts();
			foreach (var id in Res.All)
			{
				var saved = stock.Amounts[id];
				result[id] = Math.Min(Math.Max(saved, cap[id]), saved + perHour[id] * hours);
			}
			return result;
		}

		/// <summary>The stock of a loaded save at <paramref name="now"/>; the save must have a stock.</summary>
		public static Amounts StockAt(LordsProgress progress, long now)
		{
			return StockAt(
				progress.Stock,
				progress.Farm != null ? (IEnumerable<FarmPlot>)progress.Farm.Plots : new FarmPlot[0],
				progress.Farm?.Levels,
				progress.Village?.Buildings,
				now);
		}
	}
}
