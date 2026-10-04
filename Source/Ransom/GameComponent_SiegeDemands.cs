using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using RimSiege.AI;

namespace RimSiege
{
	// pay tribute before fireAtTick or the siege launches
	public class SiegeDemand : IExposable
	{
		public int id;
		public Map map;
		public Faction faction;
		public float points;
		public int tribute;
		public int fireAtTick;
		public bool late;       // engines-are-up offer: expiry just closes it, paying sends the host home
		public Lord siegeLord;

		public void ExposeData()
		{
			Scribe_Values.Look(ref id, "id");
			Scribe_References.Look(ref map, "map");
			Scribe_References.Look(ref faction, "faction");
			Scribe_Values.Look(ref points, "points");
			Scribe_Values.Look(ref tribute, "tribute");
			Scribe_Values.Look(ref fireAtTick, "fireAtTick");
			Scribe_Values.Look(ref late, "late");
			Scribe_References.Look(ref siegeLord, "siegeLord");
		}
	}

	// danegeld: a faction you pay comes back for more, at a higher price.
	// and a faction whose captain died in your jail comes back for blood
	public class FactionSiegeMemory : IExposable
	{
		public Faction faction;
		public int timesPaid;
		public int comebackTick = -1;
		public int vengeTick = -1;
		public string slain;

		public void ExposeData()
		{
			Scribe_References.Look(ref faction, "faction");
			Scribe_Values.Look(ref timesPaid, "timesPaid");
			Scribe_Values.Look(ref comebackTick, "comebackTick", -1);
			Scribe_Values.Look(ref vengeTick, "vengeTick", -1);
			Scribe_Values.Look(ref slain, "slain");
		}
	}

	// one tracked siege captain, from the field to the jail to the exchange
	public class CaptainCase : IExposable
	{
		public const int Loose = 0;      // siege running, not caught yet
		public const int Jailed = 1;     // in a cell, house hasn't written yet
		public const int OfferUp = 2;    // ransom letter on the stack
		public const int Exchange = 3;   // envoys en route or waiting
		public const int Kept = 4;       // player said no, still watching the cell
		public const int Closing = 5;    // deal done, waiting for everyone to walk off
		public const int Condemned = 6;  // executioner on his way to the block

		public int id;
		public Pawn captain;
		public Faction faction;
		public Map map;
		public int silver;
		public int state;
		public int eventTick = -1;
		public bool arrived;
		public bool fetchSent;
		public Lord envoys;
		public Pawn porter;
		public Thing banner;
		public IntVec3 waitSpot = IntVec3.Invalid;

		public void ExposeData()
		{
			Scribe_Values.Look(ref id, "id");
			Scribe_References.Look(ref captain, "captain");
			Scribe_References.Look(ref faction, "faction");
			Scribe_References.Look(ref map, "map");
			Scribe_Values.Look(ref silver, "silver");
			Scribe_Values.Look(ref state, "state");
			Scribe_Values.Look(ref eventTick, "eventTick", -1);
			Scribe_Values.Look(ref arrived, "arrived");
			Scribe_Values.Look(ref fetchSent, "fetchSent");
			Scribe_References.Look(ref envoys, "envoys");
			Scribe_References.Look(ref porter, "porter");
			Scribe_References.Look(ref banner, "banner");
			Scribe_Values.Look(ref waitSpot, "waitSpot", IntVec3.Invalid);
		}
	}

	public class GameComponent_SiegeDemands : GameComponent
	{
		private List<SiegeDemand> demands = new List<SiegeDemand>();
		private List<FactionSiegeMemory> memories = new List<FactionSiegeMemory>();
		private List<CaptainCase> cases = new List<CaptainCase>();
		private int nextId = 1;
		private int respiteUntilTick = -1;

		private const int CheckInterval = 250;
		private static int TimeoutTicks => RimSiegeMod.S.ultimatumHours * 2500;

		public bool RespiteActive => RimSiegeMod.S.danegeld && Find.TickManager.TicksGame < respiteUntilTick;

		private FactionSiegeMemory MemoryFor(Faction f, bool create)
		{
			FactionSiegeMemory m = memories.FirstOrDefault(x => x.faction == f);
			if (m == null && create && f != null)
			{
				m = new FactionSiegeMemory { faction = f };
				memories.Add(m);
			}
			return m;
		}

		// routed them hard: word spreads, nobody tries a siege for a while
		public void Notify_SiegeRouted()
		{
			if (!RimSiegeMod.S.danegeld) return;
			respiteUntilTick = Find.TickManager.TicksGame + Rand.Range(10, 18) * 60000;
		}

