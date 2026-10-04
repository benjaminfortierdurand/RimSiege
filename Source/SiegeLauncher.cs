using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using RimSiege.AI;

namespace RimSiege
{
	public static class SiegeLauncher
	{
		public static List<Pawn> GenerateWarband(Map map, Faction faction, float points)
		{
			if (map == null || faction == null) return new List<Pawn>();
			var parms = new PawnGroupMakerParms
			{
				groupKind = PawnGroupKindDefOf.Combat,
				tile = map.Tile,
				faction = faction,
				points = points,
				generateFightersOnly = true,
			};
			return PawnGroupMakerUtility.GeneratePawns(parms).ToList();
		}

		// engineDef null = no engine, they camp then charge on foot
		public static bool LaunchGroup(Map map, IntVec3 breach, ThingDef engineDef, int trebuchetCount, int ballistaCount,
			bool tarAmmo, bool carrionAmmo, float points, List<Pawn> group, Faction faction, out IntVec3 muster, out IntVec3 entry, out Pawn captain)
		{
			muster = breach;
			entry = IntVec3.Invalid;
			captain = null;
			if (map == null || faction == null || group == null || group.Count == 0 || !breach.IsValid) return false;

			if (!faction.HostileTo(Faction.OfPlayer))
				faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, canSendHostilityLetter: false);

			// vanilla order: entry cell first, camp spot from it. only extra: camp must reach the breach
			entry = FindEntryCell(map);
			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			muster = RCellFinder.FindSiegePositionFrom(entry, map, allowRoofed: false, errorOnFail: false,
				c => map.reachability.CanReach(c, breach, PathEndMode.Touch, tp));

			foreach (Pawn crew in group)
			{
				if (crew.Spawned) continue;
				GenSpawn.Spawn(crew, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
			}

			// beefiest kind in the group, not some peasant. war beasts have huge combatPower
			// and no hands, so they never lead the host
			captain = group.Where(p => !p.Dead && CanCrew(p))
				.OrderByDescending(p => p.kindDef.combatPower).FirstOrDefault();
			if (captain != null)
			{
				MakeCaptain(captain);
				GameComponent_SiegeDemands.Get()?.RegisterCaptain(captain, faction, map,
					group.Sum(p => p.kindDef.combatPower));
			}

			LordMaker.MakeNewLord(faction,
				new LordJob_MedievalSiege(faction, muster, breach, engineDef, trebuchetCount, ballistaCount, tarAmmo, carrionAmmo, points, captain), map, group);
			return true;
		}

		private static void MakeCaptain(Pawn p)
		{
			p.skills?.GetSkill(SkillDefOf.Shooting)?.EnsureMinLevelWithMargin(11);
			p.skills?.GetSkill(SkillDefOf.Melee)?.EnsureMinLevelWithMargin(11);

			// gold helm so the player can spot him
			ThingDef hat =
				DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Headgear_ArmetGilded")
				?? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Headgear_SalletGryphon")
				?? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Headgear_HeraldicGreatHelm1c")
				?? DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_TribalHeaddress");
			if (hat == null || p.apparel == null) return;

			ThingDef stuff = null;
			if (hat.MadeFromStuff)
				stuff = (ThingDefOf.Gold != null && GenStuff.AllowedStuffsFor(hat).Contains(ThingDefOf.Gold))
					? ThingDefOf.Gold
					: GenStuff.DefaultStuffFor(hat);

			Apparel a = (Apparel)ThingMaker.MakeThing(hat, stuff);
			p.apparel.Wear(a, dropReplacedApparel: false);
		}

