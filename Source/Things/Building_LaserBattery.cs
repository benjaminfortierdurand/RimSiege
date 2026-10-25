using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimSiege.Things
{
	// spacer battery, manned like the mortar. follows _concept/laser/laser_charge.mp4: it swings onto the
	// target (vanilla snaps, we ease it), charges for ChargeTicks (panels light up, a light pool on the ground,
	// motes spiral into the muzzle, arcs crackle near full), fires 3 bolts with the orb topped up between them,
	// then the vents blow cold steam on both sides
	[StaticConstructorOnStartup]
	public class Building_LaserBattery : Building_TurretGun
	{
		private const int ChargeTicks = 70;       // the 1.17 s charge sound
		private const float MuzzleReach = 0.47f;  // cannon tip from the pivot, share of turretTopDrawSize

		private static readonly Material Orb = Glow("Things/Mote/RimSiege/LaserFlash", Color.white);
		private static readonly Material Halo = Glow("Things/Mote/RimSiege/LaserGlow", LaserFx.Blue);
		private static readonly Material Pool = Glow("Things/Mote/RimSiege/LaserGlow", LaserFx.Blue);
		private static readonly Material Streak = Glow("Things/Mote/RimSiege/LaserGlow", LaserFx.Ice);
		private static readonly Material Panels = Glow("Things/Building/LaserBattery/LaserCannon_Panels", LaserFx.Ice);
		private static readonly Material PanelsSoft = Glow("Things/Building/LaserBattery/LaserCannon_PanelsSoft", LaserFx.Blue);
		private static readonly System.Random flicker = new System.Random(); // drawing only, keeps Rand out of it

		private static Material Glow(string tex, Color c) => MaterialPool.MatFrom(tex, ShaderDatabase.MoteGlow, c);

		// energy pulled in during the charge, drawn by hand so it can spiral
		private struct Mote { public int born; public float r0, a0, life; }
		private readonly List<Mote> motes = new List<Mote>();

		// none of this is saved: a load mid shot just skips a sound or a puff of steam
		private bool charging;
		private int lastBoltTick = -99999;
		private int ventedFor = -99999;
		private int ventStart = -99999;

		private bool Bursting => AttackVerb?.state == VerbState.Bursting;

		public Vector3 Muzzle
		{
			get
			{
				Vector2 o = def.building.turretTopOffset;
				Vector3 pivot = DrawPos + new Vector3(o.x, 0f, o.y);
				return pivot + Vector3.forward.RotatedBy(top.CurRotation) * (def.building.turretTopDrawSize * MuzzleReach);
			}
		}

		// 0..1: builds up over the charge, topped up between the bolts, dies down after the last one
		private float Level
		{
			get
			{
				if (burstWarmupTicksLeft > 0)
					return burstWarmupTicksLeft <= ChargeTicks ? Mathf.Pow(1f - burstWarmupTicksLeft / (float)ChargeTicks, 1.4f) : 0f;
				int since = Find.TickManager.TicksGame - lastBoltTick;
				if (Bursting) return 0.35f + 0.3f * Mathf.Clamp01(since / 12f);
				return since < 90 ? 0.35f * Mathf.Exp(-since / 22f) : 0f;
			}
		}

		protected override void Tick()
		{
			int before = burstWarmupTicksLeft;
			float turnedFrom = top.CurRotation;
			base.Tick();
			if (!Spawned) return;
			int now = Find.TickManager.TicksGame;

			// swing onto the target instead of snapping
			if (CurrentTarget.IsValid)
			{
				float to = top.CurRotation;
				float step = Mathf.Max(2f, Mathf.Abs(Mathf.DeltaAngle(turnedFrom, to)) * 0.12f);
				top.CurRotation = Mathf.MoveTowardsAngle(turnedFrom, to, step);
			}

			if (burstWarmupTicksLeft > 0 && before <= 0)
			{
				charging = false;
				RimSiegeDefOf.RimSiege_LaserBattery_Aim?.PlayOneShot(new TargetInfo(Position, Map));
			}

			if (burstWarmupTicksLeft > 0 && burstWarmupTicksLeft <= ChargeTicks)
			{
				if (!charging)
				{
					charging = true;
					RimSiegeDefOf.RimSiege_LaserBattery_Charge?.PlayOneShot(new TargetInfo(Position, Map));
				}
				float want = (1f + 5f * Level) * 0.42f;
				for (int n = (int)want + (Rand.Chance(want % 1f) ? 1 : 0); n > 0; n--)
					motes.Add(new Mote { born = now, r0 = Rand.Range(0.95f, 2f), a0 = Rand.Range(0f, 360f), life = Rand.Range(22f, 34f) });
			}
			else charging = false;
			motes.RemoveAll(m => now - m.born >= m.life);

			// burst done: hiss, then six puffs of cold steam, sides taking turns
			if (lastBoltTick > ventedFor && !Bursting && now - lastBoltTick >= 5)
			{
				ventedFor = lastBoltTick;
				ventStart = now;
				RimSiegeDefOf.RimSiege_LaserBattery_Vent?.PlayOneShot(new TargetInfo(Position, Map));
			}
			int v = now - ventStart;
			if (v >= 0 && v < 42 && v % 7 == 0)
			{
				float side = v / 7 % 2 == 1 ? 1f : -1f;
				LaserFx.Spawn(Map, RimSiegeDefOf.RimSiege_LaserSteam, DrawPos + new Vector3(side * 0.8f, 0f, 0.45f), 1.1f,
					new Color(0.55f, 0.62f, 0.72f, 0.38f), angle: side * 34f, speed: 1.13f);
			}
		}

		public void Notify_BoltFired()
		{
			lastBoltTick = Find.TickManager.TicksGame;
			LaserFx.Muzzle(Map, Muzzle);
		}

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			base.DrawAt(drawLoc, flip);
			int now = Find.TickManager.TicksGame;
			float c = Level;
			Vector3 m = Muzzle;
			m.y = AltitudeLayer.MoteOverhead.AltitudeFor();

			if (c > 0f)
			{
				float flick = c > 0.3f ? 1f + ((float)flicker.NextDouble() - 0.5f) * 0.3f : 1f;

				// light pool on the ground around the battery
				Vector3 floor = drawLoc;
				floor.y = AltitudeLayer.MoteLow.AltitudeFor();
				DrawGlow(Pool, floor, 11f, 0.32f * c);

				// the blue panels of the cannon light up, same transform as TurretTop.DrawTurret
				EquipmentUtility.Recoil(def.building.turretGunDef, AttackVerb as Verb_LaunchProjectile, out Vector3 kick, out float kickAngle, top.CurRotation);
				Vector2 o = def.building.turretTopOffset;
				Vector3 at = drawLoc + Altitudes.AltIncVect * 2f + new Vector3(o.x, 0f, o.y).RotatedBy(kickAngle) + kick;
				Quaternion q = ((float)TurretTop.ArtworkRotation + top.CurRotation).ToQuat();
				float size = def.building.turretTopDrawSize;
				Matrix4x4 mx = Matrix4x4.TRS(at, q, new Vector3(size, 1f, size));
				float shimmer = 1f + 0.12f * Mathf.Sin(now * 0.67f);
				Graphics.DrawMesh(MeshPool.plane10, mx, FadedMaterialPool.FadedVersionOf(PanelsSoft, Mathf.Min(1f, 1.6f * c)), 0);
				Graphics.DrawMesh(MeshPool.plane10, mx, FadedMaterialPool.FadedVersionOf(Panels, Mathf.Min(1f, (0.25f + 1.2f * c) * shimmer)), 0);

				// the orb: blue swell around a white core
				DrawGlow(Halo, m, 0.6f + 2.3f * c, Mathf.Min(1f, (0.3f + 1.1f * c) * flick));
				DrawGlow(Orb, m + Vector3.up * 0.01f, 0.2f + 0.94f * c, Mathf.Min(1f, (0.2f + 1.7f * c) * flick));

				// crackling arcs near full
				if (c > 0.6f)
					for (int arc = 0; arc < 2; arc++)
					{
						Vector3 p = m;
						float a = (float)flicker.NextDouble() * 360f;
						for (int seg = 0; seg < 5; seg++)
						{
							Vector3 next = p + Vector3.forward.RotatedBy(a) * (0.08f + 0.08f * (float)flicker.NextDouble());
							DrawStreak(p, next, 0.12f, Mathf.Min(1f, 1.1f * (c - 0.5f)));
							a += ((float)flicker.NextDouble() - 0.5f) * 115f;
							p = next;
						}
					}
			}

			// motes spiral in and brighten as they arrive
			foreach (Mote mo in motes)
			{
				float p = (now - mo.born) / mo.life;
				if (p < 0f || p >= 1f) continue;
				float r = mo.r0 * Mathf.Pow(1f - p, 1.3f);
				float a = mo.a0 + 80f * p;
				Vector3 head = m + Vector3.forward.RotatedBy(a) * r;
				Vector3 tail = m + Vector3.forward.RotatedBy(a - 7f) * (r * 1.08f);
				DrawStreak(tail, head, 0.15f, 0.9f * p);
				DrawGlow(Halo, head, 0.36f, 0.6f * p);
			}
		}

		private static void DrawGlow(Material mat, Vector3 at, float size, float alpha)
		{
			if (alpha <= 0.01f) return;
			Graphics.DrawMesh(MeshPool.GridPlane(new Vector2(size, size)), at, Quaternion.identity, FadedMaterialPool.FadedVersionOf(mat, alpha), 0);
		}

		private static void DrawStreak(Vector3 from, Vector3 to, float width, float alpha)
		{
			Vector3 d = to - from;
			d.y = 0f;
			float len = d.magnitude;
			if (len < 0.001f || alpha <= 0.01f) return;
			Matrix4x4 mx = Matrix4x4.TRS((from + to) / 2f, Quaternion.LookRotation(d), new Vector3(width, 1f, len + width));
			Graphics.DrawMesh(MeshPool.plane10, mx, FadedMaterialPool.FadedVersionOf(Streak, alpha), 0);
		}
	}
}
