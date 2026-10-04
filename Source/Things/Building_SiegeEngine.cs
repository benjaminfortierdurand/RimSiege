using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	public abstract class Building_SiegeEngine : Building
	{
		public IntVec3 breachTarget = IntVec3.Invalid; // wall/door we're after
		// probed while the wall still stands: what the hole opens onto, whether the colony is right there
		// or its just an airlock, and the next closed door on from it
		public IntVec3 sealedBehind = IntVec3.Invalid;
		public bool colonyBehind;
		public IntVec3 nextBreach = IntVec3.Invalid;
		public bool docked;
		public int crewNeeded = 3;

		// draw offset eaten at constant speed so the roll looks continuous
		private Vector3 drawOffset;
		private float glideStep;

		// 4-way sprites snap to Rotation. a single top-down sprite (retextures) turns freely while rolling
		private bool TopDownArt => !(Graphic is Graphic_Multi);
		protected virtual float RotateDegreesPerTick => 3f;
		private float drawAngle;
		private float targetAngle;

		public IntVec3 BackCell => Position - Rotation.FacingCell;

		public IEnumerable<IntVec3> HandleCells
		{
			get
			{
				IntVec3 perp = Rotation.Rotated(RotationDirection.Clockwise).FacingCell;
				IntVec3 back1 = Position - Rotation.FacingCell;
				IntVec3 back2 = back1 - Rotation.FacingCell;
				yield return back1;
				yield return back1 + perp;
				yield return back1 - perp;
				yield return back2;
				yield return back2 + perp;
				yield return back2 - perp;
			}
		}

		public void AimAt(IntVec3 target)
		{
			breachTarget = target;
			if (Spawned) SiegeLauncher.ProbeBehind(Map, target, out sealedBehind, out colonyBehind, out nextBreach);
			else { sealedBehind = IntVec3.Invalid; colonyBehind = false; nextBreach = IntVec3.Invalid; }
			Rotation = RotToward(Position, target);
			targetAngle = Rotation.AsAngle;
		}

		public void FaceTarget()
		{
			if (!breachTarget.IsValid) return;
			Rotation = RotToward(Position, breachTarget);
			targetAngle = Rotation.AsAngle;
		}

		public void StepTo(IntVec3 cell, int glideTicks = 30)
		{
			// Rot4 stays pointed at the wall for game logic (pusher slots), only the drawing turns freely
			if (breachTarget.IsValid) Rotation = RotToward(Position, breachTarget);
			Vector3 fromDraw = Position.ToVector3Shifted() + drawOffset;
			Position = cell;
			drawOffset = fromDraw - Position.ToVector3Shifted();
			glideStep = drawOffset.magnitude / Mathf.Max(glideTicks, 1);
			if (drawOffset.sqrMagnitude > 0.001f) targetAngle = (-drawOffset).AngleFlat();
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			if (!respawningAfterLoad) { drawAngle = Rotation.AsAngle; targetAngle = drawAngle; }
		}

		protected override void Tick()
		{
			base.Tick();
			if (drawOffset != Vector3.zero)
				drawOffset = Vector3.MoveTowards(drawOffset, Vector3.zero, glideStep > 0f ? glideStep : 0.04f);

			if (TopDownArt)
			{
				if (docked) targetAngle = Rotation.AsAngle;
				if (drawAngle != targetAngle)
					drawAngle = Mathf.MoveTowardsAngle(drawAngle, targetAngle, RotateDegreesPerTick);
			}

			if (!docked && drawOffset.sqrMagnitude > 0.0004f && this.IsHashIntervalTick(8))
				ThrowRollingDust();
		}

		private void ThrowRollingDust()
		{
			Vector3 fwd = Rotation.FacingCell.ToVector3();
			Vector3 side = Rotation.Rotated(RotationDirection.Clockwise).FacingCell.ToVector3();
			Vector3 basePos = DrawPos + drawOffset;
			for (int i = 0; i < 2; i++)
			{
				Vector3 p = basePos + fwd * Rand.Range(-1.6f, 1.0f) + side * Rand.Range(-1.7f, 1.7f);
				FleckMaker.ThrowDustPuff(p, Map, Rand.Range(1.4f, 2.1f));
			}
		}

		protected virtual Vector3 ExtraDrawOffset => Vector3.zero;

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			Vector3 loc = drawLoc + drawOffset + ExtraDrawOffset;
			if (TopDownArt)
			{
				Graphic.Draw(loc, Rot4.North, this, drawAngle);
				return;
			}
			base.DrawAt(loc, flip);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref breachTarget, "breachTarget", IntVec3.Invalid);
			Scribe_Values.Look(ref sealedBehind, "sealedBehind", IntVec3.Invalid);
			Scribe_Values.Look(ref colonyBehind, "colonyBehind");
			Scribe_Values.Look(ref nextBreach, "nextBreach", IntVec3.Invalid);
			Scribe_Values.Look(ref docked, "docked");
			Scribe_Values.Look(ref crewNeeded, "crewNeeded", 3);
			Scribe_Values.Look(ref drawOffset, "drawOffset");
			Scribe_Values.Look(ref glideStep, "glideStep");
			Scribe_Values.Look(ref drawAngle, "drawAngle");
			Scribe_Values.Look(ref targetAngle, "targetAngle");
		}

		protected static Rot4 RotToward(IntVec3 from, IntVec3 to)
		{
			int dx = to.x - from.x, dz = to.z - from.z;
			if (Math.Abs(dx) >= Math.Abs(dz))
				return dx >= 0 ? Rot4.East : Rot4.West;
			return dz >= 0 ? Rot4.North : Rot4.South;
		}
	}
}
