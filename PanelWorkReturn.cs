namespace DesktopPop;

internal sealed class PanelWorkReturn
{
	private ForegroundSnapshot lastWork;

	private ForegroundSnapshot openedOver;

	public void ObserveClosedForeground(ForegroundSnapshot foreground)
	{
		if (foreground.Kind == ForegroundKind.Work)
		{
			lastWork = foreground;
		}
		else if (foreground.Kind == ForegroundKind.Desktop)
		{
			lastWork = default;
		}
	}

	public void Open(ForegroundSnapshot foreground)
	{
		ObserveClosedForeground(foreground);
		openedOver = ((foreground.Kind == ForegroundKind.Desktop) ? default(ForegroundSnapshot) : lastWork);
	}

	public PanelClosePlan Close(ForegroundSnapshot foreground, bool explicitClose)
	{
		ForegroundKind kind = foreground.Kind;
		bool flag = ((kind == ForegroundKind.None || (uint)(kind - 3) <= 1u) ? true : false);
		bool flag2 = flag || foreground == openedOver;
		ForegroundSnapshot restore = ((explicitClose & flag2) ? openedOver : default(ForegroundSnapshot));
		ForegroundSnapshot placeBehind;
		if (foreground.Kind == ForegroundKind.Work)
		{
			placeBehind = foreground;
		}
		else
		{
			placeBehind = ((foreground.Kind == ForegroundKind.Desktop) ? default(ForegroundSnapshot) : openedOver);
		}
		openedOver = default;
		ObserveClosedForeground(foreground);
		return new PanelClosePlan(restore, placeBehind);
	}

	public static bool MayRestore(PanelClosePlan plan, ForegroundSnapshot now)
	{
		if (plan.Restore.Kind == ForegroundKind.Work)
		{
			if (now.Kind == ForegroundKind.Work)
			{
				return now == plan.Restore;
			}
			return true;
		}
		return false;
	}
}
