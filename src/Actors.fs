module Actors
open System.IO
open System.Security.Cryptography
open Domain
open OneDriveAPI

type private Msg = {
    File : FileInfo
    Reply : AsyncReplyChannel<string>
}

/// Compute hashes for a file - one at a time
type Hasher () =

    let actor = MailboxProcessor.Start(fun inbox -> 

        let rec loop () = async {
            let! {File = file; Reply = reply} = inbox.Receive ()
            use hashFile = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use sha1 = SHA1Managed.Create()
            reply.Reply (sha1.ComputeHash(hashFile) |> Array.map (sprintf "%02X") |> string)
            return! loop ()
        }

        loop ()
    )

    member __.Test f = actor.PostAndAsyncReply (fun reply -> {File = f; Reply = reply})


type Job = 
    | Scan of LocalFolder option * RemoteFolder option
    | RemoteOnly of RemoteItem
    | LocalOnly of LocalItem
    | Difference of string

type ProcessMsg = 
    | Job of Job
    | RequestJob of AsyncReplyChannel<Job option> 

/// Do something with a diff
type Something (api : IOneDriveAPI) =

    let processor = 
        MailboxProcessor<ProcessMsg>.Start(fun inbox ->
            let queue = new System.Collections.Generic.Queue<Job>()
            let rec loop () = async {
                let! message = inbox.Receive()
                match message with 
                    | Job job -> queue.Enqueue job
                    | RequestJob replyChannel -> 
                        match queue.TryDequeue () with
                        | true, item -> replyChannel.Reply (Some item)
                        | false, _ -> replyChannel.Reply None

                return! loop ()
            }
            loop ())

    let worker _id = 

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
                
            let proc (local,remote) = 
                match local, remote with
                | None, Some (RemoteFile file) -> 
                    file |> RemoteItem.RemoteFile |> Job.RemoteOnly
                | None, Some (RemoteFolder folder) -> 
                     Job.Scan (None, (Some folder))
                | Some (LocalFile local), None -> 
                    local |> LocalItem.LocalFile |> Job.LocalOnly
                | Some (LocalFolder local), None 
                    -> Job.Scan ((Some local), None)
                | Some (LocalFile localFile), Some (RemoteFile file) -> 
                    localFile.FileInfo.Name |> Job.Difference
                | Some (LocalFolder localFolder), Some (RemoteFolder remoteFolder) -> 
                    Job.Scan ((Some localFolder),(Some remoteFolder))
                | Some (LocalFolder _), Some (RemoteFile _)
                | Some (LocalFile _), Some (RemoteFolder _) ->
                    failwith "Item folder <-> file mismatch"
                | None, None -> failwith "Impossible case"

            squash local remote |> Seq.iter (proc >> ProcessMsg.Job >> processor.Post)
        }

        MailboxProcessor<unit>.Start(fun _ ->
            let rec loop () = async {
                let! job = processor.PostAndAsyncReply (fun reply -> RequestJob reply)
                
                match job with
                | None -> do! Async.Sleep(100) // No work to do right now 
                | Some (Scan (a,b)) -> do! scan a b
                | Some (RemoteOnly f) -> printfn "%s is Remote only!" f.Name
                | Some (LocalOnly f) -> printfn "%s is Local only!" f.Name
                | Some (Difference f) -> printfn "%s Diff!" f
    
                return! loop ()
            }
            loop ()
        ) |> ignore

    member __.Start localFolder remoteFolder threads = 

        // Start a few workers
        [1 .. threads] |> Seq.iter worker

        (Some localFolder, Some remoteFolder)
        |> Job.Scan
        |> ProcessMsg.Job
        |> processor.Post

        System.Threading.Thread.Sleep(10000)