		// defy / ultimatum timeout lands here. avenging set = vendetta assault, they skip the letter
		public static bool ExecuteSiege(Map map, Faction faction, float points, string avenging = null)
		{
			if (map == null || faction == null) return false;

			RimSiegeSettings s = RimSiegeMod.S;

			List<Pawn> group = GenerateWarband(map, faction, points * s.warbandFactor);
			if (group.NullOrEmpty()) return false;

			ThingDef engineDef = null;
			bool tower = false;
			if (Rand.Chance(Mathf.Clamp01((points - s.enginePointsFloor) / 1300f)))
			{
				tower = Rand.Bool;
				engineDef = tower ? RimSiegeDefOf.RimSiege_SiegeTower : RimSiegeDefOf.RimSiege_BatteringRam;
			}

			// engine rolled first, the tower prefers walls
			if (!TryFindSiegeTarget(map, preferWalls: tower, out IntVec3 breach))
			{
				foreach (Pawn p in group) if (!p.Destroyed) p.Destroy();
				return false;
			}

			int trebuchetCount = 0;
			int ballistaCount = 0;
			if (engineDef != null)
			{
				float tf = s.trebuchetPointsFloor;
				if (Rand.Chance(Mathf.Clamp01((points - tf) / 2000f))) trebuchetCount = 1;
				if (trebuchetCount == 1 && Rand.Chance(Mathf.Clamp01((points - (tf + 2000f)) / 2500f))) trebuchetCount = 2;
				if (trebuchetCount == 2 && Rand.Chance(Mathf.Clamp01((points - (tf + 4500f)) / 3000f))) trebuchetCount = 3;
				trebuchetCount = Mathf.Min(trebuchetCount, s.maxTrebuchets);

				float bf = s.ballistaPointsFloor;
				if (Rand.Chance(Mathf.Clamp01((points - bf) / 2500f))) ballistaCount = 1;
				if (ballistaCount == 1 && Rand.Chance(Mathf.Clamp01((points - (bf + 2500f)) / 3000f))) ballistaCount = 2;
				ballistaCount = Mathf.Min(ballistaCount, s.maxBallistas);
			}
			bool tarAmmo = trebuchetCount > 0 && points >= s.tarBoulderPointsFloor;
			bool carrionAmmo = s.carrionEnabled && trebuchetCount > 0 && points >= s.tarBoulderPointsFloor + 2000f;

			// the siege train brings its own crews, gunners dont come out of the fighting force
			int crews = trebuchetCount + ballistaCount;
			if (crews > 0)
			{
				PawnKindDef crewKind = CrewKindFor(faction);
				if (crewKind != null)
					for (int i = 0; i < crews; i++)
						group.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(
							crewKind, faction, PawnGenerationContext.NonPlayer,
							tile: map.Tile, forceGenerateNewPawn: true, mustBeCapableOfViolence: true,
							canGeneratePawnRelations: false)));
			}

			if (!LaunchGroup(map, breach, engineDef, trebuchetCount, ballistaCount, tarAmmo, carrionAmmo, points, group, faction, out IntVec3 muster, out IntVec3 entry, out Pawn captain))
				return false;

			int sappers = 0;
			if (Rand.Chance(Mathf.Clamp01((points - s.sapperPointsFloor) / 2300f)))
				sappers = Mathf.Min(points >= s.sapperPointsFloor + 1800f ? 2 : 1, s.maxSappers);
			if (sappers > 0)
			{
				PawnKindDef crewKind = CrewKindFor(faction);
				if (crewKind != null) TrySpawnSappers(map, faction, crewKind, breach, sappers, muster, entry);
			}

