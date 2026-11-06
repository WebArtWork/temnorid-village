using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Temnorid.Village
{
	/// <summary>A farm plot as drawn: its object (with a trigger box a game can gather at) and the decoration a game may hide when the plot is used up.</summary>
	public sealed class PlotHandle
	{
		public int Id;
		public PlotData Data;
		public Transform Root;
		/// <summary>The trees, rocks, stalks or pond on the plot, without the ground patch, the level markers and the label.</summary>
		public Transform Visual;
	}

	/// <summary>
	/// Draws a lord's village and farm from <see cref="VillageData"/> with Unity primitives (a graybox), under this object: the ground, the
	/// square and roads, the wall, a box for every building that grows with its level (a scaffold while work is running on it), a patch of land
	/// with level markers for every farm plot, and a few trees and rocks scattered from the seed. Names and levels float above them as
	/// depth-tested world text. The village centre is this object's origin; +x is east and +z north. It knows nothing about any game: a game
	/// asks it where things are (<see cref="PlotById"/>, <see cref="BuildingBySlot"/>, <see cref="StorePosition"/>, <see cref="SpawnPosition"/>)
	/// and adds its own behaviours to the objects it returns. Buildings and walls have solid colliders, plots have trigger boxes, the ground is walkable.
	/// </summary>
	public sealed class VillageView : MonoBehaviour
	{
		/// <summary>An opaque URP Lit material the others are copied from. A scene builder sets it so the shader is part of the build; if empty, the shader is looked up by name.</summary>
		public Material LitTemplate;
		/// <summary>The depth-tested text material (shader <c>Temnorid/WorldText</c> of 3d-unity-graybox). If empty it is made from the shader by name, else the font's own.</summary>
		public Material TextMaterial;
		/// <summary>
		/// How much bigger than the data's metres the village and farm are drawn. 1 is the size the layout is worked out in (a house about 3 to 5 m wide,
		/// which looks small and crowded next to a 1.8 m player); 2.5 would make everything 2.5 times bigger. Set it before <see cref="Build"/>.
		/// </summary>
		public float WorldScale = 1f;
		/// <summary>How much bigger than its 6 m graybox a farm plot (patch, trees, rocks, markers, label) is drawn: a field of about 10 m.</summary>
		public float PlotScale = 1.7f;

		readonly Dictionary<Color32, Material> _materials = new Dictionary<Color32, Material>();
		readonly List<PlotHandle> _plots = new List<PlotHandle>();
		readonly Dictionary<int, Transform> _buildings = new Dictionary<int, Transform>();
		Material _text;
		Font _font;
		Transform _root;

		public VillageData Data { get; private set; }
		public IReadOnlyList<PlotHandle> Plots => _plots;
		public Transform Root => _root;

		/// <summary>Raised after the village has been drawn.</summary>
		public event Action Built;

		public PlotHandle PlotById(int id)
		{
			foreach (var plot in _plots) if (plot.Id == id) return plot;
			return null;
		}

		/// <summary>The building that stands on a place (0 is the centre), or null.</summary>
		public Transform BuildingBySlot(int slot) => _buildings.TryGetValue(slot, out var building) ? building : null;

		/// <summary>The building that is the lord's stores (warehouse, else hall, else the centre), or null before anything is drawn.</summary>
		public Transform StoreBuilding => Data != null ? BuildingBySlot(Data.StoreSlot) : null;

		/// <summary>The world position of the lord's stores.</summary>
		public Vector3 StorePosition => StoreBuilding != null ? StoreBuilding.position : transform.position;

		/// <summary>A free spot near the stores where a player can start.</summary>
		public Vector3 SpawnPosition => Data != null && _root != null ? _root.TransformPoint(Data.SpawnX, 0.1f / Mathf.Max(0.01f, WorldScale), Data.SpawnZ) : transform.position;

		void OnDestroy() => Clear();

		/// <summary>Removes everything drawn.</summary>
		public void Clear()
		{
			if (_root != null) Kill(_root.gameObject);
			_root = null;
			_plots.Clear();
			_buildings.Clear();
			foreach (var material in _materials.Values) if (material != null) Kill(material);
			_materials.Clear();
			if (_text != null && TextMaterial == null) Kill(_text);
			_text = null;
			Data = null;
		}

		/// <summary>Draws (or redraws) the village and farm.</summary>
		public void Build(VillageData data)
		{
			Clear();
			Data = data;
			_root = new GameObject("VillageRoot").transform;
			_root.SetParent(transform, false);
			_root.localScale = Vector3.one * Mathf.Max(0.01f, WorldScale);

			BuildGround(data);
			BuildRoads(data);
			BuildSlots(data);
			BuildBuildings(data);
			BuildWall(data);
			BuildPlots(data);
			BuildScenery(data);
			Built?.Invoke();
		}

		// ---------------------------------------------------------------- ground and roads

		void BuildGround(VillageData data)
		{
			var grass = Mat(new Color(0.33f, 0.55f, 0.28f));
			var sand = Mat(new Color(0.82f, 0.75f, 0.6f));
			Prim(PrimitiveType.Cube, "Ground", _root, new Vector3(0f, -0.5f, 0f), new Vector3(data.FarmRx * 2f + 200f, 1f, data.FarmRz * 2f + 200f), grass, true);
			var plaza = Prim(PrimitiveType.Cylinder, "Plaza", _root, new Vector3(0f, 0.03f, 0f), new Vector3(34f, 0.03f, 34f), sand);
			plaza.transform.localScale = new Vector3(34f, 0.03f, 24f);
		}

		void BuildRoads(VillageData data)
		{
			var dirt = Mat(new Color(0.68f, 0.58f, 0.42f));
			foreach (var slot in data.Slots)
			{
				var length = Mathf.Sqrt(slot.X * slot.X + slot.Z * slot.Z);
				if (length < 1f) continue;
				var road = Prim(PrimitiveType.Cube, "Road_" + slot.Id, _root, new Vector3(slot.X / 2f, 0.02f, slot.Z / 2f), new Vector3(4f, 0.04f, length), dirt);
				road.transform.localRotation = Quaternion.LookRotation(new Vector3(slot.X, 0f, slot.Z));
			}
		}

		void BuildSlots(VillageData data)
		{
			var tile = Mat(new Color(0.6f, 0.5f, 0.36f));
			foreach (var slot in data.Slots)
			{
				if (data.BuildingAt(slot.Id) != null) continue;
				Prim(PrimitiveType.Cube, "FreePlace_" + slot.Id, _root, new Vector3(slot.X, 0.05f, slot.Z), new Vector3(7f, 0.1f, 7f), tile);
			}
		}

		// ---------------------------------------------------------------- buildings

		static Color BodyColour(string type)
		{
			switch (type)
			{
				case BuildingKinds.LordsHall: return new Color(0.9f, 0.8f, 0.45f);
				case "warehouse": case "granary": case "well": case "cranny": case "marketplace": case "embassy": case "tavern": case "town-hall": case "treasury": case "castle":
					return new Color(0.86f, 0.78f, 0.62f);
				case "sawmill": case "stonecutter": case "foundry": case "pottery": case "mill": case "bakery":
					return new Color(0.7f, 0.58f, 0.4f);
				default: return new Color(0.66f, 0.6f, 0.6f);
			}
		}

		void BuildBuildings(VillageData data)
		{
			var roof = Mat(new Color(0.55f, 0.22f, 0.18f));
			var wood = Mat(new Color(0.5f, 0.36f, 0.2f));
			foreach (var building in data.Buildings)
			{
				var (x, z) = VillageLayout.PositionOf(data, building.Slot);
				var level = Mathf.Max(1, Mathf.Max(building.Level, building.Target));
				var width = BuildingKinds.Width(building.Type, level);
				var depth = BuildingKinds.Depth(building.Type, level);
				var height = BuildingKinds.Height(building.Type, level);

				var root = new GameObject("Building_" + building.Slot + "_" + building.Type);
				root.transform.SetParent(_root, false);
				root.transform.localPosition = new Vector3(x, 0f, z);
				var box = root.AddComponent<BoxCollider>();
				box.size = new Vector3(width, height, depth);
				box.center = new Vector3(0f, height / 2f, 0f);

				var visual = new GameObject("Visual").transform;
				visual.SetParent(root.transform, false);
				if (building.Level > 0)
				{
					var bodyHeight = BuildingKinds.Height(building.Type, building.Level);
					var bodyWidth = BuildingKinds.Width(building.Type, building.Level);
					var bodyDepth = BuildingKinds.Depth(building.Type, building.Level);
					Prim(PrimitiveType.Cube, "Walls", visual, new Vector3(0f, bodyHeight / 2f, 0f), new Vector3(bodyWidth, bodyHeight, bodyDepth), Mat(BodyColour(building.Type)));
					Prim(PrimitiveType.Cube, "Roof", visual, new Vector3(0f, bodyHeight + 0.15f, 0f), new Vector3(bodyWidth + 0.5f, 0.3f, bodyDepth + 0.5f), roof);
				}
				else
				{
					// Not built yet: only the foundation.
					Prim(PrimitiveType.Cube, "Foundation", visual, new Vector3(0f, 0.15f, 0f), new Vector3(width, 0.3f, depth), Mat(new Color(0.55f, 0.53f, 0.5f)));
				}
				if (building.Working) BuildScaffold(visual, width, depth, height, wood);

				var title = BuildingKinds.Name(building.Type);
				var line = building.Working
					? (building.Level == 0 ? "Building to level " + building.Target : "Level " + building.Level + " > " + building.Target)
					: "Level " + building.Level;
				Label(root.transform, "Label", title + "\n" + line, new Vector3(0f, height + 3f, 0f), 0.2f);
				_buildings[building.Slot] = root.transform;
			}
		}

		void BuildScaffold(Transform parent, float width, float depth, float height, Material wood)
		{
			var top = height + 0.9f;
			var hw = width / 2f + 0.3f;
			var hd = depth / 2f + 0.3f;
			foreach (var corner in new[] { new Vector2(-hw, -hd), new Vector2(hw, -hd), new Vector2(-hw, hd), new Vector2(hw, hd) })
				Prim(PrimitiveType.Cube, "Pole", parent, new Vector3(corner.x, top / 2f, corner.y), new Vector3(0.18f, top, 0.18f), wood);
			foreach (var y in new[] { top * 0.5f, top })
			{
				Prim(PrimitiveType.Cube, "BeamX1", parent, new Vector3(0f, y, -hd), new Vector3(hw * 2f, 0.14f, 0.14f), wood);
				Prim(PrimitiveType.Cube, "BeamX2", parent, new Vector3(0f, y, hd), new Vector3(hw * 2f, 0.14f, 0.14f), wood);
				Prim(PrimitiveType.Cube, "BeamZ1", parent, new Vector3(-hw, y, 0f), new Vector3(0.14f, 0.14f, hd * 2f), wood);
				Prim(PrimitiveType.Cube, "BeamZ2", parent, new Vector3(hw, y, 0f), new Vector3(0.14f, 0.14f, hd * 2f), wood);
			}
		}

		// ---------------------------------------------------------------- wall

		void BuildWall(VillageData data)
		{
			if (data.WallLevel <= 0 && !data.WallWorking) return;
			var stone = Mat(new Color(0.62f, 0.6f, 0.56f));
			var wood = Mat(new Color(0.5f, 0.36f, 0.2f));
			var wall = new GameObject("Wall").transform;
			wall.SetParent(_root, false);
			const int segments = 64;
			var height = Mathf.Min(6f, 3f + 0.4f * Mathf.Max(1, data.WallLevel));
			for (var i = 0; i < segments; i++)
			{
				var t0 = 2f * Mathf.PI * i / segments;
				var t1 = 2f * Mathf.PI * (i + 1) / segments;
				var mid = (t0 + t1) / 2f;
				// Four gates, one on each side, wide enough for a player.
				if (Gate(mid)) continue;
				var a = new Vector3(data.VillageRx * Mathf.Cos(t0), 0f, data.VillageRz * Mathf.Sin(t0));
				var b = new Vector3(data.VillageRx * Mathf.Cos(t1), 0f, data.VillageRz * Mathf.Sin(t1));
				var centre = (a + b) / 2f;
				var piece = Prim(data.WallLevel > 0 ? PrimitiveType.Cube : PrimitiveType.Cylinder, "Segment_" + i, wall, centre + Vector3.up * (height / 2f),
					data.WallLevel > 0 ? new Vector3(1.4f, height, (b - a).magnitude * 1.04f) : new Vector3(0.6f, height / 2f, 0.6f), data.WallLevel > 0 ? stone : wood, data.WallLevel > 0);
				if (data.WallLevel > 0) piece.transform.localRotation = Quaternion.LookRotation(b - a);
			}
			Label(wall, "Label", "Wall\n" + (data.WallWorking ? (data.WallLevel == 0 ? "Building" : "Level " + data.WallLevel + " (upgrading)") : "Level " + data.WallLevel),
				new Vector3(0f, height + 1.2f, -data.VillageRz), 0.1f);
		}

		static bool Gate(float angle)
		{
			for (var k = 0; k < 4; k++)
			{
				var d = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, k * 90f));
				if (d < 9f) return true;
			}
			return false;
		}

		// ---------------------------------------------------------------- farm

		void BuildPlots(VillageData data)
		{
			foreach (var plot in data.Plots)
			{
				var colour = Res.ColorOf(plot.Resource);
				var root = new GameObject("Plot_" + plot.Id + "_" + plot.Resource);
				root.transform.SetParent(_root, false);
				root.transform.localPosition = new Vector3(plot.X, 0f, plot.Z);
				root.transform.localScale = Vector3.one * PlotScale;
				// A trigger box: nothing blocks the player, but the plot has a body a game can measure its distance to.
				var box = root.AddComponent<BoxCollider>();
				box.isTrigger = true;
				box.size = new Vector3(6.4f, 2f, 6.4f);
				box.center = new Vector3(0f, 1f, 0f);

				Prim(PrimitiveType.Cube, "Patch", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(6f, 0.16f, 6f), Mat(Color.Lerp(colour, new Color(0.3f, 0.22f, 0.12f), 0.35f)));
				var visual = new GameObject("Visual").transform;
				visual.SetParent(root.transform, false);
				Decorate(plot.Resource, visual, colour);

				var markers = Mathf.Min(plot.Level, 10);
				for (var i = 0; i < markers; i++)
					Prim(PrimitiveType.Cube, "Level" + (i + 1), root.transform, new Vector3(-2.7f + i * 0.54f, 0.45f, -2.75f), new Vector3(0.36f, 0.6f, 0.2f), Mat(new Color(1f, 0.92f, 0.45f)));
				var level = plot.Working ? "Level " + plot.Level + " > " + plot.Target : "Level " + plot.Level;
				Label(root.transform, "Label", Res.Name(plot.Resource) + "\n" + level, new Vector3(0f, 4.6f, 0f), 0.1f);

				_plots.Add(new PlotHandle { Id = plot.Id, Data = plot, Root = root.transform, Visual = visual });
			}
		}

		void Decorate(string resource, Transform visual, Color colour)
		{
			switch (resource)
			{
				case Res.Wood:
					var trunk = Mat(new Color(0.4f, 0.27f, 0.16f));
					var leaves = Mat(colour);
					foreach (var p in new[] { new Vector2(-1.6f, -0.8f), new Vector2(1.5f, -1f), new Vector2(0f, 1.4f) })
					{
						Prim(PrimitiveType.Cylinder, "Trunk", visual, new Vector3(p.x, 1.3f, p.y), new Vector3(0.5f, 1.3f, 0.5f), trunk);
						Prim(PrimitiveType.Sphere, "Crown", visual, new Vector3(p.x, 3.1f, p.y), new Vector3(2.4f, 2.2f, 2.4f), leaves);
					}
					break;
				case Res.Stone:
					var rock = Mat(Color.Lerp(colour, Color.white, 0.3f));
					Prim(PrimitiveType.Sphere, "Boulder", visual, new Vector3(-0.8f, 0.8f, 0f), new Vector3(2.8f, 1.8f, 2.4f), rock);
					Prim(PrimitiveType.Sphere, "Boulder", visual, new Vector3(1.6f, 0.5f, 0.8f), new Vector3(1.6f, 1.1f, 1.4f), rock);
					Prim(PrimitiveType.Sphere, "Boulder", visual, new Vector3(0.6f, 0.4f, -1.5f), new Vector3(1.2f, 0.8f, 1f), rock);
					break;
				case Res.Iron:
					var ore = Mat(new Color(0.3f, 0.34f, 0.44f));
					var vein = Mat(new Color(0.85f, 0.45f, 0.2f));
					Prim(PrimitiveType.Cube, "Ore", visual, new Vector3(-0.6f, 0.9f, 0f), new Vector3(2f, 1.8f, 2f), ore).transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
					Prim(PrimitiveType.Cube, "Ore", visual, new Vector3(1.6f, 0.6f, 0.9f), new Vector3(1.2f, 1.2f, 1.2f), ore).transform.localRotation = Quaternion.Euler(0f, 50f, 0f);
					Prim(PrimitiveType.Cube, "Vein", visual, new Vector3(-0.2f, 1.3f, -0.3f), new Vector3(0.5f, 0.8f, 0.5f), vein);
					break;
				case Res.Water:
					Prim(PrimitiveType.Cylinder, "Rim", visual, new Vector3(0f, 0.2f, 0f), new Vector3(5f, 0.05f, 5f), Mat(new Color(0.45f, 0.42f, 0.38f)));
					Prim(PrimitiveType.Cylinder, "Water", visual, new Vector3(0f, 0.28f, 0f), new Vector3(4.2f, 0.05f, 4.2f), Mat(Color.Lerp(colour, Color.white, 0.2f)));
					break;
				case Res.Grain:
					var stalk = Mat(Color.Lerp(colour, Color.white, 0.2f));
					for (var x = -2; x <= 2; x++)
						for (var z = -2; z <= 2; z++)
							Prim(PrimitiveType.Cube, "Stalk", visual, new Vector3(x * 1.1f, 0.6f, z * 1.1f), new Vector3(0.22f, 1f, 0.22f), stalk);
					break;
				default: // clay
					Prim(PrimitiveType.Cylinder, "Pit", visual, new Vector3(0f, 0.2f, 0f), new Vector3(3f, 0.04f, 3f), Mat(new Color(0.36f, 0.2f, 0.12f)));
					var mound = Mat(Color.Lerp(colour, Color.white, 0.15f));
					for (var k = 0; k < 6; k++)
					{
						var angle = k * Mathf.PI / 3f;
						Prim(PrimitiveType.Sphere, "Mound", visual, new Vector3(Mathf.Cos(angle) * 1.8f, 0.4f, Mathf.Sin(angle) * 1.8f), new Vector3(1.3f, 0.8f, 1.3f), mound);
					}
					break;
			}
		}

		// ---------------------------------------------------------------- scenery from the seed

		void BuildScenery(VillageData data)
		{
			var scenery = new GameObject("Scenery").transform;
			scenery.SetParent(_root, false);
			var trunk = Mat(new Color(0.4f, 0.27f, 0.16f));
			var leaves = Mat(new Color(0.2f, 0.4f, 0.2f));
			var rock = Mat(new Color(0.55f, 0.55f, 0.58f));
			var random = new System.Random(unchecked((int)(long)data.Seed));
			var placed = 0;
			for (var attempt = 0; attempt < 1800 && placed < 150; attempt++)
			{
				var x = (float)(random.NextDouble() * 2 - 1) * (data.FarmRx + 60f);
				var z = (float)(random.NextDouble() * 2 - 1) * (data.FarmRz + 60f);
				var kind = random.Next(3);
				// Keep the village, the plots and the roads out of the way.
				var vx = x / (data.VillageRx + 3f);
				var vz = z / (data.VillageRz + 3f);
				if (vx * vx + vz * vz < 1f) continue;
				var blocked = false;
				foreach (var plot in data.Plots)
				{
					if ((plot.X - x) * (plot.X - x) + (plot.Z - z) * (plot.Z - z) < 225f) { blocked = true; break; }
				}
				if (blocked) continue;
				placed++;
				if (kind == 0)
				{
					Prim(PrimitiveType.Sphere, "Rock", scenery, new Vector3(x, 0.7f, z), new Vector3(2.8f, 1.5f, 2.3f), rock);
				}
				else
				{
					var size = 1.6f + (float)random.NextDouble() * 1.0f;
					Prim(PrimitiveType.Cylinder, "Trunk", scenery, new Vector3(x, 1f * size, z), new Vector3(0.4f * size, 1f * size, 0.4f * size), trunk);
					Prim(PrimitiveType.Sphere, "Crown", scenery, new Vector3(x, 2.8f * size, z), new Vector3(2.2f * size, 2.2f * size, 2.2f * size), leaves);
				}
			}
		}

		// ---------------------------------------------------------------- helpers

		GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool keepCollider = false)
		{
			var primitive = GameObject.CreatePrimitive(type);
			primitive.name = name;
			primitive.transform.SetParent(parent, false);
			primitive.transform.localPosition = position;
			primitive.transform.localScale = scale;
			primitive.GetComponent<Renderer>().sharedMaterial = material;
			if (!keepCollider) Kill(primitive.GetComponent<Collider>());
			return primitive;
		}

		Material Mat(Color colour)
		{
			var key = (Color32)colour;
			if (_materials.TryGetValue(key, out var material) && material != null) return material;
			if (LitTemplate != null) material = new Material(LitTemplate);
			else
			{
				var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
				material = new Material(shader);
			}
			material.name = "Village " + key.r + "," + key.g + "," + key.b;
			if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
			material.color = colour;
			if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
			if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
			_materials[key] = material;
			return material;
		}

		Material TextMat()
		{
			if (TextMaterial != null) return TextMaterial;
			if (_text != null) return _text;
			if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			var shader = Shader.Find("Temnorid/WorldText");
			if (shader == null) return _font.material;
			_text = new Material(shader) { mainTexture = _font.material.mainTexture };
			return _text;
		}

		void Label(Transform parent, string name, string text, Vector3 localPosition, float size)
		{
			var go = new GameObject(name);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = localPosition;
			go.AddComponent<VillageLabel>();
			var mesh = go.AddComponent<TextMesh>();
			mesh.text = text;
			mesh.characterSize = size;
			mesh.fontSize = 64;
			mesh.anchor = TextAnchor.MiddleCenter;
			mesh.alignment = TextAlignment.Center;
			mesh.color = Color.white;
			if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			mesh.font = _font;
			go.GetComponent<MeshRenderer>().sharedMaterial = TextMat();
		}

		static void Kill(Object target)
		{
			if (target == null) return;
			if (Application.isPlaying) Object.Destroy(target);
			else Object.DestroyImmediate(target);
		}
	}
}
