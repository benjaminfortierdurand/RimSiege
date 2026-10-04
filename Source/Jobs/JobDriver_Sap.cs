using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege.Jobs
{
	public class JobDriver_Sap : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.AddFailCondition(() => TargetA.Cell.Walkable(Map)); // wall already walkable (breach open) -> stop

			yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);

			Toil plant = Toils_General.Wait(240);
			plant.WithProgressBarToilDelay(TargetIndex.A);
			plant.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			yield return plant;

			Toil place = ToilMaker.MakeToil("PlacePetard");
			place.initAction = delegate
			{
				if (RimSiegeDefOf.RimSiege_Petard == null) return;
				Thing petard = ThingMaker.MakeThing(RimSiegeDefOf.RimSiege_Petard);
				petard.SetFaction(pawn.Faction);
				GenSpawn.Spawn(petard, pawn.Position, Map);
				petard.TryGetComp<CompExplosive>()?.StartWick(pawn);
			};
			place.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return place;

			Toil setRetreat = ToilMaker.MakeToil("SapSetRetreat");
			setRetreat.initAction = delegate
			{
				IntVec3 wall = TargetA.Cell;
				if (CellFinder.TryFindRandomCellNear(pawn.Position, Map, 8,
						c => c.Standable(Map) && (c - wall).LengthHorizontalSquared >= 36, out IntVec3 dest))
					job.targetB = dest;
				else
					job.targetB = pawn.Position;
			};
			setRetreat.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return setRetreat;

			yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
		}
	}
}
