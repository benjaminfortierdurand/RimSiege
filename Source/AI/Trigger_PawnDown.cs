using Verse;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// fires once the tracked pawn is dead/downed/gone (used for the captain-rout)
	public class Trigger_PawnDown : Trigger
	{
		private Pawn pawn;

		public Trigger_PawnDown() { }
		public Trigger_PawnDown(Pawn pawn) { this.pawn = pawn; }

		public override bool ActivateOn(Lord lord, TriggerSignal signal)
		{
			if (signal.type != TriggerSignalType.Tick) return false;
			return pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned;
		}
	}
}
