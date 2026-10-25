using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimSiege
{
	public enum CampKit { Tribal, Medieval, Modern }

	// a siege camp drawn in game with KCSG's export tool (Vanilla Expanded Framework) and pasted here as rows
	// of symbols. we read it ourselves, KCSG never runs: on an enemy mortar it quietly pushes a free crewman
	// and a shell pile into BaseGen, and VEF stays optional. symbols are "Def", "Def_Stuff", "Def_Stuff_Rot"
	// or "Def_Rot", maybe ending "_Fuel100", "." is empty. KCSG writes the south edge first. the camp is
	// turned so its north edge faces the breach
	public class CampLayoutDef : Def
	{
		public CampKit kit;
		public List<string> layout = new List<string>();
		// KCSG's upper layers, each a grid like layout: things standing on the first one (MO cups and candles
		// on the tables, a bottle beside them on the next layer up)
		public List<List<string>> onTop = new List<List<string>>();
		// "Brazier:Campfire": a symbol starting with Brazier reads as Campfire when its mod (here Royalty) is off
		public List<string> fallbacks = new List<string>();
		// package ids the drawing is made of. one missing and the camp is never picked
		public List<string> requiredMods = new List<string>();
		// siege size this camp is drawn for, in threat points. a big camp with its turrets is a long build and
		// looks empty around a small host, a compact one can be capped for the big sieges
		public float minPoints = 0f;
		public float maxPoints = 999999f;

		public struct Piece
		{
			public int x, z;          // cell in the drawing, z = 0 on the south edge
			public ThingDef def, stuff;
			public Rot4 rot;
			public bool onTop;
		}

		private List<Piece> pieces;
		private int width, height;

		public int Width { get { if (pieces == null) Parse(); return width; } }
		public int Height { get { if (pieces == null) Parse(); return height; } }
		public List<Piece> Pieces { get { if (pieces == null) Parse(); return pieces; } }

		public bool Usable
		{
			get
			{
				foreach (string id in requiredMods)
					if (!ModsConfig.IsActive(id)) return false;
				return Pieces.Count > 0;
			}
		}

		// parsed on first use, every def is loaded by then. an unknown symbol is skipped quietly: a camp
		// missing a lamp is still a camp
		private void Parse()
		{
			pieces = new List<Piece>();
			height = layout.Count;
			width = 0;
			ParseLayer(layout, false);
			foreach (List<string> layer in onTop) ParseLayer(layer, true);
		}

		private void ParseLayer(List<string> rows, bool top)
		{
			for (int z = 0; z < rows.Count; z++)
			{
				string[] row = rows[z].Split(',');
				if (row.Length > width) width = row.Length;
				for (int x = 0; x < row.Length; x++)
				{
					string sym = StripFuel(row[x].Trim());
					if (sym.Length == 0 || sym == ".") continue;
					if (!TryRead(sym, out ThingDef def, out ThingDef stuff, out Rot4 rot) && !TryFallback(sym, out def, out stuff, out rot))
						continue;
					// KCSG puts whatever shares a cell in the next layer: a sandbag over a conduit is ground, not table dressing
					bool dressing = top && def.building?.isEdifice != true;
					pieces.Add(new Piece { x = x, z = z, def = def, stuff = stuff, rot = rot, onTop = dressing });
				}
			}
		}

		// KCSG tags refuelables "_Fuel100". we fill them anyway
		private static string StripFuel(string sym)
		{
			int i = sym.LastIndexOf("_Fuel");
			return i > 0 && int.TryParse(sym.Substring(i + 5), out _) ? sym.Substring(0, i) : sym;
		}

		private bool TryFallback(string sym, out ThingDef def, out ThingDef stuff, out Rot4 rot)
		{
			foreach (string f in fallbacks)
			{
				int colon = f.IndexOf(':');
				if (colon <= 0) continue;
				string from = f.Substring(0, colon), to = f.Substring(colon + 1);
				if (sym.StartsWith(from) && TryRead(to + sym.Substring(from.Length), out def, out stuff, out rot))
					return true;
			}
			def = stuff = null;
			rot = Rot4.North;
			return false;
		}

		// longest underscore prefix that names a ThingDef, the rest a stuff, then an optional rotation.
		// both carry underscores (DankPyon_CavalrySpike_DankPyon_RawWood = DankPyon_CavalrySpike + DankPyon_RawWood).
		// a stuff from a mod that isnt loaded (Campfire_DankPyon_RawWood without MO) keeps the thing, default stuff
		private static bool TryRead(string sym, out ThingDef def, out ThingDef stuff, out Rot4 rot)
		{
			def = stuff = null;
			rot = Rot4.North;
			string[] parts = sym.Split('_');
			int n = parts.Length;
			if (n > 1 && TryRot(parts[n - 1], out Rot4 r)) { rot = r; n--; }
			for (int k = n; k >= 1; k--)
			{
				ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(string.Join("_", parts, 0, k));
				if (d == null) continue;
				def = d;
				if (k < n)
				{
					ThingDef s = DefDatabase<ThingDef>.GetNamedSilentFail(string.Join("_", parts, k, n - k));
					if (s != null && s.IsStuff) stuff = s;
				}
				return true;
			}
			return false;
		}

		private static bool TryRot(string s, out Rot4 rot)
		{
			switch (s)
			{
				case "North": rot = Rot4.North; return true;
				case "East": rot = Rot4.East; return true;
				case "South": rot = Rot4.South; return true;
				case "West": rot = Rot4.West; return true;
				default: rot = Rot4.North; return false;
			}
		}

		public override IEnumerable<string> ConfigErrors()
		{
			foreach (string e in base.ConfigErrors()) yield return e;
			if (layout.NullOrEmpty()) yield return "empty layout";
		}

		// drawing north onto the camp's facing, a quarter turn per step clockwise
		public static IntVec3 Turn(IntVec3 v, Rot4 face)
		{
			switch (face.AsInt)
			{
				case 1: return new IntVec3(v.z, 0, -v.x);
				case 2: return new IntVec3(-v.x, 0, -v.z);
				case 3: return new IntVec3(-v.z, 0, v.x);
				default: return v;
			}
		}

		// cardinal step from a camp spot toward the breach: the drawing's north faces that way
		public static IntVec3 Toward(IntVec3 from, IntVec3 to)
		{
			IntVec3 d = to - from;
			return Math.Abs(d.x) >= Math.Abs(d.z) ? new IntVec3(d.x >= 0 ? 1 : -1, 0, 0) : new IntVec3(0, 0, d.z >= 0 ? 1 : -1);
		}

		// a cell a drawn piece may take: open ground, or what the camp clears away (plants, filth, rock chunks,
		// bare rock under open sky or a thin rock roof). never water, an ore vein, a thick mountain roof, or
		// a building someone owns: carving mountains could open a mountain base, and ore is the player's
		public static bool Clearable(IntVec3 c, Map map)
		{
			if (!c.InBounds(map)) return false;
			RoofDef roof = map.roofGrid.RoofAt(c);
			if (roof != null && roof.isThickRoof) return false;
			TerrainDef terrain = c.GetTerrain(map);
			if (terrain.IsWater || terrain.passability == Traversability.Impassable) return false;
			foreach (Thing t in c.GetThingList(map))
			{
				if (Junk(t)) continue;
				if (t.def.category == ThingCategory.Building || t.def.passability == Traversability.Impassable) return false;
			}
			return true;
		}

		private static bool Junk(Thing t) =>
			t is Plant || t is Filth || t.def.IsWithinCategory(ThingCategoryDefOf.Chunks)
			|| (t.Faction == null && t.def.building != null && t.def.building.isNaturalRock && !t.def.building.isResourceRock);

		// clear one cell for the camp: junk gone, a thin natural rock roof lifted so the guns see the sky
		public static void Clear(IntVec3 c, Map map)
		{
			List<Thing> things = c.GetThingList(map);
			for (int i = things.Count - 1; i >= 0; i--)
				if (Junk(things[i])) things[i].Destroy(DestroyMode.Vanish);
			RoofDef roof = map.roofGrid.RoofAt(c);
			if (roof != null && roof.isNatural && !roof.isThickRoof) map.roofGrid.SetRoof(c, null);
		}

		// how many ground pieces land on usable ground with the camp centred here
		public int Fit(Map map, IntVec3 center, IntVec3 breach)
		{
			Rot4 face = Rot4.FromIntVec3(Toward(center, breach));
			int cx = (Width - 1) / 2, cz = (Height - 1) / 2, n = 0;
			foreach (Piece p in Pieces)
				if (!p.onTop && Clearable(center + Turn(new IntVec3(p.x - cx, 0, p.z - cz), face), map)) n++;
			return n;
		}

		// how many of this gun the drawing has room for (ground layer only)
		public int MarksFor(ThingDef def)
		{
			if (def == null) return 0;
			int n = 0;
			foreach (Piece p in Pieces)
				if (!p.onTop && p.def == def) n++;
			return n;
		}

		public int GroundCount
		{
			get
			{
				int n = 0;
				foreach (Piece p in Pieces) if (!p.onTop) n++;
				return n;
			}
		}

		public static List<CampLayoutDef> AllFor(CampKit kit, float points)
		{
			var options = new List<CampLayoutDef>();
			foreach (CampLayoutDef d in DefDatabase<CampLayoutDef>.AllDefsListForReading)
				if (d.kit == kit && points >= d.minPoints && points <= d.maxPoints && d.Usable) options.Add(d);
			return options;
		}
	}
}
