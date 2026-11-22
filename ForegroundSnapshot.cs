namespace DesktopPop;

/// <summary>맨 앞 창 하나의 기록 (핸들, 프로세스, 종류).</summary>
internal readonly record struct ForegroundSnapshot(nint Handle, uint ProcessId, ForegroundKind Kind);
