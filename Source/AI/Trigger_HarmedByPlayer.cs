using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// one of the lord's pawns downed or killed by the colony (its pawns, animals, turrets, traps), or
	// taken prisoner. no instigator (heatstroke, starving) or someone else's doing doesnt count
	public class Trigger_HarmedByPlayer : Trigger
	{
		public override bool ActivateOn(Lord lord, TriggerSignal signal)
		{
			if (signal.type != TriggerSignalType.PawnLost) return false;
			if (signal.condition == PawnLostCondition.MadePrisoner) return true;
			if (signal.condition != PawnLostCondition.Incapped && signal.condition != PawnLostCondition.Killed) return false;
			Thing by = signal.dinfo.Instigator;
			return by != null && by.Faction == Faction.OfPlayer;
		}
	}
}
