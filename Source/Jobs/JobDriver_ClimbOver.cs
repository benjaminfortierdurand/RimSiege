using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.Jobs
{
	// up the tower or the ladder in plain sight, over the wall top, down the inside. the pawn stays spawned
	// the whole way, so it can be shot off. drawn with ForcedBodyOffset: on its base cell going up, on the
	// landing cell coming down
	public class JobDriver_ClimbOver : JobDriver
	{
		private Building_SiegeEngine Engine => job.GetTarget(TargetIndex.A).Thing as Building_SiegeEngine;
		private IntVec3 Inside => job.GetTarget(TargetIndex.B).Cell;

		private int climbTicks;
		private bool over;
		private bool ladder;
		private IntVec3 baseCell = IntVec3.Invalid;
		private IntVec3 wall = IntVec3.Invalid;

		private const float Height = 1.1f; // how far up the wall top is drawn, in cells

		// the ladder is a slower, steadier climb than the ramp
		private int Total => ladder ? 180 : 150;
		// tower: up the inside of the tower, then across the ramp. ladder: one long diagonal up its rungs
		private float RiseEnd => ladder ? 0.65f : 0.45f;
		private const float OverAt = 0.65f;

		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		public override Vector3 ForcedBodyOffset
		{
			get
			{
				if (climbTicks <= 0 || !baseCell.IsValid || !wall.IsValid) return Vector3.zero;
				float t = Mathf.Clamp01(climbTicks / (float)Total);
				Vector3 toWall = (wall - baseCell).ToVector3();
				Vector3 up = Vector3.forward * Height;
				Vector3 top = toWall + up; // wall top, from the base cell
				if (!over)
				{
					if (ladder) return top * Ease(t / OverAt);
					if (t < RiseEnd) return up * Ease(t / RiseEnd);
					return up + toWall * Ease((t - RiseEnd) / (OverAt - RiseEnd));
				}
				// coming down the inside face, from the wall top to the landing cell
				Vector3 from = (baseCell - Inside).ToVector3() + top;
				float p = Mathf.Clamp01((t - OverAt) / (1f - OverAt));
				return Vector3.Lerp(from, Vector3.zero, p * p);
			}
		}

		private static float Ease(float x)
		{
			x = Mathf.Clamp01(x);
			return x * (2f - x);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			// before the top they can still be called off, after it they are over whatever happens
			this.FailOn(() => !over && (Engine == null || !Engine.Spawned
				|| pawn.mindState.duty?.def != RimSiegeDefOf.RimSiege_ClimbOver));

			Toil setup = ToilMaker.MakeToil("ClimbSetup");
			setup.initAction = () =>
			{
				Building_SiegeEngine e = Engine;
				wall = e.breachTarget;
				ladder = e is Building_SiegeLadder;
				// the ladder foot stands a cell back from the wall, the tower's ramp starts at its middle
				IntVec3 foot = ladder ? e.BackCell : e.Position;
				baseCell = foot.Standable(Map) ? foot : e.Position;
			};
			setup.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return setup;

			Toil go = ToilMaker.MakeToil("ClimbGoto");
			go.initAction = () => pawn.pather.StartPath(baseCell, PathEndMode.OnCell);
			go.defaultCompleteMode = ToilCompleteMode.PatherArrival;
			go.FailOn(() => !over && !baseCell.Walkable(Map));
			yield return go;

			Toil climb = ToilMaker.MakeToil("Climb");
			climb.handlingFacing = true;
			climb.tickAction = () =>
			{
				climbTicks++;
				pawn.Rotation = Rot4.FromIntVec3(CardinalTo(wall - baseCell));
				if (!over && climbTicks >= Total * OverAt) GoOver();
				if (climbTicks >= Total) ReadyForNextToil();
			};
			climb.defaultCompleteMode = ToilCompleteMode.Never;
			yield return climb;

			Toil land = ToilMaker.MakeToil("ClimbLand");
			land.initAction = () =>
			{
				pawn.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_AssaultThroughBreach);
			};
			land.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return land;
		}

		// at the wall top the pawn moves to its landing cell, the offset carries the drawing on from there
		private void GoOver()
		{
			IntVec3 inside = Inside;
			if (!inside.Walkable(Map))
			{
				EndJobWith(JobCondition.Incompletable);
				return;
			}
			List<Thing> things = inside.GetThingList(Map);
			for (int i = things.Count - 1; i >= 0; i--)
				if (things[i] is Plant) things[i].Destroy(DestroyMode.Vanish); // landing on a bush, squash it
			over = true;
			pawn.Position = inside;
			pawn.Notify_Teleported(false, true);
			RimSiegeDefOf.RimSiege_CrewPush?.PlayOneShot(new TargetInfo(inside, Map));
		}

		private static IntVec3 CardinalTo(IntVec3 d) =>
			Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? new IntVec3(d.x >= 0 ? 1 : -1, 0, 0) : new IntVec3(0, 0, d.z >= 0 ? 1 : -1);

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref climbTicks, "climbTicks");
			Scribe_Values.Look(ref over, "over");
			Scribe_Values.Look(ref ladder, "ladder");
			Scribe_Values.Look(ref baseCell, "baseCell", IntVec3.Invalid);
			Scribe_Values.Look(ref wall, "wall", IntVec3.Invalid);
		}
	}
}
