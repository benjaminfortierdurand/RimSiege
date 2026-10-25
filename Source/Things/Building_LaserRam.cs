using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimSiege.Things
{
	// spacer laser ram: the Drakken Laser Drill head (by 空曜, used with permission) on a sled, pushed by four
	// from the back. barrel in while it moves, out once parked. LordToil_LaserRam parks it well back from the
	// gate and calls BeamTick every docked tick: a long charge (the alarm sounds three times over its start,
	// lights flashing with it, the player's warning; the charge clip closes it and ignites on the last tick),
	// then a beam held on the gate. lights and beam take the faction's colour, white core. sprite 5.72 cells,
	// Position is the muzzle's cell
	[StaticConstructorOnStartup]
	public class Building_LaserRam : Building_BatteringRam, IThingGlower
	{
		private const float Reach = 2.81f, ClosedReach = 1.91f; // barrel tip from the sprite centre, open and closed
		private const float Back = 2.31f;                        // sprite centre behind Position: open tip on the front edge
		private const int AlarmTicks = 360;                      // the three blasts of the alarm clip, at the charge's start
		private const int ChargeTicks = 480;                     // 8 s: it is the strongest thing they bring
		private const int ChargeClipLead = 165;                  // the charge clip ignites 2.75 s in, so it starts that long before the end
		private const int RampTicks = 12, FadeTicks = 15;
		private const int LoopStart = 138, LoopTicks = 216;      // 4 s beam clip under the charge clip's tail, 0.4 s fades overlapping

		// the rear screen is a charge gauge: LaserRam_Screen's size and spot, from make_laserram_tex.py
		private const float ScreenW = 0.564f, ScreenH = 0.983f, ScreenBack = 1.441f;
		private const int GaugeSteps = 48, DrainTicks = 60;
		private static readonly Mesh[] gaugeMeshes = new Mesh[GaugeSteps + 1];

		private static readonly Material Open = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_Open", ShaderDatabase.Cutout);
		private static readonly Material Closed = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_Closed", ShaderDatabase.Cutout);
		private static readonly Material White = MaterialPool.MatFrom("Things/Mote/RimSiege/LaserFlash", ShaderDatabase.MoteGlow, Color.white);
		private static readonly Material BeamCore = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_Beam", ShaderDatabase.MoteGlow, Color.white);

		// faction tinted, made on the first draw (main thread)
		private Material openLights, closedLights, beamOuter, beamMid, glow, screen;
		private Color tint;

		// not saved: a load mid beam sounds the alarm and charges again
		private int chargeStart = -1, fireStart = -1, lastFireTick = -99999;
		private Vector3 beamEnd;
		private LaserLight hitLight;

		public Building_LaserRam()
		{
			crewNeeded = 4;
		}

		// the CompGlower at the muzzle shines only while the beam burns, in the faction's colour
		public bool ShouldBeLitNow() => fireStart >= 0;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			CompGlower g = GetComp<CompGlower>();
			if (g != null) g.GlowColor = GlowOf(TintOr(Color.white));
		}

		private static ColorInt GlowOf(Color c) =>
			new ColorInt((int)(c.r * 255f * 1.45f), (int)(c.g * 255f * 1.45f), (int)(c.b * 255f * 1.45f), 0);

		public override bool OnWheels => false;  // each of the four on their own spot, like the carried engines
		protected override float RotateDegreesPerTick => 1.5f;
		// a sled: it glides where it is pushed and keeps facing its parking spot's line to the gate, the four
		// stay behind it the whole way (drawn along the path, a 6 cell body swung away from its crew)
		protected override bool TurnsWithPath => false;

		// two beside the rear block, two behind it
		public override IEnumerable<IntVec3> HandleCells
		{
			get
			{
				IntVec3 f = Rotation.FacingCell, p = Perp;
				yield return Position - f * 5 + p * 2;
				yield return Position - f * 5 - p * 2;
				yield return Position - f * 6 + p;
				yield return Position - f * 6 - p;
			}
		}

		public override IEnumerable<IntVec3> SpareCells
		{
			get
			{
				IntVec3 f = Rotation.FacingCell, p = Perp;
				yield return Position - f * 6;
				yield return Position - f * 7 + p;
				yield return Position - f * 7 - p;
				yield return Position - f * 4 + p * 2;
				yield return Position - f * 4 - p * 2;
			}
		}

		private Vector3 Ahead => Vector3.forward.RotatedBy(drawAngle);
		protected override Vector3 ExtraDrawOffset => -Ahead * Back;
		private Vector3 Centre => DrawPos + drawOffset + ExtraDrawOffset;
		public Vector3 Muzzle => Centre + Ahead * (docked ? Reach : ClosedReach);

		private float Charge => chargeStart >= 0 && fireStart < 0
			? Mathf.Pow(Mathf.Clamp01((Find.TickManager.TicksGame - chargeStart) / (float)ChargeTicks), 1.4f) : 0f;

		private bool Alarm => chargeStart >= 0 && fireStart < 0 && Find.TickManager.TicksGame - chargeStart < AlarmTicks;

		// the clip's three blasts start 0.15, 2.15 and 4.15 s in and last about 1.25 s
		private bool AlarmBlast
		{
			get
			{
				int t = Find.TickManager.TicksGame - chargeStart;
				return t % 120 >= 9 && t % 120 < 84;
			}
		}

		private float BeamLevel
		{
			get
			{
				int now = Find.TickManager.TicksGame;
				if (fireStart >= 0) return Mathf.Clamp01((now - fireStart) / (float)RampTicks);
				return Mathf.Clamp01(1f - (now - lastFireTick) / (float)FadeTicks);
			}
		}

		// one docked tick: on = crew in place and the gate still stands, aim = what the beam reaches (the gate, or
		// whatever solid stands in front of it). true while the beam burns
		public bool BeamTick(bool on, Thing gate, Thing aim)
		{
			int now = Find.TickManager.TicksGame;
			if (!on || gate == null || aim == null)
			{
				bool wasFiring = fireStart >= 0;
				chargeStart = -1;
				fireStart = -1;
				if (wasFiring) GetComp<CompGlower>()?.UpdateLit(Map);
				return false;
			}
			Vector3 muzzle = Muzzle;
			Vector3 hit = aim.TrueCenter();
			Vector3 d = hit - muzzle;
			d.y = 0f;
			beamEnd = hit - d.normalized * 0.45f; // its face, not its middle

			// the charge: alarm over its start, the charge clip timed to ignite on its last tick
			if (chargeStart < 0)
			{
				chargeStart = now;
				RimSiegeDefOf.RimSiege_LaserRam_Alarm?.PlayOneShot(new TargetInfo(Position, Map));
			}
			if (now - chargeStart == ChargeTicks - ChargeClipLead)
				RimSiegeDefOf.RimSiege_LaserRam_Charge?.PlayOneShot(new TargetInfo(Position, Map));
			if (now - chargeStart < ChargeTicks)
			{
				ChargeFx(Charge);
				return false;
			}
			if (fireStart < 0)
			{
				fireStart = now; // the charge clip carries the ignition
				Ignite(muzzle);
			}
			KeepHitLit(now);
			lastFireTick = now;
			int held = now - fireStart - LoopStart;
			if (held >= 0 && held % LoopTicks == 0)
				RimSiegeDefOf.RimSiege_LaserRam_Beam?.PlayOneShot(new TargetInfo(Position, Map));
			BeamFx(now, muzzle);
			return true;
		}

		private void ChargeFx(float c)
		{
			Vector3 m = Muzzle;
			if (this.IsHashIntervalTick(2))
			{
				float a = Rand.Range(0f, 360f);
				float r = Mathf.Lerp(1.6f, 0.7f, c);
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserMote, m + Vector3.forward.RotatedBy(a) * r,
					Rand.Range(0.3f, 0.5f), TintOr(LaserFx.Ice), angle: a + 180f, speed: r * 3f);
			}
			if (c > 0.75f && this.IsHashIntervalTick(5))
			{
				float a = Rand.Range(0f, 360f);
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserSpark, m, Rand.Range(0.3f, 0.6f), Color.white,
					rotation: a, angle: a, speed: 2f);
			}
		}

		// ignition: a white flash and a shockwave at both ends, a hard jolt, a scorched streak on the ground
		private void Ignite(Vector3 muzzle)
		{
			Color c = TintOr(Color.white);
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserFlash, muzzle, 6f, Color.white);
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserFlash, beamEnd, 7f, Color.white);
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserFlash, beamEnd, 10f, c);
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserRing, muzzle, 1.2f, c);
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserRing, beamEnd, 1.2f, c);
			if (Map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(0.35f);
			foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(Position, beamEnd.ToIntVec3()))
				if (cell != Position && cell.InBounds(Map) && cell.Walkable(Map) && Rand.Chance(0.6f))
					FilthMaker.TryMakeFilth(cell, Map, ThingDefOf.Filth_Ash);
			GetComp<CompGlower>()?.UpdateLit(Map);
		}

		// a real light where the beam hits, moved along when the beam burns through to the next thing
		private void KeepHitLit(int now)
		{
			IntVec3 cell = beamEnd.ToIntVec3();
			if (!cell.InBounds(Map) || RimSiegeDefOf.RimSiege_LaserLight == null) return;
			if (hitLight == null || hitLight.Destroyed || hitLight.Position != cell)
			{
				if (hitLight != null && !hitLight.Destroyed) hitLight.Destroy();
				hitLight = (LaserLight)GenSpawn.Spawn(RimSiegeDefOf.RimSiege_LaserLight, cell, Map);
				CompGlower g = hitLight.GetComp<CompGlower>();
				if (g != null) g.GlowColor = GlowOf(TintOr(Color.white));
			}
			hitLight.expiresAt = now + 20;
		}

		// the gate under the beam: sparks spraying back, molten drips falling, embers rising, a shockwave
		// every couple of seconds, thick smoke, the odd jolt
		private void BeamFx(int now, Vector3 muzzle)
		{
			Color c = TintOr(LaserFx.Ice);
			float back = (muzzle - beamEnd).AngleFlat();
			float a = back + Rand.Range(-70f, 70f);
			LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserSpark, beamEnd, Rand.Range(0.4f, 0.9f),
				Rand.Bool ? Color.white : c, rotation: a, angle: a, speed: Rand.Range(3f, 9f));
			if (now % 3 == 0)
			{
				float drip = 180f + Rand.Range(-25f, 25f);
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserSpark, beamEnd, Rand.Range(0.3f, 0.5f),
					new Color(1f, 0.62f, 0.25f), rotation: drip, angle: drip, speed: Rand.Range(1f, 2.5f));
			}
			if (now % 4 == 0)
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserMote, beamEnd + new Vector3(Rand.Range(-0.5f, 0.5f), 0f, 0f),
					Rand.Range(0.2f, 0.35f), c, angle: Rand.Range(-30f, 30f), speed: Rand.Range(0.6f, 1.2f));
			if (now % 40 == 0)
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserRing, beamEnd, 0.8f, new Color(c.r, c.g, c.b, 0.7f));
			if (now % 7 == 0)
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserSmoke, beamEnd + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, 0f),
					Rand.Range(1.6f, 2.6f), new Color(0.22f, 0.22f, 0.24f, 0.45f), angle: Rand.Range(-20f, 20f), speed: Rand.Range(0.3f, 0.6f));
			if (now % 30 == 0 && Map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(0.05f);
		}

		// the faction's colour, white for factions without one or too dark to glow
		private Color TintOr(Color fallback)
		{
			if (Faction == null) return fallback;
			Color c = Faction.Color;
			if (Mathf.Max(c.r, c.g, c.b) < 0.35f || (c.r > 0.9f && c.g > 0.9f && c.b > 0.9f)) return Color.white;
			return Color.Lerp(c, Color.white, 0.15f);
		}

		private void EnsureMaterials()
		{
			Color c = TintOr(Color.white);
			if (glow != null && c == tint) return;
			tint = c;
			openLights = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_OpenLights", ShaderDatabase.MoteGlow, c);
			closedLights = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_ClosedLights", ShaderDatabase.MoteGlow, c);
			beamOuter = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_Beam", ShaderDatabase.MoteGlow, c);
			beamMid = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_Beam", ShaderDatabase.MoteGlow, Color.Lerp(c, Color.white, 0.5f));
			glow = MaterialPool.MatFrom("Things/Mote/RimSiege/LaserGlow", ShaderDatabase.MoteGlow, c);
			screen = MaterialPool.MatFrom("Things/Building/LaserRam/LaserRam_Screen", ShaderDatabase.MoteGlow, c);
		}

		// 0..1: fills from the back over the charge, full while the beam burns, drains after
		private float Gauge
		{
			get
			{
				int now = Find.TickManager.TicksGame;
				if (fireStart >= 0) return 1f;
				if (chargeStart >= 0) return Mathf.Clamp01((now - chargeStart) / (float)ChargeTicks);
				return Mathf.Clamp01(1f - (now - lastFireTick) / (float)DrainTicks);
			}
		}

		// the screen's quad cut at the fill line, uv cut to match so the mask keeps its shape. made on first use
		private static Mesh GaugeMesh(float fill)
		{
			int i = Mathf.Clamp(Mathf.RoundToInt(fill * GaugeSteps), 0, GaugeSteps);
			if (gaugeMeshes[i] != null) return gaugeMeshes[i];
			float t = i / (float)GaugeSteps, hw = ScreenW / 2f, z0 = -ScreenH / 2f, z1 = z0 + ScreenH * t;
			var m = new Mesh { name = "LaserRamGauge" + i };
			m.vertices = new[] { new Vector3(-hw, 0f, z0), new Vector3(-hw, 0f, z1), new Vector3(hw, 0f, z1), new Vector3(hw, 0f, z0) };
			m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, t), new Vector2(1f, t), new Vector2(1f, 0f) };
			m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
			m.RecalculateNormals();
			m.RecalculateBounds();
			return gaugeMeshes[i] = m;
		}

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			EnsureMaterials();
			Vector3 loc = drawLoc + drawOffset + ExtraDrawOffset;
			Quaternion q = Quaternion.AngleAxis(drawAngle, Vector3.up);
			Mesh mesh = MeshPool.GridPlane(def.graphicData.drawSize);
			float charge = Charge, beam = BeamLevel;
			Graphics.DrawMesh(mesh, loc, q, docked ? Open : Closed, 0);
			float lit = Alarm ? (AlarmBlast ? 1f : 0.2f) : 0.55f + 0.45f * Mathf.Max(charge, beam);
			Graphics.DrawMesh(mesh, loc + Vector3.up * 0.01f, q, FadedMaterialPool.FadedVersionOf(docked ? openLights : closedLights, lit), 0);

			// charge gauge on the rear screen: a faint glass while it charges, the bar brightening as it fills,
			// full and pulsing while the beam burns
			float gauge = Gauge;
			if (gauge > 0f)
			{
				Vector3 at = loc - Ahead * ScreenBack + Vector3.up * 0.02f;
				int t = Find.TickManager.TicksGame;
				if (chargeStart >= 0 && fireStart < 0)
					Graphics.DrawMesh(GaugeMesh(1f), Matrix4x4.TRS(at, q, Vector3.one), FadedMaterialPool.FadedVersionOf(screen, 0.12f), 0);
				float bright = fireStart >= 0 ? 0.85f + 0.15f * Mathf.Sin(t * 0.3f)
					: chargeStart >= 0 ? 0.35f + 0.65f * gauge : gauge;
				Graphics.DrawMesh(GaugeMesh(gauge), Matrix4x4.TRS(at + Vector3.up * 0.005f, q, Vector3.one), FadedMaterialPool.FadedVersionOf(screen, bright), 0);
			}

			Vector3 m = Muzzle;
			m.y = AltitudeLayer.MoteOverhead.AltitudeFor();
			if (charge > 0f)
			{
				Glow(glow, m, 0.6f + 2.3f * charge, Mathf.Min(1f, 0.3f + 1.1f * charge));
				Glow(White, m + Vector3.up * 0.01f, 0.2f + 0.94f * charge, Mathf.Min(1f, 0.2f + 1.7f * charge));
			}
			if (beam <= 0f) return;

			int now = Find.TickManager.TicksGame;
			float flick = 1f + 0.07f * Mathf.Sin(now * 1.7f) + 0.04f * Mathf.Sin(now * 4.3f);
			Vector3 to = beamEnd;
			to.y = m.y;
			Vector3 along = to - m;
			along.y = 0f;
			float len = along.magnitude;
			if (len < 0.1f) return;
			Vector3 dir = along / len, side = new Vector3(dir.z, 0f, -dir.x), mid = (m + to) / 2f;
			// heats up the longer it holds: the hot spot on the gate swells
			float heat = fireStart >= 0 ? Mathf.Clamp01((now - fireStart) / 300f) : 1f;

			Vector3 floor = mid;
			floor.y = AltitudeLayer.MoteLow.AltitudeFor();
			Strip(beamOuter, floor, along, 4f * beam, 0.18f);                     // light on the ground along it
			Strip(beamOuter, mid, along, 3f * beam * flick, 0.22f);                 // bloom
			Strip(beamOuter, mid + Vector3.up * 0.002f, along, 1.7f * beam * flick, 0.55f);
			Strip(beamMid, mid + Vector3.up * 0.005f, along, 0.85f * beam * flick, 0.85f);
			Strip(BeamCore, mid + Vector3.up * 0.01f, along, 0.32f * beam, 1f);

			// two strands twisting round it
			const int Segs = 24;
			for (int strand = 0; strand < 2; strand++)
			{
				float phase = now * 0.25f + strand * Mathf.PI;
				Vector3 prev = m;
				for (int i = 1; i <= Segs; i++)
				{
					float d = len * i / Segs;
					float swing = Mathf.Sin(d * 2.5f - phase) * 0.35f * Mathf.Min(1f, d) * beam;
					Vector3 p = m + dir * d + side * swing;
					Strip(beamMid, (prev + p) / 2f + Vector3.up * 0.007f, p - prev, 0.12f, 0.7f);
					prev = p;
				}
			}

			// pulses of energy racing down to the gate
			for (int k = 0; k < 4; k++)
			{
				float s = (now * 0.5f / len + k / 4f) % 1f;
				Strip(BeamCore, m + dir * (s * len) + Vector3.up * 0.012f, dir * 1.4f, 0.9f * beam, 0.7f);
			}

			// muzzle: glow and a lens flare across the screen
			Glow(glow, m + Vector3.up * 0.015f, 2.6f * beam, 0.9f);
			Glow(White, m + Vector3.up * 0.02f, 1.1f * beam, 1f);
			Graphics.DrawMesh(MeshPool.GridPlane(new Vector2(7f * beam, 0.25f)), m + Vector3.up * 0.022f, Quaternion.identity,
				FadedMaterialPool.FadedVersionOf(glow, 0.5f + 0.1f * Mathf.Sin(now * 0.9f)), 0);

			// the gate: a white hot spot growing as it heats
			Glow(glow, to + Vector3.up * 0.015f, (3.2f + 1.3f * heat) * beam * flick, 0.9f);
			Glow(White, to + Vector3.up * 0.02f, (1.5f + 1.1f * heat) * beam * flick, 1f);
		}

		private static void Glow(Material mat, Vector3 at, float size, float alpha)
		{
			if (alpha <= 0.01f || size <= 0.01f) return;
			Graphics.DrawMesh(MeshPool.GridPlane(new Vector2(size, size)), at, Quaternion.identity, FadedMaterialPool.FadedVersionOf(mat, alpha), 0);
		}

		// beam texture stretched from the muzzle to the gate, its rounded cap on the gate
		private static void Strip(Material mat, Vector3 mid, Vector3 along, float width, float alpha)
		{
			along.y = 0f;
			float len = along.magnitude;
			if (len < 0.01f || width <= 0.01f || alpha <= 0.01f) return;
			Matrix4x4 mx = Matrix4x4.TRS(mid, Quaternion.LookRotation(along), new Vector3(width, 1f, len));
			Graphics.DrawMesh(MeshPool.plane10, mx, FadedMaterialPool.FadedVersionOf(mat, alpha), 0);
		}
	}
}
