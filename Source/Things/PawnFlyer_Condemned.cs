using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimSiege.Things
{
	// the arc ends how arcs end. survive it and you're a free man
	public class PawnFlyer_Condemned : PawnFlyer
	{
		public Pawn executioner;

		protected override void RespawnPawn()
		{
			Pawn victim = FlyingPawn;
			base.RespawnPawn();
			if (victim == null || victim.Dead) return;

			IntVec3 cell = victim.PositionHeld;
			Map map = victim.MapHeld;
			for (int i = 0; i < 3; i++)
			{
				if (victim.Dead) break;
				victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(25f, 45f), 10f, -1f, executioner));
			}
			if (map != null)
			{
				RimSiegeDefOf.RimSiege_RamImpact.PlayOneShot(new TargetInfo(cell, map));
				FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, 3f);
			}

			if (victim.Dead)
			{
				if (executioner != null)
				{
					ThoughtUtility.GiveThoughtsForPawnExecuted(victim, executioner, PawnExecutionKind.GenericBrutal);
					TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, executioner, victim);
				}
				return;
			}

			// stuck the landing: the sentence is considered served
			if (victim.guest?.HostFaction != null) victim.guest.SetGuestStatus(null);
			if (!victim.Downed && victim.Spawned && RCellFinder.TryFindBestExitSpot(victim, out IntVec3 exit))
			{
				Job run = JobMaker.MakeJob(JobDefOf.Goto, exit);
				run.exitMapOnArrival = true;
				victim.jobs?.StartJob(run, JobCondition.InterruptForced);
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref executioner, "executioner");
		}
	}
}
