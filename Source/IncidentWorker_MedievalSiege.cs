using RimWorld;
using Verse;

namespace RimSiege
{
	public class IncidentWorker_MedievalSiege : IncidentWorker
	{
		protected override bool CanFireNowSub(IncidentParms parms)
		{
			if (!base.CanFireNowSub(parms)) return false;
			if (!(parms.target is Map map)) return false;
			if (GameComponent_SiegeDemands.Get()?.RespiteActive ?? false) return false; // they heard what you did
			if (SiegeLauncher.FindSiegeFaction(parms.points, out _) == null) return false;
			return SiegeLauncher.TryFindSiegeTarget(map, out _); // needs a wall or door to hit
		}

		// nothing spawns yet, just the ultimatum letter. pay = it never happens
		protected override bool TryExecuteWorker(IncidentParms parms)
		{
			if (!(parms.target is Map map)) return false;

			Faction faction = SiegeLauncher.FindSiegeFaction(parms.points, out _);
			if (faction == null) return false;
			if (!SiegeLauncher.TryFindSiegeTarget(map, out _)) return false;

			parms.faction = faction;

			GameComponent_SiegeDemands comp = GameComponent_SiegeDemands.Get();

			// vendetta factions don't parley
			if (!RimSiegeMod.S.ultimatumEnabled || (comp?.HasGrudge(faction) ?? false))
				return SiegeLauncher.ExecuteSiege(map, faction, parms.points);

			if (comp == null) return false;
			comp.OpenDemand(map, faction, parms.points);
			return true;
		}
	}
}
