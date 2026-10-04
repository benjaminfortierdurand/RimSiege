using System.Collections.Generic;
using Verse;

namespace RimSiege
{
	public class ChoiceLetter_CaptainRansom : ChoiceLetter
	{
		public int caseId;

		// same deal as the ultimatum: the choice stays on the stack until it resolves
		public override bool CanDismissWithRightClick => false;

		public override IEnumerable<DiaOption> Choices
		{
			get
			{
				GameComponent_SiegeDemands comp = GameComponent_SiegeDemands.Get();
				CaptainCase c = comp?.GetCase(caseId);

				if (c != null && c.captain != null && !c.captain.Dead)
				{
					yield return new DiaOption("RimSiege_RansomAccept".Translate(c.silver))
					{
						action = () => comp.AcceptRansom(caseId),
						resolveTree = true,
					};
					yield return new DiaOption("RimSiege_RansomRefuse".Translate())
					{
						action = () => comp.RefuseRansom(caseId),
						resolveTree = true,
					};
					yield return new DiaOption("RimSiege_RansomExecute".Translate())
					{
						action = () => comp.ExecuteCaptain(caseId),
						resolveTree = true,
					};
				}

				yield return Option_Postpone;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref caseId, "caseId");
		}
	}
}
