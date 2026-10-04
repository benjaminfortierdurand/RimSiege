using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.AI
{
	// walks into the jail (lord grants him doors) and carries the captain off map
	public class JobGiver_RetrieveCaptain : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (!(pawn.mindState.duty?.focus.Thing is Pawn captain)) return null;
			if (captain.Dead || !captain.Spawned || !captain.Downed) return null;
			if (captain.CarriedBy != null) return null;
			if (!pawn.CanReach(captain, PathEndMode.ClosestTouch, Danger.Deadly)) return null;
			if (!RCellFinder.TryFindBestExitSpot(pawn, out IntVec3 exit)) return null;

			Job job = JobMaker.MakeJob(RimSiegeDefOf.CarryDownedPawnToExit, captain, exit);
			job.count = 1;
			return job;
		}
	}
}
