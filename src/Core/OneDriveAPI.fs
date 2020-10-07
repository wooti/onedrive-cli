module OneDriveAPI

open Microsoft.Graph
open Domain

type OneDriveAPIClient (client : GraphServiceClient) = 

    let toRelativePath (path : string) =
        // TODO: Make this pretty
        let rootLocation = path.IndexOf("root:/")
        
        if rootLocation > 0 then
            path.Substring(rootLocation + 6)
        else
            let rootLocation = path.IndexOf("root:")
            if rootLocation > 0 then
                path.Substring(rootLocation + 5)
            else
                // TODO Validate this assumption
                failwith "Assumption error: Paths don't always contain root:"

    let toFolder isRoot (driveItem: Microsoft.Graph.DriveItem) = {
        Name = driveItem.Name
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Path = if isRoot then "" else toRelativePath driveItem.ParentReference.Path
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
    }

    let toFile (driveItem: Microsoft.Graph.DriveItem) = {
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Path = toRelativePath driveItem.ParentReference.Path
        Name = driveItem.Name
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
        SHA1 = driveItem.File.Hashes.Sha1Hash
        QuickXOR = driveItem.File.Hashes.QuickXorHash
        Size = driveItem.Size.Value
    }

    let toPackage (driveItem: Microsoft.Graph.DriveItem) = {
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Path = driveItem.ParentReference.Path
        Name = driveItem.Name
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
            |> Async.map (toFolder true)

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

        // TODO: Delete this
        let rec getAllPages (request : IDriveItemChildrenCollectionPage) = async {
            let! remaining = async {
                match request.NextPageRequest with
                | null -> return Seq.empty
                | _ -> 
                    let! nextPage = request.NextPageRequest.GetAsync() |> Async.AwaitTask
                    return! getAllPages nextPage
            }
            return request.CurrentPage |> Seq.append remaining
        }

        let toRemoteItem (driveItem : DriveItem) = 

            match driveItem with
            | folder when folder.Folder <> null -> driveItem |> toFolder false |> RemoteFolder
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
            |> Async.map (toFolder false)
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
            client.Me.Drive.Root.ItemWithPath("aa").CreateUploadSession(props).Request().PostAsync()
            |> Async.AwaitTask

        let maxSliceSize = 320 * 1024;
        let fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, file.FileInfo.OpenRead(), maxSliceSize);

        // TODO: Wire up progress
        return! fileUploadTask.UploadAsync(progress)
        |> Async.AwaitTask
        |> Async.map (fun a -> if a.UploadSucceeded then a.ItemResponse else failwith "Upload failed")
    }

    member __.GetDrive () = getDrive
    member __.GetFolder path = getPathFolder path
    member __.GetAllChildren folder = getAllItems folder
    member __.DownloadFile file = downloadFile file    
    member __.UploadFile file progress = uploadFile file progress