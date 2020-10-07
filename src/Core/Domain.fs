module Domain
open System
open System.IO

type LocalFile = {
    FileInfo : FileInfo
}

type LocalFolder = {
    DirectoryInfo : DirectoryInfo
}

type LocalItem =
    | LocalFile of LocalFile
    | LocalFolder of LocalFolder
    member x.Name =
        match x with
        | LocalFile f -> f.FileInfo.Name
        | LocalFolder f -> f.DirectoryInfo.Name

type RemoteFile = {
    ID : string
    DriveID : string
    Path : string
    Name : string
    Created : DateTime
    Updated : DateTime
    SHA1 : string
    QuickXOR : string
    Size : int64
}

type RemoteFolder = {
    ID : string
    DriveID : string
    Path : string
    Name : string
    Created : DateTime
    Updated : DateTime
}

type RemoteItem =
    | RemoteFolder of RemoteFolder
    | RemoteFile of RemoteFile
    member x.FullName =
        match x with
        | RemoteFolder f -> Path.Combine(f.Path, f.Name)
        | RemoteFile f -> Path.Combine(f.Path, f.Name)

type Item =
    | Local of LocalItem
    | Remote of RemoteItem

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