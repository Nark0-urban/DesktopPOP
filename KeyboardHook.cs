using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopPop;

internal static class KeyboardHook
{
	private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	private struct GuiThreadInfo
	{
		public int cbSize;

		public uint flags;

		public nint hwndActive;

		public nint hwndFocus;

		public nint hwndCapture;

		public nint hwndMenuOwner;

		public nint hwndMoveSize;

		public nint hwndCaret;

		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	private sealed class Suspension : IDisposable
	{
		private bool disposed;

		public Suspension()
		{
			suspended++;
		}

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				suspended--;
			}
		}
	}

	private static nint hook;

	private static LowLevelKeyboardProc? proc;

	private static Dispatcher? dispatcher;

	private static int suspended;

	internal static bool IsSuspended => suspended > 0;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, nint hMod, uint threadId);

	[DllImport("user32.dll")]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandle(string? name);

	[DllImport("user32.dll")]
	private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hwnd, StringBuilder name, int count);

	public static IDisposable Suspend()
	{
		return new Suspension();
	}

	public static void Install()
	{
		if (hook == IntPtr.Zero)
		{
			dispatcher = Dispatcher.CurrentDispatcher;
			proc = HookProc;
			hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0u);
			if (hook == IntPtr.Zero)
			{
				App.Log($"키보드 감시 설치 실패 ({Marshal.GetLastWin32Error()})");
			}
		}
	}

	public static void Uninstall()
	{
		if (hook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(hook);
		}
		hook = IntPtr.Zero;
	}

	private static bool Down(int vk)
	{
		return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static bool OurTurn(ISelHost host)
	{
		if (suspended > 0 || App.OpenMenus > 0 || App.DragSource != null || !host.BandSelectable || !(host is Window { IsVisible: not false, IsEnabled: not false } window))
		{
			return false;
		}
		nint foregroundWindow = Native.GetForegroundWindow();
		nint handle = new WindowInteropHelper(window).Handle;
		if (handle == IntPtr.Zero || (foregroundWindow != handle && !Native.IsDesktopForeground()))
		{
			return false;
		}
		GuiThreadInfo info = new GuiThreadInfo
		{
			cbSize = Marshal.SizeOf<GuiThreadInfo>()
		};
		if (!GetGUIThreadInfo(0u, ref info))
		{
			return false;
		}
		if ((info.flags & 0x1C) != 0 || info.hwndMenuOwner != IntPtr.Zero)
		{
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder(128);
		GetClassName(info.hwndFocus, stringBuilder, stringBuilder.Capacity);
		string text = stringBuilder.ToString();
		if (text.Equals("Edit", StringComparison.OrdinalIgnoreCase) || text.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		bool flag = foregroundWindow == handle;
		if (flag)
		{
			IInputElement focusedElement = Keyboard.FocusedElement;
			bool flag2 = ((focusedElement is TextBoxBase || focusedElement is PasswordBox) ? true : false);
			flag = flag2 || (Keyboard.FocusedElement is ComboBox comboBox && comboBox.IsEditable);
		}
		if (flag)
		{
			return false;
		}
		return true;
	}

	private static nint HookProc(int nCode, nint wParam, nint lParam)
	{
		try
		{
			int num = ((IntPtr)wParam).ToInt32();
			int num2;
			int num3;
			if (nCode >= 0)
			{
				if (num != 256)
				{
					num2 = ((num == 260) ? 1 : 0);
					if (num2 == 0)
					{
						goto IL_002b;
					}
				}
				else
				{
					num2 = 1;
				}
				num3 = Marshal.ReadInt32(lParam);
				goto IL_0034;
			}
			num2 = 0;
			goto IL_002b;
			IL_002b:
			num3 = 0;
			goto IL_0034;
			IL_0034:
			int num4 = num3;
			if (num2 != 0)
			{
				App.Instance.ObserveGlobalKeyboardInput(num4);
			}
			bool flag = (byte)num2 != 0;
			if (flag)
			{
				bool flag2;
				switch (num4)
				{
				case 13:
				case 27:
				case 46:
				case 65:
				case 67:
				case 88:
				case 113:
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				ISelHost host = Sel.Host;
				if (host != null && Sel.Keys.Count > 0)
				{
					bool flag3 = Down(17);
					bool flag4 = Down(16);
					bool flag5 = Down(18);
					flag = !flag5;
					if (flag)
					{
						bool flag2;
						if (flag3)
						{
							bool flag6 = !flag4;
							if (flag6)
							{
								bool flag7 = ((num4 == 65 || num4 == 67 || num4 == 88) ? true : false);
								flag6 = flag7;
							}
							flag2 = flag6;
						}
						else
						{
							bool flag6 = ((num4 == 13 || num4 == 27 || num4 == 46) ? true : false);
							flag2 = flag6 || (num4 == 113 && !flag4);
						}
						flag = flag2;
					}
					if (!flag || !OurTurn(host))
					{
						return CallNextHookEx(hook, nCode, wParam, lParam);
					}
					Action act = null;
					if (!flag3 && !flag5)
					{
						switch (num4)
						{
						case 46:
							act = (flag4 ? ((Action)(() =>
							{
								App.Instance.DeleteSelected(host, permanent: true);
							})) : ((Action)(() =>
							{
								App.Instance.DeleteSelected(host, permanent: false);
							})));
							break;
						case 113:
							if (!flag4)
							{
								act = () =>
								{
									App.Instance.RenameSelected(host);
								};
							}
							break;
						case 13:
							if (!flag4)
							{
								act = () =>
								{
									App.Instance.OpenSelected(host);
								};
							}
							break;
						case 27:
							act = () =>
							{
								Sel.Clear();
							};
							break;
						}
					}
					else if (flag3 && !flag5 && !flag4)
					{
						switch (num4)
						{
						case 65:
							act = () =>
							{
								Sel.Set(host, host.OrderedKeys);
							};
							break;
						case 67:
							act = () =>
							{
								App.Instance.CopySelected(host, cut: false);
							};
							break;
						case 88:
							act = () =>
							{
								App.Instance.CopySelected(host, cut: true);
							};
							break;
						}
					}
					if (act != null)
					{
						string[] keys = Sel.Keys.ToArray();
						dispatcher?.InvokeAsync(() =>
						{
							if (Sel.Host != host || !keys.SequenceEqual(Sel.Keys, StringComparer.OrdinalIgnoreCase) || !OurTurn(host))
							{
								return;
							}
							using (Suspend())
							{
								act();
							}
						}, DispatcherPriority.Input);
						return new IntPtr(1);
					}
				}
			}
		}
		catch
		{
		}
		return CallNextHookEx(hook, nCode, wParam, lParam);
	}
}