		// vendetta pending: they come for blood, they don't send letters
		public bool HasGrudge(Faction f)
		{
			FactionSiegeMemory m = MemoryFor(f, false);
			return m != null && !m.slain.NullOrEmpty();
		}

		public GameComponent_SiegeDemands(Game game) { }

		public static GameComponent_SiegeDemands Get() => Current.Game?.GetComponent<GameComponent_SiegeDemands>();

		public void OpenDemand(Map map, Faction faction, float points)
		{
			// each payment makes the next demand pricier
			int paid = RimSiegeMod.S.danegeld ? (MemoryFor(faction, false)?.timesPaid ?? 0) : 0;
			int tribute = Mathf.Clamp(
				Mathf.RoundToInt(points * RimSiegeMod.S.tributeFactor * (1f + 0.5f * paid)),
				200, 5000 + 5000 * paid);

			var d = new SiegeDemand
			{
				id = nextId++,
				map = map,
				faction = faction,
				points = points,
				tribute = tribute,
				fireAtTick = Find.TickManager.TicksGame + TimeoutTicks,
			};
			demands.Add(d);

			string text = "RimSiege_UltimatumText".Translate(faction?.Name, d.tribute);
			if (paid > 0) text += "RimSiege_UltimatumPaidBefore".Translate();

			var letter = (ChoiceLetter_SiegeDemand)LetterMaker.MakeLetter(
				"RimSiege_UltimatumLabel".Translate(),
				text,
				RimSiegeDefOf.RimSiege_SiegeDemand, faction);
			letter.demandId = d.id;
			letter.StartTimeout(TimeoutTicks);
			Find.LetterStack.ReceiveLetter(letter);
		}

		// the engines are up, the price went up with them. paid = the whole host packs up
		public void OpenLateDemand(Map map, Faction faction, Lord lord, float power, int window)
		{
			if (map == null || faction == null || lord == null) return;
			if (demands.Any(x => x.siegeLord == lord)) return;
			int paid = RimSiegeMod.S.danegeld ? (MemoryFor(faction, false)?.timesPaid ?? 0) : 0;
			int tribute = Mathf.Clamp(
				Mathf.RoundToInt(power * RimSiegeMod.S.tributeFactor * 2.5f * (1f + 0.5f * paid) / 50f) * 50,
				500, 15000);

			var d = new SiegeDemand
			{
				id = nextId++,
				map = map,
				faction = faction,
				points = power,
				tribute = tribute,
				fireAtTick = Find.TickManager.TicksGame + window,
				late = true,
				siegeLord = lord,
			};
			demands.Add(d);

			var letter = (ChoiceLetter_SiegeDemand)LetterMaker.MakeLetter(
				"RimSiege_LateLabel".Translate(),
				"RimSiege_LateText".Translate(faction.Name, tribute),
				RimSiegeDefOf.RimSiege_SiegeDemand, faction);
			letter.demandId = d.id;
			letter.StartTimeout(window);
			Find.LetterStack.ReceiveLetter(letter);
		}

		public SiegeDemand GetDemand(int id) => demands.FirstOrDefault(x => x.id == id);

		// a bought-off host packs up from the camp or the push, not from inside your walls
		public bool CanBuyOff(SiegeDemand d) =>
			d.siegeLord != null && d.siegeLord.ownedPawns.Count > 0
			&& (d.siegeLord.CurLordToil is LordToil_BuildSiege || d.siegeLord.CurLordToil is LordToil_PushEngine);
		public CaptainCase GetCase(int id) => cases.FirstOrDefault(x => x.id == id);

		// vanilla TradeUtility only counts silver under a powered ORBITAL BEACON.
		// medieval colonies dont have those: count home area + storage instead
		public static int SilverAvailable(Map map)
		{
			if (map == null) return 0;
			int total = 0;
			List<Thing> silver = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
			for (int i = 0; i < silver.Count; i++)
			{
				Thing t = silver[i];
				// no fog test: home area and storage already scope this, and fog-of-war
				// mods make Fogged() true for explored cells nobody is looking at
				if (t.Spawned && (map.areaManager.Home[t.Position] || t.IsInAnyStorage()))
					total += t.stackCount;
			}
			return total;
		}

		public static bool TryPaySilver(Map map, int amount)
		{
			if (SilverAvailable(map) < amount) return false;
			List<Thing> silver = map.listerThings.ThingsOfDef(ThingDefOf.Silver)
				.Where(t => t.Spawned && (map.areaManager.Home[t.Position] || t.IsInAnyStorage()))
				.ToList();
			foreach (Thing t in silver)
			{
				int take = Mathf.Min(t.stackCount, amount);
				t.SplitOff(take).Destroy();
				amount -= take;
				if (amount <= 0) return true;
			}
			return false;
		}

