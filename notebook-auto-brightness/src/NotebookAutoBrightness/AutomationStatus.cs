using System;

namespace NotebookAutoBrightness;

internal sealed record AutomationStatus(
    int? Brightness,
    SchedulePhase? Phase,
    DateTime? NextChange,
    string ThemeLabel,
    string ScheduleSource,
    string StatusSummary,
    string StatusDetail,
    string LocationLabel,
    string SunWindowLabel);
