using System;

namespace IOConsole.Serializable;

[Serializable]
public class PartColumnConfig
{
    public int PartNumberColumn { get; set; } = 2;
    public int DeviceTypeColumn { get; set; } = 3;
    public int DeviceNameColumn { get; set; } = 4;
    public int ValueColumn { get; set; } = 5;
    public int PositiveToleranceColumn { get; set; } = 6;
    public int NegativeToleranceColumn { get; set; } = 7;
    public int CaseColumn { get; set; } = 8;
    public int CaseIdentifierColumn { get; set; } = 9;
    public int SkippedRow { get; set; } = 1;
}