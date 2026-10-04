using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace RimSiege.AI
{
	// toil state that must survive save/load (plain toil fields dont)
	public class LordToilData_PushEngine : LordToilData
	{
		public bool breachSplit;
		public HashSet<Pawn> charging = new HashSet<Pawn>();

		public override void ExposeData()
		{
			Scribe_Values.Look(ref breachSplit, "breachSplit");
			Scribe_Collections.Look(ref charging, "charging", LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (charging == null) charging = new HashSet<Pawn>();
				charging.RemoveWhere(p => p == null);
			}
		}
	}

	public class LordToilData_PushRam : LordToilData_PushEngine
	{
		public int chainedBreaches;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref chainedBreaches, "chainedBreaches");
		}
	}

	public class LordToilData_SiegeTower : LordToilData_PushEngine
	{
		public HashSet<Pawn> deployed = new HashSet<Pawn>();
		public HashSet<Pawn> boarding = new HashSet<Pawn>();

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref deployed, "deployed", LookMode.Reference);
			Scribe_Collections.Look(ref boarding, "boarding", LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (deployed == null) deployed = new HashSet<Pawn>();
				if (boarding == null) boarding = new HashSet<Pawn>();
				deployed.RemoveWhere(p => p == null);
				boarding.RemoveWhere(p => p == null);
			}
		}
	}
}
