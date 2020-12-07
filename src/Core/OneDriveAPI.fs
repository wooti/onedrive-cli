﻿module OneDriveCLI.Core.OneDriveAPI

open Microsoft.Graph
open Domain
open OneDriveCLI.Modules

type OneDriveAPIClient (client : GraphServiceClient) = 

    let toLocation (driveItem: Microsoft.Graph.DriveItem) =

        let path = 
            let parentPath = driveItem.ParentReference.Path
            if parentPath = null then 
                ""
            else 
                let relativePath = parentPath.Substring(parentPath.IndexOf("root:") + 5)
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
        SHA1 = driveItem.File.Hashes.Sha1Hash
        QuickXOR = driveItem.File.Hashes.QuickXorHash
        Size = driveItem.Size.Value
    }

    let toPackage (driveItem: Microsoft.Graph.DriveItem) = {
        Location = toLocation driveItem
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
        SHA1 = ""
        QuickXOR = ""
        Size = driveItem.Size.Value
    }

    let getDrive = async {

        let! drive =
            client.Me.Drive.Request().GetAsync() 
            |> Async.AwaitTask

        let! rootFolder = 
            client.Drives.Item(drive.Id).Root.Request().GetAsync() 
            |> Async.AwaitTask
            |> Async.map toFolder

        return {
            Name = drive.Name
            Id = drive.Id
            Type = drive.DriveType
            Size = drive.Quota.Total.GetValueOrDefault()
            Used = drive.Quota.Used.GetValueOrDefault()
            Root = rootFolder
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

    let getPathFolder path = async {
        return!
            client.Me.Drive.Root.ItemWithPath(path).Request().GetAsync()
            |> Async.AwaitTask
            |> Async.map toFolder
    }

    let downloadFile (file : RemoteFile) = async {
        return! 
            client.Drives.Item(file.DriveID).Items.Item(file.ID).Content.Request().GetAsync()
            |> Async.AwaitTask
    }

    let uploadFile (file : LocalFile) progress = async {
        let props = 
            new DriveItemUploadableProperties (
                FileSize = (file.FileInfo.Length |> System.Nullable),
                AdditionalData = (["@microsoft.graph.conflictBehavior", box "rename"] |> dict)
            )
        let! uploadSession =  
            client.Me.Drive.Root.ItemWithPath(file.Location.Folder + "/" + file.FileInfo.Name).CreateUploadSession(props).Request().PostAsync()
            |> Async.AwaitTask

        let maxSliceSize = 320 * 1024;
        let fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, file.FileInfo.OpenRead(), maxSliceSize);

        return! fileUploadTask.UploadAsync(progress)
        |> Async.AwaitTask
        |> Async.map (fun a -> if a.UploadSucceeded then a.ItemResponse else failwith "Upload failed")
    }

    let createFolder (location : Location) name = async {
        let folder = new DriveItem (Name = name, Folder = new Folder())
        return! 
            client.Drive.Root.ItemWithPath(location.Folder).Children.Request().AddAsync(folder)
            |> Async.AwaitTask
    }

    member __.GetDrive () = getDrive
    member __.GetFolder path = getPathFolder path
    member __.GetAllChildren folder = getAllItems folder
    member __.CreateFolder location name = createFolder location name
    member __.DownloadFile file = downloadFile file    
    member __.UploadFile file progress = uploadFile file progress