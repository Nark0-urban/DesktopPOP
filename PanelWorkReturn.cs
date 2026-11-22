namespace DesktopPop;

/// <summary>모두 펼쳤다가 접을 때 하던 프로그램 창으로 돌아갈지 기억.</summary>
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
		// 단축키·버튼으로 직접 접었고, 그 사이 다른 창으로 옮겨 가지 않았으면(또는 앞 창이 없거나 DesktopPop·작업 표시줄이면) 하던 창으로 돌아감
		bool nothingElseChosen = kind == ForegroundKind.None || (uint)(kind - 3) <= 1u;
		bool stillOnSameWork = nothingElseChosen || foreground == openedOver;
		ForegroundSnapshot restore = ((explicitClose & stillOnSameWork) ? openedOver : default(ForegroundSnapshot));
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
