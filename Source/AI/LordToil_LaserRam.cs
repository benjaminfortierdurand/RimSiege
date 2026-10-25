using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSiege.Things;

namespace RimSiege.AI
{
	// spacer laser ram: pushed like the ram but parked twelve cells out in front of the gate, then it charges and
	// holds a beam on the gate till it melts. the beam burns its way there: plants and rock chunks on the line go
	// at once, pawns and low colony stuff (sandbags, barricades, traps) take the burn, and the first solid thing
	// (a rock outcrop) stops it until it melts too. its own side is never hit. the crew stay on it: no crew, no
	// beam. gates in a row and airlocks chain like the ram's
	public class LordToil_LaserRam : LordToil_PushRam
	{
		private const int BurnInterval = 15;
		private const int BurnDamage = 8;   // RimSiege_LaserBlast is a Bomb: x2 on doors, x4 on walls and rock, ~64 a second on a door

		protected override ThingDef DefaultEngineDef => RimSiegeDefOf.RimSiege_LaserRam;
		protected override int DockDistanceSq => 144;
		protected override int AdvanceInterval => 45; // heavy

		protected override void DockedTick(Building_SiegeEngine engine, int now)
		{
			if (StaleDock(engine)) return;
			var ram = (Building_LaserRam)engine;
			engine.FaceTarget();
			Thing gate = LockTarget(ram);
			if (gate == null)
			{
				ram.BeamTick(false, null, null);
				TargetDown(ram);
				return;
			}

			// the cells between the muzzle and the gate, and the first solid thing on them
			List<IntVec3> line = GenSight.PointsOnLineOfSight(ram.Position, gate.Position)
				.Where(c => c != ram.Position && c != gate.Position).ToList();
			Thing block = null;
			foreach (IntVec3 c in line)
			{
				Building ed = c.InBounds(Map) ? c.GetEdifice(Map) : null;
				if (ed != null && ed.Faction != lord.faction && ed.def.passability == Traversability.Impassable) { block = ed; break; }
			}

			bool crewed = CountPushers(engine) >= engine.crewNeeded;
			if (!ram.BeamTick(crewed, gate, block ?? gate) || now % BurnInterval != 0) return;
			foreach (IntVec3 c in line)
			{
				if (block != null && c == block.Position) break;
				BurnCell(c, ram);
			}
			(block ?? gate).TakeDamage(Burn(ram));
		}

		private void BurnCell(IntVec3 c, Building_LaserRam ram)
		{
			if (!c.InBounds(Map)) return;
			List<Thing> things = c.GetThingList(Map);
			bool gone = false;
			for (int i = things.Count - 1; i >= 0; i--)
			{
				Thing t = things[i];
				if (t == ram || t.Faction == lord.faction) continue;
				if (t is Plant || t.def.IsWithinCategory(ThingCategoryDefOf.Chunks))
				{
					t.Destroy();
					gone = true;
				}
				else if (t is Pawn || t is Building)
					t.TakeDamage(Burn(ram));
			}
			if (!gone) return;
			FilthMaker.TryMakeFilth(c, Map, ThingDefOf.Filth_Ash);
			FleckMaker.ThrowSmoke(c.ToVector3Shifted(), Map, 1.2f);
		}

		private static DamageInfo Burn(Building_LaserRam ram) =>
			new DamageInfo(RimSiegeDefOf.RimSiege_LaserBlast, BurnDamage, armorPenetration: 1f, instigator: ram);
	}
}
