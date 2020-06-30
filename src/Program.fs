open System
open Microsoft.Identity.Client
open Microsoft.Graph
open Microsoft.Graph.Auth
open System.IO
open Domain
open CommandLine
open Actors

[<EntryPoint>]
let main argv =

    //let args = CommandLine.doParse argv
    let args = {
        Direction = Up
        Local = Some "C:\\#sync"
        Remote = Some "Sync"
        DryRun = true
        Recursive = true
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
    // Create a new instance of GraphServiceClient with the authentication provider.
    let client = new GraphServiceClient(authProvider)

    // Graph 1.0 REST API: https://docs.microsoft.com/en-us/graph/api/resources/onedrive?view=graph-rest-1.0
    // Graph SDK .NET: https://github.com/microsoftgraph/msgraph-sdk-dotnet

    let api = OneDriveAPI.build client

    let hasher = Actors.Hasher ()
    let processor = Actors.Something ()

    async {
        let! drive = api.GetDrive ()
        log.Info "Drive details Name = %s, Id = %s" drive.Name drive.Id

        let rec processs (localFolder : LocalFolder option) (remoteFolder : RemoteFolder option) = 

            let getLocalItems folder = 
                let allFolders = 
                    folder.DirectoryInfo.EnumerateDirectories ()
                    |> Seq.map (fun s -> s.FullName, LocalItem.LocalFolder {DirectoryInfo = s})

                let allFiles = 
                    folder.DirectoryInfo.EnumerateFiles ()
                    |> Seq.map (fun s -> s.FullName, LocalItem.LocalFile {FileInfo = s})

                Seq.append allFolders allFiles

            let getRemoteItems folder = async {
                return!
                    api.GetAllChildren folder
                    |> Async.map (Seq.map (fun s -> s.Name, s))
            }
                
            let local = localFolder |> Option.map getLocalItems |> Option.defaultValue Seq.empty
            let remote = remoteFolder |> Option.map (getRemoteItems >> Async.RunSynchronously) |> Option.defaultValue Seq.empty
                
            let squash localItems remoteItems =
                let local = localItems |> Seq.map (fun (k,v) -> k, Item.Local v)
                let remote = remoteItems |> Seq.map (fun (k,v) -> k, Item.Remote v)

                Seq.append local remote
                |> Seq.groupBy fst
                |> Seq.map (fun (_,v) -> v |> Seq.map snd |> Seq.fold (fun state item -> match item with Local l -> (Some l, snd state) | Remote r -> (fst state, Some r)) (None, None))
                    
            let proc (local,remote) = 
                match local, remote with
                | None, Some (RemoteFile file) -> 
                    file |> RemoteItem.RemoteFile |> Diff.RemoteOnly |> processor.Notify
                | None, Some (RemoteFolder folder) -> 
                    processs None (Some folder)
                | Some (LocalFile local), None -> 
                    local |> LocalItem.LocalFile |> Diff.LocalOnly |> processor.Notify
                | Some (LocalFolder local), None 
                    -> processs (Some local) None
                | Some (LocalFile localFile), Some (RemoteFile file) -> 
                    localFile.FileInfo.Name |> Diff.Difference |> processor.Notify
                    ()
                | Some (LocalFolder localFolder), Some (RemoteFolder remoteFolder) -> 
                    processs (Some localFolder) (Some remoteFolder)
                | Some (LocalFolder _), Some (RemoteFile _)
                | Some (LocalFile _), Some (RemoteFolder _) ->
                    failwith "Item folder <-> file mismatch"
                | None, None -> failwith "Impossible case"

            squash local remote |> Seq.iter proc

        let localFolder = 
            args.Local 
            |> Option.defaultValue Environment.CurrentDirectory
            |> DirectoryInfo
            |> (fun x -> {DirectoryInfo = x})

        let! remoteFolder = 
            args.Remote
            |> Option.map api.GetFolder
            |> Option.defaultValue (Async.retn drive.Root)

        processs (Some localFolder) (Some remoteFolder)
        return ()
    } 
    |> Async.RunSynchronously
    |> ignore
    
    0