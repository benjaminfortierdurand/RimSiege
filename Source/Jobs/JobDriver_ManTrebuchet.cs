using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Jobs
{
	// mans the trebuchet, never reloads it. vanilla ManTurret reloads too and skips the ammo filter
	// for non colonists, so they grab whatever junk shell is closest instead of the boulders
	public class JobDriver_ManTrebuchet : JobDriver
	{
		private Thing Turret => job.GetTarget(TargetIndex.A).Thing;

		public override bool TryMakePreToilReservations(bool errorOnFailed) =>
			pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			// Never toils dont self-cancel when the duty changes, same gotcha as the push job
			this.FailOn(() => pawn.mindState.duty?.def != RimSiegeDefOf.RimSiege_ManTrebuchet);
			this.FailOn(() => Turret.TryGetComp<CompMannable>() == null);

			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

			Toil man = ToilMaker.MakeToil("ManTrebuchet");
			man.tickAction = delegate
			{
				Turret.TryGetComp<CompMannable>()?.ManForATick(pawn);
				pawn.rotationTracker.FaceCell(Turret.Position);
			};
			man.handlingFacing = true;
			man.defaultCompleteMode = ToilCompleteMode.Never;
			man.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
			yield return man;
		}
	}
}
