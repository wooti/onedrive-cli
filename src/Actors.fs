module Actors
open System.IO
open System.Security.Cryptography
open Domain

type private Msg = {
    File : FileInfo
    Reply : AsyncReplyChannel<string>
}

/// Compute hashes for a file - one at a time
type Hasher () =

    let actor = MailboxProcessor.Start(fun inbox -> 

        let rec loop () = async {
            let! {File = file; Reply = reply} = inbox.Receive ()
            use hashFile = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use sha1 = SHA1Managed.Create()
            reply.Reply (sha1.ComputeHash(hashFile) |> Array.map (sprintf "%02X") |> string)
            return! loop ()
        }

        loop ()
    )

    member __.Test f = actor.PostAndAsyncReply (fun reply -> {File = f; Reply = reply})

type Diff = 
    | RemoteOnly of RemoteItem
    | LocalOnly of LocalItem
    | Difference of string

/// Do something with a diff
type Something () =

    let actor = MailboxProcessor.Start(fun inbox -> 

        let rec loop () = async {
            let! diff = inbox.Receive ()
            match diff with
            | RemoteOnly f -> printfn "%s is Remote only!" f.Name
            | LocalOnly f -> printfn "%s is Local only!" f.Name
            | Difference f -> printfn "%s Diff!" f
            return! loop ()
        }
        loop ()
    )

    member __.Notify f = actor.Post f