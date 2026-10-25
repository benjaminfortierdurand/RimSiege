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

		// spacer kit through the API: laser battery and ram, troops handed over unspawned so they land by shuttle
		[DebugAction("RimSiege", "Siege now via SiegeApi: spacer army (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ApiSiegeSpacer() => ApiSiege(alreadyOnMap: false, spacer: true);

		private static void ApiSiege(bool alreadyOnMap, bool spacer = false)
		{
			Map map = Find.CurrentMap;
			if (map == null) return;
			float points = ApiTestPoints;
			Faction faction = spacer ? KitFaction(SiegeLauncher.Lasers, "spacer army", makeHostile: true)
				: SiegeLauncher.FindSiegeFaction(points, out _);
			if (faction == null)
			{
				if (!spacer)
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
		private static void TribeLogRam() => KitSiege(SiegeLauncher.Tribal, RimSiegeDefOf.RimSiege_LogRam, "tribe");

		[DebugAction("RimSiege", "Siege now: tribe with ladder (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void TribeLadder() => KitSiege(SiegeLauncher.Tribal, RimSiegeDefOf.RimSiege_SiegeLadder, "tribe");

		// medieval kit: wagon ram or siege tower, forced so each gets filmed
		[DebugAction("RimSiege", "Siege now: medieval house with battering ram (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void MedievalRam() => KitSiege(Medieval, RimSiegeDefOf.RimSiege_BatteringRam, "medieval house", makeHostile: true);

		[DebugAction("RimSiege", "Siege now: medieval house with siege tower (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void MedievalTower() => KitSiege(Medieval, RimSiegeDefOf.RimSiege_SiegeTower, "medieval house", makeHostile: true);

		private static bool Medieval(Faction f) => SiegeLauncher.OldWorld(f) && !SiegeLauncher.Tribal(f);

		// gun era kit: steel breaching ram, mortars behind sandbags, smoke on the gate. 4k so the battery shows up
		[DebugAction("RimSiege", "Siege now: gun era (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void ModernSiege() => KitSiege(f => SiegeLauncher.Modern(f) && !SiegeLauncher.Lasers(f),
			RimSiegeDefOf.RimSiege_SteelRam, "gun era faction");

		// spacer kit: laser battery and the laser ram on the gate. the Empire is rarely at war with you, this
		// one turns it hostile if it has to
		[DebugAction("RimSiege", "Siege now: spacer army, laser battery + laser ram (4k points)",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void LaserSiege() => KitSiege(SiegeLauncher.Lasers, RimSiegeDefOf.RimSiege_LaserRam,
			"spacer army", makeHostile: true);

		private static void KitSiege(System.Func<Faction, bool> kit, ThingDef engine, string what, bool makeHostile = false)
		{
			Map map = Find.CurrentMap;
			if (map == null) return;
			Faction faction = KitFaction(kit, what, makeHostile);
			if (faction == null) return;
			SiegeLauncher.debugNextEngine = engine;
			if (!SiegeLauncher.ExecuteSiege(map, faction, ApiTestPoints))
				Messages.Message("RimSiege: could not launch the siege (no wall or door to hit?).",
					MessageTypeDefOf.RejectInput, historical: false);
			SiegeLauncher.debugNextEngine = null;
		}

		// a hostile faction of that kit, or one made hostile for the test. null = none, message sent
		private static Faction KitFaction(System.Func<Faction, bool> kit, string what, bool makeHostile)
		{
			var candidates = Find.FactionManager.AllFactions
				.Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden && kit(f) && SiegeLauncher.CrewKindFor(f) != null)
				.ToList();
			Faction faction = candidates.Where(f => f.HostileTo(Faction.OfPlayer)).RandomElementWithFallback();
			if (faction == null && makeHostile && candidates.TryRandomElement(out faction))
			{
				faction.TryAffectGoodwillWith(Faction.OfPlayer, -200, canSendMessage: false, canSendHostilityLetter: false);
				Messages.Message("RimSiege: " + faction.Name + " made hostile for the test.", MessageTypeDefOf.NeutralEvent, historical: false);
			}
			if (faction == null || !faction.HostileTo(Faction.OfPlayer))
			{
				Messages.Message("RimSiege: no hostile " + what + " found.", MessageTypeDefOf.RejectInput, historical: false);
				return null;
			}
			return faction;
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
