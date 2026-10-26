using Verse;

namespace RimSiege.AI
{
	// tribal ladder: same ferry as the tower, just leaned right on the wall and climbed one or two at a time.
	// light, so it moves at a trot, but the climb is slower than a ramp
	public class LordToil_SiegeLadder : LordToil_SiegeTower
	{
		protected override ThingDef DefaultEngineDef => RimSiegeDefOf.RimSiege_SiegeLadder;

		protected override int AdvanceInterval => 22;
		protected override int DockDistanceSq => 1;    // top end rests on the wall face
		protected override int DeployInterval => 150;
		protected override int MaxBoarding => 2;
		// board range stays the tower's 13: climbers wait within 2 of BackCell, that is up to 9 from Position
	}
}
