using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.AI
{
	// build phase, same idea as vanilla LordToil_Siege: blueprints + pod drop + builders, then "EnginesBuilt"
	public class LordToil_BuildSiege : LordToil
	{
		private IntVec3 center;
		private IntVec3 breach;
		private const float Radius = 24f;
		private const int StartBuildingDelay = 300;   // let the pods land
		private const int MaxBuildTicks = 9000;
		private const int CampMusterTicks = 2500;     // no-engine siege camps this long then charges
		private const float BuilderFraction = 0.45f;

		private bool placed;
		private bool campDone;
		private ThingDef engineDef;       // ram, tower, or null
		private int trebuchetCount;
		private int ballistaCount;
		private bool tarAmmo;
		private bool carrionAmmo;
		private const int BuildStallTicks = 5000;
		private float lastWorkSignature = -1f;
		private int lastWorkChangeTick = -1;
		private int bombardStartTick = -1;   // transient: a reload mid-bombardment restarts the clock
		private bool reachableAtBombardStart;
		private IntVec3 bombardSealed = IntVec3.Invalid;
		private readonly List<Pawn> gunners = new List<Pawn>(); // artillery crews, manned as soon as built

		// drawn camp (CampLayoutDef) when the kit has one: its artillery marks are where the battery goes,
		// its turrets and generators are worksites, the rest stands from the start. transient like the
		// rest of the placement, Init only runs once
		private struct Spot { public IntVec3 cell; public Rot4 rot; public ThingDef def; }
		private readonly List<Spot> artySpots = new List<Spot>();
		private bool drawnCamp;
		private int campWorks;
		private int campReach;   // how far the drawing reaches from the centre, the engine goes past it
		private const int MaxReach = 18; // + the 4 cell search stays inside the builders' 24 (RimSiege_BuildCamp)
		public Pawn captain;

		public LordToil_BuildSiege() { }

		public LordToil_BuildSiege(IntVec3 center, IntVec3 breach, ThingDef engineDef, int trebuchetCount, int ballistaCount, bool tarAmmo, bool carrionAmmo)
		{
			this.center = center;
			this.breach = breach;
			this.engineDef = engineDef;
			this.trebuchetCount = trebuchetCount;
			this.ballistaCount = ballistaCount;
			this.tarAmmo = tarAmmo;
			this.carrionAmmo = carrionAmmo;
		}

		private LordJob_MedievalSiege Job => lord.LordJob as LordJob_MedievalSiege;

		public override IntVec3 FlagLoc => center;
		public override bool ForceHighStoryDanger => true;
		public override bool AllowSatisfyLongNeeds => true;

		public override void Init()
		{
			base.Init();
			if (placed) return;
			placed = true;

			// picked at launch so the battery matches the drawing and the drawing fits the ground. null = classic camp
			CampLayoutDef drawn = Job?.camp;
			// a drawn camp reaches its corners: trample out to there or trees cost it its corner pieces
			ClearCampArea(drawn == null ? CampClearRadius
				: Mathf.Max(CampClearRadius, Mathf.Sqrt(drawn.Width * drawn.Width + drawn.Height * drawn.Height) / 2f + 1f));

			var resources = new Dictionary<ThingDef, int>();
			if (drawn != null) PlaceDrawnCamp(drawn, resources);
			if (engineDef != null)
				PlaceEngine(engineDef, null, resources);

			if (engineDef != null && trebuchetCount > 0 && SiegeLauncher.Modern(lord.faction))
				PlaceMortars(resources);
			else if (engineDef != null && trebuchetCount > 0)
			{
				ThingDef trebDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Trebuchet");
				ThingDef boulderAmmo = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_StoneBoulder");
				ThingDef tarredAmmo = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_StoneBoulderTarred");
				if (trebDef != null)
				{
					for (int i = 0; i < trebuchetCount; i++)
						PlaceArtillery(trebDef, trebDef.MadeFromStuff ? GenStuff.DefaultStuffFor(trebDef) : null, resources);

					// rich sieges tar a third of the pile: fire rains before stone
					int tarred = tarAmmo && tarredAmmo != null ? 8 * trebuchetCount : 0;
					if (boulderAmmo != null)
						resources[boulderAmmo] = (resources.TryGetValue(boulderAmmo, out int bv) ? bv : 0) + 25 * trebuchetCount - tarred;
					if (tarred > 0)
						resources[tarredAmmo] = (resources.TryGetValue(tarredAmmo, out int tv) ? tv : 0) + tarred;
				}
			}

			// camp guard artillery, up first so a sally rides into bolts
			if (engineDef != null && ballistaCount > 0)
			{
				ThingDef ballistaDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Ballista");
				ThingDef boltAmmo = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_BallistaBolt");
				if (ballistaDef != null)
				{
					for (int i = 0; i < ballistaCount; i++)
						PlaceArtillery(ballistaDef, ballistaDef.MadeFromStuff ? GenStuff.DefaultStuffFor(ballistaDef) : null, resources);

					if (boltAmmo != null)
						resources[boltAmmo] = (resources.TryGetValue(boltAmmo, out int av) ? av : 0) + 20 * ballistaCount;
				}
			}

			DropResources(resources);
			if (!drawnCamp) SetUpCamp();

			// the grim pile next to the boulders. loader feeds them to the sling once they stink
			if (carrionAmmo && trebuchetCount > 0)
				for (int i = 0; i < 3 * trebuchetCount; i++)
					SiegeLauncher.SpawnCarrion(CellFinder.RandomClosewalkCellNear(center, Map, 4), Map);
		}

		// gun era battery: mortars dug in behind sandbags, shells, smoke for the cover salvos, a spare
		// barrel each (the built one comes with a fresh barrel, 20 shots)
		private void PlaceMortars(Dictionary<ThingDef, int> resources)
		{
			ThingDef mortar = ThingDefOf.Turret_Mortar;
			ThingDef stuff = mortar.MadeFromStuff ? GenStuff.DefaultStuffFor(mortar) : null;
			for (int i = 0; i < trebuchetCount; i++)
			{
				if (TakeSpot(mortar, stuff, resources)) continue; // the drawing has its own sandbags
				IntVec3 spot = PlaceEngine(mortar, stuff, resources);
				if (spot.IsValid) DigIn(mortar, spot);
			}

			ThingDef fire = DefDatabase<ThingDef>.GetNamedSilentFail("Shell_Incendiary");
			ThingDef smoke = DefDatabase<ThingDef>.GetNamedSilentFail("Shell_Smoke");
			int incendiary = tarAmmo && fire != null ? 8 * trebuchetCount : 0;
			AddResource(resources, ThingDefOf.Shell_HighExplosive, 25 * trebuchetCount - incendiary);
			if (incendiary > 0) AddResource(resources, fire, incendiary);
			if (smoke != null) AddResource(resources, smoke, 4 * trebuchetCount);
			if (!Find.Storyteller.difficulty.classicMortars) AddResource(resources, ThingDefOf.ReinforcedBarrel, trebuchetCount);
		}

		private void PlaceArtillery(ThingDef def, ThingDef stuff, Dictionary<ThingDef, int> resources)
		{
			if (!TakeSpot(def, stuff, resources)) PlaceEngine(def, stuff, resources);
		}

		// next free artillery mark of the drawing for this gun. marks past the battery's size stay empty
		private bool TakeSpot(ThingDef def, ThingDef stuff, Dictionary<ThingDef, int> resources)
		{
			for (int i = 0; i < artySpots.Count; i++)
			{
				if (artySpots[i].def != def) continue;
				Spot s = artySpots[i];
				artySpots.RemoveAt(i);
				if (PlaceAt(def, stuff, s.cell, s.rot, resources)) return true;
				i--;
			}
			return false;
		}

		private bool PlaceAt(ThingDef def, ThingDef stuff, IntVec3 cell, Rot4 rot, Dictionary<ThingDef, int> resources)
		{
			stuff = StuffFor(def, stuff);
			if (!cell.InBounds(Map)) return false;
			AcceptanceReport ok = GenConstruct.CanPlaceBlueprintAt(def, cell, rot, Map, stuffDef: stuff,
				ignoreEdgeArea: true, ignoreInteractionSpots: true, ignoreClearableFreeBuildings: true);
			if (!ok.Accepted)
			{
				if (Prefs.DevMode) Log.Message("[RimSiege] camp worksite " + def.defName + " at " + cell + " refused: " + ok.Reason);
				return false;
			}
			Blueprint_Build bp = GenConstruct.PlaceBlueprintForBuild(def, cell, Map, rot, lord.faction, stuff);
			foreach (ThingDefCountClass cost in bp.TotalMaterialCost())
				AddResource(resources, cost.thingDef, cost.count);
			return true;
		}

		// the drawn camp, turned so its north edge faces the breach and centred on the muster point
		private void PlaceDrawnCamp(CampLayoutDef camp, Dictionary<ThingDef, int> resources)
		{
			drawnCamp = true;
			Rot4 face = Rot4.FromIntVec3(Forward);
			int cx = (camp.Width - 1) / 2, cz = (camp.Height - 1) / 2;
			campReach = Mathf.Max(camp.Width, camp.Height) / 2;
			ThingDef arty = SiegeLauncher.ArtilleryDefFor(lord.faction);
			ThingDef ballista = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Ballista");
			var deco = new List<Spot>();
			var decoStuff = new List<ThingDef>();
			var top = new List<Spot>();
			var topStuff = new List<ThingDef>();
			ThingDef houseBanner = SiegeLauncher.HouseBanner(lord.faction);

			foreach (CampLayoutDef.Piece drawnPiece in camp.Pieces)
			{
				CampLayoutDef.Piece p = drawnPiece;
				if (p.def.category != ThingCategory.Building) continue;
				// a plain MO banner in the drawing flies the besieging house's colours
				if (houseBanner != null && p.def.defName.StartsWith("DankPyon_Banner")) p.def = houseBanner;
				// a def that cant turn keeps facing north, only its spot moves
				Rot4 drawnRot = p.def.rotatable ? p.rot : Rot4.North;
				Rot4 rot = p.def.rotatable ? new Rot4((p.rot.AsInt + face.AsInt) % 4) : Rot4.North;
				IntVec3 local = new IntVec3(p.x - cx, 0, p.z - cz);
				IntVec3 cell = center + Turn(local, face);
				if (p.def.size.x != 1 || p.def.size.z != 1) cell = RootAfterTurn(p.def, local, drawnRot, rot, face, cell);
				if (!cell.InBounds(Map)) continue;
				// make room: debris, chunks, bare rock go. a footprint on water, ore or under a mountain is dropped
				if (!p.onTop)
				{
					CellRect rect = GenAdj.OccupiedRect(cell, rot, p.def.size);
					bool fits = true;
					foreach (IntVec3 c in rect) if (!CampLayoutDef.Clearable(c, Map)) { fits = false; break; }
					if (!fits) continue;
					foreach (IntVec3 c in rect) CampLayoutDef.Clear(c, Map);
				}

				if (p.def == arty || (ballista != null && p.def == ballista))
					artySpots.Add(new Spot { cell = cell, rot = rot, def = p.def });
				else if (p.onTop)
				{
					top.Add(new Spot { cell = cell, rot = rot, def = p.def });
					topStuff.Add(p.stuff);
				}
				else if (IsCampWork(p.def))
				{
					if (PlaceAt(p.def, p.stuff, cell, rot, resources))
					{
						campWorks++;
						FuelFor(p.def, resources);
					}
				}
				else
				{
					deco.Add(new Spot { cell = cell, rot = rot, def = p.def });
					decoStuff.Add(p.stuff);
				}
			}
			// worksites first so a sandbag never takes a turret's cell, then the deco, wall torches after the
			// palisades they hang on (GenConstruct.GetWallAttachedTo), then what stands on the tables
			for (int pass = 0; pass < 2; pass++)
				for (int i = 0; i < deco.Count; i++)
					if ((deco[i].def.building?.isAttachment == true) == (pass == 1))
						TrySpawnDeco(deco[i].def, deco[i].cell, decoStuff[i], deco[i].rot);
			for (int i = 0; i < top.Count; i++)
				TrySpawnOnTop(top[i].def, top[i].cell, topStuff[i], top[i].rot);
		}

		// table dressing: lands on the table itself, it only needs the cell and nothing else of its kind there
		private void TrySpawnOnTop(ThingDef def, IntVec3 c, ThingDef stuff, Rot4 rot)
		{
			if (!c.InBounds(Map) || def.building?.isEdifice != false || c.GetFirstThing(Map, def) != null) return;
			stuff = StuffFor(def, stuff);
			Thing t = ThingMaker.MakeThing(def, stuff);
			GenSpawn.Spawn(t, c, Map, rot);
			if (t.def.CanHaveFaction) t.SetFaction(lord.faction);
			t.TryGetComp<CompRefuelable>()?.Refuel(999f); // candles arrive lit
		}

		// the drawing's stuff when the def takes it (a house banner swapped in may not), else its default
		private static ThingDef StuffFor(ThingDef def, ThingDef stuff)
		{
			if (!def.MadeFromStuff) return null;
			return stuff != null && GenStuff.AllowedStuffsFor(def).Contains(stuff) ? stuff : GenStuff.DefaultStuffFor(def);
		}

		// turrets and anything on the power grid get built by the host, like the battery
		private static bool IsCampWork(ThingDef def) =>
			def.building != null && (def.building.turretGunDef != null || def.GetCompProperties<CompProperties_Power>() != null);

		// whatever a camp worksite burns or shoots comes along in the pods: one fill for a generator, four for
		// a manned gun (a scorpio holds 5 bolts). builds that arrive full (mini-turrets) or run on mortar
		// barrels bring nothing
		private static void FuelFor(ThingDef def, Dictionary<ThingDef, int> resources)
		{
			CompProperties_Refuelable fuel = def.GetCompProperties<CompProperties_Refuelable>();
			if (fuel?.fuelFilter == null || fuel.fuelIsMortarBarrel || fuel.initialFuelPercent >= 1f) return;
			bool power = def.GetCompProperties<CompProperties_Power>()?.PowerConsumption < 0f;
			bool manned = def.GetCompProperties<CompProperties_Mannable>() != null;
			if (!power && !manned) return;
			ThingDef fuelDef = fuel.fuelFilter.AllowedThingDefs.FirstOrDefault();
			int fill = Mathf.CeilToInt(fuel.fuelCapacity / fuel.FuelMultiplierCurrentDifficulty);
			if (fuelDef != null) AddResource(resources, fuelDef, manned ? 4 * fill : fill);
		}

		// drawing north onto the camp's facing, a quarter turn per step clockwise
		private static IntVec3 Turn(IntVec3 v, Rot4 face) => CampLayoutDef.Turn(v, face);

		// a 2x2 mortar's root isnt its centre: turn its whole footprint and find the root that lands on it
		private static IntVec3 RootAfterTurn(ThingDef def, IntVec3 local, Rot4 drawnRot, Rot4 rot, Rot4 face, IntVec3 fallback)
		{
			CellRect src = GenAdj.OccupiedRect(local, drawnRot, def.size);
			int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
			foreach (IntVec3 c in src)
			{
				IntVec3 t = Turn(c, face);
				minX = Mathf.Min(minX, t.x); maxX = Mathf.Max(maxX, t.x);
				minZ = Mathf.Min(minZ, t.z); maxZ = Mathf.Max(maxZ, t.z);
			}
			IntVec3 baseCell = fallback - Turn(local, face);   // camp centre
			for (int dx = -1; dx <= 1; dx++)
				for (int dz = -1; dz <= 1; dz++)
				{
					IntVec3 root = Turn(local, face) + new IntVec3(dx, 0, dz);
					CellRect r = GenAdj.OccupiedRect(root, rot, def.size);
					if (r.minX == minX && r.maxX == maxX && r.minZ == minZ && r.maxZ == maxZ) return baseCell + root;
				}
			return fallback;
		}

		private static void AddResource(Dictionary<ThingDef, int> resources, ThingDef def, int count) =>
			resources[def] = (resources.TryGetValue(def, out int v) ? v : 0) + count;

		// sandbag pit: a U of bags on the colony side of the mortar, the crew's side stays open
		private void DigIn(ThingDef gun, IntVec3 root)
		{
			CellRect rect = GenAdj.OccupiedRect(root, Rot4.North, gun.Size);
			IntVec3 manCell = ThingUtility.InteractionCellWhenAt(gun, root, Rot4.North, Map);
			IntVec3 fwd = Forward;
			Vector3 mid = rect.CenterVector3;
			foreach (IntVec3 c in rect.ExpandedBy(1).EdgeCells)
			{
				if (c == manCell) continue;
				Vector3 rel = c.ToVector3Shifted() - mid;
				if (rel.x * fwd.x + rel.z * fwd.z < 0.4f) continue; // front and its corners only
				TrySpawnDeco(ThingDefOf.Sandbags, c);
			}
		}

		// cardinal step from the camp toward the breach
		private IntVec3 Forward => CampLayoutDef.Toward(center, breach);

		private const float CampClearRadius = 13f;

		// trample a circle: looks like a camp, and tree cells arent standable so dense forests
		// used to leave no room for the blueprints
		private void ClearCampArea(float radius)
		{
			foreach (IntVec3 c in GenRadial.RadialCellsAround(center, radius, useCenter: true))
			{
				if (!c.InBounds(Map)) continue;
				List<Thing> things = c.GetThingList(Map);
				for (int i = things.Count - 1; i >= 0; i--)
				{
					Thing t = things[i];
					if (t is Plant || t is Filth || t.def.IsWithinCategory(ThingCategoryDefOf.Chunks)) t.Destroy(DestroyMode.Vanish);
				}
			}
		}

		// camp dressing. campfire + banner + torches at the heart, spikes on the flanks, some tents
		private void SetUpCamp()
		{
			IntVec3 fwd = Forward;
			IntVec3 perp = new IntVec3(fwd.z, 0, -fwd.x);

			ThingDef fire = DefDatabase<ThingDef>.GetNamedSilentFail("Campfire");
			if (fire != null && CellFinder.TryFindRandomCellNear(center, Map, 2, cc => FootprintClear(cc, fire), out IntVec3 fireSpot))
				TrySpawnDeco(fire, fireSpot);

			ThingDef banner = BannerFor();
			if (banner != null && CellFinder.TryFindRandomCellNear(center - fwd, Map, 2, cc => FootprintClear(cc, banner), out IntVec3 bannerSpot))
				TrySpawnDeco(banner, bannerSpot);

			ThingDef torch = DefDatabase<ThingDef>.GetNamedSilentFail("TorchLamp");
			if (torch != null)
				foreach (int side in new[] { 4, -4 })
					if (CellFinder.TryFindRandomCellNear(center + perp * side, Map, 2, cc => FootprintClear(cc, torch), out IntVec3 ts))
						TrySpawnDeco(torch, ts);

			ThingDef spikeDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CavalrySpike");
			if (spikeDef != null && SiegeLauncher.OldWorld(lord.faction))
			{
				int spikes = 0;
				foreach (IntVec3 c in GenRadial.RadialCellsAround(center, 6f, useCenter: false))
				{
					if (spikes >= 8) break;
					int distSq = (c - center).LengthHorizontalSquared;
					if (distSq < 20 || distSq > 36) continue;
					IntVec3 rel = c - center;
					if (rel.x * fwd.x + rel.z * fwd.z > 0) continue;  // front stays clear for the ram
					if (Rand.Value < 0.6f) continue;
					if (TrySpawnDeco(spikeDef, c)) spikes++;
				}
			}

			ThingDef tentDef = FirstExistingDef("VikingTent", "Tent", "ModernTent", "FoldingTent");
			if (tentDef != null)
			{
				int placed = 0;
				for (int i = 0; i < 40 && placed < 3; i++)
					if (CellFinder.TryFindRandomCellNear(center, Map, 5, cc => FootprintClear(cc, tentDef), out IntVec3 spot)
						&& TrySpawnDeco(tentDef, spot))
						placed++;
			}
		}

		private bool TrySpawnDeco(ThingDef def, IntVec3 c, ThingDef stuff = null, Rot4 rot = default)
		{
			if (!c.InBounds(Map) || !FootprintClear(c, def, rot)) return false;
			stuff = StuffFor(def, stuff);
			Thing t = ThingMaker.MakeThing(def, stuff);
			GenSpawn.Spawn(t, c, Map, rot);
			t.SetFaction(lord.faction);
			t.TryGetComp<CompRefuelable>()?.Refuel(999f); // fire and torches arrive lit
			return true;
		}

		// pick the banner matching the besieging house
		private ThingDef BannerFor() => SiegeLauncher.HouseBanner(lord.faction);

		private static ThingDef FirstExistingDef(params string[] names)
		{
			foreach (string n in names)
			{
				ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(n);
				if (def != null) return def;
			}
			return null;
		}

		private IntVec3 PlaceEngine(ThingDef def, ThingDef stuff, Dictionary<ThingDef, int> resources)
		{
			if (def == null) return IntVec3.Invalid;
			IntVec3 spot = drawnCamp && def == engineDef ? FindFrontSpot(def) : IntVec3.Invalid;
			if (!spot.IsValid) spot = FindBuildSpot(def);
			if (!spot.IsValid) return IntVec3.Invalid;

			Blueprint_Build bp = GenConstruct.PlaceBlueprintForBuild(def, spot, Map, Rot4.North, lord.faction, stuff);
			if (def == engineDef && Job != null) Job.engineSpot = spot;
			foreach (ThingDefCountClass cost in bp.TotalMaterialCost())
				resources[cost.thingDef] = (resources.TryGetValue(cost.thingDef, out int v) ? v : 0) + cost.count;
			return spot;
		}

		// drawn camp: pods land behind it. dropped in the middle they buried the battery's worksites under
		// wood and boulders, nothing got built there. walks in toward the centre if the map edge is close
		private IntVec3 RearDropSpot()
		{
			for (int d = Mathf.Min(campReach + 3, MaxReach); d >= campReach / 2; d -= 2)
			{
				IntVec3 rear = center - Forward * d;
				if (!rear.InBounds(Map)) continue;
				if (CellFinder.TryFindRandomCellNear(rear, Map, 4, c => c.Standable(Map) && !c.Roofed(Map)
						&& c.GetEdifice(Map) == null && !AnyWorksiteNear(c)
						&& Map.reachability.CanReach(c, center, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly),
						out IntVec3 spot))
					return spot;
			}
			return center;
		}

		// where the host waits and idle builders loiter. a pawn standing on a blueprint blocks it for good
		// (GenConstruct.BlocksConstruction, only the builder himself is ignored), and a drawn camp has its
		// battery right in the middle: 50 idle builders stood on the trebuchet marks and nothing got built.
		// so off the centre, behind the camp, as soon as a worksite sits there. the builders' work radius
		// stays on the flag (FlagLoc = center), only where they idle moves
		private IntVec3 muster = IntVec3.Invalid;

		private IntVec3 MusterSpot()
		{
			if (!AnyWorksiteNear(center, 6f)) return center;
			for (int d = 8; d <= MaxReach + 4; d += 2)
			{
				IntVec3 c = center - Forward * d;
				if (!c.InBounds(Map)) break;
				if (CellFinder.TryFindRandomCellNear(c, Map, 3, x => x.Standable(Map) && !AnyWorksiteNear(x, 6f)
						&& Map.reachability.CanReach(x, center, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly),
						out IntVec3 spot))
					return spot;
			}
			return center;
		}

		private bool AnyWorksiteNear(IntVec3 c, float radius = 3f)
		{
			foreach (IntVec3 n in GenRadial.RadialCellsAround(c, radius, useCenter: true))
			{
				if (!n.InBounds(Map)) continue;
				List<Thing> things = n.GetThingList(Map);
				for (int i = 0; i < things.Count; i++)
					if (things[i] is Blueprint || things[i] is Frame) return true;
			}
			return false;
		}

		// drawn camp: the engine is built just past its front edge, toward the breach, not in the middle of it
		private IntVec3 FindFrontSpot(ThingDef def)
		{
			IntVec3 front = center + Forward * Mathf.Min(campReach + 3, MaxReach);
			foreach (IntVec3 c in GenRadial.RadialCellsAround(front, 4f, useCenter: true))
			{
				if (!c.InBounds(Map) || c.Roofed(Map) || !FootprintClear(c, def)) continue;
				if (!Map.reachability.CanReach(c, center, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)) continue;
				return c;
			}
			return IntVec3.Invalid;
		}

		// a full siege train is 6 worksites, so widen the ring when the middle fills up.
		// giving up here means that machine silently never gets a blueprint
		private IntVec3 FindBuildSpot(ThingDef def)
		{
			foreach (int radius in new[] { 10, 14, 18 })
			{
				CellRect rect = CellRect.CenteredOn(center, radius);
				rect.ClipInsideMap(Map);
				for (int i = 0; i < 200; i++)
				{
					IntVec3 c = rect.RandomCell;
					if (!c.InHorDistOf(center, Radius - 1f)) continue; // square ring corners lay outside the builders' reach
					if (c.Roofed(Map)) continue;
					if (!Map.reachability.CanReach(c, center, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)) continue;
					if (!FootprintClear(c, def)) continue;
					return c;
				}
			}
			return IntVec3.Invalid;
		}

		private bool FootprintClear(IntVec3 root, ThingDef def, Rot4 rot = default)
		{
			foreach (IntVec3 c in GenAdj.OccupiedRect(root, rot, def.Size))
			{
				if (!c.InBounds(Map) || !c.Standable(Map)) return false;
				if (c.GetEdifice(Map) != null) return false;
				List<Thing> things = c.GetThingList(Map);
				// a conduit's blueprint may run under it (GenSpawn.SpawningWipes keeps it)
				for (int i = 0; i < things.Count; i++)
					if ((things[i] is Blueprint || things[i] is Frame) && (things[i].def.entityDefToBuild as ThingDef)?.building?.isEdifice != false) return false;
			}
			return true;
		}

		private void DropResources(Dictionary<ThingDef, int> resources)
		{
			var things = new List<Thing>();
			foreach (var kv in resources)
			{
				int remaining = Mathf.CeilToInt(kv.Value * 1.15f);
				while (remaining > 0)
				{
					Thing t = ThingMaker.MakeThing(kv.Key);
					t.stackCount = Mathf.Min(remaining, kv.Key.stackLimit);
					remaining -= t.stackCount;
					things.Add(t);
				}
			}

			int meals = Mathf.Max(4, Mathf.RoundToInt(2f * lord.ownedPawns.Count));
			while (meals > 0)
			{
				Thing m = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack);
				m.stackCount = Mathf.Min(meals, m.def.stackLimit);
				meals -= m.stackCount;
				things.Add(m);
			}

			if (things.Count == 0) return;

			var groups = new List<List<Thing>>();
			for (int i = 0; i < things.Count; i += 3)
				groups.Add(things.GetRange(i, Mathf.Min(3, things.Count - i)));
			DropPodUtility.DropThingGroupsNear(drawnCamp ? RearDropSpot() : center, Map, groups);
		}

		public override void Notify_PawnLost(Pawn victim, PawnLostCondition cond)
		{
			base.Notify_PawnLost(victim, cond);
			if (lord.ticksInToil >= StartBuildingDelay) UpdateAllDuties();
		}

		public override void UpdateAllDuties()
		{
			int guards = 0;
			bool captainUp = captain != null && captain.Spawned && !captain.Dead;
			muster = MusterSpot();

			if (lord.ticksInToil < StartBuildingDelay)
			{
				foreach (Pawn p in lord.ownedPawns)
				{
					if (captainUp && p != captain && guards < 2 && SiegeLauncher.CanCrew(p)) { AssignGuard(p); guards++; continue; }
					SetAsDefender(p);
				}
				return;
			}

			EnsureBallistaGunners();

			bool anythingToBuild = engineDef != null || trebuchetCount > 0 || campWorks > 0;
			int wanted = anythingToBuild ? Mathf.Max(1, Mathf.RoundToInt(lord.ownedPawns.Count * BuilderFraction)) : 0;
			int builders = 0;
			foreach (Pawn p in lord.ownedPawns)
			{
				if (gunners.Contains(p)) continue;
				if (captainUp && p != captain && guards < 2 && SiegeLauncher.CanCrew(p)) { AssignGuard(p); guards++; continue; }
				if (p != captain && builders < wanted && CanBuild(p)) { SetAsBuilder(p); builders++; }
				else SetAsDefender(p);
			}
		}

		// finished artillery gets a crewman right away, cheapest hands first. ballistas guard
		// the worksite, a done trebuchet opens fire while the rest is still going up
		private void EnsureBallistaGunners()
		{
			gunners.RemoveAll(g => g == null || g.Dead || g.Downed || !g.Spawned || !lord.ownedPawns.Contains(g));
			if (ballistaCount <= 0 && trebuchetCount <= 0 && !drawnCamp) { gunners.Clear(); return; }
			List<Building> guns = SiegeLauncher.SpawnedBallistas(Map, lord.faction);
			guns.AddRange(SiegeLauncher.SpawnedArtillery(Map, lord.faction));
			guns.AddRange(SiegeLauncher.SpawnedCampGuns(Map, lord.faction));
			if (guns.Count == 0) { gunners.Clear(); return; }

			while (gunners.Count < guns.Count)
			{
				// nobles cant hammer anyway, send them to the guns and keep the few
				// building-capable hands on the worksites. push phase re-picks by cost
				Pawn g = lord.ownedPawns
					.Where(p => p != captain && !p.Dead && !p.Downed && p.Spawned
						&& !gunners.Contains(p) && SiegeLauncher.CanCrew(p))
					.OrderBy(p => CanBuild(p) ? 1 : 0)
					.ThenBy(p => p.kindDef.combatPower)
					.FirstOrDefault();
				if (g == null) break;
				g.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_ManTrebuchet, guns[gunners.Count % guns.Count]);
				gunners.Add(g);
			}
		}

		// escort with radius 0 spams errors, keep the radius
		private void AssignGuard(Pawn p) =>
			p.mindState.duty = new PawnDuty(RimSiegeDefOf.Escort, captain) { radius = 5f };

		private static bool CanBuild(Pawn p) =>
			SiegeLauncher.CanCrew(p) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
			&& !p.Dead && !p.Downed;

		private void SetAsBuilder(Pawn p)
		{
			// own Build duty: vanilla only lets them work 16 cells from the flag, a big drawn camp reaches further
			p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_BuildCamp, muster) { radius = Radius };
			p.skills?.GetSkill(SkillDefOf.Construction)?.EnsureMinLevelWithMargin(6);
			p.workSettings?.EnableAndInitialize();
			foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
			{
				if (w == WorkTypeDefOf.Construction) p.workSettings?.SetPriority(w, 1);
				else p.workSettings?.Disable(w);
			}
		}

		private void SetAsDefender(Pawn p)
		{
			p.mindState.duty = new PawnDuty(DutyDefOf.Defend, muster) { radius = Radius };
		}

		public override void LordToilTick()
		{
			base.LordToilTick();

			if (lord.ticksInToil == StartBuildingDelay)
				UpdateAllDuties();
			else if (lord.ticksInToil > StartBuildingDelay && lord.ticksInToil % 500 == 0)
				UpdateAllDuties();

			if (Find.TickManager.TicksGame % 120 != 0) return;

			if (ballistaCount > 0) SiegeLauncher.RefuelBallistasNear(Map, lord.faction);
			if (trebuchetCount > 0) SiegeLauncher.LoadArtilleryFromPiles(Map, lord.faction);
			SiegeLauncher.RefuelCampWorks(Map, lord.faction);

			// no engine: camp a while, and until the drawing's turrets are up (capped), blow the horn, charge
			if (engineDef == null)
			{
				int camped = lord.ticksInToil - StartBuildingDelay;
				bool built = campWorks == 0 || !AnyEngineWorkLeft();
				if (!campDone && camped > CampMusterTicks && (built || camped > CampMusterTicks + 1200 * campWorks))
				{
					campDone = true;
					RimSiegeDefOf.RimSiege_WarHorn?.PlayOneShot(new TargetInfo(center, Map));
					lord.ReceiveMemo("BuildFailed");
				}
				return;
			}

			Building_SiegeEngine engine = OwnEngine();

			// a full late game train is 6 worksites, one pair of hands each: give it time
			int maxBuild = MaxBuildTicks + 3000 * (trebuchetCount + ballistaCount) + 1200 * campWorks;
			bool timeUp = lord.ticksInToil > StartBuildingDelay + maxBuild
				|| BuildStalled(Find.TickManager.TicksGame);
			bool workDone = !AnyEngineWorkLeft();

			if (engine != null && (workDone || timeUp))
			{
				int bombardTicks = (RimSiegeMod.S?.bombardHours ?? 0) * 2500;
				if (workDone) TryLateOffer(Mathf.Max(bombardTicks, 2500));

				// siege train complete: pound the walls a while before the push. timeUp with
				// unfinished work skips this, they attack with what they have
				if (workDone && trebuchetCount > 0 && bombardTicks > 0)
				{
					if (bombardStartTick < 0)
					{
						bombardStartTick = Find.TickManager.TicksGame;
						bombardSealed = SiegeLauncher.SealedCellBehind(Map, breach);
						reachableAtBombardStart = !bombardSealed.IsValid || CampReaches(bombardSealed);
						Messages.Message("RimSiege_BombardBegins".Translate(), new TargetInfo(center, Map),
							MessageTypeDefOf.ThreatSmall);
					}
					TryReinforce(bombardTicks);

					// the guns opened their own hole: skip the engine, charge the rubble. checked against the
					// sealed side of the breach, a colonist walking out to raid the camp isnt a hole
					if (!reachableAtBombardStart && CampReaches(bombardSealed))
					{
						RimSiegeDefOf.RimSiege_WarHorn?.PlayOneShot(new TargetInfo(center, Map));
						lord.ReceiveMemo("BuildFailed");
						return;
					}
					if (Find.TickManager.TicksGame - bombardStartTick < bombardTicks) return;
				}

				engine.AimAt(breach);
				lord.ReceiveMemo("EnginesBuilt");
				return;
			}

			// no engine and either nothing left to build or out of time (wood stolen, blueprint boxed in):
			// charge with what they have instead of camping forever
			if (lord.ticksInToil > StartBuildingDelay + 600 && engine == null && (timeUp || workDone))
				lord.ReceiveMemo("BuildFailed");
		}

		private bool CampReaches(IntVec3 c) =>
			c.IsValid && Map.reachability.CanReach(center, c, PathEndMode.OnCell, SiegeLauncher.NoDoors);

		// the engine built from this siege's own blueprint, never a leftover from an older siege
		private Building_SiegeEngine OwnEngine()
		{
			LordJob_MedievalSiege job = Job;
			if (job == null) return null;
			if (job.engine != null) return job.engine.Spawned ? job.engine : null;
			Building_SiegeEngine found;
			if (job.engineSpot.IsValid)
				found = job.engineSpot.GetFirstThing(Map, engineDef) as Building_SiegeEngine;
			else // 1.3.0 save caught mid build, no spot on record: the one standing in the camp
				found = Map.listerThings.ThingsOfDef(engineDef)
					.Where(t => t.Faction == lord.faction && (t.Position - center).LengthHorizontalSquared <= 400)
					.OrderBy(t => (t.Position - center).LengthHorizontalSquared)
					.FirstOrDefault() as Building_SiegeEngine;
			if (found == null || found.Faction != lord.faction) return null;
			job.engine = found;
			return found;
		}

		// siege moved on or died: cancel leftover construction like vanilla does, materials drop
		public override void Cleanup()
		{
			base.Cleanup();
			const float CleanupRadius = Radius; // the builders' reach, a drawn camp's far worksites included
			var leftovers = new List<Thing>();
			foreach (Thing t in Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
				if (t.Faction == lord.faction && t.Position.InHorDistOf(center, CleanupRadius)) leftovers.Add(t);
			foreach (Thing t in Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
				if (t.Faction == lord.faction && t.Position.InHorDistOf(center, CleanupRadius)) leftovers.Add(t);
			foreach (Thing t in leftovers)
				if (!t.Destroyed) t.Destroy(DestroyMode.Cancel);
		}

		// no hammer has moved in a while: builders dead, dragged into a fight by some other AI,
		// or a blueprint boxed in. stop waiting on it and get the siege moving with what stands
		// engines up: one last chance to buy them off, priced on what is still standing
		private void TryLateOffer(int window)
		{
			LordJob_MedievalSiege job = Job;
			if (job == null || job.lateOfferSent || job.supplied) return;
			job.lateOfferSent = true;
			GameComponent_SiegeDemands comp = GameComponent_SiegeDemands.Get();
			if (comp == null || !(RimSiegeMod.S?.ultimatumEnabled ?? true) || comp.HasGrudge(lord.faction)) return;
			float power = 0f;
			foreach (Pawn p in lord.ownedPawns) power += p.kindDef.combatPower;
			comp.OpenLateDemand(Map, lord.faction, lord, power, window);
		}

		// halfway through the bombardment a second column shows up
		private void TryReinforce(int bombardTicks)
		{
			LordJob_MedievalSiege job = Job;
			RimSiegeSettings s = RimSiegeMod.S;
			if (job == null || job.reinforced || job.supplied || s == null || !s.reinforcements) return;
			if (job.Points < s.reinforcePointsFloor) return;
			if (Find.TickManager.TicksGame - bombardStartTick < bombardTicks / 2) return;
			job.reinforced = true;
			SiegeLauncher.Reinforce(lord, center, job.Points * 0.35f * s.warbandFactor);
		}

		private bool BuildStalled(int now)
		{
			float sig = 0f;
			foreach (Thing t in Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
				if (t.Faction == lord.faction) sig += 1f;
			foreach (Thing t in Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
				if (t.Faction == lord.faction) sig += 1f + ((t as Frame)?.workDone ?? 0f);

			if (lastWorkChangeTick < 0 || Mathf.Abs(sig - lastWorkSignature) > 0.01f)
			{
				lastWorkSignature = sig;
				lastWorkChangeTick = now;
				return false;
			}
			return now - lastWorkChangeTick >= BuildStallTicks;
		}

		private bool AnyEngineWorkLeft()
		{
			bool anyBlueprint = Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
				.Any(t => t.Faction == lord.faction);
			bool anyFrame = Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)
				.Any(t => t.Faction == lord.faction);
			return anyBlueprint || anyFrame;
		}
	}
}