			SendSiegeBeginsLetter(faction, engineDef, tower, trebuchetCount, ballistaCount, sappers, muster, map, captain, avenging);
			return true;
		}

		public static PawnKindDef CrewKindFor(Faction f) => GetHumanlikeKind(f);

		// a second column marches in mid-siege and joins the camp
		public static int Reinforce(Lord lord, IntVec3 camp, float points)
		{
			Map map = lord?.Map;
			if (map == null || lord.faction == null) return 0;
			List<Pawn> group = GenerateWarband(map, lord.faction, points);
			if (group.NullOrEmpty()) return 0;

			IntVec3 entry = EntryCellFor(map, camp);
			foreach (Pawn p in group)
			{
				GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
				lord.AddPawn(p);
			}
			Find.LetterStack.ReceiveLetter(
				"RimSiege_ReinforceLabel".Translate(),
				"RimSiege_ReinforceText".Translate(group.Count, lord.faction.Name),
				LetterDefOf.ThreatSmall, new TargetInfo(entry, map), lord.faction);
			return group.Count;
		}

		// war animals come along with some factions. they fight, they dont push, build,
		// man engines, guard the captain, or get ransomed
		public static bool CanCrew(Pawn p) => p?.RaceProps?.Humanlike ?? false;

		private static void SendSiegeBeginsLetter(Faction faction, ThingDef engineDef, bool tower,
			int trebuchetCount, int ballistaCount, int sappers, IntVec3 muster, Map map, Pawn captain, string avenging = null)
		{
			string label, text;
			if (engineDef == null)
			{
				label = "RimSiege_LabelFootAssault".Translate();
				text = "RimSiege_TextFootAssault".Translate(faction.Name);
			}
			else if (tower)
			{
				label = "RimSiege_LabelTower".Translate();
				text = "RimSiege_TextTower".Translate(faction.Name);
			}
			else
			{
				label = "RimSiege_LabelRam".Translate();
				text = "RimSiege_TextRam".Translate(faction.Name);
			}

			if (trebuchetCount == 1) text += " " + "RimSiege_TrebOne".Translate();
			else if (trebuchetCount > 1) text += " " + "RimSiege_TrebMany".Translate(trebuchetCount);
			if (ballistaCount > 0) text += " " + "RimSiege_BallistaHint".Translate();
			if (sappers > 0) text += " " + "RimSiege_SappersHint".Translate();
			if (RimSiegeMod.S.captainRout && captain != null)
				text += " " + "RimSiege_CaptainHint".Translate(captain.Name?.ToStringFull ?? captain.LabelShort);
			if (!avenging.NullOrEmpty())
				text += "\n\n" + "RimSiege_VendettaHint".Translate(avenging);

			Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, new TargetInfo(muster, map), faction);
		}

		// same walk-in cell a vanilla raid uses
		private static IntVec3 FindEntryCell(Map map)
		{
			if (RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile))
				return entry;
			// fog-of-war mods can mark every edge cell fogged, which fails the vanilla finder
			if (RCellFinder.TryFindRandomPawnEntryCell(out entry, map, CellFinder.EdgeRoadChance_Hostile, allowFogged: true))
				return entry;
			return CellFinder.RandomEdgeCell(map);
		}

		public static bool TrySpawnSappers(Map map, Faction faction, PawnKindDef crewKind, IntVec3 mainBreach,
			int count, IntVec3 rally, IntVec3 spawnAt)
		{
			if (map == null || faction == null || crewKind == null || count <= 0) return false;
			if (!TryFindSiegeTargetAwayFrom(map, mainBreach, 15f, out IntVec3 sapWall)) return false;

			if (!spawnAt.IsValid) spawnAt = EntryCellFor(map, mainBreach);
			SpawnSappersAtWall(map, faction, crewKind, sapWall, count, spawnAt, rally);
			return true;
		}

		public static IntVec3 EntryCellFor(Map map, IntVec3 target)
		{
			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			if (RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile,
					allowFogged: false, c => map.reachability.CanReach(c, target, PathEndMode.Touch, tp)))
				return entry;
			if (RCellFinder.TryFindRandomPawnEntryCell(out entry, map, CellFinder.EdgeRoadChance_Hostile,
					allowFogged: true, c => map.reachability.CanReach(c, target, PathEndMode.Touch, tp)))
				return entry;
			return FindEntryCell(map);
		}

		// spawnAt invalid = standalone debug sapper, comes in on its own edge
		public static void SpawnSappersAtWall(Map map, Faction faction, PawnKindDef crewKind, IntVec3 sapWall,
			int count, IntVec3 spawnAt, IntVec3 rally)
		{
			if (map == null || faction == null || crewKind == null || count <= 0 || !sapWall.IsValid) return;
			if (!faction.HostileTo(Faction.OfPlayer))
				faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, canSendHostilityLetter: false);

			IntVec3 entry = spawnAt.IsValid ? spawnAt : EntryCellFor(map, sapWall);
			var group = new List<Pawn>();
			for (int i = 0; i < count; i++)
			{
				var s = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
					crewKind, faction, PawnGenerationContext.NonPlayer,
					tile: map.Tile, forceGenerateNewPawn: true, mustBeCapableOfViolence: true,
					canGeneratePawnRelations: false));
				GenSpawn.Spawn(s, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
				group.Add(s);
			}
			LordMaker.MakeNewLord(faction, new LordJob_Sappers(sapWall, rally), map, group);
		}

		// walls only, a petard on a door is pointless
		public static bool TryFindSiegeTargetAwayFrom(Map map, IntVec3 avoid, float minDist, out IntVec3 breach)
		{
			breach = IntVec3.Invalid;
			float minSq = minDist * minDist;
			var cache = new Dictionary<Region, bool>();
			List<Building> candidates = map.listerBuildings.allBuildingsColonist
				.Where(b => b.def.passability == Traversability.Impassable && b.def.fillPercent >= 0.9f
					&& !b.Destroyed && (b.Position - avoid).LengthHorizontalSquared >= minSq
					&& GuardsColony(map, b.Position, cache))
				.ToList();
			if (candidates.Count == 0) return false;
			breach = candidates.RandomElement().Position;
			return true;
		}

		public static bool TryFindSiegeTarget(Map map, out IntVec3 breach) =>
			TryFindSiegeTarget(map, preferWalls: false, out breach);

		// ram goes for the gate, tower goes for walls
		public static bool TryFindSiegeTarget(Map map, bool preferWalls, out IntVec3 breach)
		{
			breach = IntVec3.Invalid;
			var cache = new Dictionary<Region, bool>();

			List<Building> doors = map.listerBuildings.allBuildingsColonist
				.Where(b => b is Building_Door && !b.Destroyed && GuardsColony(map, b.Position, cache))
				.ToList();
			List<Building> walls = map.listerBuildings.allBuildingsColonist
				.Where(b => b.def.passability == Traversability.Impassable && b.def.fillPercent >= 0.9f
					&& GuardsColony(map, b.Position, cache))
				.ToList();

			if (doors.Count == 0 && walls.Count == 0)
			{
				// gate left open, or no real enclosure: nothing guards the colony. take anything with
				// an open side so sieges keep coming, the ram will find the way in already open
				doors = map.listerBuildings.allBuildingsColonist
					.Where(b => b is Building_Door && !b.Destroyed && HasOpenSide(map, b.Position))
					.ToList();
				walls = map.listerBuildings.allBuildingsColonist
					.Where(b => b.def.passability == Traversability.Impassable && b.def.fillPercent >= 0.9f
						&& HasOpenSide(map, b.Position))
					.ToList();
			}

			List<Building> first = preferWalls ? walls : doors;
			List<Building> second = preferWalls ? doors : walls;
			if (first.Count > 0) { breach = first.RandomElement().Position; return true; }
			if (second.Count > 0) { breach = second.RandomElement().Position; return true; }
			return false;
		}

		private static bool HasOpenSide(Map map, IntVec3 c)
		{
			foreach (Rot4 dir in CardinalDirs)
			{
				IntVec3 adj = c + dir.FacingCell;
				if (adj.InBounds(map) && adj.Walkable(map) && map.reachability.CanReachMapEdge(adj, NoDoors)) return true;
			}
			return false;
		}

		public static readonly TraverseParms NoDoors = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);

		// a wall or door is worth hitting only if it stands between open ground and the colony. a lone
		// door in a field, a pen gate, a shed out past the walls: nothing behind them worth a ram
		private static bool GuardsColony(Map map, IntVec3 c, Dictionary<Region, bool> cache)
		{
			IntVec3 inner = SealedCellBehind(map, c);
			return inner.IsValid && IsColonyCell(map, inner, cache);
		}

		// first open cell behind c (through double walls and gatehouse doors) that cant reach the map
		// edge without opening a door. Invalid when every side is open ground.
		// walkable, not standable: ppl line their walls with stakes and log piles
		public static IntVec3 SealedCellBehind(Map map, IntVec3 c)
		{
			foreach (Rot4 dir in CardinalDirs)
			{
				IntVec3 outside = c + dir.FacingCell;
				if (!outside.InBounds(map) || !outside.Walkable(map) || !map.reachability.CanReachMapEdge(outside, NoDoors)) continue;
				for (int i = 1; i <= 6; i++)
				{
					IntVec3 inner = c - dir.FacingCell * i;
					if (!inner.InBounds(map)) break;
					if (!inner.Walkable(map)) continue;
					if (!map.reachability.CanReachMapEdge(inner, NoDoors)) return inner;
					break;
				}
			}
			return IntVec3.Invalid;
		}

		// colonists or their beds somewhere in the sealed part, going through inner doors but never back
		// out through one that touches open ground. beds cover the night, everyone shut in their rooms
		private static bool IsColonyCell(Map map, IntVec3 c, Dictionary<Region, bool> cache)
		{
			Region root = c.GetRegion(map);
			if (root == null) return false;
			if (cache.TryGetValue(root, out bool known)) return known;

			var seen = new HashSet<Region>();
			RegionTraverser.BreadthFirstTraverse(root,
				(from, to) => to.Allows(NoDoors, false) || (to.door != null && !DoorTouchesOutside(map, to.door)),
				r => { seen.Add(r); return false; });

			bool found = ColonyIn(map, seen, furniture: false);
			cache[root] = found;
			return found;
		}

		// run before the ram swings, while the target still seals. the space right behind it without
		// opening any door: is the colony in there, or is it an airlock/gatehouse yard with the next
		// closed door on from it. furniture counts here so a courtyard at night still reads as colony
		public static void ProbeBehind(Map map, IntVec3 target, out IntVec3 sealedCell, out bool colonyInside, out IntVec3 nextDoor)
		{
			sealedCell = SealedCellBehind(map, target);
			colonyInside = false;
			nextDoor = IntVec3.Invalid;
			if (!sealedCell.IsValid) return;
			Region root = sealedCell.GetRegion(map);
			if (root == null) return;

			var seen = new HashSet<Region>();
			var border = new HashSet<Building_Door>();
			RegionTraverser.BreadthFirstTraverse(root,
				(from, to) =>
				{
					bool ok = to.Allows(NoDoors, false);
					if (!ok && to.door != null) border.Add(to.door);
					return ok;
				},
				r => { seen.Add(r); return false; });

			colonyInside = ColonyIn(map, seen, furniture: true);
			float best = float.MaxValue;
			foreach (Building_Door d in border)
			{
				if (DoorTouchesOutside(map, d)) continue;
				float dist = (d.Position - sealedCell).LengthHorizontalSquared;
				if (dist < best) { best = dist; nextDoor = d.Position; }
			}
		}

		private static bool ColonyIn(Map map, HashSet<Region> seen, bool furniture)
		{
			foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
				if (seen.Contains(p.GetRegion())) return true;
			foreach (Building b in map.listerBuildings.allBuildingsColonist)
			{
				bool counts = b is Building_Bed bed
					? bed.def.building.bed_humanlike && !bed.ForPrisoners
					: furniture && !(b is Frame) && !(b is Building_Door)
						&& b.def.passability != Traversability.Impassable && b.def.building.isEdifice;
				if (counts && seen.Contains(b.Position.GetRegion(map))) return true;
			}
			return false;
		}

		private static bool DoorTouchesOutside(Map map, Building_Door door) => HasOpenSide(map, door.Position);

		// can a hostile at c reach the colony without opening doors
		public static bool HostileCanReachColonyFrom(Map map, IntVec3 c)
		{
			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			foreach (Pawn col in map.mapPawns.FreeColonistsSpawned)
				if (map.reachability.CanReach(c, col.Position, PathEndMode.Touch, tp))
					return true;
			return false;
		}

		private static readonly Rot4[] CardinalDirs = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };

		// MO noble houses, sniffed off their combat kind names. null = no house
		public static string HouseOf(Faction f)
		{
			string[] houses = { "Amboise", "Hesse", "Oswin", "Soren" };
			if (f?.def?.pawnGroupMakers != null)
				foreach (PawnGroupMaker gm in f.def.pawnGroupMakers)
					if (gm.options != null)
						foreach (PawnGenOption o in gm.options)
							foreach (string h in houses)
								if (o.kind != null && o.kind.defName.Contains(h))
									return h;
			return null;
		}

		public static ThingDef HouseBanner(Faction f)
		{
			string h = HouseOf(f);
			if (h != null)
			{
				ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Banner" + h);
				if (d != null) return d;
			}
			return DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_BannerPlain");
		}

		public static PawnKindDef HouseStandardKind(Faction f)
		{
			string h = HouseOf(f);
			return h == null ? null : DefDatabase<PawnKindDef>.GetNamedSilentFail("DankPyon_Standard" + h);
		}

		public static List<Building> SpawnedBallistas(Map map, Faction faction)
		{
			ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Ballista");
			if (d == null || map == null) return new List<Building>();
			return map.listerThings.ThingsOfDef(d)
				.Where(t => t.Faction == faction && t.Spawned)
				.Cast<Building>().ToList();
		}

		public static List<Building> SpawnedTrebuchets(Map map, Faction faction)
		{
			ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Trebuchet");
			if (d == null || map == null) return new List<Building>();
			return map.listerThings.ThingsOfDef(d)
				.Where(t => t.Faction == faction && t.Spawned)
				.Cast<Building>().ToList();
		}

		// mixed volleys: fire favored, the dead and plain stone fill the gaps. strict
		// fire-first meant the tar stock outlasted the siege and carrion never flew
		public static void LoadTrebuchetsFromPiles(Map map, Faction faction)
		{
			ThingDef stone = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_StoneBoulder");
			ThingDef tarred = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_StoneBoulderTarred");
			bool carrionOk = RimSiegeMod.S.carrionEnabled && RimSiegeDefOf.RimSiege_CarrionShell != null;
			foreach (Building treb in SpawnedTrebuchets(map, faction))
			{
				Thing gun = (treb as Building_TurretGun)?.gun;
				CompChangeableProjectile changeable = gun?.TryGetComp<CompChangeableProjectile>();
				if (changeable == null || changeable.Loaded) continue;

				Thing tarPile = NearestPile(map, treb.Position, tarred);
				Corpse carrion = carrionOk ? NearestCarrion(map, treb.Position) : null;
				Thing stonePile = NearestPile(map, treb.Position, stone);

				float wTar = tarPile != null ? 3f : 0f;
				float wCar = carrion != null ? 2f : 0f;
				float wSto = stonePile != null ? 2f : 0f;
				float total = wTar + wCar + wSto;
				if (total <= 0f) continue;

				float roll = Rand.Value * total;
				if (roll < wTar)
				{
					tarPile.SplitOff(1).Destroy();
					changeable.LoadShell(tarred, 1);
				}
				else if (roll < wTar + wCar)
				{
					carrion.Destroy();
					changeable.LoadShell(RimSiegeDefOf.RimSiege_CarrionShell, 1);
				}
				else
				{
					stonePile.SplitOff(1).Destroy();
					changeable.LoadShell(stone, 1);
				}
			}
		}

		// a rotten carcass, dropped at the camp pile or where the shot lands
		public static void SpawnCarrion(IntVec3 cell, Map map)
		{
			if (map == null) return;
			PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Goat")
				?? DefDatabase<PawnKindDef>.GetNamedSilentFail("WildBoar")
				?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Hare")
				?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Rat");
			if (kind == null) return;

			Pawn a = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
				kind, null, PawnGenerationContext.NonPlayer,
				tile: map.Tile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
			a.Kill(null);
			Corpse corpse = a.Corpse;
			if (corpse == null) return;
			// past the 2.5 day rot start, or the loader's stink filter skips the fresh ones
			CompRottable rot = corpse.TryGetComp<CompRottable>();
			if (rot != null) rot.RotProgress = 200000f;
			GenPlace.TryPlaceThing(corpse, cell, map, ThingPlaceMode.Near);
		}

		// pods scatter around the camp, so search wide. the home area guard keeps the
		// loader out of the players own stocks
		private const float AmmoSearchSq = 1600f;

		// anything animal and already stinking within reach of the sling
		private static Corpse NearestCarrion(Map map, IntVec3 near)
		{
			Corpse best = null;
			float bestD = AmmoSearchSq;
			foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
			{
				if (!(t is Corpse c) || !c.Spawned) continue;
				if (map.areaManager.Home[c.Position]) continue;
				if (c.InnerPawn?.RaceProps?.Animal != true) continue;
				if (c.GetRotStage() == RotStage.Fresh) continue;
				float d = (c.Position - near).LengthHorizontalSquared;
				if (d <= bestD) { bestD = d; best = c; }
			}
			return best;
		}

		private static Thing NearestPile(Map map, IntVec3 near, ThingDef def)
		{
			if (def == null) return null;
			Thing best = null;
			float bestD = AmmoSearchSq;
			foreach (Thing t in map.listerThings.ThingsOfDef(def))
			{
				if (!t.Spawned) continue;
				if (map.areaManager.Home[t.Position]) continue;
				float d = (t.Position - near).LengthHorizontalSquared;
				if (d <= bestD) { bestD = d; best = t; }
			}
			return best;
		}

		// MO ballista runs on bolts as fuel, top it up from the camp pile. pile stolen = it goes quiet
		public static void RefuelBallistasNear(Map map, Faction faction)
		{
			ThingDef boltDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_BallistaBolt");
			if (boltDef == null) return;
			foreach (Building b in SpawnedBallistas(map, faction))
			{
				CompRefuelable fuel = b.TryGetComp<CompRefuelable>();
				if (fuel == null) continue;
				int missing = Mathf.FloorToInt(fuel.Props.fuelCapacity - fuel.Fuel);
				if (missing < 1) continue;

				Thing ammo = NearestPile(map, b.Position, boltDef);
				if (ammo == null) continue;

				int take = Mathf.Min(missing, ammo.stackCount);
				ammo.SplitOff(take).Destroy();
				fuel.Refuel(take);
			}
		}

		public static Faction FindSiegeFaction(float points, out PawnKindDef crewKind)
		{
			crewKind = null;

			// vanilla weighted roll, otherwise the same faction shows up every single time
			if (PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroup(points, out Faction faction,
					null, allowNonHostileToPlayer: false, allowHidden: false, allowDefeated: false,
					allowNonHumanlike: false))
			{
				crewKind = GetHumanlikeKind(faction);
				if (crewKind != null) return faction;
			}

			foreach (Faction f in Find.FactionManager.AllFactions)
			{
				if (f.IsPlayer || f.defeated || f.def.hidden) continue;
				if (!f.HostileTo(Faction.OfPlayer)) continue;
				PawnKindDef k = GetHumanlikeKind(f);
				if (k != null) { crewKind = k; return f; }
			}
			return null;
		}

		private static PawnKindDef GetHumanlikeKind(Faction f)
		{
			if (IsHumanlike(f.def.basicMemberKind)) return f.def.basicMemberKind;
			if (f.def.pawnGroupMakers != null)
				foreach (var gm in f.def.pawnGroupMakers)
					if (gm.options != null)
						foreach (var o in gm.options)
							if (IsHumanlike(o.kind)) return o.kind;
			return null;
		}

		private static bool IsHumanlike(PawnKindDef k) => k?.race?.race?.Humanlike ?? false;
	}
}
