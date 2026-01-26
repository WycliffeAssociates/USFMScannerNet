using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using BTTWriterLib;
using BTTWriterLib.Models;
using CSnakes.Runtime;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UsfmScannerNet.Models;
using USFMToolsSharp.Renderers.USFM;

namespace UsfmScannerNet.Services;

public class ScannerService: IHostedService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ServiceBusClient _serviceBusClient;
    private const string TopicName = "WACSEvent";
    private const string SubscriptionName = "LarrysScripts";
    private const string OutputContainerName = "scan-results";
    private const string OutputTopicName = "LintingResult";
    private ServiceBusProcessor _busProcessor;
    private readonly ILogger<ScannerService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IPythonEnvironment _pythonEnvironment;
    private string _outputPrefix;
    
    public ScannerService(IAzureClientFactory<BlobServiceClient> blobServiceClientFactory,
        IAzureClientFactory<ServiceBusClient> serviceBusClientFactory, ILogger<ScannerService> logger,
        IHttpClientFactory httpClientFactory, IPythonEnvironment pythonEnvironment, IConfiguration configuration)
    {
        _blobServiceClient = blobServiceClientFactory.CreateClient("BlobClient");
        _serviceBusClient = serviceBusClientFactory.CreateClient("ServiceBusClient");
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _pythonEnvironment = pythonEnvironment;
        _outputPrefix = configuration.GetValue<string>("OutputPrefix");
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _busProcessor = _serviceBusClient.CreateProcessor(TopicName, SubscriptionName);
        _busProcessor.ProcessMessageAsync += MessageHandler;
        _busProcessor.ProcessErrorAsync += ErrorHandler;
        _logger.LogDebug("Starting ScannerService...");
        await _busProcessor.StartProcessingAsync(cancellationToken);
    }

    private Task ErrorHandler(ProcessErrorEventArgs arg)
    {
        throw new NotImplementedException();
    }

    private async Task MessageHandler(ProcessMessageEventArgs arg)
    {
        var message = await JsonSerializer.DeserializeAsync<WACSMessage>(arg.Message.Body.ToStream());
        if (message == null)
        {
            _logger.LogError("Received null or invalid message.");
            return;
        }
        await ProcessRepoAsync(message, arg.CancellationToken);
    }

    private async Task ProcessRepoAsync(WACSMessage repo, CancellationToken cancellationToken)
    {
        var downloadUrl = GetDownloadUrl(repo);
        if (downloadUrl == null)
        {
            // TODO: Log error
        }

        var zip = await _httpClient.GetAsync(downloadUrl, cancellationToken);
        if (!zip.IsSuccessStatusCode)
        {
            if (zip.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Repo not found at {DownloadUrl}", downloadUrl);
                return;
            }
            _logger.LogError("Failed to download repo from {DownloadUrl}", downloadUrl);
            return;
        }
        
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        await ZipFile.ExtractToDirectoryAsync(await zip.Content.ReadAsStreamAsync(cancellationToken), tempDir, cancellationToken);
        try
        {
            if (File.Exists(Path.Join(tempDir, "manifest.json")))
            {
                _logger.LogDebug("Found BTT Writer project, converting to USFM for scanning");
                var bttWriterContainer = new FileSystemResourceContainer(tempDir);
                var document = BTTWriterLoader.CreateUSFMDocumentFromContainer(bttWriterContainer, false);
                var manifest = BTTWriterLoader.GetManifest(bttWriterContainer);
                var renderer = new USFMRenderer();
                var renderedFile = renderer.Render(document);
                
                // Determine the file name
                await File.WriteAllTextAsync(DetermineFileNameForWriterProject(manifest), renderedFile, cancellationToken);
            }
            var results = ScanRepoAsync(tempDir);
            var url = await UploadToStorageAsync(repo.User!, repo.Repo!, results, cancellationToken);
            await SendCompletedMessageAsync(repo, url, cancellationToken);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string DetermineFileNameForWriterProject(BTTWriterManifest manifest)
    {
        return $"{manifest.project.id}.usfm";
    }
    
    private Dictionary<string, Dictionary<string,List<LintingResultItem>>> ScanRepoAsync(string path)
    {
        var output = new Dictionary<string, Dictionary<string, List<LintingResultItem>>>();
        // Scan using python scanner
        var module = _pythonEnvironment.Interface();
        var result = module.ScanDir(path);
        foreach (var (book, bookContents) in result)
        {
            output.TryAdd(book, new Dictionary<string, List<LintingResultItem>>());
            foreach (var (chapter, items) in bookContents)
            {
                output[book].TryAdd(chapter, new List<LintingResultItem>());
                foreach (var item in items)
                {
                    output[book][chapter].Add(new LintingResultItem
                    {
                        Verse = item["verse"],
                        Message = item["message"],
                        ErrorId = item["errorId"],
                    });
                }
            }
        }
        return output;
    }
    

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _busProcessor.StopProcessingAsync(cancellationToken);
    }
    private async Task<string> UploadToStorageAsync(string user, string repoName, Dictionary<string, Dictionary<string, List<LintingResultItem>>> results, CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var blobClient = containerClient.GetBlobClient($"{user}-{repoName}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        var json = JsonSerializer.Serialize(results);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await blobClient.UploadAsync(stream, cancellationToken);
        return Path.Join(_outputPrefix, blobClient.Name);
    }
    private async Task SendCompletedMessageAsync(WACSMessage repo, string resultsUrl, CancellationToken cancellationToken)
    {
        var completedMessage = new LintingResultsMessage
        {
            Repo = repo.Repo,
            User = repo.User,
            RepoId = repo.RepoId,
            ResultsFileUrl = resultsUrl
        };
        var messageJson = JsonSerializer.Serialize(completedMessage);
        var serviceBusMessage = new ServiceBusMessage(messageJson)
        {
            ContentType = "application/json"
        };
        await using var sender = _serviceBusClient.CreateSender(OutputTopicName);
        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }

    private static string? GetDownloadUrl(WACSMessage repo)
    {
        if (repo.RepoHtmlUrl == null || repo.User == null || repo.Repo == null || repo.DefaultBranch == null)
        {
            return null;
        }
        
        var tmp = new Uri(repo.RepoHtmlUrl);
        return $"{tmp.Scheme}://{tmp.Host}/api/v1/repos/{repo.User}/{repo.Repo}/archive/{repo.DefaultBranch}.zip";
    }
}

internal class LintingResultsMessage
{
    public string? Repo { get; set; }
    public string? User { get; set; }
    public int RepoId { get; set; }
    public string? ResultsFileUrl { get; set; }
}