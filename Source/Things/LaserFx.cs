using RimWorld;
using UnityEngine;
using Verse;

namespace RimSiege.Things
{
	// the laser battery's flecks: textures are white, the colour comes in here. white core, blue edge.
	// sizes follow the previews in _concept/laser (64 px per cell)
	public static class LaserFx
	{
		public static readonly Color Blue = new Color(0.35f, 0.65f, 1f);
		public static readonly Color Ice = new Color(0.75f, 0.9f, 1f);

		public static void Spawn(Map map, FleckDef def, Vector3 at, float scale, Color color,
			float rotation = 0f, float angle = 0f, float speed = 0f, Vector3? stretch = null)
		{
			if (def == null || map == null || !at.ShouldSpawnMotesAt(map)) return;
			FleckCreationData d = FleckMaker.GetDataStatic(at, map, def, scale);
			d.instanceColor = color;
			d.rotation = rotation;
			d.velocityAngle = angle;
			d.velocitySpeed = speed;
			if (stretch.HasValue) d.exactScale = stretch;
			map.flecks.CreateFleck(d);
		}

		// a shot: white flash in a blue one, a long horizontal lens flare across the screen, a small jolt
		public static void Muzzle(Map map, Vector3 at)
		{
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlash, at, 1.6f, Color.white);
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlash, at, 4.2f, Blue);
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlare, at, 1f, Color.white, stretch: new Vector3(6.3f, 1f, 0.2f));
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlare, at, 1f, new Color(Blue.r, Blue.g, Blue.b, 0.45f), stretch: new Vector3(7.2f, 1f, 0.6f));
			if (map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(0.1f);
		}

		// the bolt lands (laser_impact.gif): white punch in a blue bloom, the lens flare again, a shockwave out
		// to ~3 cells, 40 sparks, a glow left on the scorch and cold smoke rising off it
		public static void Impact(Map map, Vector3 at)
		{
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlash, at, 2.6f, Color.white);
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlash, at, 5.4f, Blue);
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlare, at, 1f, Color.white, stretch: new Vector3(9.4f, 1f, 0.22f));
			Spawn(map, RimSiegeDefOf.RimSiege_LaserFlare, at, 1f, new Color(Blue.r, Blue.g, Blue.b, 0.55f), stretch: new Vector3(10.6f, 1f, 0.7f));
			Spawn(map, RimSiegeDefOf.RimSiege_LaserRing, at, 0.8f, Blue);
			Spawn(map, RimSiegeDefOf.RimSiege_LaserRing, at, 0.8f, new Color(1f, 1f, 1f, 0.85f));
			Spawn(map, RimSiegeDefOf.RimSiege_LaserGlow, at, 4f, new Color(Blue.r, Blue.g, Blue.b, 0.85f));
			Spawn(map, RimSiegeDefOf.RimSiege_LaserGlow, at, 1.4f, new Color(Ice.r, Ice.g, Ice.b, 0.7f));
			for (int i = 0; i < 40; i++)
			{
				float a = Rand.Range(0f, 360f);
				Spawn(map, RimSiegeDefOf.RimSiege_LaserSpark, at, Rand.Range(0.4f, 0.8f), Rand.Bool ? Color.white : Ice,
					rotation: a, angle: a, speed: Rand.Range(2.5f, 7.5f));
			}
			for (int i = 0; i < 10; i++)
				Spawn(map, RimSiegeDefOf.RimSiege_LaserSmoke, at + new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(-0.2f, 0.25f)),
					Rand.Range(1.2f, 2.3f), new Color(0.24f, 0.28f, 0.36f, Rand.Range(0.25f, 0.42f)),
					angle: Rand.Range(-20f, 20f), speed: Rand.Range(0.2f, 0.4f));
		}
	}
}
