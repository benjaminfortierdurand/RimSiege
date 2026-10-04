namespace RimSiege.Things
{
	// docked, it teleports guys over the wall (LordToil_SiegeTower). the wall stays up
	public class Building_SiegeTower : Building_SiegeEngine
	{
		protected override float RotateDegreesPerTick => 1.5f; // heavy, turns slow
	}
}
