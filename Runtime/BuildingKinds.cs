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

		/// <summary>Width of the box that stands for a building: it grows with the level up to level 8, so places 7 m apart never overlap.</summary>
		public static float Width(string type, int level)
		{
			var width = 3.0f + 0.25f * Math.Min(Math.Max(level, 1), 8);
			if (type == LordsHall || type == "castle" || type == "arena") width *= 1.25f;
			return width;
		}

		public static float Depth(string type, int level) => Width(type, level) * 0.85f;

		public static float Height(string type, int level)
		{
			var height = 2.2f + 0.3f * Math.Min(Math.Max(level, 1), 10);
			if (type == LordsHall || type == "castle") height *= 1.2f;
			return height;
		}
	}
}
