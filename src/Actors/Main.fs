module OneDriveCLI.Actors.Main

open OneDriveCLI.Core.Domain
open OneDriveCLI.Modules
open OneDriveCLI.Core.OneDriveAPI
open Worker

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

let initialise threads (api : OneDriveAPIClient) direction dryRun localPath = 
    [1 .. threads] |> Seq.iter (Worker.start api direction dryRun localPath queueJob tryGetJob)

let runToCompletion () = async {
    
    let rec reportWhileRunning () = async {
        do! Async.Sleep 500
        let! queue, workers = processor.PostAndAsyncReply (fun reply -> ProcessMsg.Status reply)
        let! status = Collector.get ()
        let active = workers |> Map.toSeq |> Seq.choose snd |> Seq.length
        Output.writer.header 0 (sprintf "Queue size: %d with %d/%d active workers " queue.Length active workers.Count)
        Output.writer.header 1 (sprintf "Downloaded Files: %d (%s), Uploaded Files %d (%s), Unchanged Files: %d " status.DownloadedFiles (status.DownloadedBytes |> Output.toReadableSize) status.UploadedFiles (status.UploadedBytes |> Output.toReadableSize) status.UnchangedFiles)
        workers |> Seq.iteri (fun i (KeyValue(x,y)) -> Output.writer.header (i + 2) (sprintf "%-3d: %s" x (y |> Option.map (fun j -> j.Description) |> Option.defaultValue "<Idle>")))
        
        if queue.Length > 0 || active > 0 then 
            do! reportWhileRunning ()
    }

    let sw = System.Diagnostics.Stopwatch.StartNew ()
    do! reportWhileRunning ()
    sw.Stop ()
    
    // Print final status
    let! status = Collector.get ()
    Output.writer.dprintfn "All done..."
    Output.writer.dprintfn "Downloaded Files: %d (%s)" status.DownloadedFiles (status.DownloadedBytes |> Output.toReadableSize)
    Output.writer.dprintfn "Uploaded Files %d (%s)" status.UploadedFiles (status.UploadedBytes |> Output.toReadableSize)
    Output.writer.dprintfn "Unchanged Files: %d" status.UnchangedFiles
    Output.writer.dprintfn "Time Taken: %O" sw.Elapsed
}