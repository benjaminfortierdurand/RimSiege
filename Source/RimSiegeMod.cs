using UnityEngine;
using Verse;

namespace RimSiege
{
	public class RimSiegeSettings : ModSettings
	{
		public bool ultimatumEnabled = true;
		public float tributeFactor = 0.6f; // x threat points, clamped 200-5000
		public int ultimatumHours = 6;

		public float enginePointsFloor = 700f; // engine chance ramps from here to floor+1300
		public float trebuchetPointsFloor = 1500f;
		public int maxTrebuchets = 3;
		public float sapperPointsFloor = 1200f;
		public int maxSappers = 2;
		public float ballistaPointsFloor = 2500f; // camp guard artillery, punishes sorties
		public int maxBallistas = 2;
		public int bombardHours = 4; // trebuchets pound the walls before the push, 0 = straight to it
		public float tarBoulderPointsFloor = 4000f; // rich sieges tar part of the pile
		public bool carrionEnabled = true; // caffa option: rotting carcasses over the walls
		public bool reinforcements = true; // second column mid-bombardment
		public float reinforcePointsFloor = 3000f;

		public float warbandFactor = 1f; // x points spent on pawns, engines stay free
		public int maxPushers = 4;
		public bool captainRout = true;
		public bool danegeld = true; // pay once, they come back for more
		public bool ransomEnabled = true; // captured captains get ransomed, dead ones get avenged

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref ultimatumEnabled, "ultimatumEnabled", true);
			Scribe_Values.Look(ref tributeFactor, "tributeFactor", 0.6f);
			Scribe_Values.Look(ref ultimatumHours, "ultimatumHours", 6);
			Scribe_Values.Look(ref enginePointsFloor, "enginePointsFloor", 700f);
			Scribe_Values.Look(ref trebuchetPointsFloor, "trebuchetPointsFloor", 1500f);
			Scribe_Values.Look(ref maxTrebuchets, "maxTrebuchets", 3);
			Scribe_Values.Look(ref sapperPointsFloor, "sapperPointsFloor", 1200f);
			Scribe_Values.Look(ref maxSappers, "maxSappers", 2);
			Scribe_Values.Look(ref ballistaPointsFloor, "ballistaPointsFloor", 2500f);
			Scribe_Values.Look(ref maxBallistas, "maxBallistas", 2);
			Scribe_Values.Look(ref bombardHours, "bombardHours", 4);
			Scribe_Values.Look(ref tarBoulderPointsFloor, "tarBoulderPointsFloor", 4000f);
			Scribe_Values.Look(ref carrionEnabled, "carrionEnabled", true);
			Scribe_Values.Look(ref reinforcements, "reinforcements", true);
			Scribe_Values.Look(ref reinforcePointsFloor, "reinforcePointsFloor", 3000f);
			Scribe_Values.Look(ref warbandFactor, "warbandFactor", 1f);
			Scribe_Values.Look(ref maxPushers, "maxPushers", 4);
			Scribe_Values.Look(ref captainRout, "captainRout", true);
			Scribe_Values.Look(ref danegeld, "danegeld", true);
			Scribe_Values.Look(ref ransomEnabled, "ransomEnabled", true);
		}
	}

	public class RimSiegeMod : Mod
	{
		public static RimSiegeSettings S;

		public RimSiegeMod(ModContentPack content) : base(content)
		{
			S = GetSettings<RimSiegeSettings>();
		}

		public override string SettingsCategory() => "RimSiege";

		public override void DoSettingsWindowContents(Rect inRect)
		{
			// two columns, one page was scraping 26 rows and clipping on small screens
			var l = new Listing_Standard();
			l.ColumnWidth = (inRect.width - Listing.ColumnSpacing) / 2f;
			l.Begin(inRect);

			Header(l, "RimSiege_SecParley");
			l.CheckboxLabeled("RimSiege_SetUltimatum".Translate(), ref S.ultimatumEnabled,
				"RimSiege_SetUltimatumTip".Translate());
			if (S.ultimatumEnabled)
			{
				l.Label("RimSiege_SetTribute".Translate(S.tributeFactor.ToString("0.0")));
				S.tributeFactor = l.Slider(S.tributeFactor, 0.2f, 2f);
				l.Label("RimSiege_SetHours".Translate(S.ultimatumHours));
				S.ultimatumHours = Mathf.RoundToInt(l.Slider(S.ultimatumHours, 1f, 24f));
			}
			l.CheckboxLabeled("RimSiege_SetDanegeld".Translate(), ref S.danegeld,
				"RimSiege_SetDanegeldTip".Translate());
			l.CheckboxLabeled("RimSiege_SetRansom".Translate(), ref S.ransomEnabled,
				"RimSiege_SetRansomTip".Translate());

			l.GapLine();
			Header(l, "RimSiege_SecHost");
			l.Label("RimSiege_SetWarband".Translate(S.warbandFactor.ToString("0.0")));
			S.warbandFactor = Mathf.Round(l.Slider(S.warbandFactor, 0.5f, 2f) * 10f) / 10f;
			l.Label("RimSiege_SetPushers".Translate(S.maxPushers));
			S.maxPushers = Mathf.RoundToInt(l.Slider(S.maxPushers, 3f, 8f));
			l.Label("RimSiege_SetSapperFloor".Translate((int)S.sapperPointsFloor));
			S.sapperPointsFloor = Mathf.Round(l.Slider(S.sapperPointsFloor, 0f, 6000f) / 50f) * 50f;
			l.Label("RimSiege_SetMaxSappers".Translate(S.maxSappers));
			S.maxSappers = Mathf.RoundToInt(l.Slider(S.maxSappers, 0f, 2f));
			l.CheckboxLabeled("RimSiege_SetCaptainRout".Translate(), ref S.captainRout,
				"RimSiege_SetCaptainRoutTip".Translate());

			l.NewColumn();
			Header(l, "RimSiege_SecTrain");
			l.Label("RimSiege_SetEngineFloor".Translate((int)S.enginePointsFloor));
			S.enginePointsFloor = Mathf.Round(l.Slider(S.enginePointsFloor, 0f, 4000f) / 50f) * 50f;
			l.Label("RimSiege_SetTrebFloor".Translate((int)S.trebuchetPointsFloor));
			S.trebuchetPointsFloor = Mathf.Round(l.Slider(S.trebuchetPointsFloor, 0f, 6000f) / 50f) * 50f;
			l.Label("RimSiege_SetMaxTrebs".Translate(S.maxTrebuchets));
			S.maxTrebuchets = Mathf.RoundToInt(l.Slider(S.maxTrebuchets, 0f, 3f));
			l.Label("RimSiege_SetBallistaFloor".Translate((int)S.ballistaPointsFloor));
			S.ballistaPointsFloor = Mathf.Round(l.Slider(S.ballistaPointsFloor, 0f, 8000f) / 50f) * 50f;
			l.Label("RimSiege_SetMaxBallistas".Translate(S.maxBallistas));
			S.maxBallistas = Mathf.RoundToInt(l.Slider(S.maxBallistas, 0f, 2f));
			l.Label("RimSiege_SetBombard".Translate(S.bombardHours));
			S.bombardHours = Mathf.RoundToInt(l.Slider(S.bombardHours, 0f, 12f));
			l.Label("RimSiege_SetTarFloor".Translate((int)S.tarBoulderPointsFloor));
			S.tarBoulderPointsFloor = Mathf.Round(l.Slider(S.tarBoulderPointsFloor, 0f, 10000f) / 50f) * 50f;
			l.CheckboxLabeled("RimSiege_SetCarrion".Translate(), ref S.carrionEnabled,
				"RimSiege_SetCarrionTip".Translate());
			l.CheckboxLabeled("RimSiege_SetReinforce".Translate(), ref S.reinforcements,
				"RimSiege_SetReinforceTip".Translate());
			if (S.reinforcements)
			{
				l.Label("RimSiege_SetReinforceFloor".Translate((int)S.reinforcePointsFloor));
				S.reinforcePointsFloor = Mathf.Round(l.Slider(S.reinforcePointsFloor, 0f, 8000f) / 50f) * 50f;
			}

			l.End();
		}

		private static void Header(Listing_Standard l, string key)
		{
			Text.Font = GameFont.Medium;
			l.Label(key.Translate());
			Text.Font = GameFont.Small;
			l.Gap(4f);
		}
	}
}
