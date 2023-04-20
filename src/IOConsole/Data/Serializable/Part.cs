using System;
using System.Text.Json.Serialization;

namespace IOConsole.Data.Serializable;

[Serializable]
public class Part
{
    [JsonPropertyName("partNumber")]
    public string PartNumber { get; set; } = string.Empty;
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
    [JsonPropertyName("positiveTolerance")]
    public string PositiveTolerance { get; set; } = string.Empty;
    [JsonPropertyName("negativeTolerance")]
    public string NegativeTolerance { get; set; } = string.Empty;
    [JsonPropertyName("caseName")]
    public string Case { get; set; } = string.Empty;
    [JsonPropertyName("caseIdentifier")]
    public string CaseIdentifier { get; set; } = string.Empty;

    public bool Equals(Part newPart)
    {
        if (PartNumber != newPart.PartNumber)
            throw new InvalidOperationException("Part Number not same.");

        return DeviceType != newPart.DeviceType
               || DeviceName != newPart.DeviceName
               || Value != newPart.Value
               || PositiveTolerance != newPart.PositiveTolerance
               || NegativeTolerance != newPart.NegativeTolerance
               || Case != newPart.Case
               || CaseIdentifier != newPart.CaseIdentifier;
    }

    public Part Copy()
    {
        return new Part
        {
            PartNumber = PartNumber,
            DeviceType = DeviceType,
            DeviceName = DeviceName,
            Value = Value,
            PositiveTolerance = PositiveTolerance,
            NegativeTolerance = NegativeTolerance,
            Case = Case,
            CaseIdentifier = CaseIdentifier
        };
    }
}