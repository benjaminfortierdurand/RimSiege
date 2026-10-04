using System.Linq;
using RimWorld;
using Verse.AI;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// fires once the main siege lord leaves its build phase. no main lord around = fire right away
	public class Trigger_MainSiegeAttacking : Trigger
	{
		public override bool ActivateOn(Lord lord, TriggerSignal signal)
		{
			if (signal.type != TriggerSignalType.Tick) return false;

			Lord main = lord.Map?.lordManager?.lords?.FirstOrDefault(l =>
				l != lord && l.faction == lord.faction && l.LordJob is LordJob_MedievalSiege);
			if (main == null) return true;
			if (main.CurLordToil is LordToil_ExitMap) return false; // packing up is not attacking
			return !(main.CurLordToil is LordToil_BuildSiege);
		}
	}
}
