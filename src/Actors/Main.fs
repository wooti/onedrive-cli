module OneDriveCLI.Actors.Main

open OneDriveCLI.Core.Domain
open OneDriveCLI.Modules

type WorkerID = int

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

let runToCompletion () = async {
    let mutable finished = false

    while not finished do
        do! Async.Sleep 500
        let! queue, active = processor.PostAndAsyncReply (fun reply -> ProcessMsg.Status reply)
        let! status = Collector.get ()
        Output.writer.header 0 (sprintf "Queue size: %d with %d active workers " queue.Length active.Count)
        Output.writer.header 1 (sprintf "Downloaded Files: %d (%d KB), Uploaded Files %d (%d KB), Unchanged Files: %d " status.DownloadedFiles (status.DownloadedBytes / 1024L) status.UploadedFiles (status.UploadedBytes / 1024L) status.UnchangedFiles)
        active |> Seq.iteri (fun i (KeyValue(x,y)) -> Output.writer.header (i + 2) (sprintf "%d: %A" x y.Description))
        finished <- queue.Length = 0 && active.IsEmpty
    } 