		public void Pay(int id)
		{
			SiegeDemand d = GetDemand(id);
			if (d == null) return;
			if (d.late && !CanBuyOff(d))
			{
				// they broke through while the letter sat there: no sale, no silver taken
				Messages.Message("RimSiege_LateTooLate".Translate(), MessageTypeDefOf.NeutralEvent);
				Resolve(d);
				return;
			}
			if (d.map != null && TryPaySilver(d.map, d.tribute))
			{
				if (d.late)
				{
					Messages.Message("RimSiege_LateTributePaid".Translate(d.tribute, d.faction?.Name),
						MessageTypeDefOf.PositiveEvent);
					if (d.siegeLord != null && d.siegeLord.ownedPawns.Count > 0) d.siegeLord.ReceiveMemo("BoughtOff");
				}
				else
					Messages.Message("RimSiege_TributePaid".Translate(d.tribute, d.faction?.Name),
						MessageTypeDefOf.NeutralEvent);

				// paying works. thats the problem — they WILL be back for more
				if (RimSiegeMod.S.danegeld)
				{
					FactionSiegeMemory m = MemoryFor(d.faction, true);
					if (m != null)
					{
						m.timesPaid++;
						m.comebackTick = Find.TickManager.TicksGame + Rand.Range(8, 15) * 60000;
					}
				}
			}
			Resolve(d);
		}

		public void Defy(int id)
		{
			SiegeDemand d = GetDemand(id);
			if (d == null) return;
			if (!d.late) Launch(d);
			Resolve(d);
		}

		// ---- captain ransom ----

		public void RegisterCaptain(Pawn captain, Faction faction, Map map, float warbandPower)
		{
			if (!RimSiegeMod.S.ransomEnabled) return;
			if (captain == null || faction == null || map == null) return;
			if (!SiegeLauncher.CanCrew(captain)) return; // no ransom letters for war beasts
			if (cases.Any(x => x.captain == captain)) return;
			cases.Add(new CaptainCase
			{
				id = nextId++,
				captain = captain,
				faction = faction,
				map = map,
				silver = Mathf.Clamp(Mathf.RoundToInt(warbandPower * 1.2f / 50f) * 50, 500, 8000),
				state = CaptainCase.Loose,
				eventTick = Find.TickManager.TicksGame + 900000, // forget him after 15 days on the loose
			});
		}

		public void AcceptRansom(int id)
		{
			CaptainCase c = GetCase(id);
			if (c == null || c.state != CaptainCase.OfferUp) return;
			RemoveRansomLetter(c);
			if (c.captain == null || c.captain.Dead || !c.captain.IsPrisonerOfColony) return;
			if (!SpawnEnvoys(c))
			{
				c.state = CaptainCase.Kept;
				return;
			}
			c.state = CaptainCase.Exchange;
			c.eventTick = Find.TickManager.TicksGame + 120000; // two days to hand him over
			Messages.Message("RimSiege_EnvoysComing".Translate(c.faction?.Name, CaptainName(c)),
				MessageTypeDefOf.NeutralEvent);
		}

		public void RefuseRansom(int id)
		{
			CaptainCase c = GetCase(id);
			if (c == null) return;
			RemoveRansomLetter(c);
			c.state = CaptainCase.Kept;
			c.eventTick = -1;
		}

		// letter button: pick who swings, then the scene runs on its own
		public void ExecuteCaptain(int id)
		{
			CaptainCase c = GetCase(id);
			if (c == null) return;
			RemoveRansomLetter(c);
			Pawn cap = c.captain;
			if (cap == null || cap.Dead) return;
			if (!cap.IsPrisonerOfColony) { c.state = CaptainCase.Kept; return; } // slipped away first

			List<Pawn> candidates = ExecutionerCandidates(cap);
			if (candidates.Count == 0) { ExecuteNow(c); return; }

			var opts = new List<FloatMenuOption>();
			foreach (Pawn p in candidates)
			{
				Pawn ex = p;
				opts.Add(new FloatMenuOption(
					"RimSiege_ExecutionerOption".Translate(ex.LabelShort,
						ex.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0),
					() => StartExecution(id, ex)));
			}
			if (FindPlayerTrebuchet(cap, candidates[0]) != null)
			{
				Pawn hauler = candidates[0];
				opts.Add(new FloatMenuOption("RimSiege_FlingOption".Translate(hauler.LabelShort),
					() => StartExecution(id, hauler, fling: true)));
			}
			Find.WindowStack.Add(new FloatMenu(opts, "RimSiege_PickExecutioner".Translate()));
		}

