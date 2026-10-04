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
