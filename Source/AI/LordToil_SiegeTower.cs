using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.AI
{
	public class LordToil_SiegeTower : LordToil_PushEngine
	{
		private const int DeployInterval = 90;  // one climb every ~1.5s
		private const int PickupRangeSq = 144;
		private const int WaitRangeSq = 1600;   // someone this close is still expected, hold the door
		private const int StallTimeout = 2500;
		private const int MaxBoarding = 3;
		private const int BoardRangeSq = 13;    // gotta be at the tower to climb, no long range tp
		private int lastDeployTick = -99999;
		private int lastDeployProgress = -1;

		private LordToilData_SiegeTower TData => (LordToilData_SiegeTower)data;
		private HashSet<Pawn> deployed => TData.deployed;
		private HashSet<Pawn> boarding => TData.boarding;

		public LordToil_SiegeTower()
		{
			data = new LordToilData_SiegeTower();
		}

		protected override ThingDef EngineDef => RimSiegeDefOf.RimSiege_SiegeTower;

		protected override int AdvanceInterval => 75; // heavy thing

		// stops 2 cells short, the ramp bridges the gap and the big sprite doesnt spill over the wall
		protected override int DockDistanceSq => 4;

		protected override bool ShouldKeepCurrentDuty(Pawn p) =>
			deployed.Contains(p) || boarding.Contains(p) || base.ShouldKeepCurrentDuty(p);

		// boarders are exactly the ones we watch, they still need to walk to the ramp
		protected override bool CommittedElsewhere(Pawn p) =>
			deployed.Contains(p) || base.CommittedElsewhere(p);

		protected override bool EngineNeedsCrewNow(Building_SiegeEngine e) => !e.docked;

		protected override void DockedTick(Building_SiegeEngine engine, int now)
		{
			engine.FaceTarget();
			if (lastDeployProgress < 0) lastDeployProgress = now;

			// sapper hole opened mid deploy: closest half keeps climbing, the rest run for the hole
			if (!breachSplit && now - lastBreachCheckTick >= 60)
			{
				lastBreachCheckTick = now;
				if (SapperBreachOpen()) SplitForBreach(engine);
			}

			boarding.RemoveWhere(p => p == null || p.Dead || p.Downed || !p.Spawned || !lord.ownedPawns.Contains(p));

			while (boarding.Count < MaxBoarding)
			{
				Pawn next = NextClimber(engine);
				if (next == null) break;
				next.mindState.duty = new PawnDuty(DutyDefOf.Defend, engine.BackCell) { radius = 2f };
				next.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				boarding.Add(next);
			}

			if (boarding.Count == 0)
			{
				// whole army goes over, only the gunners stay. dont wait forever on a guy stuck in a brawl
				if (AnyEligibleWithin(engine, WaitRangeSq) && now - lastDeployProgress < StallTimeout) return;
				lord.ReceiveMemo("BreachOpen");
				return;
			}

			if (now - lastDeployTick < DeployInterval) return;

			IntVec3 inside = FindInsideCell(engine);
			if (!inside.IsValid) { lord.ReceiveMemo("BreachOpen"); return; }

			Pawn climber = boarding.FirstOrDefault(p =>
				(p.Position - engine.Position).LengthHorizontalSquared <= BoardRangeSq);
			if (climber == null)
			{
				if (now - lastDeployProgress >= StallTimeout) lord.ReceiveMemo("BreachOpen");
				return;
			}
			lastDeployTick = now;

			// landing might be on a bush, squash it
			var things = inside.GetThingList(Map);
			for (int i = things.Count - 1; i >= 0; i--)
				if (things[i] is Plant) things[i].Destroy(DestroyMode.Vanish);

			RimSiegeDefOf.RimSiege_CrewPush?.PlayOneShot(new TargetInfo(inside, Map));
			FleckMaker.ThrowDustPuffThick(inside.ToVector3Shifted(), Map, 1.6f, Color.white);

			climber.Position = inside;
			climber.Notify_Teleported(false, false);
			climber.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_AssaultThroughBreach);
			climber.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			boarding.Remove(climber);
			deployed.Add(climber);
			lastDeployProgress = now;
		}

		private bool Eligible(Pawn p) =>
			!deployed.Contains(p) && !boarding.Contains(p) && !charging.Contains(p)
			&& !p.Dead && !p.Downed && p.Spawned && !IsGunner(p);

		private void SplitForBreach(Building_SiegeEngine engine)
		{
			breachSplit = true;
			List<Pawn> eligible = lord.ownedPawns.Where(Eligible)
				.OrderBy(p => (p.Position - engine.Position).LengthHorizontalSquared).ToList();
			int keepClimbing = (eligible.Count + 1) / 2;
			for (int i = keepClimbing; i < eligible.Count; i++)
			{
				Pawn p = eligible[i];
				p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_AssaultThroughBreach);
				p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				charging.Add(p);
			}
		}

		private Pawn NextClimber(Building_SiegeEngine engine)
		{
			Pawn best = null;
			int bestD = int.MaxValue;
			foreach (Pawn p in lord.ownedPawns)
			{
				if (!Eligible(p)) continue;
				int d = (p.Position - engine.Position).LengthHorizontalSquared;
				if (d <= PickupRangeSq && d < bestD) { best = p; bestD = d; }
			}
			return best;
		}

		private bool AnyEligibleWithin(Building_SiegeEngine engine, int rangeSq)
		{
			foreach (Pawn p in lord.ownedPawns)
				if (Eligible(p) && (p.Position - engine.Position).LengthHorizontalSquared <= rangeSq)
					return true;
			return false;
		}

		private IntVec3 FindInsideCell(Building_SiegeEngine engine) => InsideDropCell(Map, engine);

		// drop cell past the wall. not just the first standable one: double walls leave a sealed gap
		// and gatehouses a corridor between two doors, landing there strands the climbers. so the cell
		// must actually reach the colony. static because the exit toil uses the same spot
		public static IntVec3 InsideDropCell(Map map, Building_SiegeEngine engine)
		{
			if (!engine.breachTarget.IsValid) return IntVec3.Invalid;
			IntVec3 d = engine.breachTarget - engine.Position;
			IntVec3 step = (Math.Abs(d.x) >= Math.Abs(d.z))
				? new IntVec3(Math.Sign(d.x), 0, 0)
				: new IntVec3(0, 0, Math.Sign(d.z));

			// open sky first, they come in over the top
			IntVec3 roofedReach = IntVec3.Invalid;
			IntVec3 fallback = IntVec3.Invalid;
			for (int i = 1; i <= 4; i++)
			{
				IntVec3 c = engine.breachTarget + step * i;
				if (!c.InBounds(map)) break;
				if (!c.Standable(map)) continue;
				if (SiegeLauncher.HostileCanReachColonyFrom(map, c))
				{
					if (!c.Roofed(map)) return c;
					if (!roofedReach.IsValid) roofedReach = c;
				}
				else if (!fallback.IsValid) fallback = c;
			}
			if (roofedReach.IsValid) return roofedReach;

			// line is choked (trees packed behind the wall etc), search the pocket around the landing
			// point. second pass accepts a plant cell, it gets squashed on landing
			IntVec3 anchor = engine.breachTarget + step * 2;
			for (int pass = 0; pass < 2; pass++)
			{
				foreach (IntVec3 c in GenRadial.RadialCellsAround(anchor, 3.9f, useCenter: true))
				{
					if (!c.InBounds(map)) continue;
					IntVec3 rel = c - engine.breachTarget;
					if (rel.x * step.x + rel.z * step.z < 1) continue; // inside of the wall only
					bool ok = pass == 0
						? c.Standable(map)
						: c.Walkable(map) && c.GetEdifice(map) == null;
					if (ok && SiegeLauncher.HostileCanReachColonyFrom(map, c)) return c;
				}
			}
			return fallback;
		}
	}
}
