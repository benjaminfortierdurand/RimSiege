using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Debug
{
	// screenshots of the drawn camps: pick one, click the map, it stands there finished. the kit's guns on the
	// artillery marks, the kit's engine out front, all owned by a faction of that era for its colours and
	// banners. north edge up, as drawn. a gun era drawing the spacer armies still borrow comes twice: mortars
	// and steel ram, or lasers and the laser ram
	public static class DebugCampShowcase
	{
		private enum Era { Tribal, Medieval, GunEra, Spacer }

		[DebugAction("RimSiege", "Place a built camp (screenshots)...",
			actionType = DebugActionType.Action,
			allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static List<DebugActionNode> PlaceBuiltCamp()
		{
			var list = new List<DebugActionNode>();
			foreach (CampLayoutDef camp in DefDatabase<CampLayoutDef>.AllDefsListForReading.Where(c => c.Usable).OrderBy(c => c.kit).ThenBy(c => c.minPoints))
			{
				Era[] eras = camp.kit == CampKit.Tribal ? new[] { Era.Tribal }
					: camp.kit == CampKit.Medieval ? new[] { Era.Medieval }
					: camp.kit == CampKit.Spacer ? new[] { Era.Spacer }
					: CampLayoutDef.AllFor(CampKit.Spacer, camp.minPoints).Contains(camp) ? new[] { Era.GunEra, Era.Spacer }
					: new[] { Era.GunEra };
				foreach (Era era in eras)
				{
					CampLayoutDef c = camp;
					Era e = era;
					string range = c.maxPoints >= 999999f ? c.minPoints + "+" : c.minPoints + "-" + c.maxPoints;
					list.Add(new DebugActionNode(c.defName + " (" + e + ", " + range + ")", DebugActionType.ToolMap)
					{
						action = () => Place(Find.CurrentMap, UI.MouseCell(), c, e)
					});
				}
			}
			return list;
		}

		private static void Place(Map map, IntVec3 center, CampLayoutDef camp, Era era)
		{
			if (map == null || !center.InBounds(map)) return;
			Faction faction = FactionOf(era);
			ThingDef arty = Artillery(era);
			ThingDef engine = era == Era.Tribal ? RimSiegeDefOf.RimSiege_LogRam
				: era == Era.Medieval ? RimSiegeDefOf.RimSiege_BatteringRam
				: era == Era.GunEra ? RimSiegeDefOf.RimSiege_SteelRam
				: RimSiegeDefOf.RimSiege_LaserRam;
			int cx = (camp.Width - 1) / 2, cz = (camp.Height - 1) / 2;
			int body = SiegeLauncher.EngineBodyLength(engine);
			IntVec3 front = center + IntVec3.North * (Mathf.Max(cx, cz) + 3 + body);

			// a clean plot: plants, filth, chunks and bare rock go, camp and engine strip both
			CellRect plot = CellRect.FromLimits(center.x - cx - 2, center.z - cz - 2, center.x + cx + 2, front.z + 1);
			foreach (IntVec3 c in plot.ClipInsideMap(map)) CampLayoutDef.Clear(c, map);

			ThingDef banner = SiegeLauncher.HouseBanner(faction);
			var later = new List<CampLayoutDef.Piece>(); // wall torches after their walls, table dressing last
			foreach (CampLayoutDef.Piece p in camp.Pieces)
			{
				if (p.def.category != ThingCategory.Building) continue;
				if (p.onTop || p.def.building?.isAttachment == true) { later.Add(p); continue; }
				Spawn(map, center, cx, cz, p, Swap(p.def, arty, banner), faction);
			}
			foreach (CampLayoutDef.Piece p in later.OrderBy(p => p.onTop))
				Spawn(map, center, cx, cz, p, Swap(p.def, arty, banner), faction);

			if (engine != null && front.InBounds(map))
			{
				Thing e = ThingMaker.MakeThing(engine);
				GenSpawn.Spawn(e, front, map, Rot4.North);
				e.SetFaction(faction);
			}
			Messages.Message("RimSiege: " + camp.defName + " placed for " + era + (faction != null ? " (" + faction.Name + ")" : ""),
				MessageTypeDefOf.NeutralEvent, historical: false);
		}

		// artillery marks take the era's gun, a plain MO banner takes the house's
		private static ThingDef Swap(ThingDef def, ThingDef arty, ThingDef banner)
		{
			if (arty != null && SiegeLauncher.BatteryMark(def, arty)) return arty;
			if (banner != null && def.defName.StartsWith("DankPyon_Banner")) return banner;
			return def;
		}

		private static void Spawn(Map map, IntVec3 center, int cx, int cz, CampLayoutDef.Piece p, ThingDef def, Faction faction)
		{
			IntVec3 cell = center + new IntVec3(p.x - cx, 0, p.z - cz);
			if (!cell.InBounds(map)) return;
			Rot4 rot = def.rotatable ? p.rot : Rot4.North;
			ThingDef stuff = def.MadeFromStuff
				? (p.stuff != null && GenStuff.AllowedStuffsFor(def).Contains(p.stuff) ? p.stuff : GenStuff.DefaultStuffFor(def))
				: null;
			Thing t = ThingMaker.MakeThing(def, stuff);
			GenSpawn.Spawn(t, cell, map, rot);
			if (t.def.CanHaveFaction) t.SetFaction(faction);
			t.TryGetComp<CompRefuelable>()?.Refuel(999f); // fires lit, generators fuelled, guns loaded
		}

		private static ThingDef Artillery(Era era) =>
			era == Era.Medieval ? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Trebuchet")
			: era == Era.GunEra ? ThingDefOf.Turret_Mortar
			: era == Era.Spacer ? RimSiegeDefOf.RimSiege_LaserBattery
			: null;

		// a faction of that era for its colours and banners. a friendly one first: a hostile camp's mini-turrets
		// open up on colonists in range
		private static Faction FactionOf(Era era)
		{
			IEnumerable<Faction> all = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.def.hidden);
			IEnumerable<Faction> fit = era == Era.Tribal ? all.Where(SiegeLauncher.Tribal)
				: era == Era.Medieval ? all.Where(f => SiegeLauncher.OldWorld(f) && !SiegeLauncher.Tribal(f))
				: era == Era.GunEra ? all.Where(f => SiegeLauncher.Modern(f) && !SiegeLauncher.Lasers(f))
				: all.Where(SiegeLauncher.Lasers);
			// and a house with its own banner reads best for the medieval shots
			return fit.OrderBy(f => f.HostileTo(Faction.OfPlayer)).ThenByDescending(f => SiegeLauncher.HouseBanner(f) != null).FirstOrDefault();
		}
	}
}
