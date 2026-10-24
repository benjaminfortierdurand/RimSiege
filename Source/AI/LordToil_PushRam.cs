using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimSiege.Things;

namespace RimSiege.AI
{
	public class LordToil_PushRam : LordToil_PushEngine
	{
		private const int RamInterval = 180;
		private const int MaxChainedBreaches = 3; // gatehouses yes, eating through the whole base no
		private int lastRamTick = -99999;

		private LordToilData_PushRam RData => (LordToilData_PushRam)data;

		public LordToil_PushRam()
		{
			data = new LordToilData_PushRam();
		}

		protected override ThingDef DefaultEngineDef => RimSiegeDefOf.RimSiege_BatteringRam;

		// four guys trotting with a log beat a wagon rolling on its wheels
		protected override int AdvanceInterval => Engine is Building_LogRam ? 20 : base.AdvanceInterval;

		protected override void DockedTick(Building_SiegeEngine engine, int now)
		{
			// stale dock after a retarget, roll closer first
			if ((engine.Position - engine.breachTarget).LengthHorizontalSquared > DockDistanceSq)
			{
				engine.docked = false;
				InvalidatePath();
				return;
			}

			// no crew no bashing. the swing winds up SwingLead ticks ahead so the blow lands on the hit
			if (CountPushers(engine) < engine.crewNeeded) return;
			var ram = (Building_BatteringRam)engine;
			// fresh dock, or crew back after a gap: wind up first instead of hitting on the spot
			if (now - lastRamTick > RamInterval) lastRamTick = now - RamInterval + Building_BatteringRam.SwingLead;
			if (now - lastRamTick >= RamInterval - Building_BatteringRam.SwingLead && !ram.SwingStartedAfter(lastRamTick))
				ram.BeginSwing(now);
			if (now - lastRamTick >= RamInterval)
			{
				RamHit(ram);
				lastRamTick = now;
			}
		}

		// the blow lands: the crew lunge with it, dust comes off the wall, the screen jumps a touch
		private void Impact(Building_BatteringRam engine, Thing target)
		{
			foreach (Pawn p in lord.ownedPawns)
				if (p.Spawned && Pushing(engine, p))
					p.Drawer.Notify_MeleeAttackOn(target);
			Vector3 face = engine.breachTarget.ToVector3Shifted() - engine.Rotation.FacingCell.ToVector3() * 0.5f;
			for (int i = 0; i < 4; i++)
				FleckMaker.ThrowDustPuff(face + new Vector3(Rand.Range(-0.6f, 0.6f), 0f, Rand.Range(-0.6f, 0.6f)), Map, Rand.Range(0.9f, 1.5f));
			if (Map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(engine.ShakeOnHit);
		}

		private void RamHit(Building_BatteringRam engine)
		{
			engine.FaceTarget();

			// lock the exact thing on the first hit and pound it till its gone. handles doors
			// (walkable but locked), walls, and rubble-leaving mods
			if (engine.breachThing == null)
				engine.breachThing = engine.breachTarget.GetEdifice(Map);

			Thing target = engine.breachThing;
			if (target != null && !target.Destroyed && target.Spawned)
			{
				RimSiegeDefOf.RimSiege_RamImpact?.PlayOneShot(new TargetInfo(engine.Position, Map));
				target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, engine.ramDamage, armorPenetration: 1f, instigator: engine));
				Impact(engine, target);
				return;
			}

			// did the hole open the sealed part. no sealed part on record means the target never was a
			// barrier (guns got there first, or the colony has no enclosure), the way in is open already
			IntVec3 hole = engine.breachTarget;
			bool open = !engine.sealedBehind.IsValid
				|| Map.reachability.CanReach(engine.Position, engine.sealedBehind, PathEndMode.OnCell, SiegeLauncher.NoDoors);

			// airlock or gatehouse yard: we are in, but the colony is behind one more door. AimAt on the
			// new target probes again, so a second airlock chains the same way
			if (open && engine.sealedBehind.IsValid && !engine.colonyBehind
				&& RData.chainedBreaches < MaxChainedBreaches && engine.nextBreach.IsValid && BlocksBreach(engine.nextBreach))
			{
				Retarget(engine, engine.nextBreach);
				return;
			}

			// gatehouses have two doors and walls come layered. if the way in is still blocked further
			// down the line, roll forward and bash that too instead of calling it a breach
			if (!open && RData.chainedBreaches < MaxChainedBreaches)
			{
				IntVec3 step = hole - engine.Position;
				if (Math.Abs(step.x) + Math.Abs(step.z) == 1)
				{
					for (int i = 1; i <= 6; i++)
					{
						IntVec3 c = hole + step * i;
						if (!c.InBounds(Map)) break;
						if (BlocksBreach(c)) { Retarget(engine, c); return; }
						if (c.Standable(Map) && Map.reachability.CanReach(c, engine.sealedBehind, PathEndMode.OnCell, SiegeLauncher.NoDoors))
						{ open = true; break; }
					}
				}
			}

			// hole leads nowhere. pick a fresh target instead of storming empty ground
			if (!open && RData.chainedBreaches < MaxChainedBreaches && SiegeLauncher.TryFindSiegeTarget(Map, out IntVec3 fresh))
			{
				Retarget(engine, fresh);
				return;
			}

			lord.ReceiveMemo("BreachOpen");
		}

		private void Retarget(Building_BatteringRam engine, IntVec3 c)
		{
			RData.chainedBreaches++;
			engine.breachThing = null;
			engine.AimAt(c);
			engine.docked = false;
			InvalidatePath(); // else it insta-redocks on the consumed path
		}

		// solid wall or a door closed to us. rubble n other passable leftovers dont count
		private bool BlocksBreach(IntVec3 c)
		{
			Building ed = c.GetEdifice(Map);
			if (ed == null) return false;
			if (ed.def.passability == Traversability.Impassable) return true;
			return ed is Building_Door door && !door.FreePassage;
		}
	}
}
