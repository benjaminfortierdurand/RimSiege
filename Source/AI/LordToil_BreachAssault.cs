using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// post-breach: everyone gets RimSiege_AssaultThroughBreach (fight + rush the colonists, no sapping,
	// no wrecking buildings). they cant punch a wall so they pour through the only gap, the breach.
	public class LordToil_BreachAssault : LordToil
	{
		public override bool AllowSatisfyLongNeeds => false;

		public override void UpdateAllDuties()
		{
			foreach (Pawn p in lord.ownedPawns)
				p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_AssaultThroughBreach);
		}
	}
}
