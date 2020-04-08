open System
open Microsoft.Identity.Client
open Microsoft.Graph
open Microsoft.Graph.Auth
open FSharp.Control.Tasks.V2.ContextInsensitive

[<EntryPoint>]
let main argv =

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

    let drive = task {
        let! drive = client.Me.Drive.Request().GetAsync()

        let! drives = client.Drives.Request().GetAsync()

        ()
    } 
    
    drive.Result

    0