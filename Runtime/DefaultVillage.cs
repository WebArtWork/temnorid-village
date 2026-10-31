using System;
using System.Collections.Generic;
using System.Globalization;

namespace Temnorid.Village
{
	/// <summary>
	/// The standard village shown to a guest, a signed-out or offline player, or a Google player with no Lords save yet. It is built in code,
	/// the same every time, in the same shape as a Lords save so every rule and the view treat it alike (<see cref="Create"/> returns a
	/// <see cref="LordsProgress"/>). It has no work in progress, and its farm does not produce: a local stock only changes by what the game does.
	/// <list type="bullet">
	/// <item>12 building places on an ellipse round the centre (map units: centre 900 x 500, radii 520 x 330), numbered clockwise from the north; the lord's hall stands in the centre (place 0).</item>
	/// <item>Warehouse 3, granary 2, well 1 and a few other buildings at levels 1 to 3; two places are left free.</item>
	/// <item>A farm of 12 plots, two for each resource, at levels 2 and 1, one patch per resource on a ring round the village (radii 630 x 262 map units).</item>
	/// <item>A stock of <see cref="LordsRules.StartStock"/> (750, Lords' START_STOCK) of every resource.</item>
	/// </list>
	/// </summary>
	public static class DefaultVillage
	{
		public const int Seed = 20240607;
		public const int PlaceCount = 12;
		public const int PlotsPerResource = 2;

		const double VillageCentreX = 900, VillageCentreY = 500, VillageRx = 520, VillageRy = 330;
		const double FarmCentreX = 900, FarmCentreY = 500, FarmRx = 630, FarmRy = 262;
		const double PlotSpread = 45;

		public static Amounts StartAmounts() => Amounts.Filled(LordsRules.StartStock);

		public static LordsProgress Create(long now)
		{
			var village = new VillageSave { Seed = Seed, Layout = 2 };
			for (var i = 0; i < PlaceCount; i++)
			{
				var angle = (-90.0 + i * 360.0 / PlaceCount) * Math.PI / 180.0;
				village.Slots.Add(new VillageSlot
				{
					Id = i + 1,
					X = Math.Round(VillageCentreX + VillageRx * Math.Cos(angle), 1),
					Y = Math.Round(VillageCentreY + VillageRy * Math.Sin(angle), 1),
				});
			}

			Put(village, 0, BuildingKinds.LordsHall, 3);
			Put(village, 1, BuildingKinds.Warehouse, 3);
			Put(village, 2, "sawmill", 2);
			Put(village, 3, "rally-point", 1);
			Put(village, 4, BuildingKinds.Granary, 2);
			Put(village, 5, "barracks", 1);
			Put(village, 6, BuildingKinds.Well, 1);
			Put(village, 7, "marketplace", 2);
			Put(village, 8, "smithy", 1);
			// Place 9 stays free.
			Put(village, 10, "cranny", 1);
			Put(village, 11, "embassy", 1);
			// Place 12 stays free.

			var farm = new FarmSave { Seed = Seed, Layout = 2 };
			var id = 1;
			for (var r = 0; r < Res.All.Length; r++)
			{
				var angle = (-90.0 + r * 60.0) * Math.PI / 180.0;
				var centreX = FarmCentreX + FarmRx * Math.Cos(angle);
				var centreY = FarmCentreY + FarmRy * Math.Sin(angle);
				// The two plots of a patch sit side by side along the ring.
				var tangentX = -Math.Sin(angle);
				var tangentY = Math.Cos(angle);
				for (var k = 0; k < PlotsPerResource; k++)
				{
					var side = k == 0 ? -1 : 1;
					farm.Plots.Add(new FarmPlot
					{
						Id = id,
						Resource = Res.All[r],
						X = Math.Round(centreX + side * PlotSpread * tangentX, 1),
						Y = Math.Round(centreY + side * PlotSpread * tangentY, 1),
						Patch = 0,
					});
					farm.Levels[id.ToString(CultureInfo.InvariantCulture)] = new Construction { Level = k == 0 ? 2 : 1 };
					id++;
				}
			}

			return new LordsProgress
			{
				Lord = new LordInfo { Name = "" },
				Stock = new StockSave(StartAmounts(), now),
				Village = village,
				Farm = farm,
			};
		}

		static void Put(VillageSave village, int slot, string type, int level)
		{
			village.Buildings[slot.ToString(CultureInfo.InvariantCulture)] = new VillageBuilding { Type = type, Level = level };
		}
	}
}
