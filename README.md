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

### Running Tests
```bash
dotnet test
```
`UsfmScannerNet.Tests` covers the `AuthInjector` credential handler: HTTPS gating, host matching, and
the configuration shapes used in deployment.

### Running Locally
Set the required configuration values (see Configuration section below) and run:
```bash
dotnet run --project UsfmScannerNet/UsfmScannerNet.csproj
```

### Using Docker
Build the Docker image. The Dockerfile lives in `UsfmScannerNet/` but copies paths relative to the
repository root, so the build context must be the root:
```bash
docker build -t usfmscannernet -f UsfmScannerNet/Dockerfile .
```

Run the container with required environment variables:
```bash
docker run --env BlobServiceConnectionString="your-connection-string" \
           --env ServiceBusConnectionString="your-servicebus-connection-string" \
           --env OutputPrefix="your-output-prefix" \
           --env 'Gitea__git.example.org__User=usfm-scanner' \
           --env 'Gitea__git.example.org__Password=gitea-access-token' \
           usfmscannernet
```

### Using Docker Compose
`docker-compose.yml` takes its values from the deploy host's environment. The credential host is
assembled inside the compose file, so every variable you export stays shell-safe:

```bash
export DEPLOY_ENV="master"
export BlobServiceConnectionString="your-connection-string"
export ServiceBusConnectionString="your-servicebus-connection-string"
export OutputPrefix="your-output-prefix"
export MaxRepoSizeInMB="200"
export GiteaHost="git.example.org"
export GiteaUser="usfm-scanner"
export GiteaPassword="gitea-access-token"
docker compose up -d
```

Set `GiteaHost`, `GiteaUser`, and `GiteaPassword` together, or leave all three unset to download
anonymously.

## Configuration Details

The application requires the following configuration values:

| Configuration Key | Description | Example Value |
|-------------------|-------------|---------------|
| `BlobServiceConnectionString` | Connection string for Azure Blob Storage where scan results are uploaded | `DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=mykey;EndpointSuffix=core.windows.net` |
| `ServiceBusConnectionString` | Connection string for Azure Service Bus used for message processing | `Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=mykey;SharedAccessKey=mysecret` |
| `OutputPrefix` | Base URL prefix for generating result file URLs (e.g., blob storage public URL) | `https://myaccount.blob.core.windows.net/scan-results/` |
| `MaxRepoSizeInMB` | Optional. Maximum repository size (in MB) to process. Repositories larger than this, as reported by the Gitea webhook payload, are skipped with a warning. Defaults to `200`. | `200` |
| `Gitea:<host>:User` | Optional. Username for HTTP Basic auth when downloading repositories from `<host>`. Only needed for repositories that are not publicly readable. See [Gitea Credentials](#gitea-credentials). | `usfm-scanner` |
| `Gitea:<host>:Password` | Optional. Password or access token paired with `Gitea:<host>:User`. | `gitea-access-token` |
| `AllowInsecureAuth` | Optional. When `true`, credentials may be sent over plain `http://` as well as `https://`. Local development only. Defaults to `false`. | `false` |

### Configuration Options in .NET

Configuration values can be set in the following ways (in order of precedence):

1. **Environment Variables** (recommended for production):
   ```bash
   export BlobServiceConnectionString="your-connection-string"
   export ServiceBusConnectionString="your-servicebus-connection-string"
   export OutputPrefix="your-output-prefix"
   export MaxRepoSizeInMB="200"
   ```
   Gitea credentials are keyed by host name, so their variable names contain dots and cannot be
   set with `export` — see [Gitea Credentials](#gitea-credentials).

2. **appsettings.json file**:
   Create an `appsettings.json` file in the application directory:
   ```json
   {
     "BlobServiceConnectionString": "your-connection-string",
     "ServiceBusConnectionString": "your-servicebus-connection-string",
     "OutputPrefix": "your-output-prefix",
     "MaxRepoSizeInMB": 200,
     "Gitea": {
       "git.example.org": {
         "User": "usfm-scanner",
         "Password": "gitea-access-token"
       }
     }
   }
   ```

3. **Command-line arguments**:
   ```bash
   dotnet run -- BlobServiceConnectionString="your-connection-string"
   ```

4. **Azure Key Vault** or other configuration providers (can be added via dependency injection).

### Gitea Credentials

Repository downloads are anonymous by default. To scan repositories that are not publicly readable,
supply HTTP Basic auth credentials keyed by repository host. The host key must match the host in the
webhook's `RepoHtmlUrl` (matched case-insensitively); add one entry per host:

```json
{
  "Gitea": {
    "git.example.org": {
      "User": "usfm-scanner",
      "Password": "gitea-access-token"
    }
  }
}
```

Credentials are attached to `https://` requests only. If a webhook reports an `http://` URL, they
are withheld and the download falls back to anonymous access; set `AllowInsecureAuth=true` to send them
over cleartext anyway, which is intended for local development against a test instance.

As environment variables, `:` becomes `__` and the host keeps its dots:

```
Gitea__git.example.org__User=usfm-scanner
Gitea__git.example.org__Password=gitea-access-token
```

Bash cannot `export` a name containing dots, so supply these through `appsettings.json`, Docker
`--env` flags, an `env_file`, or `env` for a local run:

```bash
env 'Gitea__git.example.org__User=usfm-scanner' \
    'Gitea__git.example.org__Password=gitea-access-token' \
    dotnet run --project UsfmScannerNet/UsfmScannerNet.csproj
```

Set the user and password together. A host entry with blank credentials sends empty Basic auth, and
Gitea answers `401` rather than falling back to anonymous access.

## Application Overview

### Key Components
- **ScannerService**: Main hosted service that processes Service Bus messages and orchestrates the scanning workflow
- **USFM Verification**: Python-based tool (`usfmtools`) that checks USFM files for formatting errors and inconsistencies
- **BTT Writer Support**: Automatically converts BTT Writer project files to USFM format for scanning
- **Manifest Validation**: Parses the repository's JSON and YAML manifests to confirm they are well-formed, reporting `MD01` for invalid JSON and `MD02` for invalid YAML
- **Azure Integration**: Uses Azure Service Bus for event-driven processing and Azure Blob Storage for result persistence

### Processing Flow
1. Receives repository update message via Service Bus
2. Downloads repository ZIP from the provided URL
3. Extracts and processes the repository content
4. Converts BTT Writer projects to USFM if detected
5. Validates the repository's JSON and YAML manifests, recording an error for any that fail to parse
6. Scans all USFM files using the Python verification tool
7. Uploads structured linting results to Blob Storage
8. Sends completion message with result URL via Service Bus

### Supported File Types
- Standard USFM files (.usfm)
- BTT Writer project directories (automatically converted to USFM)
- Manifests at the repository root, checked for well-formedness only:
  - `manifest.json` and `metadata.json` — a parse failure is reported as `MD01`
  - `manifest.yaml` and `manifest.yml` — a parse failure is reported as `MD02`

Manifest results are filed under the book `Unknown` and keyed by the manifest's path inside the
extracted archive, for example `<repo-name>/manifest.json`.

### Error Codes
`ErrorCodes.csv` in the repository root maps every error ID the scanner can emit to a human-readable
description, covering both the USFM codes from `usfmtools` and the `MD01`/`MD02` manifest codes.

This service does not read the file at runtime — it is the source of truth for other applications
that display or interpret scan results. Add a row here whenever a new error code is introduced.
