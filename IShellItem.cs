using System;
using System.Runtime.InteropServices;

namespace DesktopPop;

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
	void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);

	void GetParent(out IShellItem ppsi);

	void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

	void GetAttributes(uint mask, out uint attribs);

	void Compare(IShellItem psi, uint hint, out int order);
}
