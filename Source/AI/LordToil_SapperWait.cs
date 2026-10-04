using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	public class LordToil_SapperWait : LordToil
	{
		private IntVec3 rally;

		public LordToil_SapperWait() { }
		public LordToil_SapperWait(IntVec3 rally) { this.rally = rally; }

		public override void UpdateAllDuties()
		{
			foreach (Pawn p in lord.ownedPawns)
				p.mindState.duty = new PawnDuty(DutyDefOf.Defend, rally) { radius = 12f };
		}
	}
}