		public void StartExecution(int id, Pawn executioner, bool fling = false)
		{
			CaptainCase c = GetCase(id);
			if (c == null) return;
			Pawn cap = c.captain;
			if (cap == null || cap.Dead) return;
			if (!cap.IsPrisonerOfColony) { c.state = CaptainCase.Kept; return; }
			if (executioner == null || executioner.Dead || executioner.Downed) { ExecuteNow(c); return; }

			Thing block = null;
			JobDef sceneDef = RimSiegeDefOf.RimSiege_ExecuteCaptainJob;
			if (fling)
			{
				block = FindPlayerTrebuchet(cap, executioner);
				if (block != null) sceneDef = RimSiegeDefOf.RimSiege_TrebuchetExecutionJob;
			}
			if (block == null)
			{
				block = FindExecutionBlock(cap, executioner);
				if (block != null) sceneDef = RimSiegeDefOf.RimSiege_ExecutionCeremonyJob;
			}
			Job job = block != null
				? JobMaker.MakeJob(sceneDef, cap, block.InteractionCell, block)
				: JobMaker.MakeJob(RimSiegeDefOf.RimSiege_ExecuteCaptainJob, cap);
			job.count = 1;

			if (!executioner.jobs.TryTakeOrderedJob(job, JobTag.Misc)) { ExecuteNow(c); return; }

			Messages.Message("RimSiege_ExecutionOrdered".Translate(executioner.LabelShort, CaptainName(c)),
				cap, MessageTypeDefOf.NeutralEvent);
			c.state = CaptainCase.Condemned;
			c.eventTick = Find.TickManager.TicksGame + 30000;
			if (block != null) InviteSpectators(block, executioner);
		}

		private static List<Pawn> ExecutionerCandidates(Pawn cap)
		{
			Map map = cap.MapHeld;
			if (map == null) return new List<Pawn>();
			return map.mapPawns.FreeColonistsSpawned
				.Where(p => !p.Downed && !p.Drafted && !p.InMentalState
					&& !p.WorkTagIsDisabled(WorkTags.Violent)
					&& p.CanReach(cap, PathEndMode.Touch, Danger.Deadly))
				.OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0)
				.ToList();
		}

