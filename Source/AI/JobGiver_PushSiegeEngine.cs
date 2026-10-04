using RimWorld;
using Verse;
using Verse.AI;
using RimSiege.Things;

namespace RimSiege.AI
{
	// DutyDef.thinkNode + ThinkNode_JobGiver.TryGiveJob is the proper hook, no forced StartJob.
	public class JobGiver_PushSiegeEngine : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			var engine = pawn.mindState?.duty?.focus.Thing as Building_SiegeEngine;
			if (engine == null || engine.Destroyed || !engine.Spawned)
				return null; // crew works the engine as long as it exists
			return JobMaker.MakeJob(RimSiegeDefOf.RimSiege_PushSiegeEngine, engine);
		}
	}
}
