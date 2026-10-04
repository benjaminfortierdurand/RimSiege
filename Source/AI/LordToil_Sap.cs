using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	public class LordToil_Sap : LordToil
	{
		private IntVec3 targetWall;

		public LordToil_Sap() { }
		public LordToil_Sap(IntVec3 targetWall) { this.targetWall = targetWall; }

		public override void UpdateAllDuties()
		{
			foreach (Pawn p in lord.ownedPawns)
				p.mindState.duty = new PawnDuty(RimSiegeDefOf.RimSiege_Sap, targetWall);
		}
	}
}
