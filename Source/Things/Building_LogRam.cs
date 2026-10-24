using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	// tribal ram: a log on rope grips carried by four, two each side. no roof, no wheels.
	// the art (drawSize 5) puts its grips 2 cells apart, so drawn one cell back the front grips sit beside
	// the engine cell, the back ones beside back2, and the tip meets the wall face when docked
	public class Building_LogRam : Building_BatteringRam
	{
		public Building_LogRam()
		{
			crewNeeded = 4;
			ramDamage = 85;
		}

		protected override float SwingBack => 0.32f;
		public override float ShakeOnHit => 0.06f;
		public override bool OnWheels => false;

		public override IEnumerable<IntVec3> HandleCells
		{
			get
			{
				IntVec3 perp = Rotation.Rotated(RotationDirection.Clockwise).FacingCell;
				IntVec3 back2 = Position - Rotation.FacingCell * 2;
				yield return Position + perp;
				yield return Position - perp;
				yield return back2 + perp;
				yield return back2 - perp;
			}
		}

		// along a wall: the blocked side moves to the middle of the open side, then the butt end
		public override IEnumerable<IntVec3> SpareCells
		{
			get
			{
				IntVec3 perp = Rotation.Rotated(RotationDirection.Clockwise).FacingCell;
				IntVec3 back1 = Position - Rotation.FacingCell;
				IntVec3 back3 = Position - Rotation.FacingCell * 3;
				yield return back1 + perp;
				yield return back1 - perp;
				yield return back3;
				yield return back3 + perp;
				yield return back3 - perp;
			}
		}

		public override IntVec3 CrewLookAt(Pawn p) => p.Position + Rotation.FacingCell;

		protected override Vector3 ExtraDrawOffset => -Rotation.FacingCell.ToVector3() + SwingOffset;
	}
}
