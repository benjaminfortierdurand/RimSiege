using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege
{
	// for other mods (Hegemony first). plain static calls, no harmony on either side: check RimSiege
	// is active, then call through reflection or a small compat dll. game thread only
	public static class SiegeApi
	{
		// goes up only when something gets added, old calls keep working. a property so it isnt baked into callers
		public static int Version => 1;

		// a hostile faction and a wall or gate on this home map worth sieging. runs the same
		// wall search a launch does, so ask once when you plan the attack, not every tick
		public static bool CanSiege(Map map, Faction faction) =>
			ValidTarget(map, faction) && SiegeLauncher.TryFindSiegeTarget(map, out _);

		// full RimSiege siege run with your own pawns: war camp, ram or tower, trebuchets, push, breach.
		// troops: fighters of this faction, alive, not downed, in no lord, at least one humanlike, no kids
		// (they would end up pushing the ram). animals and mechs are fine, they fight but never crew.
		// free unspawned ones walk in from the map edge. ones already on this map, standing or in
		// incoming pods, stay put and the camp goes up near them. one RimSiege host per faction per map,
		// no sieges on vacuum maps.
		// nothing extra gets made: no siege crews (guns are manned out of your troops), sappers come out
		// of your troops too, no second column, no late tribute, no ransom or vendetta, the captain keeps
		// their own gear and skills. like a vanilla siege, engine materials, ammo, camp gear and meals drop in
		// by pod and builders get construction 6 + construction-only work.
		// engines need 5+ humanlike adults. medieval factions get a ram or tower, plus trebuchets and ballistas
		// with Medieval Overhaul. tribes carry theirs: a log ram (6+ adults, four on the grips) or a ladder,
		// never artillery. a gun era army gets the camp, the captain and a foot assault.
		// sappers need gunpowder: medieval and gun era armies only, never a tribe.
		// survivors leave by walking off the map (or back over the tower or ladder).
		// false = nothing done, pawns untouched: not hostile, bad pawn list, or no wall worth a siege.
		// run your normal assault then
		public static bool TryLaunchSiege(Map map, Faction faction, List<Pawn> troops, bool sendLetter)
		{
			if (!ValidTarget(map, faction) || !ValidTroops(map, faction, troops)) return false;
			return SiegeLauncher.LaunchSupplied(map, faction, troops, sendLetter);
		}

		private static bool ValidTarget(Map map, Faction faction) =>
			map != null && map.IsPlayerHome && Find.Maps.Contains(map) && !map.Biome.inVacuum
			&& faction != null && !faction.IsPlayer && !faction.defeated
			&& faction.HostileTo(Faction.OfPlayer) && !SiegeRunning(map, faction);

		// guns, blueprints and ammo piles are tracked per faction, two hosts would mix up each others camp
		private static bool SiegeRunning(Map map, Faction faction) =>
			map.lordManager.lords.Any(l => l.faction == faction && l.LordJob is AI.LordJob_MedievalSiege);

		private static bool ValidTroops(Map map, Faction faction, List<Pawn> troops)
		{
			if (troops == null || troops.Count == 0) return false;
			if (troops.Distinct().Count() != troops.Count) return false;
			foreach (Pawn p in troops)
			{
				if (p == null || p.Dead || p.Destroyed || p.Downed || p.Faction != faction) return false;
				if (p.IsPrisoner || p.HostFaction == Faction.OfPlayer) return false;
				if (p.RaceProps.Humanlike && !p.DevelopmentalStage.Adult()) return false;
				if (p.GetLord() != null) return false;
				if (p.MapHeld != null && p.MapHeld != map) return false;
			}
			return troops.Any(SiegeLauncher.CanCrew);
		}
	}
}
