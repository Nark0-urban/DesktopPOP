using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPop;

/// <summary>
/// 어디서든 누르면 모든 패널과 가방을 펼치고/접는 단축키 (Windows RegisterHotKey).
/// 설정 문자열 예: "Ctrl+Alt+D", "Ctrl+Alt+Space". 비어 있으면 끔.
/// </summary>
internal static class GlobalHotkey
{
	private const int HotkeyId = 0x4450;

	private const int WM_HOTKEY = 0x0312;

	private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

	public const string Default = "Ctrl+Alt+D";

	/// <summary>트레이 메뉴에서 고를 수 있는 단축키들.</summary>
	public static readonly string[] Presets = { "Ctrl+Alt+D", "Ctrl+Alt+Space", "Ctrl+Alt+Q", "Ctrl+Shift+Space" };

	private static HwndSource? source;

	private static Action? pressed;

	private static uint currentMods;

	private static uint currentVk;

	public static bool IsRegistered { get; private set; }

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	/// <summary>"Ctrl+Alt+D", "Ctrl+Shift+F5", "Alt+PageUp" → 보조키·키 코드.</summary>
	public static bool TryParse(string? combo, out uint mods, out uint vk)
	{
		mods = 0;
		vk = 0;
		if (string.IsNullOrWhiteSpace(combo))
		{
			return false;
		}
		foreach (string raw in combo.Split('+'))
		{
			string part = raw.Trim();
			switch (part.ToUpperInvariant())
			{
			case "CTRL":
			case "CONTROL":
				mods |= MOD_CONTROL;
				continue;
			case "ALT":
				mods |= MOD_ALT;
				continue;
			case "SHIFT":
				mods |= MOD_SHIFT;
				continue;
			case "WIN":
				mods |= MOD_WIN;
				continue;
			case "SPACE":
				vk = 0x20;
				continue;
			}
			if (vk != 0 || !TryParseKey(part, out Key key))
			{
				return false; // 일반 키가 두 개거나 모르는 키
			}
			vk = (uint)KeyInterop.VirtualKeyFromKey(key);
		}
		return vk != 0 && IsAllowed(mods, vk);
	}

	/// <summary>
	/// 타자 칠 때 먹혀 버리지 않을 조합인지: Ctrl·Alt·Win 중 하나는 꼭 있어야 함.
	/// (Shift 만 있으면 F1~F24 같은 기능키일 때만 허용)
	/// </summary>
	private static bool IsAllowed(uint mods, uint vk)
	{
		if ((mods & (MOD_CONTROL | MOD_ALT | MOD_WIN)) != 0)
		{
			return true;
		}
		return mods == MOD_SHIFT && vk >= 0x70 && vk <= 0x87;
	}

	/// <summary>"D", "7", "F5", "Space", "PageUp" 같은 키 이름 → WPF Key.</summary>
	private static bool TryParseKey(string name, out Key key)
	{
		key = Key.None;
		if (name.Length == 1 && char.IsAsciiDigit(name[0]))
		{
			key = Key.D0 + (name[0] - '0');
			return true;
		}
		if (int.TryParse(name, out _) || !Enum.TryParse(name, ignoreCase: true, out key) || key == Key.None || IsModifier(key))
		{
			return false;
		}
		return KeyInterop.VirtualKeyFromKey(key) != 0;
	}

	private static bool IsModifier(Key k)
	{
		return k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
	}

	/// <summary>
	/// 키 입력 창에서 누른 키 → "Ctrl+Alt+K" 같은 문자열. 쓸 수 없는 조합이면 null.
	/// </summary>
	public static string? Format(ModifierKeys modifiers, bool win, Key key)
	{
		if (key == Key.None || IsModifier(key))
		{
			return null;
		}
		string name = key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString();
		string combo = ((modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "")
			+ ((modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "")
			+ ((modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "")
			+ (win ? "Win+" : "")
			+ name;
		return TryParse(combo, out _, out _) ? combo : null;
	}

	/// <summary>단축키 등록. 다른 프로그램이 이미 쓰고 있으면 false.</summary>
	public static bool Register(string? combo, Action onPressed)
	{
		Unregister();
		if (!TryParse(combo, out uint mods, out uint vk))
		{
			return false;
		}
		if (source == null)
		{
			var p = new HwndSourceParameters("DesktopPop.Hotkey")
			{
				ParentWindow = new IntPtr(-3), // HWND_MESSAGE: 화면에 안 보이는 메시지 전용 창
				Width = 0,
				Height = 0,
				WindowStyle = 0
			};
			source = new HwndSource(p);
			source.AddHook(WndProc);
		}
		if (!RegisterHotKey(source.Handle, HotkeyId, mods | MOD_NOREPEAT, vk))
		{
			App.Log($"단축키 '{combo}' 등록 실패 ({Marshal.GetLastWin32Error()}) - 다른 프로그램이 쓰는 중일 수 있음");
			return false;
		}
		pressed = onPressed;
		currentMods = mods;
		currentVk = vk;
		IsRegistered = true;
		return true;
	}

	public static void Unregister()
	{
		if (IsRegistered && source != null)
		{
			UnregisterHotKey(source.Handle, HotkeyId);
		}
		IsRegistered = false;
		pressed = null;
		currentMods = 0;
		currentVk = 0;
	}

	public static void Dispose()
	{
		Unregister();
		source?.Dispose();
		source = null;
	}

	/// <summary>
	/// 지금 눌린 키가 우리 단축키인지 (키보드 감시가 "다른 데서 키를 눌렀으니 접자"로 오해하지 않게).
	/// </summary>
	public static bool IsHotkeyKey(int vk)
	{
		if (!IsRegistered || vk != currentVk)
		{
			return false;
		}
		static bool Down(int k) => (Native.GetAsyncKeyState(k) & 0x8000) != 0;
		uint mods = 0;
		if (Down(0x11)) mods |= MOD_CONTROL;
		if (Down(0x12)) mods |= MOD_ALT;
		if (Down(0x10)) mods |= MOD_SHIFT;
		if (Down(0x5B) || Down(0x5C)) mods |= MOD_WIN;
		return mods == currentMods;
	}

	private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
		{
			handled = true;
			try
			{
				pressed?.Invoke();
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
		}
		return IntPtr.Zero;
	}
}
