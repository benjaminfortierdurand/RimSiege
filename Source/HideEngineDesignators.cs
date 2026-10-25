using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimSiege
{
	// the engines keep their Security category: blueprints and frames only get generated for buildables,
	// and the besiegers build from those. Archotech tech hides them from players, god mode shows every
	// designator, so they come out of the menu here. canGenerateDefaultDesignator=false hid them too but
	// with Ideology it makes them ideoligion buildings that nobody in the siege is allowed to build.
	// designators are resolved before static constructors run (both queued on ExecuteWhenFinished)
	[StaticConstructorOnStartup]
	public static class HideEngineDesignators
	{
		static HideEngineDesignators()
		{
			var engines = new HashSet<BuildableDef>
			{
				RimSiegeDefOf.RimSiege_BatteringRam, RimSiegeDefOf.RimSiege_SiegeTower, RimSiegeDefOf.RimSiege_LogRam,
				RimSiegeDefOf.RimSiege_SiegeLadder, RimSiegeDefOf.RimSiege_SteelRam,
			};
			foreach (DesignationCategoryDef cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
				cat.AllResolvedDesignators.RemoveAll(d => d is Designator_Build b && engines.Contains(b.PlacingDef));
		}
	}
}
