using System.Text.Json.Serialization;

namespace UsfmScannerNet.Models;

public class LintingResultItem
{
    [JsonPropertyName("verse")]
    public string Verse { get; set; }
    [JsonPropertyName("message")]
    public string Message { get; set; }
    [JsonPropertyName("errorId")]
    public string ErrorId { get; set; }
}