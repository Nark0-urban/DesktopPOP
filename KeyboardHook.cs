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

/// <summary>
/// 원래 바탕화면 같은 단축키(Delete, Shift+Delete, F2, Enter, Esc, Ctrl+A/C/X)를 판·가방에서 선택한 아이콘에 적용하는 키보드 감시.
/// 선택된 게 있고 바탕화면이나 DesktopPop 창이 맨 앞일 때만 키를 가져가며, 다른 프로그램에서 타자 칠 때는 건드리지 않음.
/// 모두 펼친 상태에서 다른 곳 키 입력을 보고 접는 일도 여기서 알려 줌.
/// </summary>
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

	/// <summary>
	/// 지금 이 키를 DesktopPop 이 가져가도 되는지: 선택한 판·가방이 보이고, 바탕화면이나 그 창이 맨 앞이고,
	/// 메뉴가 열려 있거나 글자를 입력하는 칸에 있는 게 아닐 때만.
	/// </summary>
	private static bool OurTurn(ISelHost host)
	{
		if (suspended > 0 || App.OpenMenus > 0 || App.DragSource != null || !host.BandSelectable || !(host is Window { IsVisible: not false, IsEnabled: not false } window))
		{
			return false;
		}
		nint foreground = Native.GetForegroundWindow();
		nint hostHandle = new WindowInteropHelper(window).Handle;
		if (hostHandle == IntPtr.Zero || (foreground != hostHandle && !Native.IsDesktopForeground()))
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
		// 메뉴·이동·크기 조절 중 (GUI_INMENUMODE | GUI_INMOVESIZE | GUI_POPUPMENUMODE)
		if ((info.flags & 0x1C) != 0 || info.hwndMenuOwner != IntPtr.Zero)
		{
			return false;
		}
		StringBuilder focusClass = new StringBuilder(128);
		GetClassName(info.hwndFocus, focusClass, focusClass.Capacity);
		string focusClassName = focusClass.ToString();
		if (focusClassName.Equals("Edit", StringComparison.OrdinalIgnoreCase) || focusClassName.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
		{
			return false; // 이름 바꾸기 같은 입력 칸
		}
		if (foreground == hostHandle)
		{
			IInputElement focused = Keyboard.FocusedElement;
			bool typingInWpf = focused is TextBoxBase || focused is PasswordBox || (focused is ComboBox comboBox && comboBox.IsEditable);
			if (typingInWpf)
			{
				return false;
			}
		}
		return true;
	}

	private const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;

	private const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_DELETE = 0x2E, VK_A = 0x41, VK_C = 0x43, VK_X = 0x58, VK_F2 = 0x71;

	private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

	private static nint HookProc(int nCode, nint wParam, nint lParam)
	{
		try
		{
			int message = ((IntPtr)wParam).ToInt32();
			bool keyDown = nCode >= 0 && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN);
			int vk = keyDown ? Marshal.ReadInt32(lParam) : 0; // KBDLLHOOKSTRUCT 맨 앞이 키 코드
			if (keyDown)
			{
				// 모두 펼친 상태에서 다른 곳에서 키를 누르면 접기 (펼치기 단축키 자체는 제외)
				App.Instance.ObserveGlobalKeyboardInput(vk);
			}
			bool handledKey = keyDown && vk is VK_RETURN or VK_ESCAPE or VK_DELETE or VK_A or VK_C or VK_X or VK_F2;
			ISelHost? host = Sel.Host;
			if (handledKey && host != null && Sel.Keys.Count > 0)
			{
				bool ctrl = Down(VK_CONTROL);
				bool shift = Down(VK_SHIFT);
				bool alt = Down(VK_MENU);
				// 원래 바탕화면 단축키만: Delete / Shift+Delete / F2 / Enter / Esc / Ctrl+A / Ctrl+C / Ctrl+X
				bool isShortcut = !alt && (ctrl
					? (!shift && vk is VK_A or VK_C or VK_X)
					: (vk is VK_RETURN or VK_ESCAPE or VK_DELETE || (vk == VK_F2 && !shift)));
				if (!isShortcut || !OurTurn(host))
				{
					return CallNextHookEx(hook, nCode, wParam, lParam);
				}
				Action? act = null;
				if (!ctrl)
				{
					switch (vk)
					{
					case VK_DELETE:
						act = shift
							? () => App.Instance.DeleteSelected(host, permanent: true)
							: () => App.Instance.DeleteSelected(host, permanent: false);
						break;
					case VK_F2:
						act = () => App.Instance.RenameSelected(host);
						break;
					case VK_RETURN:
						if (!shift)
						{
							act = () => App.Instance.OpenSelected(host);
						}
						break;
					case VK_ESCAPE:
						act = () => Sel.Clear();
						break;
					}
				}
				else
				{
					switch (vk)
					{
					case VK_A:
						act = () => Sel.Set(host, host.OrderedKeys);
						break;
					case VK_C:
						act = () => App.Instance.CopySelected(host, cut: false);
						break;
					case VK_X:
						act = () => App.Instance.CopySelected(host, cut: true);
						break;
					}
				}
				if (act != null)
				{
					// 실제 처리는 UI 스레드에서. 그 사이 선택이 바뀌었거나 차례가 아니면 하지 않음
					string[] keysAtPress = Sel.Keys.ToArray();
					dispatcher?.InvokeAsync(() =>
					{
						if (Sel.Host != host || !keysAtPress.SequenceEqual(Sel.Keys, StringComparer.OrdinalIgnoreCase) || !OurTurn(host))
						{
							return;
						}
						using (Suspend())
						{
							act();
						}
					}, DispatcherPriority.Input);
					return new IntPtr(1); // 이 키는 DesktopPop 이 처리 (숨겨진 원래 바탕화면이 엉뚱한 걸 지우지 않게)
				}
			}
		}
		catch
		{
		}
		return CallNextHookEx(hook, nCode, wParam, lParam);
	}
}
