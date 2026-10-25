using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	// laser bolt: flies over walls like a shell (flyOverhead) but drawn as a glowing streak with no shadow,
	// a soft halo and a trail of fading motes. it climbs out of the cannon's mouth and curves down onto the
	// target (arcHeightFactor), the streak follows the curve
	[StaticConstructorOnStartup]
	public class Projectile_LaserBolt : Projectile_Explosive
	{
		private static readonly Material Halo = MaterialPool.MatFrom("Things/Mote/RimSiege/LaserGlow", ShaderDatabase.MoteGlow,
			new Color(LaserFx.Blue.r, LaserFx.Blue.g, LaserFx.Blue.b, 0.7f));

		public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
			ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
		{
			base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
			Building_LaserBattery battery = Map != null ? origin.ToIntVec3().GetFirstThing<Building_LaserBattery>(Map) : null;
			if (battery == null) return;
			this.origin = battery.Muzzle;
			battery.Notify_BoltFired();
		}

		// vanilla's private ArcHeightFactor: the arc never tops a fifth of the distance
		private float ArcHeight
		{
			get
			{
				float h = def.projectile.arcHeightFactor;
				float dist = (destination - origin).MagnitudeHorizontal();
				return Mathf.Min(h, dist * 0.2f);
			}
		}

		private Vector3 OnArc(float f) =>
			origin.Yto0() + (destination - origin).Yto0() * f + Vector3.forward * (ArcHeight * GenMath.InverseParabola(f))
			+ Vector3.up * def.Altitude;

		protected override void Tick()
		{
			base.Tick();
			if (!Spawned) return;
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserMote, OnArc(DistanceCoveredFractionArc), Rand.Range(0.5f, 0.75f), LaserFx.Blue,
				angle: Rand.Range(0f, 360f), speed: 0.15f);
		}

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			float f = DistanceCoveredFractionArc;
			Vector3 at = OnArc(f);
			Vector3 ahead = OnArc(Mathf.Min(1f, f + 0.02f)) - OnArc(Mathf.Max(0f, f - 0.02f));
			ahead.y = 0f;
			Quaternion rot = ahead.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(ahead) : ExactRotation;
			Vector2 size = def.graphicData.drawSize;
			Graphics.DrawMesh(MeshPool.GridPlane(new Vector2(size.x * 3f, size.y * 1.3f)), at, rot, Halo, 0);
			Graphics.DrawMesh(MeshPool.GridPlane(size), at + Vector3.up * 0.01f, rot, DrawMat, 0);
			Comps_PostDraw();
		}

		protected override void Explode()
		{
			Map map = Map;
			Vector3 at = ExactPosition;
			base.Explode();
			LaserFx.Impact(map, at);
		}
	}
}
