namespace DesktopPop;

internal readonly record struct WorkStackEntry(ForegroundSnapshot Window, Native.RECT Bounds, bool Topmost);
