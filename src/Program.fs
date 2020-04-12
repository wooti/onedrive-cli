open System
open Microsoft.Identity.Client
open Microsoft.Graph
open Microsoft.Graph.Auth
open FSharp.Control.Tasks.V2.ContextInsensitive

[<EntryPoint>]
let main argv =

    let args = CommandLine.doParse argv
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
    // Create a new instance of GraphServiceClient with the authentication provider.
    let client = new GraphServiceClient(authProvider)

    // Graph 1.0 REST API: https://docs.microsoft.com/en-us/graph/api/resources/onedrive?view=graph-rest-1.0
    // Graph SDK .NET: https://github.com/microsoftgraph/msgraph-sdk-dotnet

    let api = OneDriveAPI.build client

    async {
        let! drive = api.GetDrives ()
        log.Info "Drive details Name = %s, Id = %s" drive.Name drive.Id

        let! items = api.GetAllItems drive
        return items
    } 
    |> Async.RunSynchronously
    |> ignore
    
    0