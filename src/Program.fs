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
    let authProvider = new DeviceCodeProvider(application, ["files.read.all"], (fun dcr -> someCallback dcr));
    // Create a new instance of GraphServiceClient with the authentication provider.
    let client = new GraphServiceClient(authProvider);

    // Graph 1.0 REST API: https://docs.microsoft.com/en-us/graph/api/resources/onedrive?view=graph-rest-1.0
    // Graph SDK .NET: https://github.com/microsoftgraph/msgraph-sdk-dotnet

    let drive = task {
        let! drive = client.Me.Drive.Request().GetAsync()

        log.Debug "Drive details Name = %s, Type = %s" drive.Name drive.DriveType
        log.Info "Size = %O" drive.Quota.Total

        let listChildren (item : DriveItem) =
            client.Drives.Item(drive.Id).Items.Item(item.Id).Children.Request().GetAsync()

        let! root = client.Drives.Item(drive.Id).Root.Request().GetAsync()

        let! rootItems = listChildren root

        for item in rootItems do
            log.Debug "%s - %d" item.Name item.Folder.ChildCount.Value



        //let! drives = client.Drives.Request().GetAsync()

        ()
    } 
    
    drive.Result

    0