module Output

open Konsole

System.Console.CursorVisible <- false

type SplitOutput() = 
    
    let console = new ConcurrentWriter()

    let top = console.SplitTop()
    let bottom = console.SplitBottom()

    member __.header i (text : string) =
        top.PrintAt(0, i, text)

    member __.printfn text =
        bottom.WriteLine(sprintf "%s: %s" (System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) text)
        
let writer = new SplitOutput()