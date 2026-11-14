using System.Runtime.InteropServices;

namespace DesktopPop;

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
	[PreserveSig]
	int GetImage(Native.SIZE size, int flags, out nint phbm);
}
