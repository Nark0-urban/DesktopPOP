using System.Runtime.InteropServices;

namespace DesktopPop;

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
/// <summary>Windows 셸 아이콘·썸네일 그림을 받는 COM 인터페이스.</summary>
internal interface IShellItemImageFactory
{
	[PreserveSig]
	int GetImage(Native.SIZE size, int flags, out nint phbm);
}
