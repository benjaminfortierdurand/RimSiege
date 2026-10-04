using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimSiege.Jobs
{
	// vanilla spectate without the job-override check that kept yanking the crowd back to work
	public class JobDriver_WatchExecution : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.ReserveSittableOrSpot(job.GetTarget(TargetIndex.A).Cell, job, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
			Toil watch = ToilMaker.MakeToil("Watch");
			watch.tickAction = delegate
			{
				pawn.rotationTracker.FaceCell(job.GetTarget(TargetIndex.B).Cell);
			};
			watch.defaultCompleteMode = ToilCompleteMode.Never;
			watch.handlingFacing = true;
			yield return watch;
		}
	}
}
