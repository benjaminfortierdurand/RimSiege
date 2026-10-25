using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// post-breach: everyone gets RimSiege_AssaultThroughBreach (fight + rush the colonists, no wrecking
	// buildings). they pour through the breach; they only bash a door or wall when nobody can be reached
	// without one (ladder dropped them in an airlock, colonists shut in their rooms)
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
