module Domain
open System

type ItemInfo = {
    Name : string
    ID : string
    Path : string
    Created : DateTimeOffset
    Updated : DateTimeOffset
}

type FileInfo = {
    SHA1 : string
    Size : int64
}

type Drive = {
    Name : string
    Id : string
    Type : string
    Size : int64
    Used : int64
}

type Item =
    | Folder of ItemInfo * Children : Item list
    | File of ItemInfo * FileInfo
    | Package of ItemInfo
with
    member __.ItemInfo = function
        | Folder (i, _)
        | File (i,_) -> i
        | Package i -> i
