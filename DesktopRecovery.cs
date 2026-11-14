using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace DesktopPop;

internal static class DesktopRecovery
{
	private const string Flag = "--restore-desktop-on-exit";

	private static bool armed;

	public static bool Arm()
	{
		if (armed)
		{
			return true;
		}
		try
		{
			string text = "Local\\DesktopPop.Recovery." + Guid.NewGuid().ToString("N");
			using EventWaitHandle eventWaitHandle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, text);
			using Process process = Process.GetCurrentProcess();
			ProcessStartInfo processStartInfo = SelfStartInfo();
			processStartInfo.ArgumentList.Add("--restore-desktop-on-exit");
			processStartInfo.ArgumentList.Add(process.Id.ToString(CultureInfo.InvariantCulture));
			processStartInfo.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
			processStartInfo.ArgumentList.Add(text);
			using Process process2 = Process.Start(processStartInfo);
			armed = process2 != null && eventWaitHandle.WaitOne(TimeSpan.FromSeconds(5L));
			if (!armed)
			{
				App.Log("바탕화면 복구 감시를 시작하지 못해 원래 아이콘을 숨기지 않았습니다.");
			}
			return armed;
		}
		catch (Exception ex)
		{
			App.Log(ex);
			return false;
		}
	}

	internal static ProcessStartInfo SelfStartInfo()
	{
		string? text = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 찾을 수 없습니다.");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		if (string.Equals(Path.GetFileNameWithoutExtension(text), "dotnet", StringComparison.OrdinalIgnoreCase))
		{
			processStartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(App).Assembly.GetName().Name + ".dll"));
		}
		return processStartInfo;
	}

	public static void Restore()
	{
		if (!armed)
		{
			return;
		}
		try
		{
			DesktopShell.SetIconsVisible(visible: true);
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
	}

	public static bool TryRunHelper(string[] args)
	{
		if (args.Length == 0 || args[0] != "--restore-desktop-on-exit")
		{
			return false;
		}
		if (args.Length != 4 || !int.TryParse(args[1], out var result) || !long.TryParse(args[2], out var result2))
		{
			return true;
		}
		try
		{
			using Process process = Process.GetProcessById(result);
			if (process.StartTime.ToUniversalTime().Ticks != result2)
			{
				return true;
			}
			using (EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting(args[3]))
			{
				eventWaitHandle.Set();
			}
			process.WaitForExit();
			using Mutex mutex = new Mutex(initiallyOwned: false, "DesktopPop.SingleInstance.v1");
			bool flag;
			try
			{
				flag = mutex.WaitOne(0);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (!flag)
			{
				return true;
			}
			try
			{
				for (int i = 0; i < 5; i++)
				{
					DesktopShell.SetIconsVisible(visible: true);
					if (DesktopShell.IconsVisible() != true)
					{
						Thread.Sleep(400);
						continue;
					}
					break;
				}
			}
			finally
			{
				mutex.ReleaseMutex();
			}
		}
		catch (Exception ex2)
		{
			App.Log(ex2);
		}
		return true;
	}
}
