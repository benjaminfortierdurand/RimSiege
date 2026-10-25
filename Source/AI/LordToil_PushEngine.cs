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

		// gun era: a smoke shell on the gate every so often while the breaching team closes in and batters it
		private const int SmokeInterval = 1500;
		private const int SmokeRangeSq = 625;   // ram within 25 cells of the gate
		private int lastSmokeTick = -99999;

		protected int lastBreachCheckTick = -99999;

		private const int EngineStallTimeout = 5000;
		private int lastEngineProgressTick = -1;
		private int dockedStarvedSince = -1;
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

		// the siege's own engine def. the default only covers 1.3.0 saves that never recorded one
		protected abstract ThingDef DefaultEngineDef { get; }
		protected ThingDef EngineDef => (lord?.LordJob as LordJob_MedievalSiege)?.EngineDef ?? DefaultEngineDef;
		protected abstract void DockedTick(Building_SiegeEngine engine, int now);

		protected virtual bool ShouldKeepCurrentDuty(Pawn p) => charging.Contains(p);

		// docked tower ferries on its own, no crew needed
		protected virtual bool EngineNeedsCrewNow(Building_SiegeEngine e) => true;

		protected virtual int MaxPushers => RimSiegeMod.S?.maxPushers ?? 4;

		protected Building_SiegeEngine Engine
		{
			get
			{
				var job = lord.LordJob as LordJob_MedievalSiege;
				if (job?.engine != null) return job.engine.Spawned ? job.engine : null;
				// 1.3.0 save already rolling, nothing on record yet
				var e = Map.listerThings.ThingsOfDef(EngineDef).FirstOrDefault(t => t.Faction == lord.faction) as Building_SiegeEngine;
				if (e != null && job != null) job.engine = e;
				return e;
			}
		}

		public override void Init()
		{
			base.Init();
			IntVec3 pos = Engine?.Position ?? (lord.ownedPawns.Count > 0 ? lord.ownedPawns[0].Position : Map.Center);
			// a long dock turns to its parking spot's facing now, so the crew line up behind it once
			if (Engine != null && DockRange > 2) Engine.PlanDock(DockSpot(Engine));
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
			// the engine crew comes first: bodyguards only out of hands left over, else after a few
			// losses the ram sat docked with two pushers forever
			int maxGuards = 2;
			if (engine != null && EngineNeedsCrewNow(engine))
			{
				int hands = 0;
				foreach (Pawn p in lord.ownedPawns)
					if (!gunners.Contains(p) && !ShouldKeepCurrentDuty(p) && p != captain && !p.Dead && !p.Downed
						&& SiegeLauncher.CanCrew(p))
						hands++;
				maxGuards = Math.Max(0, Math.Min(2, hands - engine.crewNeeded));
			}
			// the log ram needs its four even if the settings cap pushers lower. carried engines take
			// exactly their crew, one more would stand on the log
			int crewCap = engine == null ? 0
				: engine.OnWheels ? Math.Max(MaxPushers, engine.crewNeeded) : engine.crewNeeded;
			foreach (Pawn p in lord.ownedPawns)
			{
				if (gunners.Contains(p)) continue;
				if (ShouldKeepCurrentDuty(p)) continue;
				bool crew = SiegeLauncher.CanCrew(p); // war beasts escort, they dont work

				// escort duty with radius 0 spams errors, dont remove it
				if (crew && captainUp && p != captain && guards < maxGuards)
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
				if (crew && p != captain && pushers < crewCap)
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
			CoverWithSmoke(engine, now);
			CheckEngineReachable(engine, now);

			if (!engine.docked)
			{
				if (!engine.breachTarget.IsValid) { engine.docked = true; return; }
				IntVec3 dv = engine.breachTarget - engine.Position;
				int distSq = dv.LengthHorizontalSquared;
				// dock only square in front of the wall, never diagonally or alongside it
				if (distSq <= DockDistanceSq && (dv.x == 0 || dv.z == 0) && (DockRange <= 2 || LineClear(engine.Position, engine.breachTarget)))
				{ engine.FaceTarget(); engine.docked = true; return; }

				if (lastEngineProgressTick < 0) lastEngineProgressTick = now;
				if (now - lastAdvanceTick >= AdvanceInterval && CountPushers(engine) >= engine.crewNeeded)
				{
					lastAdvanceTick = now;
					lastEngineProgressTick = now;
					if (DockRange <= 2 && distSq <= DockDistanceSq + 2) AlignFront(engine); else Advance(engine);
				}
				// they can walk to it, they just never show up to shove (crew dead, or another
				// AI keeping them in the fight). drop the engine and raid on foot
				else if (now - lastEngineProgressTick >= EngineStallTimeout)
					lord.ReceiveMemo("RamLost");
			}
			else
			{
				// docked but short of hands (crew dead, gunners took the rest): it never swings again.
				// same deal as a stalled roll, ditch it and raid on foot
				if (EngineNeedsCrewNow(engine) && CountPushers(engine) < engine.crewNeeded)
				{
					if (dockedStarvedSince < 0) dockedStarvedSince = now;
					else if (now - dockedStarvedSince >= EngineStallTimeout) { lord.ReceiveMemo("RamLost"); return; }
				}
				else dockedStarvedSince = -1;
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
			// next step would enter the dock ring: hand over to the sidestep logic so it ends up square.
			// a long dock already paths to its spot
			if (DockRange <= 2 && (next - engine.breachTarget).LengthHorizontalSquared <= DockDistanceSq)
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
			if (engine.OnWheels && t - lastRollSoundTick >= 70)
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
				// a long dock (the laser ram) heads for its parking spot, the others for the gate itself
				IntVec3 spot = DockRange > 2 ? DockSpot(engine) : IntVec3.Invalid;
				if (DockRange > 2) engine.PlanDock(spot);
				pp = spot.IsValid
					? Map.pathFinder.FindPathNow(engine.Position, spot, tp, peMode: PathEndMode.OnCell)
					: Map.pathFinder.FindPathNow(engine.Position, engine.breachTarget, tp, peMode: PathEndMode.Touch);
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

		private int DockRange => Math.Max(1, (int)Math.Round(Math.Sqrt(DockDistanceSq)));

		// where the engine parks: DockRange cells out on a cardinal of the gate, reachable. a long dock (the laser
		// ram) wants a line to the gate that crosses no built wall or door, trees and rock are fair game for its
		// beam, and it creeps closer when the far spots are taken. Invalid = boxed in
		private IntVec3 DockSpot(Building_SiegeEngine engine)
		{
			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			IntVec3 best = IntVec3.Invalid;
			int bestD = int.MaxValue;
			for (int range = DockRange; range >= 1 && !best.IsValid; range = range > 2 ? range - 1 : 0)
				foreach (Rot4 dir in CardinalDirs)
				{
					IntVec3 c = engine.breachTarget + dir.FacingCell * range;
					if (!c.InBounds(Map)) continue;
					if (c != engine.Position)
					{
						if (!c.Standable(Map) || c.GetEdifice(Map) != null) continue;
						if (!Map.reachability.CanReach(engine.Position, c, PathEndMode.OnCell, tp)) continue;
					}
					if (range > 2 && !LineClear(c, engine.breachTarget)) continue;
					int d = (c - engine.Position).LengthHorizontalSquared;
					if (d < bestD) { bestD = d; best = c; }
				}
			return best;
		}

		// straight cardinal line between two aligned cells: no built wall or door on it, natural rock is fine
		protected bool LineClear(IntVec3 from, IntVec3 to)
		{
			IntVec3 step = new IntVec3(Math.Sign(to.x - from.x), 0, Math.Sign(to.z - from.z));
			if (step.x != 0 && step.z != 0) return false;
			for (IntVec3 c = from + step; c != to; c += step)
			{
				if (!c.InBounds(Map)) return false;
				Building ed = c.GetEdifice(Map);
				if (ed == null || (ed.def.building?.isNaturalRock ?? false)) continue;
				if (ed.def.passability == Traversability.Impassable || ed is Building_Door) return false;
			}
			return true;
		}

		// sidestep onto the cell straight in front of the wall so the engine always docks square
		private void AlignFront(Building_SiegeEngine engine)
		{
			IntVec3 best = DockSpot(engine);
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
			// wheels: loose radius, the engine is 1 logical cell but the sprite is fat. the wagon's crew stands
			// past it at the back end, their own handle counts too
			// carried: everyone on their handle, else the log runs off ahead of the guys holding it
			int n = 0;
			foreach (Pawn p in lord.ownedPawns)
				if (Pushing(engine, p)) n++;
			return n;
		}

		protected static bool Pushing(Building_SiegeEngine engine, Pawn p) =>
			p.CurJobDef == RimSiegeDefOf.RimSiege_PushSiegeEngine
			&& (engine.OnWheels ? (p.Position - engine.Position).LengthHorizontalSquared <= 6 || engine.OnHandle(p) : engine.OnHandle(p));

		private void CrushPlantsAt(IntVec3 c)
		{
			var things = c.GetThingList(Map);
			for (int i = things.Count - 1; i >= 0; i--)
				if (things[i] is Plant) things[i].Destroy(DestroyMode.Vanish);
		}

		private void EnsureGunners() => SiegeLauncher.ManTheGuns(lord, gunners, captain);

		// manned MO turret shoots on its own, reload it from the camp pile. pile gone = gun goes quiet
		private void KeepTrebuchetsLoaded()
		{
			int now = Find.TickManager.TicksGame;
			if (now - lastLoadTick < LoadCheckInterval) return;
			lastLoadTick = now;
			SiegeLauncher.ReloadCamp(Map, lord.faction);
		}

		// tries every 2s until a manned mortar with a smoke shell fires, then waits out the cloud
		private void CoverWithSmoke(Building_SiegeEngine engine, int now)
		{
			if (!(engine is Building_SteelRam) || !engine.breachTarget.IsValid) return;
			if (now - lastSmokeTick < SmokeInterval || now % 120 != 0) return;
			if ((engine.Position - engine.breachTarget).LengthHorizontalSquared > SmokeRangeSq) return;
			if (SiegeLauncher.FireSmokeAt(Map, lord.faction, engine.breachTarget)) lastSmokeTick = now;
		}
	}
}
