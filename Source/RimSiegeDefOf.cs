using RimWorld;
using Verse;
using Verse.AI;

namespace RimSiege
{
	[DefOf]
	public static class RimSiegeDefOf
	{
		public static ThingDef RimSiege_BatteringRam;
		public static ThingDef RimSiege_SiegeTower;
		public static ThingDef RimSiege_TrebuchetBoulder;
		public static ThingDef RimSiege_Petard;
		public static ThingDef RimSiege_CondemnedFlyer;
		public static ThingDef RimSiege_CarrionShell;

		public static JobDef RimSiege_PushSiegeEngine;
		public static JobDef RimSiege_SapJob;
		public static JobDef RimSiege_ManTrebuchetJob;
		public static JobDef RimSiege_ExecuteCaptainJob;
		public static JobDef RimSiege_ExecutionCeremonyJob;
		public static JobDef RimSiege_TrebuchetExecutionJob;
		public static JobDef RimSiege_WatchExecutionJob;

		public static DutyDef RimSiege_PushRam;

		public static DutyDef RimSiege_AssaultThroughBreach;

		public static DutyDef RimSiege_Sap;

		public static DutyDef RimSiege_ManTrebuchet;
		public static DutyDef RimSiege_RetrieveCaptain;
		public static DutyDef Escort; // vanilla, captain bodyguards
		public static JobDef CarryDownedPawnToExit; // vanilla, envoy carries the captain out

		public static LetterDef RimSiege_SiegeDemand;
		public static LetterDef RimSiege_RansomOffer;

		public static FactionDef RimSiege_Envoys;

		public static SoundDef RimSiege_RamImpact;
		public static SoundDef RimSiege_RamRolling;
		public static SoundDef RimSiege_CrewPush;
		public static SoundDef RimSiege_WarHorn;
		public static SoundDef RimSiege_WarDrums;

		static RimSiegeDefOf()
		{
			DefOfHelper.EnsureInitializedInCtor(typeof(RimSiegeDefOf));
		}
	}
}
