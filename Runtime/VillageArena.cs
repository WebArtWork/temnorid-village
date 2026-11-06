using UnityEngine;

namespace Temnorid.Village
{
	/// <summary>Where a game places the player and its notice board in the village arena.</summary>
	public sealed class ArenaPlaces
	{
		public Vector3 PlayerSpawn;
		public Quaternion PlayerRotation;
		/// <summary>The middle of the north side, in front of the wall: a good place for a board that faces the player's spawn.</summary>
		public Vector3 BoardPosition;
	}

	/// <summary>
	/// The village's arena as a place any Temnorid game can start in: a paved stone courtyard with walls, pillars, banners and torches, made from
	/// primitives. A game adds its own boards and doors to it (Dungeons: the dungeon board). It is common so every game that opens in the
	/// lord's arena opens in the same one.
	/// </summary>
	public static class VillageArena
	{
		public const float Width = 56f;
		public const float Depth = 44f;
		public const float WallHeight = 5f;

		/// <param name="pillarsOnNorth">False when the game hangs something on the north wall (a board) that pillars would stand in front of.</param>
		public static ArenaPlaces Build(Transform parent, Material template, bool pillarsOnNorth = true)
		{
			var root = new GameObject("VillageArena").transform;
			root.SetParent(parent, false);

			var floor = Mat(template, new Color(0.34f, 0.34f, 0.38f));
			var floorAlt = Mat(template, new Color(0.30f, 0.30f, 0.34f));
			var stone = Mat(template, new Color(0.46f, 0.44f, 0.46f));
			var trim = Mat(template, new Color(0.55f, 0.52f, 0.5f));
			var banner = Mat(template, new Color(0.55f, 0.12f, 0.14f));
			var wood = Mat(template, new Color(0.40f, 0.27f, 0.16f));

			// The paving: a chequer of two shades of stone.
			const int tiles = 14;
			var tileW = Width / tiles;
			var tileD = Depth / tiles;
			Piece(PrimitiveType.Cube, "FloorBase", root, new Vector3(0f, -0.3f, 0f), new Vector3(Width + 6f, 0.6f, Depth + 6f), floor, true);
			for (var x = 0; x < tiles; x++)
			{
				for (var z = 0; z < tiles; z++)
				{
					if ((x + z) % 2 == 0) continue;
					Piece(PrimitiveType.Cube, "Tile", root, new Vector3(-Width * 0.5f + tileW * (x + 0.5f), 0.01f, -Depth * 0.5f + tileD * (z + 0.5f)), new Vector3(tileW, 0.04f, tileD), floorAlt, false);
				}
			}

			// The wall round the courtyard.
			var hx = Width * 0.5f;
			var hz = Depth * 0.5f;
			Piece(PrimitiveType.Cube, "WallNorth", root, new Vector3(0f, WallHeight * 0.5f, hz), new Vector3(Width + 2f, WallHeight, 1.2f), stone, true);
			Piece(PrimitiveType.Cube, "WallSouth", root, new Vector3(0f, WallHeight * 0.5f, -hz), new Vector3(Width + 2f, WallHeight, 1.2f), stone, true);
			Piece(PrimitiveType.Cube, "WallEast", root, new Vector3(hx, WallHeight * 0.5f, 0f), new Vector3(1.2f, WallHeight, Depth + 2f), stone, true);
			Piece(PrimitiveType.Cube, "WallWest", root, new Vector3(-hx, WallHeight * 0.5f, 0f), new Vector3(1.2f, WallHeight, Depth + 2f), stone, true);
			foreach (var side in new[] { -1f, 1f })
			{
				Piece(PrimitiveType.Cube, "CopingNS" + side, root, new Vector3(0f, WallHeight + 0.15f, side * hz), new Vector3(Width + 2.6f, 0.3f, 1.8f), trim, false);
				Piece(PrimitiveType.Cube, "CopingEW" + side, root, new Vector3(side * hx, WallHeight + 0.15f, 0f), new Vector3(1.8f, 0.3f, Depth + 2.6f), trim, false);
			}

			// Pillars along the long sides, with a banner and a torch on each.
			var lights = new GameObject("Torches").transform;
			lights.SetParent(root, false);
			for (var i = 0; i < 5; i++)
			{
				var x = -hx + 6f + i * (Width - 12f) / 4f;
				foreach (var side in new[] { -1f, 1f })
				{
					var z = side * (hz - 2f);
					if (side > 0f && !pillarsOnNorth)
					{
						AddTorch(lights, new Vector3(x, 4f, z));
						continue;
					}
					Piece(PrimitiveType.Cube, "Pillar", root, new Vector3(x, 2.4f, z), new Vector3(1.4f, 4.8f, 1.4f), trim, true);
					Piece(PrimitiveType.Cube, "PillarCap", root, new Vector3(x, 4.95f, z), new Vector3(1.9f, 0.3f, 1.9f), stone, false);
					Piece(PrimitiveType.Cube, "Banner", root, new Vector3(x, 3.2f, z - side * 0.8f), new Vector3(0.8f, 2f, 0.06f), banner, false);
					AddTorch(lights, new Vector3(x, 4f, z - side * 1.6f));
				}
			}

			// A few crates and barrels by the walls, so it looks used.
			Crates(root, wood, new Vector3(-hx + 3f, 0f, -hz + 4f));
			Crates(root, wood, new Vector3(hx - 3f, 0f, -hz + 4f));
			Crates(root, wood, new Vector3(-hx + 3f, 0f, 3f));

			// The ring of sand in the middle, as an arena has.
			var sand = Mat(template, new Color(0.62f, 0.54f, 0.38f));
			Piece(PrimitiveType.Cylinder, "Sand", root, new Vector3(0f, 0.03f, -3f), new Vector3(16f, 0.03f, 16f), sand, false);

			return new ArenaPlaces
			{
				PlayerSpawn = new Vector3(0f, 0.1f, -hz + 6f),
				PlayerRotation = Quaternion.identity,
				BoardPosition = new Vector3(0f, 0f, hz - 4.5f),
			};
		}

