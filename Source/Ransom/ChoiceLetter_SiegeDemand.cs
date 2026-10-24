using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimSiege
{
	public class ChoiceLetter_SiegeDemand : ChoiceLetter
	{
		public int demandId;

		// dismiss it and you lose the pay option forever, so no
		public override bool CanDismissWithRightClick => false;

		public override IEnumerable<DiaOption> Choices
		{
			get
			{
				GameComponent_SiegeDemands comp = GameComponent_SiegeDemands.Get();
				SiegeDemand d = comp?.GetDemand(demandId);

				if (d != null)
				{
					var pay = new DiaOption("RimSiege_PayOption".Translate(d.tribute))
					{
						action = () => comp.Pay(demandId),
						resolveTree = true,
					};
					int have = GameComponent_SiegeDemands.SilverAvailable(d.map);
					if (d.map == null || have < d.tribute)
						pay.Disable("RimSiege_NotEnoughSilver".Translate(d.tribute, have));
					else if (d.late && !comp.CanBuyOff(d))
						pay.Disable("RimSiege_LateTooLate".Translate());
					yield return pay;

					yield return new DiaOption("RimSiege_DefyOption".Translate())
					{
						action = () => comp.Defy(demandId),
						resolveTree = true,
					};
				}

				// postpone greys out for good once the timeout passed, and the dialog has no X:
				// a settled demand reopened from history would lock the game. give it a close
				yield return d == null ? Option_Close : Option_Postpone;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref demandId, "demandId");
		}
	}
}
