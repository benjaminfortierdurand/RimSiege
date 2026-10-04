using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.AI
{
	public class JobGiver_ManTrebuchet : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (!(pawn.mindState.duty?.focus.Thing is Building treb)) return null;
			if (treb.Destroyed || !treb.Spawned) return null;
			if (treb.TryGetComp<CompMannable>() == null) return null;
			if (!pawn.CanReserveAndReach(treb, PathEndMode.InteractionCell, Danger.Deadly)) return null;
			return JobMaker.MakeJob(RimSiegeDefOf.RimSiege_ManTrebuchetJob, treb);
		}
	}
}
