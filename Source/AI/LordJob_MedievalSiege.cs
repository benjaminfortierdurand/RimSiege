using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// build camp -> push engine -> pour through the breach -> leave
	public class LordJob_MedievalSiege : LordJob
	{
		private Faction faction;
		private IntVec3 siegeSpot;
		private IntVec3 breach;
		private ThingDef engineDef;  // null = foot assault
		private int trebuchetCount;
		private int ballistaCount;
		private bool tarAmmo;
		private bool carrionAmmo;
		private float points;
		private Pawn captain;
		public bool reinforced;     // second column already marched in
		public bool lateOfferSent;  // the engines-are-up tribute letter went out

		public float Points => points;

		public override bool GuiltyOnDowned => true;

		public LordJob_MedievalSiege() { }

		public LordJob_MedievalSiege(Faction faction, IntVec3 siegeSpot, IntVec3 breach,
			ThingDef engineDef, int trebuchetCount, int ballistaCount, bool tarAmmo, bool carrionAmmo, float points, Pawn captain = null)
		{
			this.faction = faction;
			this.siegeSpot = siegeSpot;
			this.breach = breach;
			this.engineDef = engineDef;
			this.trebuchetCount = trebuchetCount;
			this.ballistaCount = ballistaCount;
			this.tarAmmo = tarAmmo;
			this.carrionAmmo = carrionAmmo;
			this.points = points;
			this.captain = captain;
		}

		public override StateGraph CreateGraph()
		{
			var graph = new StateGraph();

			// sappers on: this is where a siege lands when the engine is lost or walled off.
			// without it they stand outside an intact wall forever
			StateGraph assaultGraph = graph.AttachSubgraph(
				new LordJob_AssaultColony(faction, canKidnap: true, canTimeoutOrFlee: true, sappers: true).CreateGraph());
			LordToil assault = assaultGraph.StartingToil;

			// tower sieges retreat back over the tower, everyone else digs out if trapped
			LordToil exit = (engineDef == RimSiegeDefOf.RimSiege_SiegeTower)
				? (LordToil)new LordToil_ExitViaTower()
				: new LordToil_ExitMap(LocomotionUrgency.Jog, canDig: true, interruptCurrentJob: true);
			graph.AddToil(exit);

			var build = new LordToil_BuildSiege(siegeSpot, breach, engineDef, trebuchetCount, ballistaCount, tarAmmo, carrionAmmo) { captain = captain };
			graph.AddToil(build);
			graph.StartingToil = build;

			LordToil push = null;
			if (engineDef != null)
			{
				push = (engineDef == RimSiegeDefOf.RimSiege_SiegeTower) ? (LordToil)new LordToil_SiegeTower() : new LordToil_PushRam();
				((LordToil_PushEngine)push).captain = captain;
				graph.AddToil(push);

				var breachAssault = new LordToil_BreachAssault();
				graph.AddToil(breachAssault);

				var buildToPush = new Transition(build, push);
				buildToPush.AddTrigger(new Trigger_Memo("EnginesBuilt"));
				buildToPush.AddPostAction(new TransitionAction_WakeAll());
				graph.AddTransition(buildToPush);

				var toBreach = new Transition(push, breachAssault);
				toBreach.AddTrigger(new Trigger_Memo("BreachOpen"));
				toBreach.AddPostAction(new TransitionAction_WakeAll());
				graph.AddTransition(toBreach);

				var toAssault = new Transition(push, assault);
				toAssault.AddTrigger(new Trigger_Memo("RamLost"));
				toAssault.AddTrigger(new Trigger_FractionPawnsLost(0.7f)); // decimated, ditch the engine
				toAssault.AddPostAction(new TransitionAction_WakeAll());
				graph.AddTransition(toAssault);

				// breach assault stalled (wall rebuilt or whatever), fall back to a plain assault
				var breachToAssault = new Transition(breachAssault, assault);
				breachToAssault.AddTrigger(new Trigger_TicksPassed(5000));
				graph.AddTransition(breachToAssault);

				var toExit = new Transition(push, exit);
				toExit.AddSource(breachAssault);
				toExit.AddSource(assault);
				toExit.AddTrigger(new Trigger_BecameNonHostileToPlayer());
				graph.AddTransition(toExit);
			}
			else
			{
				var toExit = new Transition(assault, exit);
				toExit.AddTrigger(new Trigger_BecameNonHostileToPlayer());
				graph.AddTransition(toExit);
			}

			var buildToAssault = new Transition(build, assault);
			buildToAssault.AddTrigger(new Trigger_Memo("BuildFailed"));
			buildToAssault.AddTrigger(new Trigger_FractionPawnsLost(0.7f));
			buildToAssault.AddPostAction(new TransitionAction_WakeAll());
			graph.AddTransition(buildToAssault);

			// captain down before the breach = the whole thing routs.
			// a foot war party has no breach: their charge breaks whenever the leader falls
			if (captain != null && (RimSiegeMod.S?.captainRout ?? true))
			{
				AddCaptainRout(graph, build, exit);
				if (push != null) AddCaptainRout(graph, push, exit);
				else AddCaptainRout(graph, assault, exit, assaultGraph.lordToils);
			}

			// paid the late tribute: pack up and go, engines stay where they stand
			var boughtOff = new Transition(build, exit);
			if (push != null) boughtOff.AddSource(push);
			boughtOff.AddTrigger(new Trigger_Memo("BoughtOff"));
			boughtOff.AddPostAction(new TransitionAction_Custom((System.Action)delegate { DismissSappers(); }));
			boughtOff.AddPostAction(new TransitionAction_WakeAll());
			graph.AddTransition(boughtOff);

			return graph;
		}

		// the second-breach squad has no business sapping once the host packs up
		private void DismissSappers()
		{
			if (lord?.Map == null) return;
			foreach (Lord l in lord.Map.lordManager.lords)
				if (l != lord && l.faction == faction && l.LordJob is LordJob_Sappers)
					l.ReceiveMemo("SiegeOver");
		}

		private void AddCaptainRout(StateGraph graph, LordToil from, LordToil exit, List<LordToil> extraSources = null)
		{
			var rout = new Transition(from, exit);
			if (extraSources != null)
				foreach (LordToil t in extraSources)
					if (t != from) rout.AddSource(t);
			rout.AddTrigger(new Trigger_PawnDown(captain));
			rout.AddPostAction(new TransitionAction_Message(
				"RimSiege_CaptainFell".Translate(captain.Name?.ToStringShort ?? captain.LabelShort),
				MessageTypeDefOf.PositiveEvent));
			rout.AddPostAction(new TransitionAction_Custom((System.Action)delegate
			{
				GameComponent_SiegeDemands.Get()?.Notify_SiegeRouted(); // word spreads, long respite
				DismissSappers();
			}));
			rout.AddPostAction(new TransitionAction_WakeAll());
			graph.AddTransition(rout);
		}

		public override void ExposeData()
		{
			Scribe_References.Look(ref faction, "faction");
			Scribe_Values.Look(ref siegeSpot, "siegeSpot");
			Scribe_Values.Look(ref breach, "breach");
			Scribe_Defs.Look(ref engineDef, "engineDef");
			Scribe_Values.Look(ref trebuchetCount, "trebuchetCount");
			Scribe_Values.Look(ref ballistaCount, "ballistaCount");
			Scribe_Values.Look(ref tarAmmo, "tarAmmo");
			Scribe_Values.Look(ref carrionAmmo, "carrionAmmo");
			Scribe_Values.Look(ref points, "points");
			Scribe_Values.Look(ref reinforced, "reinforced");
			Scribe_Values.Look(ref lateOfferSent, "lateOfferSent");
			Scribe_References.Look(ref captain, "captain");
		}
	}
}
