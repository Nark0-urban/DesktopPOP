namespace DesktopPop;

/// <summary>화면에 겹쳐 있는 창 하나 (위에서부터 순서 계산용).</summary>
internal readonly record struct WorkStackEntry(ForegroundSnapshot Window, Native.RECT Bounds, bool Topmost);
