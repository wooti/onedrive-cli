module Domain
open System
open System.IO

type Location = string

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
    Name : string
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
    Name : string
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