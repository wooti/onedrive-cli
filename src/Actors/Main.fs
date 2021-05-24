module OneDriveCLI.Actors.Main

open OneDriveCLI.Core.Domain
open OneDriveCLI.Utilities

type private ProcessMsg = 
    | Job of Job
    | Status of AsyncReplyChannel<Job array * Map<WorkerID, Job option>>
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
                    activeWorkers |> Map.add workerID (Some item)
                | false, _ -> 
                    replyChannel.Reply None
                    activeWorkers |> Map.add workerID None

        return! loop workersNow
    }
    loop Map.empty)

let queueJob job =
    job |> ProcessMsg.Job |> processor.Post

let tryGetJob workerID = 
    processor.PostAndAsyncReply (fun reply -> RequestJob (workerID, reply))

let initialise threads args = 
    [1 .. threads] |> Seq.iter (Worker.start args queueJob tryGetJob)

let runToCompletion () = async {
    
    let rec reportWhileRunning () = async {
        do! Async.Sleep 500
        let! queue, workers = processor.PostAndAsyncReply (fun reply -> ProcessMsg.Status reply)
        let! status = Collector.get ()
        let active = workers |> Map.toSeq |> Seq.choose snd |> Seq.length
        Output.writer.header 0 (sprintf "Queue size: %d with %d/%d active workers " queue.Length active workers.Count)
        Output.writer.header 1 (sprintf "Downloaded Files: %d (%s), Uploaded Files %i (%s), Unchanged Files: %i, Extra Files: %i, Ignored Files %i " status.DownloadedFiles (status.DownloadedBytes |> Output.toReadableSize) status.UploadedFiles (status.UploadedBytes |> Output.toReadableSize) status.UnchangedFiles (status.ExtraLocalFiles + status.ExtraRemoteFiles) status.IgnoredFiles)
        workers |> Seq.iteri (fun i (KeyValue(x,y)) -> Output.writer.header (i + 2) (sprintf "%-3d: %s" x (y |> Option.map (fun j -> j.Description) |> Option.defaultValue "<Idle>")))
        
        if queue.Length > 0 || active > 0 then 
            do! reportWhileRunning ()
    }

    let sw = System.Diagnostics.Stopwatch.StartNew ()
    do! reportWhileRunning ()
    sw.Stop ()
    
    // Print final status
    let! status = Collector.get ()
    Output.writer.printfn "All done..."
    Output.writer.printfn "Downloaded Files: %i (%s)" status.DownloadedFiles (status.DownloadedBytes |> Output.toReadableSize)
    Output.writer.printfn "Uploaded Files %i (%s)" status.UploadedFiles (status.UploadedBytes |> Output.toReadableSize)
    Output.writer.printfn "Unchanged Files: %i" status.UnchangedFiles
    Output.writer.printfn "Extra Files: %i local, %i remote" status.ExtraLocalFiles status.ExtraRemoteFiles
    Output.writer.printfn "Ignored Files: %i" status.IgnoredFiles
    Output.writer.printfn "Time Taken: %O" sw.Elapsed
    Output.writer.flush ()
}