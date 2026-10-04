using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimSiege.Jobs
{
	// the block scene: walk up, drum roll, cut
	public class JobDriver_ExecuteCaptain : JobDriver
	{
		private const int DrumRollTicks = 180;

		private Pawn Victim => (Pawn)job.targetA.Thing;

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.Reserve(Victim, job, 1, -1, null, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDestroyedOrNull(TargetIndex.A);
			this.FailOn(() => Victim.Dead || !Victim.IsPrisonerOfColony);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

			Toil drums = ToilMaker.MakeToil("DrumRoll");
			drums.initAction = delegate
			{
				RimSiegeDefOf.RimSiege_WarDrums.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
				CameraJumper.TryJump(Victim);
			};
			drums.tickAction = delegate
			{
				pawn.rotationTracker.FaceTarget(Victim);
			};
			drums.defaultCompleteMode = ToilCompleteMode.Delay;
			drums.defaultDuration = DrumRollTicks;
			yield return drums;

			Toil cut = ToilMaker.MakeToil("Cut");
			cut.initAction = delegate
			{
				ExecutionUtility.DoExecutionByCut(cut.actor, Victim);
				ThoughtUtility.GiveThoughtsForPawnExecuted(Victim, cut.actor, PawnExecutionKind.GenericBrutal);
				TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, Victim);
			};
			cut.defaultCompleteMode = ToilCompleteMode.Instant;
			cut.activeSkill = () => SkillDefOf.Melee;
			yield return cut;
		}
	}
}
