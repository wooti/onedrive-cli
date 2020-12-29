module OneDriveCLI.Modules.CommandLine

open Argu

type Direction =
    | Up
    | Down

type Arguments = {
    Direction : Direction
    Local : string option
    Remote : string option
    Ignore : string list
    IgnoreFile : string option
    Threads : int option
    DryRun : bool
    Verbose : bool
}

type private CliArguments =
    | [<Mandatory; Unique>] Direction of Direction
    | [<Unique>] Local of string
    | [<Unique>] Remote of string
    | Ignore of string
    | [<Unique>] Ignore_File of string
    | [<Unique; AltCommandLine("-t")>] Threads of int
    | [<AltCommandLine("-n")>] Dry_Run
    | [<AltCommandLine("-v")>] Verbose
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Direction _ -> "direction of sync <up|down>."
            | Local _ -> "local root path."
            | Remote _ -> "remote root path."
            | Ignore _ -> "ignore file glob"
            | Ignore_File _ -> "file containing globs to ignore"
            | Threads _ -> "number of threads to run on"
            | Dry_Run _ -> "don't make any changes"
            | Verbose _ -> "verbose output"

let doParse args = 

    let parser = ArgumentParser.Create<CliArguments>()
    let results = parser.Parse args

    {
        Direction = results.GetResult Direction
        Local = results.TryGetResult Local
        Remote = results.TryGetResult Remote
        Ignore = results.GetResults Ignore
        IgnoreFile = results.TryGetResult Ignore_File
        Threads = results.TryGetResult Threads
        DryRun = results.Contains Dry_Run
        Verbose = results.Contains Verbose
    }
