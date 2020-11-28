module Worker

open Domain
open System.IO
open Main
open OneDriveAPI

let start (api : OneDriveAPIClient) (direction : Direction) (dryRun : bool) (localPath : string) id = 

    let scan (localFolder : LocalFolder option) (remoteFolder : RemoteFolder option) = async {

        let toLocation path = 
            "/" + Path.GetRelativePath(localPath, path).Replace('\\', '/')
        let getLocalItems folder = 
            let allFolders = 
                folder.DirectoryInfo.EnumerateDirectories ()
                |> Seq.map (fun s -> LocalItem.LocalFolder {Location = toLocation s.FullName; DirectoryInfo = s})

            let allFiles = 
                folder.DirectoryInfo.EnumerateFiles ()
                |> Seq.map (fun s -> LocalItem.LocalFile {Location = toLocation s.FullName; FileInfo = s})

            Seq.append allFolders allFiles

        let getRemoteItems folder = async {
            return! api.GetAllChildren folder
        }
        
        let local = localFolder |> Option.map getLocalItems |> Option.defaultValue Seq.empty
        let! remote = remoteFolder |> Option.map getRemoteItems |> Option.defaultValue (Async.retn Seq.empty)
        
        let squash localItems remoteItems =
            let local = localItems |> Seq.map (fun (x : LocalItem) -> x.Location, Item.Local x)
            let remote = remoteItems |> Seq.map (fun (x : RemoteItem) -> x.Location, Item.Remote x)

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
        |> Seq.iter (proc >> Seq.iter Main.queueJob)
    }

    let applyChange local remote = async {

        let uploadFile file = async {
            Collector.report (Collector.Upload file)
            if dryRun then 
                printfn "Would upload %s" file.FileInfo.Name
            else
                // TODO: Report progress and item
                let progress = {new System.IProgress<_> with member __.Report _ = ()}
                let! item = api.UploadFile file progress
                printfn "Uploaded %s" item.Name
        }

        let downloadFile (file : RemoteFile) = async {
            Collector.report (Collector.Download file)
            if dryRun then
                printfn "Would download %s" file.Name
            else 
                let! stream = api.DownloadFile file
                let relativeLocation = file.Location.Substring(1).Replace('/', System.IO.Path.DirectorySeparatorChar)
                let targetFile = FileInfo(Path.Combine(localPath, relativeLocation))
                targetFile.Directory.Create()

                use fileStream = targetFile.OpenWrite ()
                stream.Seek(0L, SeekOrigin.Begin) |> ignore
                do! stream.CopyToAsync(fileStream) |> Async.AwaitTask
                fileStream.Close()
            
                // Set local attributes
                targetFile.CreationTime <- file.Created
                targetFile.LastAccessTime <- file.Updated
                targetFile.LastWriteTime <- file.Updated
                printfn "Downloaded %s" targetFile.FullName
        }

        match local, remote, direction with
        | Some (LocalFolder folder), None, Up ->
            () // UPLOAD CREATE FOLDER 
        | Some (LocalFile file), None, Up ->
            do! uploadFile file
        | Some(LocalFile file), Some (RemoteFile _), Up ->
            do! uploadFile file
        | None, Some (RemoteFolder folder), Down ->
            () // DOWNLOAD CREATE FOLDER
        | None, Some (RemoteFile file), Down ->
            do! downloadFile file
        | Some(LocalFile _), Some (RemoteFile remoteFile), Down ->
            do! downloadFile remoteFile
        | _ -> 
            () // DO NOTHING
    }

    MailboxProcessor<unit>.Start(fun _ ->
        let rec loop () = async {
            let! job = Main.tryGetJob id
            
            match job with
            | None -> do! Async.Sleep(100) // No work to do right now 
            | Some (Job.Scan (a,b)) -> 
                do! scan a b
            | Some (Job.DiffContent (a,b)) ->
                do! applyChange a b
            | Some (Job.Compare (local, remote)) -> 
                match (local, remote) with
                | LocalFolder a, RemoteFolder b -> 
                    // TODO: Check folder attributes
                    ()
                | LocalFile localFile, RemoteFile remoteFile -> 
                    let! same = async {
                        match remoteFile with
                        | {SHA1 = remoteHash} when not (System.String.IsNullOrEmpty(remoteHash)) ->
                            let hash = Hasher.generateHash Hasher.SHA1 localFile 
                            return hash = remoteHash
                        | {QuickXOR = remoteHash} when not (System.String.IsNullOrEmpty(remoteHash)) ->
                            let hash = Hasher.generateHash Hasher.QuickXOR localFile 
                            return hash = remoteHash
                        | _ -> return failwith "No valid remote hashes"
                    }
                    if same then
                        Collector.report Collector.Same
                    else
                        Job.DiffContent (Some local, Some remote) |> Main.queueJob

                | LocalFolder _, RemoteFile _
                | LocalFile _, RemoteFolder _ ->
                    failwith "Item type folder <-> file mismatch"

            return! loop ()
        }
        loop ()
    )
    |> ignore

