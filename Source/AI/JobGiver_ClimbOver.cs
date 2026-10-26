using RimWorld;
using Verse;
using Verse.AI;
using RimSiege.Things;

namespace RimSiege.AI
{
	// duty focus = the tower or ladder, focusSecond = the landing cell past the wall
	public class JobGiver_ClimbOver : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			PawnDuty duty = pawn.mindState?.duty;
			var engine = duty?.focus.Thing as Building_SiegeEngine;
			IntVec3 inside = duty?.focusSecond.Cell ?? IntVec3.Invalid;
			// already over (job cut short after the top): get on with the assault, dont climb back
			if (engine == null || !engine.Spawned || !inside.IsValid || PastTheWall(pawn, engine))
			{
				if (duty != null) pawn.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_AssaultThroughBreach);
				return null;
			}
			return JobMaker.MakeJob(RimSiegeDefOf.RimSiege_ClimbOverJob, engine, inside);
		}

		private static bool PastTheWall(Pawn pawn, Building_SiegeEngine engine)
		{
			IntVec3 dir = engine.breachTarget - engine.Position;
			IntVec3 rel = pawn.Position - engine.breachTarget;
			return rel.x * dir.x + rel.z * dir.z > 0;
		}
	}
}
