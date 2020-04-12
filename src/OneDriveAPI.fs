module OneDriveAPI

open Microsoft.Graph
open Domain

type IOneDriveAPI = 
    abstract member GetDrives : unit -> Async<Drive>
    abstract member GetAllItems : Drive -> Async<Item>

let build (client : GraphServiceClient) = 

    let toItemInfo (driveItem: Microsoft.Graph.DriveItem) = {
        Name = driveItem.Name
        ID = driveItem.Id
        Path = driveItem.ParentReference.Path
        Created = driveItem.CreatedDateTime.Value
        Updated = driveItem.LastModifiedDateTime.Value
    }

    let toFileInfo (driveItem: Microsoft.Graph.DriveItem) = {
        Size = driveItem.Size.Value
        SHA1 = driveItem.File.Hashes.Sha1Hash
    }

    let toDrive (drive: Microsoft.Graph.Drive) = {
        Name = drive.Name
        Id = drive.Id
        Type = drive.DriveType
        Size = drive.Quota.Total.GetValueOrDefault()
        Used = drive.Quota.Used.GetValueOrDefault()
    }

    let getDrives = async {
        let! drive = client.Me.Drive.Request().GetAsync() |> Async.AwaitTask
        return drive |> toDrive
    }

    let getAllItems drive = async {

        let! rootItem = client.Drives.Item(drive.Id).Root.Request().GetAsync() |> Async.AwaitTask

        let rec getChildren (item : DriveItem) = async {

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

            let! children = client.Drives.Item(drive.Id).Items.Item(item.Id).Children.Request().GetAsync() |> Async.AwaitTask
            let! allChildren = getAllPages children

            return! allChildren |> Seq.map (function
                | folder when folder.Folder <> null -> async {
                    let! subChildren = getChildren folder
                    return Item.Folder(folder |> toItemInfo, subChildren |> Seq.toList)}
                | child when child.File <> null -> async {
                    return Item.File(child |> toItemInfo, child |> toFileInfo)}
                | child -> async {
                    return Item.Package(child |> toItemInfo)}
            )
            |> Async.Parallel
        }

        let! children = getChildren rootItem
        return Item.Folder(rootItem |> toItemInfo, children |> Seq.toList)
    }
      
    { 
        new IOneDriveAPI with
            member __.GetDrives () = getDrives
            member __.GetAllItems drive = getAllItems drive
    }