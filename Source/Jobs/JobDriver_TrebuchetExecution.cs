using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.Jobs
{
	// same ceremony, but the block is a trebuchet and the blade is gravity
	public class JobDriver_TrebuchetExecution : JobDriver_ExecutionCeremony
	{
		protected override Toil FinalStrike()
		{
			Toil fling = ToilMaker.MakeToil("Fling");
			fling.initAction = delegate
			{
				Pawn victim = Victim;
				if (!victim.Spawned || !pawn.Position.InHorDistOf(victim.PositionHeld, 5f)) return;
				Thing treb = job.GetTarget(TargetIndex.C).Thing;
				Map map = pawn.Map;
				if (treb == null || map == null) return;

				IntVec3 dest = FindLandingCell(map, treb.Position, victim);
				if (!dest.IsValid)
				{
					// walls of mountain on every side: back to the old fashioned way
					ExecutionUtility.DoExecutionByCut(fling.actor, victim);
					ThoughtUtility.GiveThoughtsForPawnExecuted(victim, fling.actor, PawnExecutionKind.GenericBrutal);
					TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, victim);
					return;
				}

				PawnFlyer flyer = PawnFlyer.MakeFlyer(RimSiegeDefOf.RimSiege_CondemnedFlyer, victim, dest,
					null, null, flyWithCarriedThing: false, overrideStartVec: treb.TrueCenter());
				if (flyer == null) return;
				if (flyer is PawnFlyer_Condemned pf) pf.executioner = fling.actor;
				GenSpawn.Spawn(flyer, treb.Position, map);
				RimSiegeDefOf.RimSiege_CrewPush.PlayOneShot(new TargetInfo(treb.Position, map));
				FleckMaker.ThrowDustPuff(treb.TrueCenter(), map, 2.5f);
				Messages.Message("RimSiege_Flung".Translate(victim.LabelShort), flyer,
					MessageTypeDefOf.NeutralEvent);
			};
			fling.defaultCompleteMode = ToilCompleteMode.Instant;
			return fling;
		}

		// somewhere past the walls, away from the colony
		private static IntVec3 FindLandingCell(Map map, IntVec3 from, Pawn victim)
		{
			var builds = map.listerBuildings.allBuildingsColonist;
			IntVec3 center = map.Center;
			if (builds.Count > 0)
			{
				long sx = 0, sz = 0;
				int n = Mathf.Min(builds.Count, 100);
				for (int i = 0; i < n; i++) { sx += builds[i].Position.x; sz += builds[i].Position.z; }
				center = new IntVec3((int)(sx / n), 0, (int)(sz / n));
			}
			Vector3 dir = (from - center).ToVector3();
			if (dir.sqrMagnitude < 1f) dir = Rand.InsideUnitCircleVec3;
			dir.Normalize();

			for (int i = 0; i < 40; i++)
			{
				Vector3 d = dir.RotatedBy(Rand.Range(-35f, 35f));
				IntVec3 cell = from + (d * Rand.Range(26f, 38f)).ToIntVec3();
				if (!cell.InBounds(map) || cell.DistanceToEdge(map) < 4 || cell.Fogged(map)) continue;
				if (!JumpUtility.ValidJumpTarget(victim, map, cell)) continue;
				return cell;
			}
			return IntVec3.Invalid;
		}
	}
}
