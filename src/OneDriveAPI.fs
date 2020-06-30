module OneDriveAPI

open Microsoft.Graph
open Domain

type IOneDriveAPI = 
    abstract member GetDrive : unit -> Async<Drive>
    abstract member GetFolder : string -> Async<RemoteFolder>
    abstract member GetAllChildren : RemoteFolder -> Async<seq<RemoteItem>>

let build (client : GraphServiceClient) = 

    let toFolder (driveItem: Microsoft.Graph.DriveItem) = {
        Name = driveItem.Name
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Path = driveItem.ParentReference.Path
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
    }

    let toFile (driveItem: Microsoft.Graph.DriveItem) = {
        ID = driveItem.Id
        DriveID = driveItem.ParentReference.DriveId
        Path = driveItem.ParentReference.Path
        Name = driveItem.Name
        Created = driveItem.CreatedDateTime.Value.DateTime
        Updated = driveItem.LastModifiedDateTime.Value.DateTime
        SHA1 = driveItem.File.Hashes.Sha1Hash
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
            | folder when folder.Folder <> null -> driveItem |> toFolder |> RemoteFolder
            | child when child.File <> null -> driveItem |> toFile |> RemoteFile
            | child when child.Package <> null -> driveItem |> toPackage |> RemoteFile
            | child -> failwithf "Unknown DriveItem type for file %s in %s" child.Name child.ParentReference.Path

        return!
            client.Drives.Item(folder.DriveID).Items.Item(folder.ID).Children.Request().GetAsync() 
            |> Async.AwaitTask
            |> Async.bind getAllPages
            |> Async.map (Seq.map toRemoteItem)
    }

    let getPathFolder path = async {
        return!
            client.Me.Drive.Root.ItemWithPath(path).Request().GetAsync()
            |> Async.AwaitTask
            |> Async.map toFolder
    }

    { 
        new IOneDriveAPI with
            member __.GetDrive () = getDrive
            member __.GetFolder path = getPathFolder path
            member __.GetAllChildren folder = getAllItems folder
    }