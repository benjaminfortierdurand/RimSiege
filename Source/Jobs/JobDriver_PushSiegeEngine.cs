using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using RimSiege.Things;

namespace RimSiege.Jobs
{
	// doesnt actually move it, the lord toil counts pushers and does that. kill the crew and it stalls
	public class JobDriver_PushSiegeEngine : JobDriver
	{
		private Building_SiegeEngine Engine => job.GetTarget(TargetIndex.A).Thing as Building_SiegeEngine;

		// last spot the tick handed out. not saved, set again on the next tick
		private IntVec3 spot = IntVec3.Invalid;

		// carriers lean in to the load (vanilla adds this to DrawPos, no patch)
		public override Vector3 ForcedBodyOffset
		{
			get
			{
				var engine = Engine;
				return engine != null && engine.Spawned ? engine.CrewDrawOffset(pawn, spot) : Vector3.zero;
			}
		}

		// handles move with the engine -> no hard reservation
		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			this.FailOn(() => Engine == null || Engine.Destroyed
				|| pawn.mindState.duty?.def != RimSiegeDefOf.RimSiege_PushRam);

			var push = new Toil { defaultCompleteMode = ToilCompleteMode.Never, handlingFacing = true };
			push.tickAction = () =>
			{
				var engine = Engine;
				if (engine == null || !engine.Spawned) return;

				// own handle or a spare. nothing set (boxed in) -> nearest free handle
				spot = engine.SpotOf(pawn);
				bool own = spot.IsValid;
				if (!own)
					spot = engine.HandleCells
						.Where(c => engine.UsableHandle(c)
									&& (pawn.Map.thingGrid.ThingAt<Pawn>(c) == null || c == pawn.Position))
						.OrderBy(c => pawn.Position.DistanceToSquared(c))
						.DefaultIfEmpty(engine.BackCell)
						.First();

				if (pawn.Position != spot) // not in place yet -> follow the engine
				{
					// engine stepped while walking: head for the new handle now, not after the old one
					if (!pawn.pather.Moving || (own && pawn.pather.Destination.Cell != spot))
						pawn.pather.StartPath(spot, PathEndMode.OnCell);
					return;
				}

				pawn.rotationTracker.FaceCell(engine.CrewLookAt(pawn)); // in place, push (or carry)
				if (pawn.IsHashIntervalTick(60))
					FleckMaker.ThrowDustPuff(pawn.DrawPos, pawn.Map, 0.7f);
			};
			yield return push;
		}
	}
}
