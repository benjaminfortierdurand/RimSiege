using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimSiege.Debug
{
	public static class DebugActions_RimSiege
	{
		[DebugAction("RimSiege", "Siege now (current threat points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void SiegeNow()
		{
			Map map = Find.CurrentMap;
			if (map == null) return;
			Launch(map, StorytellerUtility.DefaultThreatPointsNow(map));
		}

		// the whole siege train: engine, 3 trebuchets, ballistas, tar, carrion, sappers
		[DebugAction("RimSiege", "Siege now (10k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void MaxSiege()
		{
			Map map = Find.CurrentMap;
			if (map == null) return;
			Launch(map, 10000f);
		}

		// SiegeApi path the way another mod would call it: an army made outside RimSiege, handed over as is.
		// 4k so the whole train shows up (engine, trebuchet, sappers), a fresh colony's threat points
		// make a one man army that tests nothing
		private const float ApiTestPoints = 4000f;

		[DebugAction("RimSiege", "Siege now via SiegeApi (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApiSiege() => ApiSiege(alreadyOnMap: false);

		// how Hegemony hands it over: army spawned at its own entry cell first, no lord yet
		[DebugAction("RimSiege", "Siege now via SiegeApi (4k points, army already on the map)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApiSiegeSpawned() => ApiSiege(alreadyOnMap: true);

		private static void ApiSiege(bool alreadyOnMap)
		{
			Map map = Find.CurrentMap;
			if (map == null) return;
			float points = ApiTestPoints;
			Faction faction = SiegeLauncher.FindSiegeFaction(points, out _);
			if (faction == null)
			{
				Messages.Message("RimSiege: no hostile humanlike faction found.",
					MessageTypeDefOf.RejectInput, historical: false);
				return;
			}
			var army = SiegeLauncher.GenerateWarband(map, faction, points);
			if (alreadyOnMap)
			{
				if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map, CellFinder.EdgeRoadChance_Hostile))
					edge = CellFinder.RandomEdgeCell(map);
				foreach (Pawn p in army) GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 8), map);
			}
			if (SiegeApi.TryLaunchSiege(map, faction, army, sendLetter: true)) return;
			foreach (Pawn p in army) if (!p.Destroyed) p.Destroy();
			Messages.Message("RimSiege: SiegeApi said no (no wall or door to hit, or this faction already has a siege here).",
				MessageTypeDefOf.RejectInput, historical: false);
		}

		// tribal kit: log ram or ladder, no artillery, no sappers. forced engine so both get a test
		[DebugAction("RimSiege", "Siege now: tribe with log ram (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void TribeLogRam() => TribeSiege(RimSiegeDefOf.RimSiege_LogRam);

		[DebugAction("RimSiege", "Siege now: tribe with ladder (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void TribeLadder() => TribeSiege(RimSiegeDefOf.RimSiege_SiegeLadder);

		private static void TribeSiege(ThingDef engine)
		{
			Map map = Find.CurrentMap;
			if (map == null) return;
			Faction faction = Find.FactionManager.AllFactions
				.Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden && SiegeLauncher.Tribal(f)
					&& f.HostileTo(Faction.OfPlayer) && SiegeLauncher.CrewKindFor(f) != null)
				.RandomElementWithFallback();
			if (faction == null)
			{
				Messages.Message("RimSiege: no hostile tribe found.", MessageTypeDefOf.RejectInput, historical: false);
				return;
			}
			SiegeLauncher.debugNextEngine = engine;
			if (!SiegeLauncher.ExecuteSiege(map, faction, ApiTestPoints))
				Messages.Message("RimSiege: could not launch the siege (no wall or door to hit?).",
					MessageTypeDefOf.RejectInput, historical: false);
			SiegeLauncher.debugNextEngine = null;
		}

		// both skip the ultimatum. for that one use the vanilla incident tool
		private static void Launch(Map map, float points)
		{
			Faction faction = SiegeLauncher.FindSiegeFaction(points, out _);
			if (faction == null)
			{
				Messages.Message("RimSiege: no hostile humanlike faction found.",
					MessageTypeDefOf.RejectInput, historical: false);
				return;
			}
			if (!SiegeLauncher.ExecuteSiege(map, faction, points))
				Messages.Message("RimSiege: could not launch the siege (no wall or door to hit?).",
					MessageTypeDefOf.RejectInput, historical: false);
		}
	}
}