		static void AddTorch(Transform parent, Vector3 position)
		{
			var torch = new GameObject("Torch");
			torch.transform.SetParent(parent, false);
			torch.transform.position = position;
			var light = torch.AddComponent<Light>();
			light.type = LightType.Point;
			light.range = 20f;
			light.intensity = 2.6f;
			light.color = new Color(1f, 0.72f, 0.45f);
			light.shadows = LightShadows.None;
		}

		static void Crates(Transform root, Material wood, Vector3 at)
		{
			Piece(PrimitiveType.Cube, "Crate", root, at + new Vector3(0f, 0.7f, 0f), new Vector3(1.4f, 1.4f, 1.4f), wood, true);
			Piece(PrimitiveType.Cube, "Crate", root, at + new Vector3(1.6f, 0.5f, 0.4f), new Vector3(1f, 1f, 1f), wood, true);
			Piece(PrimitiveType.Cylinder, "Barrel", root, at + new Vector3(0.2f, 0.6f, 1.8f), new Vector3(0.9f, 0.6f, 0.9f), wood, true);
		}

		static Material Mat(Material template, Color colour) => new Material(template) { color = colour };

		static Transform Piece(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool collider)
		{
			var piece = GameObject.CreatePrimitive(type);
			piece.name = name;
			if (!collider)
			{
				var c = piece.GetComponent<Collider>();
				if (c != null) Object.Destroy(c);
			}
			piece.transform.SetParent(parent, false);
			piece.transform.localPosition = localPosition;
			piece.transform.localScale = scale;
			piece.GetComponent<Renderer>().sharedMaterial = material;
			return piece.transform;
		}
	}
}
