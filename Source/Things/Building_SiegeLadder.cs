using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	// tribal ladder: two carry it to the rampart and plant it, then the war band goes over one at a time
	// (LordToil_SiegeLadder, same ferry as the tower). one top-down sprite, ~3.2 cells long.
	// carried: both men on one flank, one near each end, ladder held against their side.
	// planted: it tips forward so the top end sticks out over the wall
	public class Building_SiegeLadder : Building_SiegeTower
	{
		private const float CarryShift = 0.4f;   // toward the carriers, rail in their hands
		private const float CarryBack = -1.12f;  // top end at the engine cell's front edge
		private const float PlantedFwd = 0.18f;  // top end ~0.3 past the far face of the wall

		private int side = 1;   // flank the carriers walk on, swaps when a wall or trees take it
		private float shift;    // eased toward side * CarryShift
		private float lean;     // 0 carried, 1 planted on the wall

		public Building_SiegeLadder()
		{
			crewNeeded = 2;
		}

		protected override float RotateDegreesPerTick => 6f; // light, swings round quick
		protected override bool TurnsWithPath => false;  // carriers hold it square, it shouldnt slide out of their hands
		public override bool OnWheels => false;

		private IntVec3 Flank => Rotation.Rotated(RotationDirection.Clockwise).FacingCell * side;

		public override IEnumerable<IntVec3> HandleCells
		{
			get
			{
				IntVec3 s = Flank;
				yield return Position + s;
				yield return Position - Rotation.FacingCell * 2 + s;
			}
		}

		// one end blocked: that man takes the same end on the other flank, then the middle, then the foot
		public override IEnumerable<IntVec3> SpareCells
		{
			get
			{
				IntVec3 s = Flank;
				IntVec3 back1 = Position - Rotation.FacingCell;
				IntVec3 back2 = back1 - Rotation.FacingCell;
				yield return Position - s;
				yield return back2 - s;
				yield return back1 + s;
				yield return back1 - s;
				yield return back2 - Rotation.FacingCell;
			}
		}

		public override IntVec3 CrewLookAt(Pawn p) => p.Position + Rotation.FacingCell;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			if (docked) lean = 1f; // loaded planted, dont replay the tip
		}

		protected override void Tick()
		{
			base.Tick();
			if (!docked && this.IsHashIntervalTick(10))
			{
				// walking along a wall: carry from the open flank
				IntVec3 s = Flank, back2 = Position - Rotation.FacingCell * 2;
				int here = (UsableHandle(Position + s) ? 1 : 0) + (UsableHandle(back2 + s) ? 1 : 0);
				int there = (UsableHandle(Position - s) ? 1 : 0) + (UsableHandle(back2 - s) ? 1 : 0);
				if (there > here) side = -side;
			}
			shift = Mathf.MoveTowards(shift, side * CarryShift, 0.05f);
			lean = Mathf.MoveTowards(lean, docked ? 1f : 0f, 1f / 40f);
		}

		protected override Vector3 ExtraDrawOffset
		{
			get
			{
				Vector3 fwd = Rotation.FacingCell.ToVector3();
				Vector3 perp = Rotation.Rotated(RotationDirection.Clockwise).FacingCell.ToVector3();
				float t = lean * lean * (3f - 2f * lean); // ease in and out
				return fwd * Mathf.Lerp(CarryBack, PlantedFwd, t) + perp * shift * (1f - t);
			}
		}
	}
}
