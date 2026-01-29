namespace UsfmScannerNet.Models;

public class WACSMessage
{
    public string? RepoHtmlUrl { get; set; }
    public string? DefaultBranch { get; set; }
    public string? User { get; set; }
    public string? Repo { get; set; }
    public int RepoId { get; set; }
}