		// a manned colony trebuchet doubles as a launcher for the condemned
		private static Thing FindPlayerTrebuchet(Pawn cap, Pawn ex)
		{
			Map map = cap.MapHeld;
			ThingDef trebDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Trebuchet");
			if (map == null || trebDef == null) return null;
			Thing best = null;
			float bestD = float.MaxValue;
			foreach (Building b in map.listerBuildings.AllBuildingsColonistOfDef(trebDef))
			{
				// MO puts the man-spot INSIDE the walk-through 3x3 footprint: Standable is false there, Walkable isnt
				IntVec3 cell = b.InteractionCell;
				if (!cell.IsValid || !cell.InBounds(map) || !cell.Walkable(map)) continue;
				if (ex != null && !ex.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
				float d = (b.Position - cap.PositionHeld).LengthHorizontalSquared;
				if (d < bestD) { bestD = d; best = b; }
			}
			return best;
		}

		// the block: an altar if the colony worships, MO's butcher block otherwise, else the cell
		private static Thing FindExecutionBlock(Pawn cap, Pawn ex)
		{
			Map map = cap.MapHeld;
			if (map == null) return null;
			Thing best = null;
			float bestScore = float.MaxValue;
			foreach (Building b in map.listerBuildings.allBuildingsColonist)
			{
				bool altar = b.def.isAltar;
				if (!altar && b.def.defName != "DankPyon_ButchersBlock") continue;
				IntVec3 cell = b.InteractionCell;
				if (!cell.IsValid || !cell.InBounds(map) || !cell.Standable(map)) continue;
				if (!ex.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
				float score = (b.Position - cap.PositionHeld).LengthHorizontalSquared;
				if (altar) score -= 1000000f; // the altar always wins
				if (score < bestScore) { bestScore = score; best = b; }
			}
			return best;
		}

		// called at the order AND again when the condemned reaches the block,
		// so a long haul cant disperse the crowd before the cut
		public static void InviteSpectators(Thing block, Pawn executioner)
		{
			Map map = block.Map;
			if (map == null) return;
			CellRect rect = block.OccupiedRect();
			int invited = 0;
			var taken = new List<IntVec3>(); // no two spectators on the same cell
			foreach (Pawn p in map.mapPawns.FreeColonistsSpawned
				.OrderBy(x => (x.Position - block.Position).LengthHorizontalSquared).ToList())
			{
				if (invited >= 8) break;
				if (p.CurJobDef == RimSiegeDefOf.RimSiege_WatchExecutionJob)
				{
					invited++; // already in the crowd, keep his spot out of the pool
					taken.Add(p.CurJob.targetA.Cell);
					continue;
				}
				if (p == executioner || p.Downed || p.Drafted || p.InMentalState || !p.Awake()) continue;
				if (!SpectatorCellFinder.TryFindCircleSpectatorCellFor(p, rect, 2f, 5f, map, out IntVec3 cell, taken)) continue;
				Job job = JobMaker.MakeJob(RimSiegeDefOf.RimSiege_WatchExecutionJob, cell, block.Position);
				job.expiryInterval = 7500;
				if (p.jobs.TryTakeOrderedJob(job, JobTag.Misc)) { invited++; taken.Add(cell); }
			}
		}

		private void ExecuteNow(CaptainCase c)
		{
			Pawn cap = c.captain;
			Pawn ex = cap.MapHeld?.mapPawns.FreeColonistsSpawned
				.Where(p => !p.Downed && !p.WorkTagIsDisabled(WorkTags.Violent))
				.OrderBy(p => (p.Position - cap.PositionHeld).LengthHorizontalSquared)
				.FirstOrDefault();
			if (ex != null)
			{
				ExecutionUtility.DoExecutionByCut(ex, cap);
				ThoughtUtility.GiveThoughtsForPawnExecuted(cap, ex, PawnExecutionKind.GenericBrutal);
			}
			else cap.Kill(null);
			FinishExecution(c);
		}

		private void FinishExecution(CaptainCase c)
		{
			Messages.Message("RimSiege_CaptainExecuted".Translate(CaptainName(c), c.faction?.Name),
				MessageTypeDefOf.NegativeEvent);
			SwearVendetta(c);
			Close(c);
		}

		public void Notify_EnvoysArrived(int caseId)
		{
			CaptainCase c = GetCase(caseId);
			if (c == null || c.state != CaptainCase.Exchange) return;
			c.arrived = true;

			// plant the house banner at the meeting spot. MO banners are stuffed furniture
			ThingDef bannerDef = SiegeLauncher.HouseBanner(c.faction);
			if (bannerDef != null && c.banner == null && c.map != null && c.waitSpot.IsValid)
			{
				foreach (IntVec3 cell in GenRadial.RadialCellsAround(c.waitSpot, 3f, useCenter: true))
				{
					if (!cell.InBounds(c.map) || !cell.Standable(c.map) || cell.GetEdifice(c.map) != null) continue;
					ThingDef stuff = bannerDef.MadeFromStuff ? GenStuff.DefaultStuffFor(bannerDef) : null;
					c.banner = GenSpawn.Spawn(ThingMaker.MakeThing(bannerDef, stuff), cell, c.map);
					break;
				}
			}
		}

		public void Notify_EnvoysHarmed(int caseId)
		{
			CaptainCase c = GetCase(caseId);
			if (c == null || c.state != CaptainCase.Exchange) return;
			Messages.Message("RimSiege_EnvoysBetrayed".Translate(), MessageTypeDefOf.NegativeEvent);
			ResetCustody(c.captain);
			SwearVendetta(c);
			c.state = CaptainCase.Kept;
			c.eventTick = -1;
			DestroyBanner(c);
		}

		public override void GameComponentTick()
		{
			int now = Find.TickManager.TicksGame;
			if (now % CheckInterval != 0) return;

			for (int i = demands.Count - 1; i >= 0; i--)
			{
				SiegeDemand d = demands[i];
				// late offer whose host is already gone: stale letter, just drop it
				bool stale = d.late && (d.siegeLord == null || d.siegeLord.ownedPawns.Count == 0);
				if (now >= d.fireAtTick || stale)
				{
					if (!d.late) Launch(d); // ignored us, too bad
					Resolve(d);
				}
			}

			for (int i = cases.Count - 1; i >= 0; i--)
				TickCase(cases[i], now);

			// vendettas fire no matter what settings say about ultimatums
			foreach (FactionSiegeMemory m in memories)
			{
				if (m.vengeTick < 0 || now < m.vengeTick) continue;
				m.vengeTick = -1;
				string slain = m.slain;
				m.slain = null;
				if (m.faction == null || m.faction.defeated || !m.faction.HostileTo(Faction.OfPlayer)) continue;
				Map vmap = Find.AnyPlayerHomeMap;
				if (vmap == null || !SiegeLauncher.TryFindSiegeTarget(vmap, out _)) continue;
				SiegeLauncher.ExecuteSiege(vmap, m.faction,
					StorytellerUtility.DefaultThreatPointsNow(vmap) * 1.25f, slain);
			}

			// danegeld comebacks
			if (!RimSiegeMod.S.danegeld || !RimSiegeMod.S.ultimatumEnabled) return;
			foreach (FactionSiegeMemory m in memories)
			{
				if (m.comebackTick < 0 || now < m.comebackTick) continue;
				m.comebackTick = -1;
				if (m.faction == null || m.faction.defeated || !m.faction.HostileTo(Faction.OfPlayer)) continue;
				if (demands.Any(x => x.faction == m.faction)) continue; // already knocking
				Map map = Find.AnyPlayerHomeMap;
				if (map == null || !SiegeLauncher.TryFindSiegeTarget(map, out _)) continue;
				OpenDemand(map, m.faction, StorytellerUtility.DefaultThreatPointsNow(map));
			}
		}

		private void TickCase(CaptainCase c, int now)
		{
			Pawn cap = c.captain;
			if (cap == null) { Close(c); return; }

			switch (c.state)
			{
				case CaptainCase.Loose:
					if (cap.Dead || (now >= c.eventTick)) { Close(c); return; }
					// SpawnedOrAnyParentSpawned: being hauled to a prison bed counts as still here
					if (!cap.SpawnedOrAnyParentSpawned && !cap.IsPrisonerOfColony) { Close(c); return; }
					if (cap.IsPrisonerOfColony)
					{
						c.state = CaptainCase.Jailed;
						c.eventTick = now + Rand.Range(30000, 90000); // house takes a while to hear of it
					}
					break;

				case CaptainCase.Jailed:
					if (HandleCustody(c, cap)) return;
					if (now >= c.eventTick)
					{
						if (c.faction == null || c.faction.defeated) { Close(c); return; }
						OpenOffer(c, now);
					}
					break;

				case CaptainCase.OfferUp:
					if (HandleCustody(c, cap, removeLetter: true)) return;
					if (now >= c.eventTick)
					{
						RemoveRansomLetter(c); // no answer is an answer
						c.state = CaptainCase.Kept;
						c.eventTick = -1;
					}
					break;

				case CaptainCase.Exchange:
					if (cap.Dead)
					{
						Messages.Message("RimSiege_CaptainDiedCustody".Translate(CaptainName(c), c.faction?.Name),
							MessageTypeDefOf.NegativeEvent);
						SwearVendetta(c);
						SendMemo(c, LordJob_RansomEnvoys.MemoLeave);
						Close(c);
						return;
					}
					if (cap.Faction == Faction.OfPlayer) { FailExchange(c); return; } // turned his coat mid-deal

					// success checked before the deadline, or a pickup on the last tick
					// would pay nothing and still lose the captain
					if (c.arrived)
					{
						bool handedOver = (cap.guest?.Released ?? false)
							|| (cap.CarriedBy != null && cap.CarriedBy.GetLord() == c.envoys)
							|| !cap.IsPrisonerOfColony;
						if (handedOver) { SucceedExchange(c, now); return; }
					}
					if (now >= c.eventTick) { FailExchange(c); return; }
					if (!c.arrived) break;

					if (cap.Downed)
					{
						if (!c.fetchSent)
						{
							c.fetchSent = true;
							SendMemo(c, LordJob_RansomEnvoys.MemoFetch);
						}
					}
					else
					{
						cap.guest?.SetExclusiveInteraction(PrisonerInteractionModeDefOf.Release);
					}
					break;

				case CaptainCase.Kept:
					HandleCustody(c, cap);
					break;

				case CaptainCase.Condemned:
					if (cap.Dead) { FinishExecution(c); return; }
					if (!cap.IsPrisonerOfColony) { c.state = CaptainCase.Kept; c.eventTick = -1; return; } // fled the block
					if (now >= c.eventTick) { ExecuteNow(c); return; } // scene never happened, axe falls anyway
					break;

				case CaptainCase.Closing:
					if (cap.Dead || !cap.Spawned || now >= c.eventTick)
					{
						if (!cap.Dead && !cap.Spawned && cap.guest?.HostFaction == Faction.OfPlayer)
							cap.guest.SetGuestStatus(null);
						Close(c);
					}
					break;
			}
		}

		// shared jail watch: death means vendetta, mercy earns a respite. true = case resolved
		private bool HandleCustody(CaptainCase c, Pawn cap, bool removeLetter = false)
		{
			if (cap.Dead)
			{
				if (removeLetter) RemoveRansomLetter(c);
				Messages.Message("RimSiege_CaptainDiedCustody".Translate(CaptainName(c), c.faction?.Name),
					MessageTypeDefOf.NegativeEvent);
				SwearVendetta(c);
				Close(c);
				return true;
			}
			if (!cap.IsPrisonerOfColony)
			{
				if (removeLetter) RemoveRansomLetter(c);
				if (cap.Faction != Faction.OfPlayer && (cap.guest?.Released ?? false))
				{
					// let go for nothing: the gesture buys quiet walls for a while
					respiteUntilTick = Mathf.Max(respiteUntilTick,
						Find.TickManager.TicksGame + Rand.Range(5, 10) * 60000);
					Messages.Message("RimSiege_CaptainFreed".Translate(CaptainName(c), c.faction?.Name),
						MessageTypeDefOf.PositiveEvent);
				}
				Close(c);
				return true;
			}
			return false;
		}

		private void OpenOffer(CaptainCase c, int now)
		{
			c.state = CaptainCase.OfferUp;
			c.eventTick = now + 30000; // half a day to make up your mind

			var letter = (ChoiceLetter_CaptainRansom)LetterMaker.MakeLetter(
				"RimSiege_RansomLabel".Translate(CaptainName(c)),
				"RimSiege_RansomText".Translate(c.faction?.Name, CaptainName(c), c.silver),
				RimSiegeDefOf.RimSiege_RansomOffer, c.faction);
			letter.caseId = c.id;
			letter.StartTimeout(30000);
			Find.LetterStack.ReceiveLetter(letter);
		}

		private bool SpawnEnvoys(CaptainCase c)
		{
			Map map = c.map;
			if (map == null || !Find.Maps.Contains(map)) return false;
			Faction envoyFaction = EnvoyFaction();
			if (envoyFaction == null) return false;

			PawnKindDef crew = SiegeLauncher.CrewKindFor(c.faction);
			PawnKindDef standard = SiegeLauncher.HouseStandardKind(c.faction) ?? crew;
			if (crew == null) return false;

			IntVec3 entry = SiegeLauncher.EntryCellFor(map, c.captain.PositionHeld);

			// same spot visitors chill at: right outside the colony, not some map-edge ditch
			TraverseParms tp = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
			if (!RCellFinder.TryFindRandomSpotJustOutsideColony(entry, map, null, out IntVec3 spot,
					cell => map.reachability.CanReach(entry, cell, PathEndMode.OnCell, tp)
						&& SiegeLauncher.HostileCanReachColonyFrom(map, cell)))
				spot = RCellFinder.FindSiegePositionFrom(entry, map, allowRoofed: false, errorOnFail: false,
					cell => SiegeLauncher.HostileCanReachColonyFrom(map, cell));
			c.waitSpot = spot.IsValid ? spot : entry;

			var party = new List<Pawn>();
			Pawn bannerman = MakeEnvoy(standard, envoyFaction, map, entry, stripWeapons: false);
			Pawn carrier = MakeEnvoy(crew, envoyFaction, map, entry, stripWeapons: true);
			c.porter = MakeEnvoy(crew, envoyFaction, map, entry, stripWeapons: true);
			if (bannerman != null) party.Add(bannerman);
			if (carrier != null) party.Add(carrier);
			if (c.porter != null) party.Add(c.porter);
			if (party.Count == 0) return false;
			if (carrier == null) carrier = party[0];

			// the porter hauls the actual coin. kill him for it and see what it costs you
			if (c.porter != null)
			{
				int rest = c.silver;
				while (rest > 0)
				{
					Thing coin = ThingMaker.MakeThing(ThingDefOf.Silver);
					coin.stackCount = Mathf.Min(coin.def.stackLimit, rest);
					rest -= coin.stackCount;
					c.porter.inventory.innerContainer.TryAdd(coin);
				}
			}

			c.envoys = LordMaker.MakeNewLord(envoyFaction,
				new LordJob_RansomEnvoys(c.id, c.faction, c.waitSpot, c.captain, carrier), map, party);
			c.arrived = false;
			c.fetchSent = false;
			return true;
		}

		private static Pawn MakeEnvoy(PawnKindDef kind, Faction faction, Map map, IntVec3 entry, bool stripWeapons)
		{
			if (kind == null) return null;
			Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
				kind, faction, PawnGenerationContext.NonPlayer,
				tile: map.Tile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
			if (stripWeapons) p.equipment?.DestroyAllEquipment();
			GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 6), map);
			return p;
		}

