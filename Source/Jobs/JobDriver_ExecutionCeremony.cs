using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimSiege.Jobs
{
	// full scene: haul the condemned to the block, let the crowd gather, drum roll, cut. A=captain B=drop cell C=block
	public class JobDriver_ExecutionCeremony : JobDriver
	{
		private const int GatherTicks = 900;
		private const int DrumRollTicks = 420;

		protected Pawn Victim => (Pawn)job.targetA.Thing;

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.Reserve(Victim, job, 1, -1, null, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDestroyedOrNull(TargetIndex.A);
			this.FailOnDestroyedOrNull(TargetIndex.C);
			this.FailOn(() => Victim.Dead || (!Victim.IsPrisonerOfColony && Victim.CarriedBy != pawn));
			// show's over, however it ended: the crowd goes home
			AddFinishAction(delegate { DismissSpectators(); });

			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
				.FailOnSomeonePhysicallyInteracting(TargetIndex.A);
			yield return Toils_Haul.StartCarryThing(TargetIndex.A);
			yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

			Toil drop = ToilMaker.MakeToil("DropAtBlock");
			drop.initAction = delegate
			{
				pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out Thing _);
				KeepVictimWaiting(GatherTicks + DrumRollTicks + 180);
				// the haul may have outlasted the first invitations, call the crowd back
				if (job.GetTarget(TargetIndex.C).Thing is Thing block)
					GameComponent_SiegeDemands.InviteSpectators(block, pawn);
			};
			drop.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return drop;

			// step off the block cell so the condemned lies there alone
			Toil sidestep = ToilMaker.MakeToil("Sidestep");
			sidestep.initAction = delegate
			{
				IntVec3 side = IntVec3.Invalid;
				foreach (IntVec3 adj in GenAdj.CellsAdjacent8Way(pawn.Position, Rot4.North, IntVec2.One))
					if (adj.InBounds(base.Map) && adj.Standable(base.Map)) { side = adj; break; }
				if (side.IsValid) pawn.pather.StartPath(side, PathEndMode.OnCell);
				else sidestep.actor.jobs.curDriver.ReadyForNextToil();
			};
			sidestep.defaultCompleteMode = ToilCompleteMode.PatherArrival;
			yield return sidestep;

			// the condemned stands at the block while the colony walks over to watch
			Toil gather = ToilMaker.MakeToil("GatherCrowd");
			gather.tickAction = delegate
			{
				pawn.rotationTracker.FaceTarget(Victim);
				KeepVictimWaiting(600);
			};
			gather.defaultCompleteMode = ToilCompleteMode.Delay;
			gather.defaultDuration = GatherTicks;
			yield return gather;

			Toil drums = ToilMaker.MakeToil("DrumRoll");
			drums.initAction = delegate
			{
				RimSiegeDefOf.RimSiege_WarDrums.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
				CameraJumper.TryJump(Victim);
			};
			drums.tickAction = delegate
			{
				pawn.rotationTracker.FaceTarget(Victim);
				KeepVictimWaiting(600);
			};
			drums.defaultCompleteMode = ToilCompleteMode.Delay;
			drums.defaultDuration = DrumRollTicks;
			yield return drums;

			yield return FinalStrike();
		}

		// the blade. the trebuchet variant overrides this
		protected virtual Toil FinalStrike()
		{
			Toil cut = ToilMaker.MakeToil("Cut");
			cut.initAction = delegate
			{
				// slipped away during the drums: no blade at a distance, the case timer picks it up
				if (!pawn.Position.InHorDistOf(Victim.PositionHeld, 5f)) return;
				ExecutionUtility.DoExecutionByCut(cut.actor, Victim);
				ThoughtUtility.GiveThoughtsForPawnExecuted(Victim, cut.actor, PawnExecutionKind.GenericBrutal);
				TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, Victim);
			};
			cut.defaultCompleteMode = ToilCompleteMode.Instant;
			cut.activeSkill = () => SkillDefOf.Melee;
			return cut;
		}

		private void KeepVictimWaiting(int ticks)
		{
			if (Victim.Spawned && !Victim.Dead && !Victim.Downed && !Victim.pather.MovingNow
				&& Victim.CurJobDef != JobDefOf.Wait)
				Victim.jobs?.StartJob(JobMaker.MakeJob(JobDefOf.Wait, ticks), JobCondition.InterruptForced);
		}

		private void DismissSpectators()
		{
			Map map = pawn.MapHeld;
			if (map == null) return;
			foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
				if (p.CurJobDef == RimSiegeDefOf.RimSiege_WatchExecutionJob)
					p.jobs.EndCurrentJob(JobCondition.Succeeded);
		}
	}
}
