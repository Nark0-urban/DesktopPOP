using System;
using System.IO;
using Microsoft.Win32;

namespace DesktopPop;

/// <summary>"윈도우 켤 때 자동 실행" (HKCU Run 레지스트리 값).</summary>
internal static class AutoStart
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string Name = "DesktopPop";

	public static bool IsEnabled
	{
		get
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			return registryKey?.GetValue("DesktopPop") != null;
		}
	}

	/// <summary>
	/// 자동 실행이 켜져 있는데 등록된 exe 가 없어졌으면(폴더를 옮겼거나 지움) 지금 실행 중인 exe 로 고쳐 둠.
	/// 등록된 exe 가 아직 있으면 그대로 둠 (다른 폴더에서 잠깐 시험 삼아 켠 것일 수 있어서).
	/// </summary>
	public static void RepairPath()
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
			if (registryKey?.GetValue(Name) is not string current || Environment.ProcessPath == null)
			{
				return;
			}
			string registered = current.Trim().Trim('"');
			if (string.Equals(registered, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) || File.Exists(registered))
			{
				return;
			}
			registryKey.SetValue(Name, "\"" + Environment.ProcessPath + "\"");
			App.Diag("  자동 실행 경로를 고침: '" + registered + "' → '" + Environment.ProcessPath + "'");
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
	}

	public static void Set(bool on)
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
		if (registryKey != null)
		{
			if (on)
			{
				registryKey.SetValue("DesktopPop", "\"" + Environment.ProcessPath + "\"");
			}
			else
			{
				registryKey.DeleteValue("DesktopPop", throwOnMissingValue: false);
			}
		}
	}
}
