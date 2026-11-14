using System;

namespace DesktopPop;

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
		bool flag = foreground != lastForeground;
		lastForeground = foreground;
		if (flag)
		{
			return !isOurWindow;
		}
		return false;
	}
}
