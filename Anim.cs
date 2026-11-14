using System;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopPop;

internal static class Anim
{
	public static DoubleAnimation To(double to, double ms, double delayMs = 0.0, IEasingFunction? ease = null)
	{
		return new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
		{
			BeginTime = TimeSpan.FromMilliseconds(delayMs),
			EasingFunction = ease
		};
	}

	public static void After(double ms, Action a)
	{
		DispatcherTimer t = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(ms)
		};
		t.Tick += (object? s, EventArgs e) =>
		{
			t.Stop();
			a();
		};
		t.Start();
	}
}
