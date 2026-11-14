namespace DesktopPop;

internal readonly record struct PanelClosePlan(ForegroundSnapshot Restore, ForegroundSnapshot PlaceBehind);
