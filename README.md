# OneDrive CLI

This is a .NET Core OneDrive client for OneDrive Personal / OneDrive Business drives. It is written in F# .NET and targets .NET 5.0.

## Pre-requisites

[.NET 5.0](https://dotnet.microsoft.com/download/dotnet/5.0)

## Build

1. Clone this repository
1. Run `build.cmd`

## Run

```
USAGE: OneDriveCLI.exe [--help] --direction <up|down> [--local <string>] [--remote <string>] [--ignore <string>] [--ignore-file <string>] [--threads <int>] [--dry-run]
```
Details of the options:
```
    --direction <up|down>  direction of sync <up|down>.
    --local <string>       local root path.
    --remote <string>      remote root path.
    --ignore <string>      ignore file glob
    --ignore-file <string> file containing globs to ignore
    --threads, -t <int>    number of threads to run on
    --dry-run, -n          don't make any changes
    --help                 display this list of options.
```

## References

- [Microsoft Graph 1.0 REST API](https://docs.microsoft.com/en-us/graph/api/resources/onedrive?view=graph-rest-1.0)
- [Microsoft Graph .NET Client Library](https://github.com/microsoftgraph/msgraph-sdk-dotnet)
  - [Authentication](https://docs.microsoft.com/en-us/graph/sdks/choose-authentication-providers?tabs=CS)
  - [Large File Uploads](https://docs.microsoft.com/en-us/graph/sdks/large-file-upload?tabs=csharp)