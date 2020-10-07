module Hasher

open System
open System.IO
open System.Security.Cryptography
open Domain

// Dumb copy of QuickXorHash.cs, not really idiomatic F# - but it works!
// https://gist.github.com/rgregg/c07a91964300315c6c3e77f7b5b861e4
type private QuickXorHash() =
    inherit HashAlgorithm()

    let bitsInLastCell = 32
    let shift = 11
    let widthInBits = 160

    let mutable _data = Array.zeroCreate ((widthInBits - 1) / 64 + 1)
    let mutable _lengthSoFar = 0L
    let mutable _shiftSoFar = 0

    override __.HashCore(array : byte[], ibStart : int, cbSize : int) = 
        let mutable currentShift = _shiftSoFar
        let mutable vectorArrayIndex = currentShift / 64
        let mutable vectorOffset = currentShift % 64

        for i in 0 .. Math.Min (cbSize, widthInBits) - 1 do
            let isLastCell = vectorArrayIndex = _data.Length - 1
            let bitsInVectorCell = if isLastCell then bitsInLastCell else 64
            if vectorOffset <= bitsInVectorCell - 8 then 
                for j in ibStart + i .. widthInBits .. cbSize + ibStart - 1 do
                    _data.[vectorArrayIndex] <- _data.[vectorArrayIndex] ^^^ ((uint64 array.[j] <<< vectorOffset))
            else 
                let index1 = vectorArrayIndex
                let index2 = if isLastCell then 0 else vectorArrayIndex + 1
                let low = byte (bitsInVectorCell - vectorOffset)
                let mutable xoredByte = 0uy
                for j in ibStart + i .. widthInBits .. cbSize + ibStart - 1 do
                    xoredByte <- xoredByte ^^^ array.[j]
                _data.[index1] <- _data.[index1] ^^^ ((uint64 xoredByte) <<< vectorOffset)
                _data.[index2] <- _data.[index2] ^^^ ((uint64 xoredByte) >>> int low)
            vectorOffset <- vectorOffset + shift
            while (vectorOffset >= bitsInVectorCell) do
                vectorArrayIndex <- if isLastCell then 0 else vectorArrayIndex + 1
                vectorOffset <- vectorOffset - bitsInVectorCell

        _shiftSoFar <- (_shiftSoFar + shift * (cbSize % widthInBits)) % widthInBits
        _lengthSoFar <- _lengthSoFar + int64 cbSize

    override __.HashFinal() = 
        let (rgb : byte[]) = Array.zeroCreate ((widthInBits - 1) / 8 + 1)
        for i in 0 .. _data.Length - 2 do
            Buffer.BlockCopy (BitConverter.GetBytes (_data.[i]), 0, rgb, i * 8, 8)
        
        Buffer.BlockCopy (BitConverter.GetBytes (_data.[_data.Length - 1]), 0, rgb, (_data.Length - 1) * 8, rgb.Length - (_data.Length - 1) * 8)
        
        let lengthBytes = BitConverter.GetBytes (_lengthSoFar)
        System.Diagnostics.Debug.Assert (lengthBytes.Length = 8)
        for i in 0 .. lengthBytes.Length - 1 do
            let idx = (widthInBits / 8) - lengthBytes.Length + i
            rgb.[idx] <- rgb.[idx] ^^^ lengthBytes.[i]
        rgb

    override __.Initialize() = 
        _data <- Array.zeroCreate ((widthInBits - 1) / 64 + 1)
        _shiftSoFar <- 0
        _lengthSoFar <- 0L

    override __.HashSize with get() = widthInBits

type HashType = SHA1 | QuickXOR

type private HashMsg = HashType * FileInfo * AsyncReplyChannel<string>

let private actor = MailboxProcessor<HashMsg>.Start(fun inbox -> 
    let rec loop () = async {
        let! (typ, file, reply) = inbox.Receive ()
        use hashFile = new BufferedStream(file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite), 1024 * 1024 * 1024)
        use hashAlgo : HashAlgorithm = match typ with SHA1 -> SHA1Managed.Create() :> _ | QuickXOR -> new QuickXorHash() :> _
        reply.Reply (hashAlgo.ComputeHash(hashFile) |> System.Convert.ToBase64String)
        return! loop ()
    }
    loop ()
)

let get typ localFile = 
    actor.PostAndAsyncReply (fun reply -> (typ, localFile.FileInfo, reply))
        