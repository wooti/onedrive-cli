module OneDriveCLI.Utilities.Output

open Konsole

System.Console.CursorVisible <- false

type private ThreadSafeWriterMessage =
    | Write of string
    | Flush of AsyncReplyChannel<unit>

type SplitOutput() = 
    
    let console = new ConcurrentWriter()

    let top = console.SplitTop()
    let bottom = console.SplitBottom()

    let ws = new string(Seq.replicate console.WindowWidth ' ' |> Seq.toArray)

    let threadSafeWriter = MailboxProcessor.Start(fun inbox -> 
    
        let rec loop () = async {
            let! (message : ThreadSafeWriterMessage) = inbox.Receive ()
            match message with
            | Write msg -> bottom.WriteLine msg
            | Flush r -> r.Reply ()
    
            return! loop ()
        }

        loop ()
    )

    member __.header i (text : string) =
        top.PrintAt(0, i, (text + ws).Substring(0, console.WindowWidth))

    /// Thread safe printing of output
    member __.printfn fmt =
        let doAfter s = 
            sprintf "%s: %s" (System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) s 
            |> ThreadSafeWriterMessage.Write 
            |> threadSafeWriter.Post

        Printf.kprintf doAfter fmt

    member __.flush () = 
        threadSafeWriter.PostAndReply Flush

let writer = new SplitOutput()

let toReadableSize (bytes : int64) = 
    let kb = 1024m
    let mb = kb * 1024m
    let gb = mb * 1024m

    let size, unit = 
        let size = bytes |> decimal
        if size > gb then size / gb, "GB"
        else if size > mb then size / mb, "MB"
        else if size > kb then size / kb, "KB"
        else size, "bytes"

    sprintf "%.2f %s" size unit