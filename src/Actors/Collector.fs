module Collector

open Domain

type CollectorReport =
    | Download of RemoteFile
    | Upload of LocalFile
    | Same

type private CollectorMsg =
    | Report of CollectorReport
    | Get of AsyncReplyChannel<unit>

// TODO: Collect updates from work that's been done
let private collector = MailboxProcessor.Start(fun inbox -> 

    let rec loop () = async {
        let! message = inbox.Receive ()
        match message with 
        | Report _ -> 
            ()
        | Get r ->
            r.Reply ()
        return! loop ()
    }

    loop ()
)

let report report =
    collector.Post (Report(report))

let get () = 
    collector.PostAndAsyncReply (fun reply -> Get(reply))
        