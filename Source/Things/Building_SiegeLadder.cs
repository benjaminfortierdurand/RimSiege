using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	// tribal ladder: two carry it to the rampart and plant it, then the war band goes over one at a time
	// (LordToil_SiegeLadder, same ferry as the tower). one top-down sprite, ~3.2 cells long.
	// carried: both men on one flank, one near each end, ladder held against their side.
	// planted: it tips forward so the top end rests on the wall, the whole ladder stays on the outside
	public class Building_SiegeLadder : Building_SiegeTower
	{
		private const float CarryShift = 0.4f;   // toward the carriers, rail in their hands
		private const float CarryBack = -1.12f;  // top end at the engine cell's front edge
		private const float PlantedFwd = -0.62f; // top end on the middle of the wall cell (half length 1.62)

		private static readonly int[] Ends = { 0, 2 };
		private float lean;     // 0 carried, 1 planted on the wall

		public Building_SiegeLadder()
		{
			crewNeeded = 2;
		}

		protected override float RotateDegreesPerTick => 6f; // light, swings round quick
		protected override bool TurnsWithPath => false;  // carriers hold it square, it shouldnt slide out of their hands
		public override bool OnWheels => false;

		public override IEnumerable<IntVec3> HandleCells
		{
			get
			{
				IntVec3 s = CarryFlank;
				yield return Position + s;
				yield return Position - Rotation.FacingCell * 2 + s;
			}
		}

		// one end blocked: that man takes the same end on the other flank, then the middle, then the foot
		public override IEnumerable<IntVec3> SpareCells
		{
			get
			{
				IntVec3 s = CarryFlank;
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
			TickCarryFlank(CarryShift, Ends);
			lean = Mathf.MoveTowards(lean, docked ? 1f : 0f, 1f / 40f);
		}

		protected override Vector3 ExtraDrawOffset
		{
			get
			{
				Vector3 fwd = Rotation.FacingCell.ToVector3();
				float t = lean * lean * (3f - 2f * lean); // ease in and out
				return fwd * Mathf.Lerp(CarryBack, PlantedFwd, t) + Perp.ToVector3() * carryShift * (1f - t);
			}
		}
	}
}
