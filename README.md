# USFMScannerNet

UsfmScannerNet is a .NET service that scans repositories for USFM (Unified Standard Format Markers) files, used primarily in Bible translation projects. It processes incoming messages from Azure Service Bus, downloads and extracts repositories, converts BTT Writer projects to USFM if necessary, scans the content using a Python-based USFM verification tool, and uploads the linting results to Azure Blob Storage.

## Description

This application listens for repository update events via Azure Service Bus. When a message is received, it downloads the repository as a ZIP archive, extracts it, and scans all USFM files for errors and inconsistencies. The service supports BTT Writer projects by automatically converting them to USFM format before scanning. Results are stored in Azure Blob Storage and a completion message is sent back via Service Bus.

## Instructions for Running

### Prerequisites
- .NET 10.0 SDK
- Azure Service Bus namespace
- Azure Storage account
- Python environment (automatically managed via CSnakes)

### Building the Application
```bash
dotnet build
```

### Running Locally
Set the required configuration values (see Configuration section below) and run:
```bash
dotnet run --project UsfmScannerNet/UsfmScannerNet.csproj
```

### Using Docker
Build the Docker image:
```bash
docker build -t usfmscannernet UsfmScannerNet/
```

Run the container with required environment variables:
```bash
docker run --env BlobServiceConnectionString="your-connection-string" \
           --env ServiceBusConnectionString="your-servicebus-connection-string" \
           --env OutputPrefix="your-output-prefix" \
           usfmscannernet
```

## Configuration Details

The application requires the following configuration values:

| Configuration Key | Description | Example Value |
|-------------------|-------------|---------------|
| `BlobServiceConnectionString` | Connection string for Azure Blob Storage where scan results are uploaded | `DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=mykey;EndpointSuffix=core.windows.net` |
| `ServiceBusConnectionString` | Connection string for Azure Service Bus used for message processing | `Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=mykey;SharedAccessKey=mysecret` |
| `OutputPrefix` | Base URL prefix for generating result file URLs (e.g., blob storage public URL) | `https://myaccount.blob.core.windows.net/scan-results/` |

### Configuration Options in .NET

Configuration values can be set in the following ways (in order of precedence):

1. **Environment Variables** (recommended for production):
   ```bash
   export BlobServiceConnectionString="your-connection-string"
   export ServiceBusConnectionString="your-servicebus-connection-string"
   export OutputPrefix="your-output-prefix"
   ```

2. **appsettings.json file**:
   Create an `appsettings.json` file in the application directory:
   ```json
   {
     "BlobServiceConnectionString": "your-connection-string",
     "ServiceBusConnectionString": "your-servicebus-connection-string",
     "OutputPrefix": "your-output-prefix"
   }
   ```

3. **Command-line arguments**:
   ```bash
   dotnet run -- BlobServiceConnectionString="your-connection-string"
   ```

4. **Azure Key Vault** or other configuration providers (can be added via dependency injection).

## Application Overview

### Key Components
- **ScannerService**: Main hosted service that processes Service Bus messages and orchestrates the scanning workflow
- **USFM Verification**: Python-based tool (`usfmtools`) that checks USFM files for formatting errors and inconsistencies
- **BTT Writer Support**: Automatically converts BTT Writer project files to USFM format for scanning
- **Azure Integration**: Uses Azure Service Bus for event-driven processing and Azure Blob Storage for result persistence

### Processing Flow
1. Receives repository update message via Service Bus
2. Downloads repository ZIP from the provided URL
3. Extracts and processes the repository content
4. Converts BTT Writer projects to USFM if detected
5. Scans all USFM files using the Python verification tool
6. Uploads structured linting results to Blob Storage
7. Sends completion message with result URL via Service Bus

### Supported File Types
- Standard USFM files (.usfm)
- BTT Writer project directories (automatically converted to USFM)