		private Faction EnvoyFaction()
		{
			Faction f = Find.FactionManager.FirstFactionOfDef(RimSiegeDefOf.RimSiege_Envoys);
			if (f == null)
			{
				// saves from before this update never generated one
				FactionGenerator.CreateFactionAndAddToManager(RimSiegeDefOf.RimSiege_Envoys);
				f = Find.FactionManager.FirstFactionOfDef(RimSiegeDefOf.RimSiege_Envoys);
			}
			return f;
		}

		private void SucceedExchange(CaptainCase c, int now)
		{
			// healed up mid-deal and never got walked out: run the proper release so he leaves on his own
			Pawn cap = c.captain;
			if (cap != null && cap.Spawned && !cap.Downed && cap.IsPrisonerOfColony
				&& cap.CarriedBy == null && !(cap.guest?.Released ?? false))
				GenGuest.PrisonerRelease(cap);

			if (c.porter != null && !c.porter.Dead && c.porter.Spawned && c.porter.inventory != null)
				c.porter.inventory.innerContainer.TryDropAll(c.porter.Position, c.map, ThingPlaceMode.Near);
			Messages.Message("RimSiege_RansomPaid".Translate(c.silver, CaptainName(c)),
				MessageTypeDefOf.PositiveEvent);
			SendMemo(c, LordJob_RansomEnvoys.MemoLeave);
			DestroyBanner(c);
			c.state = CaptainCase.Closing;
			c.eventTick = now + 60000;
		}

