module Actors
open System.IO
open System.Security.Cryptography
open Domain
open OneDriveAPI

type WorkerID = int

type Job = 
    | Scan of LocalFolder option * RemoteFolder option
    | Compare of LocalItem * RemoteItem
    | DiffContent of LocalItem option * RemoteItem option

type ProcessMsg = 
    | Job of Job
    | Status of AsyncReplyChannel<Job array * Map<WorkerID, Job>>
    | RequestJob of WorkerID * AsyncReplyChannel<Job option> 

type HashMsg =
    | Get of FileInfo * AsyncReplyChannel<string>

type CollectorMsg =
    | Report
    | Get

/// Do something with a diff
type MainWorker (api : OneDriveAPIClient, threads, direction : Direction, dryRun : bool) =

    let processor = MailboxProcessor.Start(fun inbox ->
        let queue = new System.Collections.Generic.Queue<Job>()
        let rec loop activeWorkers = async {
            let! message = inbox.Receive()
            let workersNow = 
                match message with 
                | Job job -> 
                    queue.Enqueue job
                    activeWorkers
                | Status replyChannel ->
                    replyChannel.Reply(queue |> Seq.toArray, activeWorkers)
                    activeWorkers
                | RequestJob (workerID, replyChannel) -> 
                    match queue.TryDequeue () with
                    | true, item -> 
                        replyChannel.Reply (Some item)
                        activeWorkers |> Map.add workerID item
                    | false, _ -> 
                        replyChannel.Reply None
                        activeWorkers |> Map.remove workerID

            return! loop workersNow
        }
        loop Map.empty)

    let hasher = MailboxProcessor.Start(fun inbox -> 

        let rec loop () = async {
            let! HashMsg.Get(file, reply) = inbox.Receive ()
            use hashFile = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use sha1 = SHA1Managed.Create()
            reply.Reply (sha1.ComputeHash(hashFile) |> Array.map (sprintf "%02X") |> string)
            return! loop ()
        }

        loop ()
    )

    // TODO: Collect updates from work that's been done
    let collector = MailboxProcessor.Start(fun inbox -> 

        let rec loop () = async {
            let! message = inbox.Receive ()
            match message with 
            | Report -> 
                ()
            | Get ->
                ()
            return! loop ()
        }

        loop ()
    )

    let scan (localFolder : LocalFolder option) (remoteFolder : RemoteFolder option) = async {

        let getLocalItems folder = 
            let allFolders = 
                folder.DirectoryInfo.EnumerateDirectories ()
                |> Seq.map (fun s -> s.FullName, LocalItem.LocalFolder {DirectoryInfo = s})

            let allFiles = 
                folder.DirectoryInfo.EnumerateFiles ()
                |> Seq.map (fun s -> s.FullName, LocalItem.LocalFile {FileInfo = s})

            Seq.append allFolders allFiles

        let getRemoteItems folder = async {
            return! api.GetAllChildren folder |> Async.map (Seq.map (fun s -> s.Name, s))
        }
            
        let local = localFolder |> Option.map getLocalItems |> Option.defaultValue Seq.empty
        let! remote = remoteFolder |> Option.map getRemoteItems |> Option.defaultValue (Async.retn Seq.empty)
            
        let squash localItems remoteItems =
            let local = localItems |> Seq.map (fun (k,v) -> k, Item.Local v)
            let remote = remoteItems |> Seq.map (fun (k,v) -> k, Item.Remote v)

            Seq.append local remote
            |> Seq.groupBy fst
            |> Seq.map (fun (_,v) -> v |> Seq.map snd |> Seq.fold (fun state item -> match item with Local l -> (Some l, snd state) | Remote r -> (fst state, Some r)) (None, None))
                
        let proc = function 
            | None, Some (RemoteFile file) -> 
                [
                    Job.DiffContent(None, file |> RemoteItem.RemoteFile |> Some)
                ]
            | None, Some (RemoteFolder folder) -> 
                [
                    Job.DiffContent(None, folder |> RemoteItem.RemoteFolder |> Some)
                    Job.Scan (None, (Some folder))
                ]
            | Some (LocalFile local), None -> 
                [
                    Job.DiffContent(local |> LocalItem.LocalFile |> Some, None)
                ]
            | Some (LocalFolder local), None ->
                [
                    Job.DiffContent(local |> LocalItem.LocalFolder |> Some, None)
                    Job.Scan ((Some local), None)
                ]
            | Some (LocalFolder localFolder), Some (RemoteFolder remoteFolder) -> 
                [
                    Job.Compare (localFolder |> LocalItem.LocalFolder, remoteFolder |> RemoteItem.RemoteFolder)
                    Job.Scan ((Some localFolder), (Some remoteFolder))
                ]
            | Some (localFile), Some (remoteFile) -> 
                [
                    Job.Compare (localFile, remoteFile)
                ]
            | None, None -> failwith "Impossible case"

        squash local remote 
        |> Seq.iter (proc >> Seq.iter (ProcessMsg.Job >> processor.Post))
    }

    let applyChange local remote = async {

        let uploadFile file = async {
            // TODO: Report progress and item
            let progress = {new System.IProgress<_> with member __.Report _ = ()}
            let! _item = api.UploadFile file progress
            ()
        }

        let downloadFile file = async {
            // TODO: Write the stream to a file
            let! stream = api.DownloadFile file
            // TODO: Set local file attributes
            ()
        }

        match local, remote, direction with
        | Some (LocalFolder folder), None, Up ->
            () // UPLOAD CREATE FOLDER 
        | Some (LocalFile file), None, Up ->
            do! uploadFile file
        | None, Some (RemoteFolder folder), Down ->
            () // DOWNLOAD CREATE FOLDER
        | None, Some (RemoteFile file), Down ->
            do! downloadFile file
        | Some(LocalFile _), Some (RemoteFile remoteFile), Down ->
            do! downloadFile remoteFile
        | Some(LocalFile file), Some (RemoteFile _), Up ->
            do! uploadFile file
        | _ -> 
            () // DO NOTHING
    }

    let worker id = MailboxProcessor<unit>.Start(fun _ ->
        let rec loop () = async {
            let! job = processor.PostAndAsyncReply (fun reply -> RequestJob (id,reply))
                
            match job with
            | None -> do! Async.Sleep(100) // No work to do right now 
            | Some (Scan (a,b)) -> 
                do! scan a b
            | Some (DiffContent (a,b)) ->
                do! applyChange a b
            | Some (Compare (local, remote)) -> 
                match (local, remote) with
                | LocalFolder a, RemoteFolder b -> 
                    // TODO: Check folder attributes
                    ()
                | LocalFile localFile, RemoteFile remoteFile -> 
                    let! hash = hasher.PostAndAsyncReply (fun reply -> HashMsg.Get(localFile.FileInfo, reply))
                    if hash = remoteFile.SHA1 then
                        //printfn "%s is in sync" localFile.FileInfo.Name
                        // TODO: Check file attributes too
                        ()
                    else
                        Job.DiffContent (Some local, Some remote) |> ProcessMsg.Job |> processor.Post
                | LocalFolder _, RemoteFile _
                | LocalFile _, RemoteFolder _ ->
                    failwith "Item type folder <-> file mismatch"
    
            return! loop ()
        }
        loop ()
    )

    // Start the workers
    do [1 .. threads] |> Seq.iter (worker >> ignore)

    member __.Start localFolder remoteFolder = 

        (Some localFolder, Some remoteFolder)
        |> Job.Scan
        |> ProcessMsg.Job
        |> processor.Post

        async {
            let mutable finished = false
            while not finished do
                do! Async.Sleep 1000
                let! queue, active = processor.PostAndAsyncReply (fun reply -> ProcessMsg.Status reply)
                printfn "Queue size: %d with %d active workers" queue.Length active.Count
                finished <- queue.Length = 0 && active.IsEmpty
        } 