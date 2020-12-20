module OneDriveCLI.Modules.Output

open Konsole
open System.IO

System.Console.CursorVisible <- false

type SplitOutput() = 
    
    let console = new ConcurrentWriter()

    let top = console.SplitTop()
    let bottom = console.SplitBottom()

    let ws = new string(Seq.replicate console.WindowWidth ' ' |> Seq.toArray)

    let threadSafeWriter = MailboxProcessor.Start(fun inbox -> 
    
        let rec loop () = async {
            let! (message : string) = inbox.Receive ()
            bottom.WriteLine message
    
            return! loop ()
        }

        loop ()
    )

    member __.header i (text : string) =
        top.PrintAt(0, i, (text + ws).Substring(0, console.WindowWidth))

    /// Thread safe printing of output
    member __.printfn fmt =
        let doAfter s = 
            sprintf "%s: %s" (System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) s |> threadSafeWriter.Post

        Printf.kprintf doAfter fmt 

    /// Direct printing of output
    member __.dprintfn fmt =
        let doAfter s = 
            sprintf "%s: %s" (System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) s |> bottom.WriteLine

        Printf.kprintf doAfter fmt 

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