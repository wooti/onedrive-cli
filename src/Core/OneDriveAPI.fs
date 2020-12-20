module OneDriveCLI.Core.OneDriveAPI

open Microsoft.Graph
open Domain
open OneDriveCLI.Modules

type OneDriveAPIClient (client : GraphServiceClient, remoteRoot : string) = 

    let toLocation (driveItem: Microsoft.Graph.DriveItem) =

        let path = 
            let parentPath = driveItem.ParentReference.Path
            if parentPath = null then 
                ""
            else if parentPath = "/drive/root:" then
                ""
            else
                let relativePath = parentPath.Substring(parentPath.IndexOf("root:/" + remoteRoot) + remoteRoot.Length + 6)
                if relativePath.StartsWith('/') then relativePath.Substring(1) else relativePath

        {Folder = path; Name = driveItem.Name}

    let toFolder (driveItem: Microsoft.Graph.DriveItem) = {
        Location = toLocation driveItem
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
    }

    let toFile (driveItem: Microsoft.Graph.DriveItem) = {
        Location = toLocation driveItem
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
        SHA1 = if driveItem.File.Hashes <> null && driveItem.File.Hashes.Sha1Hash <> null then Some driveItem.File.Hashes.Sha1Hash else None
        QuickXOR = if driveItem.File.Hashes <> null && driveItem.File.Hashes.QuickXorHash <> null then Some driveItem.File.Hashes.QuickXorHash else None
        Length = driveItem.Size.Value
    }

    let toPackage (driveItem: Microsoft.Graph.DriveItem) = {
        Location = toLocation driveItem
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
        SHA1 = None
        QuickXOR = None
        Length = driveItem.Size.Value
    }

    let toDto dt = 
        new System.DateTimeOffset(dt) |> System.Nullable

    let getDrive () = async {

        let! drive =
            client.Me.Drive.Request().GetAsync() 
            |> Async.AwaitTask

        return {
            Name = drive.Name
            Id = drive.Id
            Type = drive.DriveType
            Size = drive.Quota.Total.GetValueOrDefault()
            Used = drive.Quota.Used.GetValueOrDefault()
        }
    }

    let getAllItems folder = async {

        let toRemoteItem (driveItem : DriveItem) = 

            match driveItem with
            | folder when folder.Folder <> null -> driveItem |> toFolder |> RemoteFolder
            | child when child.File <> null -> driveItem |> toFile |> RemoteFile
            | child when child.Package <> null -> driveItem |> toPackage |> RemoteFile
            | child -> failwithf "Unknown DriveItem type for file %s in %s" child.Name child.ParentReference.Path

        let! data =
            client.Drives.Item(folder.DriveID).Items.Item(folder.ID).Children.Request().GetAsync() 
            |> Async.AwaitTask

        let items = new System.Collections.Generic.List<_>()
        do! 
            PageIterator<_>.CreatePageIterator(client, data, (fun a -> items.Add a; true)).IterateAsync()
            |> Async.AwaitTask

        return items |> Seq.map toRemoteItem
    }

    let getRootFolder () = async {
        return!
            client.Me.Drive.Root.ItemWithPath(remoteRoot).Request().GetAsync()
            |> Async.AwaitTask
            |> Async.map toFolder
    }

    let downloadFile (file : RemoteFile) = async {
        return! 
            client.Drives.Item(file.DriveID).Items.Item(file.ID).Content.Request().GetAsync()
            |> Async.AwaitTask
    }

    let uploadFile (file : LocalFile) progress = async {

        // Upload small files directly
        if file.FileInfo.Length < 1024L * 1024L then

            let target = remoteRoot + "/" + file.Location.FullName
            let! uploadedItem = 
                client.Me.Drive.Root.ItemWithPath(target).Content.Request().PutAsync(file.FileInfo.OpenRead())
                |> Async.AwaitTask

            let timestamps = new DriveItem (FileSystemInfo = new FileSystemInfo(CreatedDateTime = (file.FileInfo.CreationTimeUtc |> toDto), LastModifiedDateTime = (file.FileInfo.LastWriteTimeUtc |> toDto)))
            return! client.Me.Drive.Items.Item(uploadedItem.Id).Request().UpdateAsync(timestamps)
                |> Async.AwaitTask
                |> Async.map toFile


        // Do a multi-part resumable upload
        else

            let _props = 
                new DriveItemUploadableProperties (
                    FileSize = (file.FileInfo.Length |> System.Nullable)
                    //AdditionalData = (["@microsoft.graph.conflictBehavior", box "replace"] |> dict)
                )
            let! uploadSession =  
                client.Me.Drive.Root.ItemWithPath(remoteRoot + "/" + file.Location.FullName).CreateUploadSession().Request().PostAsync()
                |> Async.AwaitTask

            let maxSliceSize = 320 * 1024 * 16; // 5MB
            let fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, file.FileInfo.OpenRead(), maxSliceSize);

            try
                return! fileUploadTask.UploadAsync(progress)
                |> Async.AwaitTask
                |> Async.map (fun a -> if a.UploadSucceeded then a.ItemResponse else failwith "Upload failed")
                |> Async.map toFile
            with ex ->
                Output.writer.dprintfn "Unable to upload file %s due to %s" file.Location.FullName ex.Message
                return raise ex
    }

    let createFolder (location : Location) name = async {

        try
            let folder = new DriveItem (Folder = new Folder())
            let target = remoteRoot + "/" + location.FullName
            return! 
                client.Me.Drive.Root.ItemWithPath(target).Request().UpdateAsync(folder)
                |> Async.AwaitTask
                |> Async.map toFolder
        with ex ->
            Output.writer.dprintfn "Unable to create folder /%s/%s/%s due to %s" remoteRoot location.Folder name ex.Message
            return raise ex
    }

    member __.GetDrive () = getDrive ()
    member __.GetRoot () = getRootFolder ()
    member __.GetAllChildren folder = getAllItems folder
    member __.CreateFolder location name = createFolder location name
    member __.DownloadFile file = downloadFile file    
    member __.UploadFile file progress = uploadFile file progress