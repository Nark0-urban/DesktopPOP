using System;

namespace DesktopPop;

/// <summary>"모두 펼치기" 상태를 언제 접을지 판단: 바깥 클릭·다른 프로그램에서 키 입력·다른 창으로 옮겨 감.</summary>
internal sealed class AllPanelsDismissal
{
	private nint lastForeground;

	private long settleUntil;

	public bool Active { get; private set; }

	public int Version { get; private set; }

	public void Open(nint foreground, long now)
	{
		Active = true;
		Version++;
		lastForeground = foreground;
		settleUntil = now + 250;
	}

	public void Close()
	{
		Active = false;
		Version++;
	}

	public bool ShouldDismissInput(bool insideOurUi, bool interactionActive)
	{
		if (Active && !insideOurUi)
		{
			return !interactionActive;
		}
		return false;
	}

	public bool ObserveForeground(nint foreground, bool isOurWindow, bool interactionActive, long now)
	{
		if (!Active || foreground == IntPtr.Zero)
		{
			return false;
		}
		if (now < settleUntil)
		{
			lastForeground = foreground;
			return false;
		}
		if (interactionActive)
		{
			if (isOurWindow)
			{
				lastForeground = foreground;
			}
			return false;
		}
		bool foregroundChanged = foreground != lastForeground;
		lastForeground = foreground;
		if (foregroundChanged)
		{
			return !isOurWindow;
		}
		return false;
	}
}
