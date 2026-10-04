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
		private readonly List<Pawn> gunners = new List<Pawn>(); // artillery crews, manned as soon as built
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

			ClearCampArea();

			var resources = new Dictionary<ThingDef, int>();
			if (engineDef != null)
				PlaceEngine(engineDef, null, resources);

			if (engineDef != null && trebuchetCount > 0)
			{
				ThingDef trebDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Trebuchet");
				ThingDef boulderAmmo = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_StoneBoulder");
				ThingDef tarredAmmo = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_StoneBoulderTarred");
				if (trebDef != null)
				{
					for (int i = 0; i < trebuchetCount; i++)
						PlaceEngine(trebDef, trebDef.MadeFromStuff ? GenStuff.DefaultStuffFor(trebDef) : null, resources);

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
						PlaceEngine(ballistaDef, ballistaDef.MadeFromStuff ? GenStuff.DefaultStuffFor(ballistaDef) : null, resources);

					if (boltAmmo != null)
						resources[boltAmmo] = (resources.TryGetValue(boltAmmo, out int av) ? av : 0) + 20 * ballistaCount;
				}
			}

			DropResources(resources);
			SetUpCamp();

			// the grim pile next to the boulders. loader feeds them to the sling once they stink
			if (carrionAmmo && trebuchetCount > 0)
				for (int i = 0; i < 3 * trebuchetCount; i++)
					SiegeLauncher.SpawnCarrion(CellFinder.RandomClosewalkCellNear(center, Map, 4), Map);
		}

		private const float CampClearRadius = 13f;

		// trample a circle: looks like a camp, and tree cells arent standable so dense forests
		// used to leave no room for the blueprints
		private void ClearCampArea()
		{
			foreach (IntVec3 c in GenRadial.RadialCellsAround(center, CampClearRadius, useCenter: true))
			{
				if (!c.InBounds(Map)) continue;
				List<Thing> things = c.GetThingList(Map);
				for (int i = things.Count - 1; i >= 0; i--)
				{
					Thing t = things[i];
					if (t is Plant || t is Filth) t.Destroy(DestroyMode.Vanish);
				}
			}
		}

		// camp dressing. campfire + banner + torches at the heart, spikes on the flanks, some tents
		private void SetUpCamp()
		{
			IntVec3 d = breach - center;
			IntVec3 fwd = (Mathf.Abs(d.x) >= Mathf.Abs(d.z))
				? new IntVec3(d.x >= 0 ? 1 : -1, 0, 0)
				: new IntVec3(0, 0, d.z >= 0 ? 1 : -1);
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
			if (spikeDef != null)
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

		private bool TrySpawnDeco(ThingDef def, IntVec3 c)
		{
			if (!c.InBounds(Map) || !FootprintClear(c, def)) return false;
			ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
			Thing t = ThingMaker.MakeThing(def, stuff);
			GenSpawn.Spawn(t, c, Map);
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

		private void PlaceEngine(ThingDef def, ThingDef stuff, Dictionary<ThingDef, int> resources)
		{
			if (def == null) return;
			IntVec3 spot = FindBuildSpot(def);
			if (!spot.IsValid) return;

			Blueprint_Build bp = GenConstruct.PlaceBlueprintForBuild(def, spot, Map, Rot4.North, lord.faction, stuff);
			foreach (ThingDefCountClass cost in bp.TotalMaterialCost())
				resources[cost.thingDef] = (resources.TryGetValue(cost.thingDef, out int v) ? v : 0) + cost.count;
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
					if (c.Roofed(Map)) continue;
					if (!Map.reachability.CanReach(c, center, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)) continue;
					if (!FootprintClear(c, def)) continue;
					return c;
				}
			}
			return IntVec3.Invalid;
		}

		private bool FootprintClear(IntVec3 root, ThingDef def)
		{
			foreach (IntVec3 c in GenAdj.OccupiedRect(root, Rot4.North, def.Size))
			{
				if (!c.InBounds(Map) || !c.Standable(Map)) return false;
				if (c.GetEdifice(Map) != null) return false;
				List<Thing> things = c.GetThingList(Map);
				for (int i = 0; i < things.Count; i++)
					if (things[i] is Blueprint || things[i] is Frame) return false;
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
			DropPodUtility.DropThingGroupsNear(center, Map, groups);
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

			bool anythingToBuild = engineDef != null || trebuchetCount > 0;
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
			if (ballistaCount <= 0 && trebuchetCount <= 0) { gunners.Clear(); return; }
			List<Building> guns = SiegeLauncher.SpawnedBallistas(Map, lord.faction);
			guns.AddRange(SiegeLauncher.SpawnedTrebuchets(Map, lord.faction));
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
			p.mindState.duty = new PawnDuty(DutyDefOf.Build, center) { radius = Radius };
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
			p.mindState.duty = new PawnDuty(DutyDefOf.Defend, center) { radius = Radius };
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
			if (trebuchetCount > 0) SiegeLauncher.LoadTrebuchetsFromPiles(Map, lord.faction);

			// no engine: camp a while, blow the horn, charge
			if (engineDef == null)
			{
				if (!campDone && lord.ticksInToil > StartBuildingDelay + CampMusterTicks)
				{
					campDone = true;
					RimSiegeDefOf.RimSiege_WarHorn?.PlayOneShot(new TargetInfo(center, Map));
					lord.ReceiveMemo("BuildFailed");
				}
				return;
			}

			Building_SiegeEngine engine = Map.listerThings.ThingsOfDef(engineDef)
				.FirstOrDefault(t => t.Faction == lord.faction) as Building_SiegeEngine;

			// a full late game train is 6 worksites, one pair of hands each: give it time
			int maxBuild = MaxBuildTicks + 3000 * (trebuchetCount + ballistaCount);
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
						reachableAtBombardStart = SiegeLauncher.HostileCanReachColonyFrom(Map, center);
						Messages.Message("RimSiege_BombardBegins".Translate(), new TargetInfo(center, Map),
							MessageTypeDefOf.ThreatSmall);
					}
					TryReinforce(bombardTicks);

					// the guns opened their own hole: skip the engine, charge the rubble
					if (!reachableAtBombardStart && SiegeLauncher.HostileCanReachColonyFrom(Map, center))
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

			if (lord.ticksInToil > StartBuildingDelay + 600 && engine == null && !AnyEngineWorkLeft())
				lord.ReceiveMemo("BuildFailed");
		}

		// siege moved on or died: cancel leftover construction like vanilla does, materials drop
		public override void Cleanup()
		{
			base.Cleanup();
			const float CleanupRadius = 16f;
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
			if (job == null || job.lateOfferSent) return;
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
			if (job == null || job.reinforced || s == null || !s.reinforcements) return;
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
