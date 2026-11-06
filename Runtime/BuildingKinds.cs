using System;

namespace Temnorid.Village
{
	/// <summary>The kinds of building of Lords (building.data.ts): ids, English names, and how big a graybox box for one is.</summary>
	public static class BuildingKinds
	{
		public const string LordsHall = "lords-hall";
		public const string Warehouse = "warehouse";
		public const string Granary = "granary";
		public const string Well = "well";
		public const string Wall = "wall";

		/// <summary>The place of the village centre in Lords' save; the wall is saved under <see cref="WallPlace"/>.</summary>
		public const int CenterPlace = 0;
		public const int WallPlace = -1;

		public static string Name(string type)
		{
			switch (type)
			{
				case "lords-hall": return "Lord's hall";
				case "rally-point": return "Rally point";
				case "warehouse": return "Warehouse";
				case "granary": return "Granary";
				case "well": return "Well";
				case "cranny": return "Cranny";
				case "marketplace": return "Marketplace";
				case "embassy": return "Embassy";
				case "sawmill": return "Sawmill";
				case "stonecutter": return "Stonecutter";
				case "foundry": return "Foundry";
				case "pottery": return "Pottery";
				case "mill": return "Mill";
				case "bakery": return "Bakery";
				case "barracks": return "Barracks";
				case "smithy": return "Smithy";
				case "academy": return "Academy";
				case "stable": return "Stable";
				case "workshop": return "Workshop";
				case "wall": return "Wall";
				case "infirmary": return "Infirmary";
				case "arena": return "Arena";
				case "tavern": return "Tavern";
				case "castle": return "Castle";
				case "town-hall": return "Town hall";
				case "treasury": return "Treasury";
				default: return type;
			}
		}

		/// <summary>
		/// Width of the box that stands for a building, in metres, sized like buildings of about the year 1300: a house is 5 to 7 m wide and 3.5 to 6 m
		/// to the eaves, a warehouse or granary is bigger, the hall, a castle and the arena are the biggest, a well is small. It grows a little with
		/// the level up to level 8. The places are about 17 m apart (see <see cref="VillageLayout"/>), so buildings never touch.
		/// </summary>
		public static float Width(string type, int level)
		{
			var width = 5.0f + 0.25f * Math.Min(Math.Max(level, 1), 8);
			switch (type)
			{
				case LordsHall: case "castle": return width * 1.7f;
				case "arena": return width * 2.6f;
				case Warehouse: case Granary: case "barracks": case "stable": return width * 1.35f;
				case "marketplace": case "embassy": case "academy": case "town-hall": case "treasury": return width * 1.2f;
				case Well: return width * 0.5f;
				default: return width;
			}
		}

		public static float Depth(string type, int level) => Width(type, level) * 0.85f;

		/// <summary>Height to the eaves; the roof sits on top of it.</summary>
		public static float Height(string type, int level)
		{
			var height = 3.2f + 0.25f * Math.Min(Math.Max(level, 1), 10);
			switch (type)
			{
				case LordsHall: case "castle": return height * 1.5f;
				case "arena": return height * 0.8f;
				case Well: return height * 0.4f;
				case Warehouse: case Granary: case "barracks": return height * 1.1f;
				default: return height;
			}
		}
	}
}
