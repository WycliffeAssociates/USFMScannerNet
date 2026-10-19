using Microsoft.Extensions.Logging.Abstractions;
using UsfmScannerNet.Models;
using UsfmScannerNet.Services;

namespace UsfmScannerNet.Tests;

public class BttWriterConversionTests : IDisposable
{
    private const string ProjectFolderName = "jid_mat_text_reg";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly Dictionary<string, Dictionary<string, List<LintingResultItem>>> _results = new();

    public BttWriterConversionTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task LegacyManifestWithNumericBuild_RecordsMd03InsteadOfThrowing()
    {
        // ts-android wrote generator.build as a number; BTTWriterLib 0.10.1 expects a string and throws JsonException.
        var projectFolder = CreateProject(generatorBuild: "4");

        var converted = await ConvertAsync(projectFolder);

        Assert.False(converted);
        var finding = Assert.Single(_results["Unknown"][$"{ProjectFolderName}/manifest.json"]);
        Assert.Equal("MD03", finding.ErrorId);
        Assert.Equal("Unknown", finding.Verse);
        Assert.StartsWith("Could not load BTT Writer project:", finding.Message);
        Assert.DoesNotContain(_tempDir, finding.Message);
        Assert.False(File.Exists(Path.Join(_tempDir, "mat.usfm")));
    }

    [Fact]
    public async Task ManifestWithStringBuild_ConvertsToUsfm()
    {
        var projectFolder = CreateProject(generatorBuild: "\"4\"");

        var converted = await ConvertAsync(projectFolder);

        Assert.True(converted);
        Assert.Empty(_results);
        var usfm = await File.ReadAllTextAsync(Path.Join(_tempDir, "mat.usfm"));
        Assert.Contains("\\v 1", usfm);
    }

    private Task<bool> ConvertAsync(string projectFolder) =>
        ScannerService.ConvertBttWriterProjectAsync(projectFolder, _tempDir, _results, NullLogger.Instance,
            "bwuh", ProjectFolderName, CancellationToken.None);

    /// <summary>
    /// Writes a minimal tStudio project, modelled on bwuh/jid_mat_text_reg, into the temp dir the same way the
    /// service extracts an archive: a single top-level folder holding manifest.json and the chunk files.
    /// </summary>
    /// <param name="generatorBuild">The raw JSON value for generator.build</param>
    private string CreateProject(string generatorBuild)
    {
        var projectFolder = Path.Join(_tempDir, ProjectFolderName);
        Directory.CreateDirectory(Path.Join(projectFolder, "01"));
        File.WriteAllText(Path.Join(projectFolder, "manifest.json"), $$"""
            {
              "project": { "id": "mat", "name": "" },
              "type": { "id": "text", "name": "Text" },
              "generator": { "name": "ts-android", "build": {{generatorBuild}} },
              "package_version": 7,
              "target_language": {
                "name": "Bu",
                "direction": "ltr",
                "anglicized_name": "Bu",
                "region": "Africa",
                "is_gateway_language": false,
                "id": "jid"
              },
              "format": "usfm",
              "resource": { "id": "reg" },
              "translators": [ "bwuh" ],
              "source_translations": [
                {
                  "language_id": "en",
                  "resource_id": "ulb",
                  "checking_level": "3",
                  "date_modified": 20170329,
                  "version": "9"
                }
              ],
              "finished_chunks": [ "01-title", "01-01" ]
            }
            """);
        File.WriteAllText(Path.Join(projectFolder, "01", "title.txt"), "Sura 1");
        File.WriteAllText(Path.Join(projectFolder, "01", "01.txt"), "\\v 1 Verse one text. \\v 2 Verse two text.");
        return projectFolder;
    }
}
