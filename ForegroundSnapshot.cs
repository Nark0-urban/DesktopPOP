namespace DesktopPop;

internal readonly record struct ForegroundSnapshot(nint Handle, uint ProcessId, ForegroundKind Kind);
