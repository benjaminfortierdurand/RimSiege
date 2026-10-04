using Verse;
using Verse.AI;

namespace RimSiege.AI
{
	public class JobGiver_Sap : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			LocalTargetInfo focus = pawn.mindState.duty != null ? pawn.mindState.duty.focus : LocalTargetInfo.Invalid;
			if (!focus.IsValid) return null;

			IntVec3 wall = focus.Cell;
			if (wall.Walkable(pawn.Map)) return null;                           // already walkable (breach open, rubble included) -> done
			if (PetardNear(pawn.Map, wall)) return null;                        // a charge is already ticking -> wait
			if (!pawn.CanReach(wall, PathEndMode.Touch, Danger.Deadly)) return null;

			return JobMaker.MakeJob(RimSiegeDefOf.RimSiege_SapJob, wall);
		}

		private static bool PetardNear(Map map, IntVec3 wall)
		{
			foreach (Thing t in map.listerThings.ThingsOfDef(RimSiegeDefOf.RimSiege_Petard))
				if (t.Spawned && (t.Position - wall).LengthHorizontalSquared <= 4)
					return true;
			return false;
		}
	}
}
