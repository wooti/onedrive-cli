module OneDriveCLI.Utilities.Hasher

open System
open System.IO
open System.Security.Cryptography
open System.Collections

let BLOCK_LEN = 20
let BITS_IN_BYTE = 8
let SHIFT_BITS = 11

let private xor (block : byte[]) (otherBlock : byte[]) = 
    BitArray(block).Xor(BitArray(otherBlock)).CopyTo(block, 0)

let private hashInternal (stream : Stream) = 

    let chunkUp size (stream : Stream) = seq {
        let mutable reading = true
        while reading do
            let fileBytes = Array.zeroCreate size
            let cnt = stream.Read(fileBytes, 0, size)
            yield fileBytes
            reading <- cnt = size
    }

    let blockLength = BLOCK_LEN * BITS_IN_BYTE
    let hashedFile = Array.zeroCreate blockLength
    for chunk in chunkUp blockLength stream do xor hashedFile chunk
        
    let blocksToHash = Array2D.zeroCreate 8 20

    for i in 0 .. blockLength - 1 do
        let originalBytePos = i * SHIFT_BITS % blockLength
        let missalignedBits = originalBytePos % BITS_IN_BYTE
        let byteAlignIndex = originalBytePos / BITS_IN_BYTE
        blocksToHash.[missalignedBits,byteAlignIndex] <- hashedFile.[i]

    let arrayLine line mda =
        [for i in 0 .. Array2D.length2 mda - 1 do yield mda.[line,i]] |> List.toArray

    let rotateLeft bitsLen (block : byte[]) = 
        if bitsLen = 0 then
            block
        else
            let mutable (lastCarry : System.Byte) = 0uy
            let shiftCarry = BITS_IN_BYTE - bitsLen

            for i in 0 .. block.Length - 1 do
                let b = block.[i]
                block.[i] <- (b <<< bitsLen ||| lastCarry)
                lastCarry <- (b >>> shiftCarry)

            block.[0] <- block.[0] ||| lastCarry
            block

    let returnBlock = blocksToHash |> arrayLine 0
    for i in 1 .. Array2D.length1 blocksToHash - 1 do
        xor returnBlock (blocksToHash |> arrayLine i |> rotateLeft i)

    returnBlock

/// Implementation of Microsoft's QuickXOR hash
let private getHash (stream : Stream) length = 

    let longToBlock (long : int64) =
        let bytes = BitConverter.GetBytes long
        Array.append (Array.zeroCreate (BLOCK_LEN - bytes.Length)) bytes

    let hashResult = longToBlock length
    xor hashResult (hashInternal stream)
    Convert.ToBase64String hashResult

type HashType = SHA1 | QuickXOR

let generateHash typ (file : FileInfo) = 
    use hashFile = new BufferedStream(file.OpenRead(), 1024 * 1024)
    match typ with 
    | SHA1 -> 
        let hasher = SHA1Managed.Create()
        hasher.ComputeHash hashFile |> System.Convert.ToBase64String
    | QuickXOR -> 
        getHash hashFile file.Length
        