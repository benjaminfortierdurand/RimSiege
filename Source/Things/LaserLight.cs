using Verse;

namespace RimSiege.Things
{
	// a real light where the laser ram's beam hits: an unseen thing with a CompGlower, kept alive by the ram
	// every tick it burns. left alone (beam off, or a load cut the ram's link) it goes out on its own
	public class LaserLight : ThingWithComps
	{
		public int expiresAt;

		protected override void Tick()
		{
			base.Tick();
			if (Find.TickManager.TicksGame > expiresAt) Destroy();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref expiresAt, "expiresAt");
		}
	}
}
