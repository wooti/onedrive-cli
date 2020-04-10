module Logging

type Level = Debug | Info | Warning | Error 

/// Logging interface
type ILogger = 
    abstract member Debug : Printf.StringFormat<'a,unit> -> 'a
    abstract member Info : Printf.StringFormat<'a,unit> -> 'a
    abstract member Warning : Printf.StringFormat<'a,unit> -> 'a
    abstract member Error : Printf.StringFormat<'a,unit> -> 'a

/// Simple logger
let simpleLogger = 
    let print level format = 
        Printf.kprintf (printfn "%s: [%s] - %s" (System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) (level.ToString().ToUpperInvariant())) format
    
    { new ILogger with
        member __.Debug format = print Debug format
        member __.Info format = print Info format
        member __.Warning format = print Warning format
        member __.Error format = print Error format   
    }