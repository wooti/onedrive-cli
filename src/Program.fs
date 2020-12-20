module OneDriveCLI.Program

open System
open Microsoft.Identity.Client
open Microsoft.Graph
open Microsoft.Graph.Auth
open System.IO
open OneDriveCLI.Modules
open OneDriveCLI.Core.OneDriveAPI
open OneDriveCLI.Core.Domain
open OneDriveCLI.Actors

[<EntryPoint>]
let main argv =

    //let args = CommandLine.doParse argv
    let args = {
        CommandLine.Direction = CommandLine.Up
        CommandLine.Local = Some @"C:\Repos\onedrive-cli\temp"
        CommandLine.Remote = Some "CLI"
        CommandLine.Threads = Some 10
        CommandLine.DryRun = false
        CommandLine.Verbose = true
    }

    let clientId = "52b727d4-693f-4a5c-b420-5e9d0afaf8c6"
    let tokenStorage = "user.token"

    // Build a client application.
    let application = 
        PublicClientApplicationBuilder
            .Create(clientId)
            .Build();

    TokenSaver.register application.UserTokenCache tokenStorage

    let someCallback (dcr : DeviceCodeResult) = 
        printfn "%s" dcr.Message
        Threading.Tasks.Task.CompletedTask

    // Create an authentication provider by passing in a client application and graph scopes.
    let authProvider = new DeviceCodeProvider(application, ["Files.ReadWrite.All"], (fun dcr -> someCallback dcr))
    
    // TODO: Test run with ChaosHandler

    // Create a new instance of GraphServiceClient with the authentication provider.
    let client = new GraphServiceClient(authProvider)

    // Graph 1.0 REST API: https://docs.microsoft.com/en-us/graph/api/resources/onedrive?view=graph-rest-1.0
    // Graph SDK .NET: https://github.com/microsoftgraph/msgraph-sdk-dotnet

    // https://docs.microsoft.com/en-us/graph/sdks/large-file-upload?tabs=csharp

    let remoteRoot = 
        args.Remote |> Option.map (fun remote ->
            let result = remote.Replace('\\', '/')
            if result.StartsWith('/') then result.Substring(1) else result
        )
        |> Option.defaultValue ""

    let api = new OneDriveAPIClient(client, remoteRoot)

    async {
        let! drive = api.GetDrive ()
        Output.writer.printfn "Drive details Name = %s, Id = %s" drive.Name drive.Id

        let localFolder = 
            args.Local 
            |> Option.defaultValue Environment.CurrentDirectory
            |> DirectoryInfo
            |> (fun x -> {Location = {Folder = ""; Name = ""}; DirectoryInfo = x})

        let! remoteFolder = api.GetRoot ()
        let direction = match args.Direction with CommandLine.Up -> Up | CommandLine.Down -> Down
        let localPath = args.Local |> Option.defaultValue (Directory.GetCurrentDirectory())

        // Initialise the main worker
        Main.initialise args.Threads.Value api direction args.DryRun localPath

        // Kick off processing
        (Some localFolder, Some remoteFolder) |> Job.Scan |> Main.queueJob

        // Wait for completion
        do! Main.runToCompletion ()
    } 
    |> Async.RunSynchronously
    |> ignore
    
    0