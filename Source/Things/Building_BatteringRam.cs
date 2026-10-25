using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	public class Building_BatteringRam : Building_SiegeEngine
	{
		public Thing breachThing; // locked in on the first swing
		public int ramDamage = 120;

		// the blow: pull back over WindUp ticks, slam in Lunge, ease back over Settle. the toil starts it
		// SwingLead ticks before it deals the damage so the hit lands on the frame the ram meets the wall
		public const int WindUp = 40, Lunge = 5, Settle = 20;
		public const int SwingLead = WindUp + Lunge;
		private int swingStart = -99999; // not saved, a load mid swing just skips that one

		// how far it pulls back, in cells. the wheeled ram only jolts, the log ram swings from the arms
		protected virtual float SwingBack => 0.1f;
		public virtual float ShakeOnHit => 0.1f;
		public virtual int StrikeInterval => 180; // ticks between blows

		public void BeginSwing(int now) => swingStart = now;
		public bool SwingStartedAfter(int tick) => swingStart > tick;
		protected int SwingTick => Find.TickManager.TicksGame - swingStart; // ticks since the wind-up began

		protected Vector3 SwingOffset
		{
			get
			{
				int t = Find.TickManager.TicksGame - swingStart;
				if (t < 0 || t >= SwingLead + Settle) return Vector3.zero;
				float back = SwingBack, reach = SwingBack * 0.25f, d;
				if (t < WindUp) d = -back * Mathf.Sin(t / (float)WindUp * Mathf.PI / 2f);
				else if (t < SwingLead) { float p = (t - WindUp) / (float)Lunge; d = -back + (back + reach) * p * p; }
				else { float p = (t - SwingLead) / (float)Settle; d = reach * (1f - p) - back * 0.2f * Mathf.Sin(p * Mathf.PI); }
				return Rotation.FacingCell.ToVector3() * d;
			}
		}

		protected override Vector3 ExtraDrawOffset => SwingOffset;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref breachThing, "breachThing");
			Scribe_Values.Look(ref ramDamage, "ramDamage", 120);
		}
	}
}
