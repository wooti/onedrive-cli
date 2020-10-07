module Main
open Domain

type WorkerID = int

type Job = 
    | Scan of LocalFolder option * RemoteFolder option
    | Compare of LocalItem * RemoteItem
    | DiffContent of LocalItem option * RemoteItem option
    member x.Description =
        match x with
        | Scan (f, y) -> sprintf "Scan: %O" f
        | Compare (_, remote) -> sprintf "Compare: %s" remote.FullName
        | DiffContent (local, remote) -> sprintf "Sync: %O %O" local remote

type private ProcessMsg = 
    | Job of Job
    | Status of AsyncReplyChannel<Job array * Map<WorkerID, Job>>
    | RequestJob of WorkerID * AsyncReplyChannel<Job option> 

let private processor = MailboxProcessor.Start(fun inbox ->
    let queue = new System.Collections.Generic.Queue<Job>()
    let rec loop activeWorkers = async {
        let! message = inbox.Receive()
        let workersNow = 
            match message with 
            | Job job -> 
                queue.Enqueue job
                activeWorkers
            | Status replyChannel ->
                replyChannel.Reply(queue |> Seq.toArray, activeWorkers)
                activeWorkers
            | RequestJob (workerID, replyChannel) -> 
                match queue.TryDequeue () with
                | true, item -> 
                    replyChannel.Reply (Some item)
                    activeWorkers |> Map.add workerID item
                | false, _ -> 
                    replyChannel.Reply None
                    activeWorkers |> Map.remove workerID

        return! loop workersNow
    }
    loop Map.empty)

let queueJob job =
    job |> ProcessMsg.Job |> processor.Post

let tryGetJob workerID = 
    processor.PostAndAsyncReply (fun reply -> RequestJob (workerID, reply))

let awaitFinish () = async {
    let mutable finished = false
    while not finished do
        do! Async.Sleep 1000
        System.Console.Clear ()
        let! queue, active = processor.PostAndAsyncReply (fun reply -> ProcessMsg.Status reply)
        printfn "Queue size: %d with %d active workers" queue.Length active.Count
        active |> Seq.iter (fun (KeyValue(x,y)) -> printfn "%d: %A" x y.Description)
        finished <- queue.Length = 0 && active.IsEmpty
        
    } 