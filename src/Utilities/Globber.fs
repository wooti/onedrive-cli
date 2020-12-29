module OneDriveCLI.Utilities.Globber

open Microsoft.Extensions.FileSystemGlobbing
open System.IO

type IgnoreGlobber(ignores, ignoreFile) = 

    let matcher = new Matcher()

    do
        ignores |> List.iter (matcher.AddInclude >> ignore)
        ignoreFile |> Option.iter (fun f -> f |> File.ReadAllLines |> Array.iter (matcher.AddInclude >> ignore))

    member __.IsIgnored (path : string) =
        let result = matcher.Match path
        result.HasMatches