		private void FailExchange(CaptainCase c)
		{
			Messages.Message("RimSiege_RansomFailed".Translate(), MessageTypeDefOf.NeutralEvent);
			ResetCustody(c.captain);
			SendMemo(c, LordJob_RansomEnvoys.MemoLeave);
			DestroyBanner(c);
			c.state = CaptainCase.Kept;
			c.eventTick = -1;
		}

		// deal fell through: back to being a regular prisoner, wardens feed him again
		private static void ResetCustody(Pawn cap)
		{
			if (cap?.guest == null || !cap.IsPrisonerOfColony) return;
			cap.guest.Released = false;
			cap.guest.SetExclusiveInteraction(PrisonerInteractionModeDefOf.MaintainOnly);
		}

		private void SwearVendetta(CaptainCase c)
		{
			if (c.faction == null || c.faction.defeated) return;
			FactionSiegeMemory m = MemoryFor(c.faction, true);
			if (m == null) return;
			m.slain = CaptainName(c);
			m.vengeTick = Find.TickManager.TicksGame + Rand.Range(4, 8) * 60000;
			Find.LetterStack.ReceiveLetter(
				"RimSiege_VendettaLabel".Translate(c.faction.Name),
				"RimSiege_VendettaText".Translate(c.faction.Name, m.slain),
				LetterDefOf.NegativeEvent, null, c.faction);
		}

