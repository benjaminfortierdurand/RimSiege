using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.AI
{
	public abstract class LordToil_PushEngine : LordToil
	{
		protected virtual int AdvanceInterval => 30; // ticks per cell

		// ram docks against the wall, tower stays 2 back so the big sprite doesnt spill over
		protected virtual int DockDistanceSq => 1;

		private const int RedutyInterval = 500;
		private const int GruntInterval = 110;

		private int lastAdvanceTick = -99999;
		private int lastGruntTick = -99999;
		private int lastRollSoundTick = -99999;

		private List<IntVec3> path;
		private int pathIndex;

		// MO trebuchet fires itself while manned, we just keep shoving boulders in
		private const int LoadCheckInterval = 60;
		private int lastLoadTick = -99999;
		private readonly List<Pawn> gunners = new List<Pawn>();
		public Pawn captain;

		protected bool IsGunner(Pawn p) => gunners.Contains(p);

		private static bool trebLookedUp;
		private static ThingDef trebDef;
		private static ThingDef TrebuchetDef
		{
			get
			{
				if (!trebLookedUp)
				{
					trebDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Trebuchet");
					trebLookedUp = true;
				}
				return trebDef;
			}
		}

		protected int lastBreachCheckTick = -99999;

		private const int EngineStallTimeout = 5000;
		private int lastEngineProgressTick = -1;
		private const int ReachCheckInterval = 120;
		private const int UnreachableTimeout = 2500;
		private int lastReachCheckTick = -99999;
		private int unreachableSince = -1;

		// split state lives in the toil data so it survives save/load
		private LordToilData_PushEngine PData => (LordToilData_PushEngine)data;
		protected bool breachSplit { get => PData.breachSplit; set => PData.breachSplit = value; }
		protected HashSet<Pawn> charging => PData.charging;

		protected LordToil_PushEngine()
		{
			data = new LordToilData_PushEngine();
		}

		protected abstract ThingDef EngineDef { get; }
		protected abstract void DockedTick(Building_SiegeEngine engine, int now);

		protected virtual bool ShouldKeepCurrentDuty(Pawn p) => charging.Contains(p);

		// docked tower ferries on its own, no crew needed
		protected virtual bool EngineNeedsCrewNow(Building_SiegeEngine e) => true;

		protected virtual int MaxPushers => RimSiegeMod.S?.maxPushers ?? 4;

		protected Building_SiegeEngine Engine =>
			Map.listerThings.ThingsOfDef(EngineDef).FirstOrDefault(t => t.Faction == lord.faction) as Building_SiegeEngine;

		public override void Init()
		{
			base.Init();
			IntVec3 pos = Engine?.Position ?? (lord.ownedPawns.Count > 0 ? lord.ownedPawns[0].Position : Map.Center);
			RimSiegeDefOf.RimSiege_WarHorn?.PlayOneShot(new TargetInfo(pos, Map));
			RimSiegeDefOf.RimSiege_WarDrums?.PlayOneShot(new TargetInfo(pos, Map));
		}

		// refill pusher/gunner/guard slots right away instead of waiting for the reduty pass
		public override void Notify_PawnLost(Pawn victim, PawnLostCondition cond)
		{
			base.Notify_PawnLost(victim, cond);
			UpdateAllDuties();
		}

		public override void UpdateAllDuties()
		{
			EnsureGunners();
			var engine = Engine;
			int pushers = 0, guards = 0;
			// no bodyguards once the captain went in with the assault, or the reduty pass steals
			// pushers to chase him and the engine starves
			bool captainUp = captain != null && captain.Spawned && !captain.Dead && !ShouldKeepCurrentDuty(captain);
			foreach (Pawn p in lord.ownedPawns)
			{
				if (gunners.Contains(p)) continue;
				if (ShouldKeepCurrentDuty(p)) continue;
				bool crew = SiegeLauncher.CanCrew(p); // war beasts escort, they dont work

				// escort duty with radius 0 spams errors, dont remove it
				if (crew && captainUp && p != captain && guards < 2)
				{
					p.mindState.duty = new PawnDuty(RimSiegeDefOf.Escort, captain) { radius = 5f };
					guards++;
					continue;
				}

				if (engine == null)
				{
					p.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
					continue;
				}

				// captain never pushes, losing him routs everything
				if (crew && p != captain && pushers < MaxPushers)
				{
					p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_PushRam, engine);
					pushers++;
				}
				else
				{
					p.mindState.duty = new PawnDuty(DutyDefOf.Defend, engine.Position) { radius = 10f };
				}
			}
		}

		public override void LordToilTick()
		{
			base.LordToilTick();

			var engine = Engine;
			if (engine == null || engine.Destroyed || !engine.Spawned) { lord.ReceiveMemo("RamLost"); return; }

			if (lord.ticksInToil > 0 && lord.ticksInToil % RedutyInterval == 0)
				UpdateAllDuties();

			int now = Find.TickManager.TicksGame;

			// sapper hole opened mid push: escorts charge through it, crew stays on the engine
			if (!breachSplit && !engine.docked && now - lastBreachCheckTick >= 60)
			{
				lastBreachCheckTick = now;
				if (SapperBreachOpen()) SplitEscortsToBreach();
			}
			if (breachSplit && EngineNeedsCrewNow(engine) && !CanStillCrew(engine))
			{ lord.ReceiveMemo("BreachOpen"); return; }

			if (now - lastGruntTick >= GruntInterval && CountPushers(engine) >= engine.crewNeeded)
			{
				RimSiegeDefOf.RimSiege_CrewPush?.PlayOneShot(new TargetInfo(engine.Position, Map));
				lastGruntTick = now;
			}

			KeepTrebuchetsLoaded();
			CheckEngineReachable(engine, now);

			if (!engine.docked)
			{
				if (!engine.breachTarget.IsValid) { engine.docked = true; return; }
				IntVec3 dv = engine.breachTarget - engine.Position;
				int distSq = dv.LengthHorizontalSquared;
				// dock only square in front of the wall, never diagonally or alongside it
				if (distSq <= DockDistanceSq && (dv.x == 0 || dv.z == 0))
				{ engine.FaceTarget(); engine.docked = true; return; }

				if (lastEngineProgressTick < 0) lastEngineProgressTick = now;
				if (now - lastAdvanceTick >= AdvanceInterval && CountPushers(engine) >= engine.crewNeeded)
				{
					lastAdvanceTick = now;
					lastEngineProgressTick = now;
					if (distSq <= DockDistanceSq + 2) AlignFront(engine); else Advance(engine);
				}
				// they can walk to it, they just never show up to shove (crew dead, or another
				// AI keeping them in the fight). drop the engine and raid on foot
				else if (now - lastEngineProgressTick >= EngineStallTimeout)
					lord.ReceiveMemo("RamLost");
			}
			else
			{
				DockedTick(engine, now);
			}
		}

		// pawns already inside or off to another breach arent waiting on this engine
		protected virtual bool CommittedElsewhere(Pawn p) => charging.Contains(p);

		// player locked the engine away behind a door or a fresh wall: quit staring at it
		// and raid like anyone else. the fallback assault can sap its way in
		private void CheckEngineReachable(Building_SiegeEngine engine, int now)
		{
			if (now - lastReachCheckTick < ReachCheckInterval) return;
			lastReachCheckTick = now;

			bool anyWaiting = false;
			foreach (Pawn p in lord.ownedPawns)
			{
				if (p.Dead || p.Downed || !p.Spawned || CommittedElsewhere(p)) continue;
				anyWaiting = true;
				if (p.CanReach(engine, PathEndMode.Touch, Danger.Deadly)) { unreachableSince = -1; return; }
			}
			if (!anyWaiting) { unreachableSince = -1; return; }

			if (unreachableSince < 0) unreachableSince = now;
			else if (now - unreachableSince >= UnreachableTimeout) lord.ReceiveMemo("RamLost");
		}

		private void Advance(Building_SiegeEngine engine)
		{
			if (path == null && !ComputePath(engine)) { lord.ReceiveMemo("RamLost"); return; }
			if (pathIndex >= path.Count) { engine.docked = true; return; }

			IntVec3 next = path[pathIndex];
			// next step would enter the dock ring: hand over to the sidestep logic so it ends up square
			if ((next - engine.breachTarget).LengthHorizontalSquared <= DockDistanceSq)
			{ AlignFront(engine); return; }
			// only solid stuff blocks, breach-mod rubble gets rolled over
			if (BlockedAhead(next, engine))
			{
				if (!ComputePath(engine)) { lord.ReceiveMemo("RamLost"); return; }
				if (pathIndex >= path.Count) { engine.docked = true; return; }
				next = path[pathIndex];
				if (BlockedAhead(next, engine)) { lord.ReceiveMemo("RamLost"); return; }
			}

			CrushPlantsAt(next);
			engine.StepTo(next, AdvanceInterval);

			int t = Find.TickManager.TicksGame;
			if (t - lastRollSoundTick >= 70)
			{
				RimSiegeDefOf.RimSiege_RamRolling?.PlayOneShot(new TargetInfo(engine.Position, Map));
				lastRollSoundTick = t;
			}
			pathIndex++;
		}

		// call this whenever breachTarget changes or the engine insta-redocks on the stale path
		protected void InvalidatePath()
		{
			path = null;
			pathIndex = 0;
		}

		private bool BlockedAhead(IntVec3 next, Building_SiegeEngine engine)
		{
			if (next == engine.breachTarget) return false;
			Building ed = next.GetEdifice(Map);
			return ed != null && ed.def.passability == Traversability.Impassable;
		}

		private bool ComputePath(Building_SiegeEngine engine)
		{
			path = null;
			pathIndex = 0;
			PawnPath pp = null;
			try
			{
				TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
				pp = Map.pathFinder.FindPathNow(engine.Position, engine.breachTarget, tp, peMode: PathEndMode.Touch);
				if (pp.Found && pp.NodesReversed.Count >= 2)
				{
					path = new List<IntVec3>(pp.NodesReversed);
					path.Reverse();
					Straighten(path);
					pathIndex = 1;
					return true;
				}
				return false;
			}
			catch (Exception e)
			{
				Log.Warning("[RimSiege] engine pathfinding failed: " + e.Message);
				return false;
			}
			finally
			{
				pp?.ReleaseToPool();
			}
		}

		// string-pulling. A* staircase wiggles look terrible on a big engine sprite
		private void Straighten(List<IntVec3> nodes)
		{
			if (nodes.Count < 3) return;
			var pulled = new List<IntVec3> { nodes[0] };
			int i = 0;
			while (i < nodes.Count - 1)
			{
				int jump = i + 1;
				for (int j = nodes.Count - 1; j > i + 1; j--)
					if (ClearLine(nodes[i], nodes[j])) { jump = j; break; }
				foreach (IntVec3 c in LineCells(nodes[i], nodes[jump]))
					if (c != nodes[i]) pulled.Add(c);
				i = jump;
			}
			nodes.Clear();
			nodes.AddRange(pulled);
		}

		private bool ClearLine(IntVec3 a, IntVec3 b)
		{
			IntVec3 prev = a;
			foreach (IntVec3 c in LineCells(a, b))
			{
				if (c == a) { prev = c; continue; }
				if (!CellClearForRoll(c)) return false;
				// no corner cutting on diagonals, the engine is fat
				if (c.x != prev.x && c.z != prev.z
					&& (!CellClearForRoll(new IntVec3(prev.x, 0, c.z)) || !CellClearForRoll(new IntVec3(c.x, 0, prev.z))))
					return false;
				prev = c;
			}
			return true;
		}

		private bool CellClearForRoll(IntVec3 c)
		{
			if (!c.InBounds(Map) || !c.Walkable(Map)) return false;
			Building ed = c.GetEdifice(Map);
			return ed == null || (ed.def.passability != Traversability.Impassable && !(ed is Building_Door));
		}

		// bresenham, 8-connected
		private static IEnumerable<IntVec3> LineCells(IntVec3 a, IntVec3 b)
		{
			int x = a.x, z = a.z;
			int dx = Math.Abs(b.x - a.x), dz = Math.Abs(b.z - a.z);
			int sx = b.x > a.x ? 1 : -1, sz = b.z > a.z ? 1 : -1;
			int err = dx - dz;
			while (true)
			{
				yield return new IntVec3(x, 0, z);
				if (x == b.x && z == b.z) yield break;
				int e2 = 2 * err;
				if (e2 > -dz) { err -= dz; x += sx; }
				if (e2 < dx) { err += dx; z += sz; }
			}
		}

		private int DockRange => DockDistanceSq >= 4 ? 2 : 1;

		// sidestep onto the cell straight in front of the wall so the engine always docks square
		private void AlignFront(Building_SiegeEngine engine)
		{
			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			IntVec3 best = IntVec3.Invalid;
			int bestD = int.MaxValue;
			foreach (Rot4 dir in CardinalDirs)
			{
				IntVec3 c = engine.breachTarget + dir.FacingCell * DockRange;
				if (!c.InBounds(Map)) continue;
				if (c != engine.Position)
				{
					if (!c.Standable(Map) || c.GetEdifice(Map) != null) continue;
					if (!Map.reachability.CanReach(engine.Position, c, PathEndMode.OnCell, tp)) continue;
				}
				int d = (c - engine.Position).LengthHorizontalSquared;
				if (d < bestD) { bestD = d; best = c; }
			}
			if (!best.IsValid) { engine.FaceTarget(); engine.docked = true; return; } // boxed in, settle

			if (engine.Position == best) { engine.FaceTarget(); engine.docked = true; return; }

			IntVec3 stepTo = IntVec3.Invalid;
			int stepBest = int.MaxValue;
			for (int i = 0; i < 8; i++)
			{
				IntVec3 c = engine.Position + GenAdj.AdjacentCells[i];
				if (!c.InBounds(Map) || !c.Walkable(Map)) continue;
				Building ed = c.GetEdifice(Map);
				if (ed != null && ed.def.passability == Traversability.Impassable) continue;
				int d = (c - best).LengthHorizontalSquared;
				if (d < stepBest) { stepBest = d; stepTo = c; }
			}
			if (!stepTo.IsValid || stepBest >= (engine.Position - best).LengthHorizontalSquared)
			{ engine.FaceTarget(); engine.docked = true; return; } // cant get closer, settle
			CrushPlantsAt(stepTo);
			engine.StepTo(stepTo, AdvanceInterval);
		}

		private static readonly Rot4[] CardinalDirs = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };

		protected bool SapperBreachOpen()
		{
			foreach (Lord l in Map.lordManager.lords)
				if (l != lord && l.faction == lord.faction && l.LordJob is LordJob_Sappers sj
					&& sj.TargetWall.IsValid && sj.TargetWall.Walkable(Map))
					return true;
			return false;
		}

		private void SplitEscortsToBreach()
		{
			breachSplit = true;
			foreach (Pawn p in lord.ownedPawns)
			{
				if (p.Dead || p.Downed || !p.Spawned || IsGunner(p)) continue;
				if (p.mindState.duty?.def == RimSiegeDefOf.RimSiege_PushRam) continue;
				p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_AssaultThroughBreach);
				p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				charging.Add(p);
			}
		}

		private bool CanStillCrew(Building_SiegeEngine engine)
		{
			int n = 0;
			foreach (Pawn p in lord.ownedPawns)
				if (!p.Dead && !p.Downed && p.Spawned && !IsGunner(p) && !charging.Contains(p))
					n++;
			return n >= engine.crewNeeded;
		}

		protected int CountPushers(Building_SiegeEngine engine)
		{
			// loose radius, the engine is 1 logical cell but the sprite is fat
			int n = 0;
			foreach (Pawn p in lord.ownedPawns)
				if (p.CurJobDef == RimSiegeDefOf.RimSiege_PushSiegeEngine
					&& (p.Position - engine.Position).LengthHorizontalSquared <= 6)
					n++;
			return n;
		}

		private void CrushPlantsAt(IntVec3 c)
		{
			var things = c.GetThingList(Map);
			for (int i = things.Count - 1; i >= 0; i--)
				if (things[i] is Plant) things[i].Destroy(DestroyMode.Vanish);
		}

		private IEnumerable<Building> Trebuchets =>
			TrebuchetDef == null ? Enumerable.Empty<Building>() :
			Map.listerThings.ThingsOfDef(TrebuchetDef).Where(t => t.Faction == lord.faction && t.Spawned).Cast<Building>();

		private void EnsureGunners()
		{
			// trebuchets and ballistas both, cheapest hands man the machines while knights fight
			var guns = Trebuchets.Concat(SiegeLauncher.SpawnedBallistas(Map, lord.faction)).ToList();
			gunners.RemoveAll(g => g == null || g.Dead || g.Downed || !g.Spawned || !lord.ownedPawns.Contains(g));
			if (guns.Count == 0) { gunners.Clear(); return; }

			while (gunners.Count < guns.Count)
			{
				Pawn g = lord.ownedPawns
					.Where(p => p != captain && !p.Dead && !p.Downed && p.Spawned
						&& !gunners.Contains(p) && SiegeLauncher.CanCrew(p))
					.OrderBy(p => p.kindDef.combatPower)
					.FirstOrDefault();
				if (g == null) break;
				g.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_ManTrebuchet, guns[gunners.Count % guns.Count]);
				gunners.Add(g);
			}
		}

		// manned MO turret shoots on its own, reload it from the camp pile. pile gone = gun goes quiet
		private void KeepTrebuchetsLoaded()
		{
			int now = Find.TickManager.TicksGame;
			if (now - lastLoadTick < LoadCheckInterval) return;
			lastLoadTick = now;
			SiegeLauncher.RefuelBallistasNear(Map, lord.faction);
			SiegeLauncher.LoadTrebuchetsFromPiles(Map, lord.faction);
		}
	}
}
