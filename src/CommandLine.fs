module CommandLine

open Argu

type Direction =
    | Up
    | Down

type Arguments = {
    Direction : Direction
    Local : string option
    Remote : string option
    DryRun : bool
    Recursive : bool
    Verbose : bool
}

type private CliArguments =
    | [<Mandatory; Unique>] Direction of Direction
    | [<Unique>] Local of string
    | [<Unique>] Remote of string
    | [<AltCommandLine("-n")>] Dry_Run
    | [<AltCommandLine("-r")>] Recursive
    | [<AltCommandLine("-v")>] Verbose
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Direction _ -> "direction of sync <up|down>."
            | Local _ -> "local root path."
            | Remote _ -> "remote root path."
            | Dry_Run _ -> "don't make any changes"
            | Recursive _ -> "recurse into directories"
            | Verbose _ -> "verbose output"

let doParse args = 

    let parser = ArgumentParser.Create<CliArguments>(programName = "onedrive-cli.exe")
    let results = parser.Parse args

    {
        Direction = results.GetResult Direction
        Local = results.TryGetResult Local
        Remote = results.TryGetResult Remote
        DryRun = results.Contains Dry_Run
        Recursive = results.Contains Recursive
        Verbose = results.Contains Verbose
    }
