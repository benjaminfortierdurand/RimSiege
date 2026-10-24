using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.AI
{
	// tower siege retreat: guys stuck inside walk back to the drop point and get lifted back over.
	// tower gone -> base class digs them out like vanilla
	public class LordToil_ExitViaTower : LordToil_ExitMap
	{
		private const int FerryInterval = 90;
		private const int BoardRangeSq = 13;
		private int lastFerryTick = -99999;
		private bool towerLost;

		public LordToil_ExitViaTower()
			: base(LocomotionUrgency.Jog, canDig: true, interruptCurrentJob: true) { }

		// the drop point is picked once: recomputing it moved it away from the guys already waiting there
		private IntVec3 drop = IntVec3.Invalid;

		private Building_SiegeEngine Tower
		{
			get
			{
				var job = lord.LordJob as LordJob_MedievalSiege;
				if (job?.engine != null)
					return job.engine.Spawned && job.engine is Building_SiegeTower ? job.engine : null; // ladder too
				return Map.listerThings.ThingsOfDef(RimSiegeDefOf.RimSiege_SiegeTower)
					.FirstOrDefault(t => t.Faction == lord.faction && t.Spawned) as Building_SiegeEngine;
			}
		}

		private IntVec3 DropPoint(Building_SiegeEngine tower)
		{
			if (!drop.IsValid || !drop.Walkable(Map))
				drop = LordToil_SiegeTower.InsideDropCell(Map, tower);
			return drop;
		}

		public override void UpdateAllDuties()
		{
			Building_SiegeEngine tower = Tower;
			IntVec3 drop = tower != null ? DropPoint(tower) : IntVec3.Invalid;
			if (!drop.IsValid) { base.UpdateAllDuties(); return; }

			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			foreach (Pawn p in lord.ownedPawns)
			{
				if (Map.reachability.CanReachMapEdge(p.Position, tp))
					p.mindState.duty = new PawnDuty(DutyDefOf.ExitMapBest) { locomotion = LocomotionUrgency.Jog };
				else if (Map.reachability.CanReach(p.Position, drop, PathEndMode.Touch, tp))
					p.mindState.duty = new PawnDuty(DutyDefOf.Defend, drop) { radius = 2f }; // wait at the drop point
				else
					p.mindState.duty = new PawnDuty(DutyDefOf.ExitMapBest) { locomotion = LocomotionUrgency.Jog, canDig = true };

				if (p.jobs?.curJob != null) p.jobs.EndCurrentJob(JobCondition.InterruptForced);
			}
		}

		public override void LordToilTick()
		{
			base.LordToilTick();
			int now = Find.TickManager.TicksGame;
			if (now % 30 != 0) return;

			Building_SiegeEngine tower = Tower;
			if (tower == null)
			{
				// tower burned mid retreat, dig out instead
				if (!towerLost) { towerLost = true; base.UpdateAllDuties(); }
				return;
			}

			if (now - lastFerryTick < FerryInterval) return;
			IntVec3 drop = DropPoint(tower);
			if (!drop.IsValid) return;

			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			foreach (Pawn p in lord.ownedPawns)
			{
				if (p.Dead || p.Downed || !p.Spawned) continue;
				if (Map.reachability.CanReachMapEdge(p.Position, tp)) continue;
				if ((p.Position - drop).LengthHorizontalSquared > BoardRangeSq) continue;

				IntVec3 outCell = CellFinder.RandomClosewalkCellNear(tower.BackCell, Map, 2);
				RimSiegeDefOf.RimSiege_CrewPush?.PlayOneShot(new TargetInfo(outCell, Map));
				FleckMaker.ThrowDustPuffThick(outCell.ToVector3Shifted(), Map, 1.6f, UnityEngine.Color.white);

				p.Position = outCell;
				p.Notify_Teleported(false, false);
				p.mindState.duty = new PawnDuty(DutyDefOf.ExitMapBest) { locomotion = LocomotionUrgency.Jog };
				p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				lastFerryTick = now;
				break;
			}
		}
	}
}
