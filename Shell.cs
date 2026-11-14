using System;
using System.Windows;

namespace DesktopPop;

internal static class Shell
{
	public static bool TryRunHelper(string[] args)
	{
		return ShellOperations.TryRunHelper(args);
	}

	public static void Launch(DesktopItem item)
	{
		if (Application.Current is App app)
		{
			app.DismissAllPanels();
		}
		Run("open", item);
	}

	public static void OpenLocation(DesktopItem item)
	{
		if (!item.IsVirtual)
		{
			Run("locate", item);
		}
	}

	public static void ShowProperties(DesktopItem item)
	{
		Run("properties", item);
	}

	private static void Run(string action, DesktopItem item)
	{
		try
		{
			ShellOperations.Start(action, item.Path);
		}
		catch (Exception ex)
		{
			App.Log(ex);
			MessageBox.Show("작업을 시작하지 못했어요.\n" + item.Name + "\n\n" + ex.Message, "DesktopPop");
		}
	}
}
