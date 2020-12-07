module OneDriveCLI.Core.Domain
open System
open System.IO

type Location = {
    Folder : string
    Name : string
}
with 
    member x.FullName = x.Folder + "/" + x.Name

type LocalFile = {
    Location : Location
    FileInfo : FileInfo
}

type LocalFolder = {
    Location : Location
    DirectoryInfo : DirectoryInfo
}

type LocalItem =
    | LocalFile of LocalFile
    | LocalFolder of LocalFolder
    member x.Location = match x with LocalFile f -> f.Location | LocalFolder f -> f.Location

type RemoteFile = {
    Location : Location
    ID : string
    DriveID : string
    Created : DateTime
    Updated : DateTime
    SHA1 : string
    QuickXOR : string
    Size : int64
}

type RemoteFolder = {
    Location : Location
    ID : string
    DriveID : string
    Created : DateTime
    Updated : DateTime
}

type RemoteItem =
    | RemoteFolder of RemoteFolder
    | RemoteFile of RemoteFile
    member x.Location = match x with RemoteFolder f -> f.Location | RemoteFile f -> f.Location

type Item =
    | Local of LocalItem
    | Remote of RemoteItem
    member x.Location = match x with Local f -> f.Location | Remote f -> f.Location

type Drive = {
    Name : string
    Id : string
    Type : string
    Size : int64
    Used : int64
    Root : RemoteFolder
}

type Direction =
    | Up
    | Down

type Job = 
    | Scan of LocalFolder option * RemoteFolder option
    | Compare of LocalItem * RemoteItem
    | Download of RemoteItem
    | Upload of LocalItem
    member x.Description =
        match x with
        | Scan (f, y) -> 
            sprintf "Scan: %s, %s" 
                (f |> Option.map (fun a -> a.Location.FullName) |> Option.defaultValue "<None>")
                (y |> Option.map (fun a -> a.Location.FullName) |> Option.defaultValue "<None>")
        | Compare (_, remote) -> sprintf "Compare: %s" remote.Location.FullName
        | Download (remote) -> 
            sprintf "Download: %s" remote.Location.FullName
        | Upload (local) -> 
            sprintf "Upload: %s" local.Location.FullName