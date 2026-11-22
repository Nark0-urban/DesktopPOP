namespace DesktopPop;

/// <summary>가방 창 하나와 그 중심 좌표 (모두 접을 때 가방을 프로그램 창들 뒤로 내릴 때 씀).</summary>
internal readonly record struct BagWindowPlacement(nint Handle, Native.POINT Center);
