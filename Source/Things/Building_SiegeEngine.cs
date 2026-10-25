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
		// false keeps the sprite square to the wall like the crew around it, it only swings when Rotation flips
		protected virtual bool TurnsWithPath => true;
		private float drawAngle;
		private float targetAngle;

		public IntVec3 BackCell => Position - Rotation.FacingCell;

		// pushers stand behind it. carried engines (log ram, ladder) put their crew on the flanks
		public virtual IEnumerable<IntVec3> HandleCells
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

		// every crewman keeps one handle. nearest-free sent two or three to the same cell, they walked
		// glued together and carried the log stacked on one side. not saved, handed out again on load
		private readonly List<Pawn> crewSlots = new List<Pawn>();

		// handle in a wall or a tree (log running along the rampart): that crewman takes one of these instead,
		// open side or butt end. empty = old nearest free fallback
		public virtual IEnumerable<IntVec3> SpareCells { get { yield break; } }

		// where this crewman stands: own handle, else a spare. Invalid = nowhere set, caller falls back
		public IntVec3 SpotOf(Pawn p)
		{
			int i = crewSlots.IndexOf(p);
			if (i < 0)
			{
				i = crewSlots.FindIndex(q => q == null || !Crewing(q));
				if (i < 0) { crewSlots.Add(p); i = crewSlots.Count - 1; }
				else crewSlots[i] = p;
			}
			return Spot(i);
		}

		private IntVec3 Spot(int slot)
		{
			var handles = new List<IntVec3>(HandleCells);
			IntVec3 own = slot < handles.Count ? handles[slot] : IntVec3.Invalid; // more crew than handles
			if (UsableHandle(own)) return own;

			// spares go out in slot order, the n-th blocked crewman gets the n-th free spare, so two never
			// share one. a spare someone holds as their own handle is skipped
			int rank = 0;
			var taken = new List<IntVec3>();
			for (int j = 0; j < crewSlots.Count; j++)
			{
				if (crewSlots[j] == null || !Crewing(crewSlots[j])) continue;
				IntVec3 h = j < handles.Count ? handles[j] : IntVec3.Invalid;
				if (UsableHandle(h)) taken.Add(h);
				else if (j < slot) rank++;
			}
			foreach (IntVec3 c in SpareCells)
			{
				if (!UsableHandle(c) || taken.Contains(c)) continue;
				if (rank-- == 0) return c;
			}
			return IntVec3.Invalid;
		}

		private bool Crewing(Pawn q) =>
			q.Spawned && !q.Dead && q.CurJob?.def == RimSiegeDefOf.RimSiege_PushSiegeEngine && q.CurJob.targetA.Thing == this;

		// a closed door is standable, but a raider cant step into the colony's door: the crewman sent there
		// never arrived and the log waited along the wall
		public bool UsableHandle(IntVec3 c) => c.IsValid && c.InBounds(Map) && c.Standable(Map) && c.GetDoor(Map) == null;

		// one-flank carry (ladder, breaching ram): both men on the same side, the sprite eased toward them,
		// side swaps when a wall or trees take it. not saved, it settles again within a few ticks
		protected int carrySide = 1;
		protected float carryShift;
		protected IntVec3 Perp => Rotation.Rotated(RotationDirection.Clockwise).FacingCell;
		protected IntVec3 CarryFlank => Perp * carrySide;

		// handles = how many cells back from Position each carrier stands
		protected void TickCarryFlank(float toward, int[] handles)
		{
			if (!docked && this.IsHashIntervalTick(10))
			{
				IntVec3 s = CarryFlank;
				int here = 0, there = 0;
				foreach (int k in handles)
				{
					IntVec3 c = Position - Rotation.FacingCell * k;
					if (UsableHandle(c + s)) here++;
					if (UsableHandle(c - s)) there++;
				}
				if (there > here) carrySide = -carrySide;
			}
			carryShift = Mathf.MoveTowards(carryShift, carrySide * toward, 0.05f);
		}

		// carried engines only move with the crew under them, each one standing on their own spot.
		// nowhere set (boxed in): they hold on from wherever they could get to
		public bool OnHandle(Pawn p)
		{
			int i = crewSlots.IndexOf(p);
			IntVec3 c = i < 0 ? IntVec3.Invalid : Spot(i);
			if (c.IsValid) return p.Position == c;
			return (p.Position - Position).LengthHorizontalSquared <= 6;
		}

		// where a crewman in place looks: pushers lean on the engine, carriers look where they march
		public virtual IntVec3 CrewLookAt(Pawn p) => Position;

		// carriers on both flanks stand a whole cell off the axis: their body is drawn this much closer,
		// so the hands meet the load. 0 = drawn on their cell
		protected virtual float CrewPull => 0f;

		// in toward the axis from the flank of the crewman's spot, faded in over the last cell (JobDriver.ForcedBodyOffset)
		public Vector3 CrewDrawOffset(Pawn p, IntVec3 spot)
		{
			if (CrewPull <= 0f || !spot.IsValid) return Vector3.zero;
			IntVec3 perp = Perp;
			IntVec3 d = spot - Position;
			int side = Math.Sign(d.x * perp.x + d.z * perp.z);
			if (side == 0) return Vector3.zero;
			// tweened pos, not DrawPos: DrawPos adds this offset and would call back in here
			Vector3 gap = p.Drawer.tweener.TweenedPos - spot.ToVector3Shifted();
			float near = Mathf.Clamp01(2f - new Vector2(gap.x, gap.z).magnitude);
			return -perp.ToVector3() * (side * CrewPull * near);
		}

		// wheels throw dust and rumble. a log or a ladder in someone's arms does neither
		public virtual bool OnWheels => true;

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
				if (docked || !TurnsWithPath) targetAngle = Rotation.AsAngle;
				if (drawAngle != targetAngle)
					drawAngle = Mathf.MoveTowardsAngle(drawAngle, targetAngle, RotateDegreesPerTick);
			}

			if (OnWheels && !docked && drawOffset.sqrMagnitude > 0.0004f && this.IsHashIntervalTick(8))
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
