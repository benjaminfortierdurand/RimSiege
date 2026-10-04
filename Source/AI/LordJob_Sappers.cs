using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	public class LordJob_Sappers : LordJob
	{
		private IntVec3 targetWall;
		private IntVec3 rally; // invalid = sap right away

		public IntVec3 TargetWall => targetWall; // the main lord watches this for the 2nd breach

		public override bool GuiltyOnDowned => true;

		public LordJob_Sappers() { }
		public LordJob_Sappers(IntVec3 targetWall, IntVec3 rally)
		{
			this.targetWall = targetWall;
			this.rally = rally;
		}

		public override StateGraph CreateGraph()
		{
			var graph = new StateGraph();

			var sap = new LordToil_Sap(targetWall);
			graph.AddToil(sap);

			var exit = new LordToil_ExitMap(LocomotionUrgency.Jog, canDig: true, interruptCurrentJob: true);
			graph.AddToil(exit);

			var toExit = new Transition(sap, exit);
			toExit.AddTrigger(new Trigger_BecameNonHostileToPlayer());

			// main host bought off or routed: nothing left to open a breach for
			var over = new Transition(sap, exit);
			over.AddTrigger(new Trigger_Memo("SiegeOver"));

			if (rally.IsValid)
			{
				// hang at the camp, peel off to sap once the assault starts
				var wait = new LordToil_SapperWait(rally);
				graph.AddToil(wait);
				graph.StartingToil = wait;

				var toSap = new Transition(wait, sap);
				toSap.AddTrigger(new Trigger_MainSiegeAttacking());
				graph.AddTransition(toSap);

				toExit.AddSource(wait);
				over.AddSource(wait);
			}
			else
			{
				graph.StartingToil = sap;
			}

			graph.AddTransition(toExit);
			graph.AddTransition(over);
			return graph;
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref targetWall, "targetWall");
			Scribe_Values.Look(ref rally, "rally");
		}
	}
}
