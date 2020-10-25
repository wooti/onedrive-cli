open System
open Microsoft.Identity.Client
open Microsoft.Graph
open Microsoft.Graph.Auth
open System.IO
open Domain
open CommandLine
open OneDriveAPI
open Main

[<EntryPoint>]
let main argv =

    //let args = CommandLine.doParse argv
    let args = {
        Direction = Down
        Local = Some @"C:\Repos\onedrive-cli\temp"
        Remote = None
        Threads = Some 10
        DryRun = false
        Verbose = true
    }

    let log = Logging.simpleLogger

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
    let authProvider = new DeviceCodeProvider(application, ["files.read.all"], (fun dcr -> someCallback dcr))
    
    // TODO: Run with ChaosHandler

    // Create a new instance of GraphServiceClient with the authentication provider.
    let client = new GraphServiceClient(authProvider)

    // Graph 1.0 REST API: https://docs.microsoft.com/en-us/graph/api/resources/onedrive?view=graph-rest-1.0
    // Graph SDK .NET: https://github.com/microsoftgraph/msgraph-sdk-dotnet

    // https://docs.microsoft.com/en-us/graph/sdks/large-file-upload?tabs=csharp

    let api = new OneDriveAPIClient(client)

    async {
        let! drive = api.GetDrive ()
        log.Info "Drive details Name = %s, Id = %s" drive.Name drive.Id

        let localFolder = 
            args.Local 
            |> Option.defaultValue Environment.CurrentDirectory
            |> DirectoryInfo
            |> (fun x -> {Location = "/"; DirectoryInfo = x})

        let! remoteFolder = 
            args.Remote
            |> Option.map api.GetFolder
            |> Option.defaultValue (Async.retn drive.Root)

        // Kick off processing
        (Some localFolder, Some remoteFolder) |> Job.Scan |> Main.queueJob

        // Start workers
        do [1 .. args.Threads.Value] |> Seq.iter (Worker.start api args.Direction args.DryRun args.Local.Value)

        // Wait for completion
        do! Main.awaitFinish ()
    } 
    |> Async.RunSynchronously
    |> ignore
    
    0