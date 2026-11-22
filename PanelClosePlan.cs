namespace DesktopPop;

/// <summary>모두 접을 때 다시 앞으로 가져올 창과 가방을 내려 둘 창.</summary>
internal readonly record struct PanelClosePlan(ForegroundSnapshot Restore, ForegroundSnapshot PlaceBehind);
