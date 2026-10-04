using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// truce party: walk in, wait by the banner, take the captain home
	public class LordJob_RansomEnvoys : LordJob
	{
		public const string MemoFetch = "RimSiege_FetchCaptain";
		public const string MemoLeave = "RimSiege_EnvoysLeave";

		private int caseId;
		private Faction house;
		private IntVec3 waitSpot;
		private Pawn captain;
		private Pawn carrier;

		public LordJob_RansomEnvoys() { }

		public LordJob_RansomEnvoys(int caseId, Faction house, IntVec3 waitSpot, Pawn captain, Pawn carrier)
		{
			this.caseId = caseId;
			this.house = house;
			this.waitSpot = waitSpot;
			this.captain = captain;
			this.carrier = carrier;
		}

		// only the designated carrier may walk through colony doors: while fetching his lord,
		// and while carrying him out (the lord flips to exit the moment he picks him up)
		public override bool CanOpenAnyDoor(Pawn p) =>
			p == carrier
			&& (lord?.CurLordToil is LordToil_FetchCaptain
				|| (captain != null && p.carryTracker?.CarriedThing == captain));

		public override StateGraph CreateGraph()
		{
			var graph = new StateGraph();
			LordToil travel = graph.AttachSubgraph(new LordJob_Travel(waitSpot).CreateGraph()).StartingToil;
			graph.StartingToil = travel;

			var wait = new LordToil_EnvoyWait(waitSpot);
			graph.AddToil(wait);
			var fetch = new LordToil_FetchCaptain(waitSpot, captain, carrier);
			graph.AddToil(fetch);
			var exit = new LordToil_ExitMap(LocomotionUrgency.Walk, canDig: false);
			graph.AddToil(exit);

			var arrive = new Transition(travel, wait);
			arrive.AddTrigger(new Trigger_Memo("TravelArrived"));
			arrive.AddPostAction(new TransitionAction_Custom((System.Action)delegate
			{
				GameComponent_SiegeDemands.Get()?.Notify_EnvoysArrived(caseId);
			}));
			graph.AddTransition(arrive);

			var goFetch = new Transition(wait, fetch);
			goFetch.AddTrigger(new Trigger_Memo(MemoFetch));
			graph.AddTransition(goFetch);

			// no EndAllJobs here: it would make the carrier drop the captain mid-handover
			var leave = new Transition(travel, exit);
			leave.AddSources(wait, fetch);
			leave.AddTrigger(new Trigger_Memo(MemoLeave));
			graph.AddTransition(leave);

			// blood under the white flag, deal's off. exits dont count as losses here
			var harmed = new Transition(travel, exit);
			harmed.AddSources(wait, fetch);
			harmed.AddTrigger(new Trigger_PawnLost(PawnLostCondition.Incapped));
			harmed.AddTrigger(new Trigger_PawnLost(PawnLostCondition.Killed));
			harmed.AddTrigger(new Trigger_PawnLost(PawnLostCondition.MadePrisoner));
			harmed.AddPostAction(new TransitionAction_Custom((System.Action)delegate
			{
				GameComponent_SiegeDemands.Get()?.Notify_EnvoysHarmed(caseId);
			}));
			harmed.AddPostAction(new TransitionAction_EndAllJobs());
			graph.AddTransition(harmed);

			// hard backstop so they never squat the map
			var stale = new Transition(travel, exit);
			stale.AddSources(wait, fetch);
			stale.AddTrigger(new Trigger_TicksPassed(150000));
			graph.AddTransition(stale);

			return graph;
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref caseId, "caseId");
			Scribe_References.Look(ref house, "house");
			Scribe_Values.Look(ref waitSpot, "waitSpot");
			Scribe_References.Look(ref captain, "captain");
			Scribe_References.Look(ref carrier, "carrier");
		}
	}

	public class LordToil_EnvoyWait : LordToil
	{
		private IntVec3 spot;

		public LordToil_EnvoyWait(IntVec3 spot) { this.spot = spot; }

		public override void UpdateAllDuties()
		{
			foreach (Pawn p in lord.ownedPawns)
				p.mindState.duty = new PawnDuty(DutyDefOf.Defend, spot) { radius = 4f };
		}
	}

	public class LordToil_FetchCaptain : LordToil
	{
		private IntVec3 spot;
		private Pawn captain;
		private Pawn carrier;

		public LordToil_FetchCaptain(IntVec3 spot, Pawn captain, Pawn carrier)
		{
			this.spot = spot;
			this.captain = captain;
			this.carrier = carrier;
		}

		public override void UpdateAllDuties()
		{
			foreach (Pawn p in lord.ownedPawns)
			{
				if (p == carrier && captain != null && !captain.Dead)
					p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_RetrieveCaptain, captain);
				else
					p.mindState.duty = new PawnDuty(DutyDefOf.Defend, spot) { radius = 4f };
			}
		}
	}
}
