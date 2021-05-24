module OneDriveCLI.Actors.Collector

open OneDriveCLI.Core.Domain

type CollectorStatus = {
    UploadedFiles : int
    UploadedFolders : int
    UploadedBytes : int64
    DownloadedFiles : int
    DownloadedFolders : int
    DownloadedBytes : int64
    UnchangedFolders : int
    UnchangedFiles : int
    ExtraLocalFiles : int
    ExtraRemoteFiles : int
    IgnoredFiles : int
}

type CollectorReport =
    | Download of RemoteItem
    | Upload of LocalItem
    | Same of LocalItem * RemoteItem
    | Ignored of LocalItem option * RemoteItem option
    | Extra of Item

type private CollectorMsg =
    | Report of CollectorReport
    | Get of AsyncReplyChannel<CollectorStatus>

let private collector = MailboxProcessor.Start(fun inbox -> 

    let rec loop s = async {
        let! message = inbox.Receive ()
        
        let newStatus = 
            match message with 
            | Report (Download (RemoteFile d)) -> {s with DownloadedFiles = s.DownloadedFiles + 1; DownloadedBytes = s.DownloadedBytes + d.Length }
            | Report (Download (RemoteFolder d)) -> {s with DownloadedFolders = s.DownloadedFolders + 1}
            | Report (Upload (LocalFile d)) -> {s with UploadedFiles = s.UploadedFiles + 1; UploadedBytes = s.UploadedBytes + d.FileInfo.Length}
            | Report (Upload (LocalFolder d)) -> {s with UploadedFolders = s.UploadedFolders + 1}
            | Report (Same (LocalFolder d, _)) -> {s with UnchangedFolders = s.UnchangedFolders + 1}
            | Report (Same (LocalFile d, _)) -> {s with UnchangedFiles = s.UnchangedFiles + 1}
            | Report (Extra (Local _)) -> {s with ExtraLocalFiles = s.ExtraLocalFiles + 1}
            | Report (Extra (Remote _)) -> {s with ExtraRemoteFiles = s.ExtraRemoteFiles + 1}
            | Report (Ignored _) -> {s with IgnoredFiles = s.IgnoredFiles + 1}
            | Get r ->
                r.Reply s
                s

        return! loop newStatus
    }

    let status = {UploadedFiles = 0; UploadedFolders = 0; UploadedBytes = 0L; DownloadedFiles = 0; DownloadedFolders = 0; DownloadedBytes = 0L; UnchangedFiles = 0; UnchangedFolders = 0; ExtraLocalFiles = 0; ExtraRemoteFiles = 0; IgnoredFiles = 0}

    loop status
)

let report report =
    collector.Post (Report(report))

let get () = 
    collector.PostAndAsyncReply (fun reply -> Get(reply))
        