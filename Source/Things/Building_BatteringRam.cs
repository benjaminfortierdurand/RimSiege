using Verse;

namespace RimSiege.Things
{
	public class Building_BatteringRam : Building_SiegeEngine
	{
		public Thing breachThing; // locked in on the first swing
		public int ramDamage = 120;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref breachThing, "breachThing");
			Scribe_Values.Look(ref ramDamage, "ramDamage", 120);
		}
	}
}
