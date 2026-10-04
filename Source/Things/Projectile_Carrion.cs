using RimWorld;
using Verse;

namespace RimSiege.Things
{
	// caffa 1346: the dead go over the walls
	public class Projectile_Carrion : Projectile
	{
		private const float PlagueChance = 0.2f;
		private const float PlagueRadius = 3f;

		protected override void Impact(Thing hitThing, bool blockedByShield = false)
		{
			Map map = base.Map;
			IntVec3 cell = base.Position;
			base.Impact(hitThing, blockedByShield);
			if (map == null || !cell.InBounds(map)) return;

			SiegeLauncher.SpawnCarrion(cell, map);

			GasUtility.AddGas(cell, map, GasType.RotStink, 255);
			foreach (IntVec3 off in GenAdj.AdjacentCells)
			{
				IntVec3 adj = cell + off;
				if (adj.InBounds(map)) GasUtility.AddGas(adj, map, GasType.RotStink, 90);
			}

			HediffDef plague = DefDatabase<HediffDef>.GetNamedSilentFail("Plague");
			if (plague == null) return;
			foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, PlagueRadius, useCenter: true))
			{
				if (!c.InBounds(map)) continue;
				var things = c.GetThingList(map);
				for (int i = 0; i < things.Count; i++)
				{
					if (things[i] is Pawn p && !p.Dead && p.RaceProps.IsFlesh
						&& !p.health.hediffSet.HasHediff(plague) && Rand.Chance(PlagueChance))
						p.health.AddHediff(plague);
				}
			}
		}
	}
}
