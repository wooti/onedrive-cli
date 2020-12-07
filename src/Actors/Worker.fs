module OneDriveCLI.Actors.Worker

open System.IO
open Main
open OneDriveCLI.Core.OneDriveAPI
open OneDriveCLI.Core.Domain
open OneDriveCLI.Modules

let start (api : OneDriveAPIClient) (direction : Direction) (dryRun : bool) (localPath : string) (remotePath : string) id = 

    let toUploadDownload local remote =  
        match direction, local, remote with
        | Up, Some local, _ ->
            [local |> Job.Upload]
        | Down, _, Some remote ->
            [remote |> Job.Download]
        | Up, None, Some remote ->
            Output.writer.printfn "Extra remote file: %s" remote.Location.FullName
            []
        | Down, Some local, None ->
            Output.writer.printfn "Extra local file: %s" local.Location.FullName
            []
        | _ -> 
            [] // DO NOTHING

    let scan (localFolder : LocalFolder option) (remoteFolder : RemoteFolder option) = async {

        let toLocation dirName name = 
            let relative = Path.GetRelativePath(localPath, dirName).Replace('\\', '/')
            {Folder = (if relative = "." then "" else relative); Name = name}

        let getLocalItems folder = 
            let allFolders = 
                folder.DirectoryInfo.EnumerateDirectories ()
                |> Seq.map (fun s -> LocalItem.LocalFolder {Location = toLocation s.Parent.FullName s.Name; DirectoryInfo = s})

            let allFiles = 
                folder.DirectoryInfo.EnumerateFiles ()
                |> Seq.map (fun s -> LocalItem.LocalFile {Location = toLocation s.DirectoryName s.Name; FileInfo = s})

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
                toUploadDownload None (file |> RemoteItem.RemoteFile |> Some)
            | None, Some (RemoteFolder folder) -> 
                Job.Scan (None, (Some folder)) :: toUploadDownload None (folder |> RemoteItem.RemoteFolder |> Some)
            | Some (LocalFile local), None -> 
                toUploadDownload (local |> LocalItem.LocalFile |> Some) None
            | Some (LocalFolder local), None ->
                Job.Scan ((Some local), None) :: toUploadDownload (local |> LocalItem.LocalFolder |> Some) None
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

    let uploadFile file = async {
        file |> LocalFile |> Collector.Upload |> Collector.report
        if dryRun then 
            Output.writer.printfn "Would upload %s" file.Location.FullName
        else
            // TODO: Report progress and item
            let progress = {new System.IProgress<_> with member __.Report _ = ()}
            let! _item = api.UploadFile file progress
            Output.writer.printfn "Uploaded %s" file.Location.FullName
    }

    let uploadFolder (folder : LocalFolder) = async {
        folder |> LocalFolder |> Collector.Upload |> Collector.report
        if dryRun then 
            Output.writer.printfn "Would create remote folder %s" folder.Location.FullName
        else
            do! api.CreateFolder folder.Location folder.DirectoryInfo.Name |> Async.Ignore
    }

    let downloadFile (file : RemoteFile) = async {
        file |> RemoteFile |> Collector.Download |> Collector.report
        if dryRun then
            Output.writer.printfn "Would download %s" file.Location.FullName
        else 
            let! stream = api.DownloadFile file
            let relativeLocation = file.Location.Folder.Replace('/', System.IO.Path.DirectorySeparatorChar)
            let targetFile = FileInfo(Path.Combine(localPath, relativeLocation, file.Location.Name))
            targetFile.Directory.Create()

            use fileStream = targetFile.OpenWrite ()
            stream.Seek(0L, SeekOrigin.Begin) |> ignore
            do! stream.CopyToAsync(fileStream) |> Async.AwaitTask
            fileStream.Close()
        
            // Set local attributes
            targetFile.CreationTime <- file.Created
            targetFile.LastAccessTime <- file.Updated
            targetFile.LastWriteTime <- file.Updated
            Output.writer.printfn "Downloaded %s" file.Location.FullName
    }

    let downloadFolder (folder : RemoteFolder) = 
        folder |> RemoteFolder |> Collector.Download |> Collector.report
        if dryRun then
            Output.writer.printfn "Would create local folder %s" folder.Location.Name
        else 
            let relativeLocation = folder.Location.Folder.Replace('/', System.IO.Path.DirectorySeparatorChar)
            let targetDirectory = DirectoryInfo(Path.Combine(localPath, relativeLocation, folder.Location.Name))
            targetDirectory.Create()

            // Set local attributes
            targetDirectory.CreationTime <- folder.Created
            targetDirectory.LastAccessTime <- folder.Updated
            targetDirectory.LastWriteTime <- folder.Updated
            Output.writer.printfn "Created local folder %s" targetDirectory.FullName

    MailboxProcessor<unit>.Start(fun _ ->
        let rec loop () = async {
            let! job = Main.tryGetJob id
            
            match job with
            | None -> do! Async.Sleep(100) // No work to do right now 
            | Some (Job.Scan (a,b)) -> 
                do! scan a b
            | Some (Job.Download (RemoteFile f)) ->
                do! downloadFile f
            | Some (Job.Download (RemoteFolder f)) ->
                do downloadFolder f
            | Some (Job.Upload (LocalFile f)) ->
                do! uploadFile f
            | Some (Job.Upload (LocalFolder f)) ->
                do! uploadFolder f
            | Some (Job.Compare (local, remote)) -> 
                match (local, remote) with
                | LocalFolder localFolder, RemoteFolder remoteFolder -> 
                    // TODO: Check folder attributes
                    let same = true

                    if same then
                        (local, remote) |> Collector.Same |> Collector.report

                | LocalFile localFile, RemoteFile remoteFile -> 
                    let! same = async {
                        match remoteFile with
                        | {SHA1 = remoteHash} when not (System.String.IsNullOrEmpty(remoteHash)) ->
                            let hash = Hasher.generateHash Hasher.SHA1 localFile.FileInfo
                            return hash = remoteHash
                        | {QuickXOR = remoteHash} when not (System.String.IsNullOrEmpty(remoteHash)) ->
                            let hash = Hasher.generateHash Hasher.QuickXOR localFile.FileInfo
                            return hash = remoteHash
                        | _ -> return failwith "No valid remote hashes"
                    }

                    if same then
                        (local, remote) |> Collector.Same |> Collector.report
                    else
                        toUploadDownload (Some local) (Some remote) |> List.iter Main.queueJob

                | LocalFolder _, RemoteFile _
                | LocalFile _, RemoteFolder _ ->
                    failwith "Item type folder <-> file mismatch"

            return! loop ()
        }
        loop ()
    )
    |> ignore