		private static string CaptainName(CaptainCase c) =>
			c.captain?.Name?.ToStringShort ?? c.captain?.LabelShort ?? "";

		private void SendMemo(CaptainCase c, string memo)
		{
			if (c.envoys != null && c.envoys.ownedPawns.Count > 0) c.envoys.ReceiveMemo(memo);
		}

		private void DestroyBanner(CaptainCase c)
		{
			if (c.banner != null && !c.banner.Destroyed) c.banner.Destroy();
			c.banner = null;
		}

		private void Close(CaptainCase c)
		{
			DestroyBanner(c);
			cases.Remove(c);
		}

		private void RemoveRansomLetter(CaptainCase c)
		{
			ChoiceLetter_CaptainRansom letter = Find.LetterStack.LettersListForReading
				.OfType<ChoiceLetter_CaptainRansom>().FirstOrDefault(l => l.caseId == c.id);
			if (letter != null) Find.LetterStack.RemoveLetter(letter);
		}

		private void Launch(SiegeDemand d)
		{
			if (d.late) return;
			if (d.faction == null || d.faction.defeated) return;
			if (d.map == null || !Find.Maps.Contains(d.map)) return;
			SiegeLauncher.ExecuteSiege(d.map, d.faction, d.points);
		}

		private void Resolve(SiegeDemand d)
		{
			demands.Remove(d);
			ChoiceLetter_SiegeDemand letter = Find.LetterStack.LettersListForReading
				.OfType<ChoiceLetter_SiegeDemand>().FirstOrDefault(l => l.demandId == d.id);
			if (letter != null) Find.LetterStack.RemoveLetter(letter);
		}

		public override void ExposeData()
		{
			Scribe_Collections.Look(ref demands, "demands", LookMode.Deep);
			Scribe_Collections.Look(ref memories, "memories", LookMode.Deep);
			Scribe_Collections.Look(ref cases, "cases", LookMode.Deep);
			Scribe_Values.Look(ref nextId, "nextId", 1);
			Scribe_Values.Look(ref respiteUntilTick, "respiteUntilTick", -1);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (demands == null) demands = new List<SiegeDemand>();
				if (memories == null) memories = new List<FactionSiegeMemory>();
				if (cases == null) cases = new List<CaptainCase>();
				memories.RemoveAll(m => m == null || m.faction == null);
				cases.RemoveAll(x => x == null || x.captain == null);
			}
		}
	}
}
