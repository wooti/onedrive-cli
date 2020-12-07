module OneDriveCLI.Modules.Output

open Konsole
open System.IO

System.Console.CursorVisible <- false

type MyWriter(c : IConsole) =
    inherit TextWriter()
    override x.Encoding = stdout.Encoding
    override x.Write (s:string) = c.Write s
    override x.WriteLine (s:string) = c.WriteLine s
    override x.WriteLine() = c.WriteLine ""

type SplitOutput() = 
    
    let console = new ConcurrentWriter()

    let top = console.SplitTop()
    let bottom = new MyWriter(console.SplitBottom())

    let ws = new string(Seq.replicate console.WindowWidth ' ' |> Seq.toArray)

    member __.header i (text : string) =
        top.PrintAt(0, i, (text + ws).Substring(0, console.WindowWidth))

    member __.printfn fmt =
        Printf.kprintf (fun s -> fprintfn bottom "%s: %s" (System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) s) fmt
        
let writer = new SplitOutput()