using System;
using System.Text.Json.Serialization;

namespace IOConsole.Serializable;

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

    public static bool Updated(Part originalPart, Part newPart)
    {
        if (originalPart.PartNumber != newPart.PartNumber)
            throw new InvalidOperationException("Part Number not same.");

        return originalPart.DeviceType != newPart.DeviceType 
               || originalPart.DeviceName != newPart.DeviceName
               || originalPart.Value != newPart.Value
               || originalPart.PositiveTolerance != newPart.PositiveTolerance
               || originalPart.NegativeTolerance != newPart.NegativeTolerance
               || originalPart.Case != newPart.Case
               || originalPart.CaseIdentifier != newPart.CaseIdentifier;
    }

    public static Part Copy(Part originalPart)
    {
        return new Part
        {
            PartNumber = originalPart.PartNumber,
            DeviceType = originalPart.DeviceType,
            DeviceName = originalPart.DeviceName,
            Value = originalPart.Value,
            PositiveTolerance = originalPart.PositiveTolerance,
            NegativeTolerance = originalPart.NegativeTolerance,
            Case = originalPart.Case,
            CaseIdentifier = originalPart.CaseIdentifier
        };
    }
}