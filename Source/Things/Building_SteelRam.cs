using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	// gun era breaching ram: steel tube, strike plate each end, handle bar on top. two breachers on one
	// flank, one at each end of the bar, the ram held against their side like the ladder. 2 cells long:
	// drawn half a cell back so the front plate meets the door. same swing as the log ram, lighter crew,
	// quicker blows, and it doesnt burn
	public class Building_SteelRam : Building_LogRam
	{
		// how far the ram sits toward the breachers. side view needs more: the man in the next row down is
		// drawn a cell lower, at 0.4 the ram floated over his head. the breachers stand a cell off the door's
		// line, so at the door the ram rests a bit toward them, the wind-up pulls it into their chests and the
		// blow swings it across: the plate lands centred on the door (a fixed 0.5 hit half on the wall)
		private const float CarrySide = 0.6f, CarryTop = 0.5f, AtDoor = 0.3f, PullIn = 0.25f;
		private static readonly int[] Grips = { 0, 1 };

		public Building_SteelRam()
		{
			crewNeeded = 2;
			ramDamage = 75;
		}

		protected override float SwingBack => 0.26f;
		public override float ShakeOnHit => 0.08f;
		public override int StrikeInterval => 100; // short rest between blows, the swing does the work

		// front and back of the handle bar, same flank
		public override IEnumerable<IntVec3> HandleCells
		{
			get
			{
				IntVec3 s = CarryFlank;
				yield return Position + s;
				yield return Position - Rotation.FacingCell + s;
			}
		}

		// grip blocked: same grip from the other flank, then one cell further back, then behind the ram
		public override IEnumerable<IntVec3> SpareCells
		{
			get
			{
				IntVec3 s = CarryFlank;
				IntVec3 back1 = Position - Rotation.FacingCell;
				IntVec3 back2 = back1 - Rotation.FacingCell;
				yield return Position - s;
				yield return back1 - s;
				yield return back2 + s;
				yield return back2 - s;
				yield return back2;
			}
		}

		// sideways part of the blow, toward the breachers positive: in to them on the wind-up, across to the
		// door's line on the lunge (cancels the rest offset at the hit), eased back while it settles
		private float Across()
		{
			int t = SwingTick;
			if (!docked || t < 0 || t >= SwingLead + Settle) return 0f;
			if (t < WindUp) return PullIn * Mathf.SmoothStep(0f, 1f, t / (float)WindUp);
			if (t < SwingLead)
			{
				float p = (t - WindUp) / (float)Lunge;
				return Mathf.Lerp(PullIn, -AtDoor, p * p);
			}
			return Mathf.Lerp(-AtDoor, 0f, Mathf.SmoothStep(0f, 1f, (t - SwingLead) / (float)Settle));
		}

		protected override void Tick()
		{
			base.Tick();
			TickCarryFlank(docked ? AtDoor : Rotation.IsHorizontal ? CarrySide : CarryTop, Grips);
		}

		protected override Vector3 ExtraDrawOffset
		{
			get
			{
				Vector3 fwd = Rotation.FacingCell.ToVector3();
				return -fwd * 0.5f + SwingOffset + Perp.ToVector3() * (carryShift + carrySide * Across());
			}
		}
	}
}
