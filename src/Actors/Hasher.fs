module Hasher

open System
open System.IO
open System.Security.Cryptography
open Domain
open System.Diagnostics
open System.Collections

(*
import Foundation


struct QuickXorHash
{
    static private let BLOCK_LEN:Int = 20
    static private let BITS_IN_BYTE:Int = 8
    static private let SHIFT_BITS:Int = 11
    static private let INT64_BYTES_LEN = MemoryLayout<UInt64>.size

    // returns a block with all zero bits.
    static private func zeroUInt8(_ blockLength:Int) -> [UInt8]
    {
        return [UInt8](repeating: 0, count: blockLength)
    }
    
    // returns a block with all zero bits.
    static private func zeroUInt64(_ blockLength:Int) -> [UInt64]
    {
        return [UInt64](repeating: 0, count: blockLength)
    }
    
    // returns a block of all zero bits except for the lower 64 bits which come from i
    // and are in little-endian byte order.
    static private func extend64(_ blockLength:Int,lowerBytes:UInt64) -> [UInt8]
    {
        var block:[UInt8] = zeroUInt8(blockLength)
        
        let len = min(blockLength, MemoryLayout<UInt64>.size /*sizeof(lowerBytes)*/ )
        for i in 0..<len
        {
            block[len-i-1] = UInt8( (lowerBytes >> (BITS_IN_BYTE*i)) & 0xFF )
        }
        return block
    }
    
    static private func rotateLeftByBits(_ block: inout [UInt8], bitsLen:Int)
    {
        if (bitsLen == 0)
        {
            return
        }
        
        var lastCarry:UInt8 = 0
        var byte:UInt8 = 0
        let shiftCarry = BITS_IN_BYTE - bitsLen
        
        for i in 0..<block.count
        {
            byte = block[i]
            block[i] = (byte << bitsLen) | lastCarry
            lastCarry = byte >> shiftCarry
        }
        block[0] = block[0] | lastCarry
    }
    
    static private func xor(_ block: inout [UInt8],otherBlock:[UInt8])
    {
        let len = min(block.count, otherBlock.count)
        for i in 0..<len
        {
            block[i] = block[i] ^ otherBlock[i]
        }
    }
    
    static private func xor(_ block: inout [UInt64],otherBlock:[UInt64])
    {
        let len = min(block.count, otherBlock.count)
        for i in 0..<len
        {
            block[i] = block[i] ^ otherBlock[i]
        }
    }
    
    /// take a pointer to file data formatted in 8 bytes (UInt64) and range in that file
    /// and xor it with the given block, and store it in that block
    /// if the range is smaller than the block size, xor only the range length
    /// if the block size is smaller than the range interval, xor only the block size
    static private func inplaceXor(_ block: inout [UInt64], fileBytes: UnsafePointer<UInt64>, range: Range<Int>)
    {
        let len = min(block.count, range.count)
        var blockIndex = 0
        for i in range.startIndex..<(range.startIndex+len)
        {
            block[blockIndex] = block[blockIndex] ^ ( (fileBytes + i).pointee )
            blockIndex += 1
        }
    }
    
    static private func convertUInt64ArrayToUInt8Array(_ arrayUInt64:[UInt64]) -> [UInt8]
    {
        // init an array big enough to hold the given arrayUInt64 as byte array
        var arrayUInt8 = zeroUInt8(arrayUInt64.count * INT64_BYTES_LEN)
        
        for uint64Index in 0..<arrayUInt64.count
        {
            if (arrayUInt64[uint64Index] == 0) { continue } // skip zeros, our byte array is already zeroed
            
            // for each 8 bytes chunk (UInt64) break it down to 8 signle bytes:
            // 1. shift the 8 bytes to the right (BITS_IN_BYTE * bytePosIndex) bits
            // 2. mask out all other data in the 8 bytes we shifted by AND 0xFF (i.e. 0xE123C9 & 0xFF = 0xC9)
            // 3. cast the result to an byte (UInt8)
            // 4. place the result in the current index in our new byte array
            for bytePosIndex in 0..<INT64_BYTES_LEN
            {
                let uint8Index = ( uint64Index * INT64_BYTES_LEN ) + bytePosIndex
                let bitsToShift = BITS_IN_BYTE * bytePosIndex
                
                arrayUInt8[uint8Index] = UInt8( ( arrayUInt64[uint64Index] >> bitsToShift ) & 0xFF )
            }
        }
        
        return arrayUInt8
    }
    
    // here we will read the file
    // we are reading the file in blocks each block is 160 bytes long, after we read each block we xor it
    // with the pervious block we read (if we just started reading the file we'll xor it with zero)
    // if the file size isn't divisible by 160 then the last 160 byte block we read will be less than 160 bytes
    // so we just xor all the bytes we do have read
    //
    // instead of reading the file byte by byte (which is very slow) we read the file 8 bytes (UInt64) at a time
    // this means that now our 160 byte blocks have a size of 20 (since each block unit is UInt64 - 8 bytes and
    // not UInt8 - one byte) all the calculations of the xor actions and file size will be adjusted accordingly
    //
    // an optimization in the xor operation: when we read the 160 bytes block, 8 bytes at a time, we don't want
    // to copy it to a temporary array and do the xor operation on that, since on large files it will be costly.
    // instead we just take the offset in the file and the length of 8 bytes chunks we want to read and do the
    // xor operation with the actual bytes in the file, since the result of the xor operation is saved in a different
    // UInt64 array the file will be unaffected
    //
    // since we are reading 8 bytes at a time, if the file size isn't divisible by 8 then in the last 8 bytes
    // we might have junk data in some of the bytes, so just before the last xor operation we xero out the
    // data that we know doesn't belong to the file inside the 8 bytes
    static private func readfileIntoXorBlocks(_ fileBytes:Data,fileBytesLen:Int, blockSize:Int) -> [UInt64]
    {
        let bytesPerBlockUnit = blockSize * BITS_IN_BYTE
        
        //init for reading the file in 8 byte chunks (UInt64) at the time
        let blockLength = bytesPerBlockUnit / INT64_BYTES_LEN
        
        let fileData = NSData(data: fileBytes).bytes.assumingMemoryBound(to: UInt64.self)
        
        let fileLength8ByteChunks = (fileBytesLen / INT64_BYTES_LEN)
        
        var hashedFile:[UInt64] = zeroUInt64(blockLength)
        let numberOfFullUnitBlockInFile = (fileLength8ByteChunks / blockLength)
        let numberOfUInt64InLastBlock = (fileLength8ByteChunks % blockLength) + ( (fileBytesLen % INT64_BYTES_LEN) != 0 ? 1 : 0 )
        // xor all blocks of 160 bytes from the file, 8 byte (UInt64) chunks at a time
        for i in 0..<numberOfFullUnitBlockInFile
        {
            let indexOffset = i*blockLength
            // instead of copying 160 bytes to an array and xoring them with hashedFile, we take a pointer to the file data
            // and range of 8 byte chunks we want to xor with hashedFile and save it in hashedFile
            inplaceXor(&hashedFile, fileBytes: fileData, range: indexOffset..<( indexOffset + blockLength ) )
        }
        
        // if the end of the file have less than 160 bytes (20 chunks of 8 bytes), read the left
        // over bytes, and if needed read the last 8 bytes byte-by-byte inorder to not exceed the
        // size of the file
        
        if (numberOfUInt64InLastBlock != 0)
        {
            var fileBlock = zeroUInt64(blockLength)
            let int64IndexOffset = numberOfFullUnitBlockInFile * blockLength
            
            // copy all the 8 byte chunks to a temporary array except the last 8 bytes
            for j in 0..<numberOfUInt64InLastBlock-1
            {
                let boffset = fileData + (int64IndexOffset + j)
                fileBlock[j] = boffset.pointee
            }
            
            // read the last 8 bytes - byte by byte
            let fileBlockOffset = numberOfUInt64InLastBlock-1
            let lastUInt64Offset = int64IndexOffset + fileBlockOffset
            let lastBytesOffset = lastUInt64Offset * INT64_BYTES_LEN
            let lastBytesData = fileBytes.subdata(in: lastBytesOffset..<fileBytes.count)
            
            var lastUInt64:UInt64 = 0
            
            // merge all available bytes to UInt64
            for j in 0..<lastBytesData.count
            {
                let byteToUInt64 = ( UInt64(lastBytesData[j]) ) << (j * BITS_IN_BYTE)
                lastUInt64 = lastUInt64 | byteToUInt64
            }
            // add the last UInt64 to the block
            fileBlock[ fileBlockOffset ] = lastUInt64
            
            // xor the last block from the file
            xor(&hashedFile,otherBlock: fileBlock)
        }
        
        return hashedFile
    }
    
    
    // after we read the file and get back a block of 160 xored bytes out of it we need to
    // correctly shift each bytes to its correct position inside the 160 bytes we have
    // we need to do that since although it is correct that each 160th byte in the file is xores
    // together we still have to move those xored bytes to their corrent position inside that 160
    // byte block so we can divide it to 8 blocks of 20 bytes and xor them to get the final result
    // of this algorithem
    //
    // to approuch this we first notice that each byte in the 160 byte block could only be shifted
    // between 0 to 7 bits to the left (if there was a byte that shifted more than 7 bits then it would
    // have been xored with other bytes when we read the file, that is due to the fact that 11 is a generator
    // of group Z160 { 11 * i mod 160 | i in N} = Z160)
    //
    // since we know that each byte has a 0..7 bits to be shifted and each byte has it own unique position
    // in the 160 byte block (since 11 is a generator) we can group each byte according to how many bits
    // it needs to be shifted to be in the correct position, (i.e. group index 0 will have all the bytes
    // shouldn'r be shifted, group index 1 will have all the bytes that need to move one bit etc.)
    //
    // after we created these 8 groups of bytes (each one of the in the size of a block ,which is 20 bytes
    // long) we can simply shift each block according to it's index and get all the bytes in their correct
    // position
    //
    // finally to get the final block we only need to xor the 8 groups we created and we're done, all the
    // bytes in the 160 byte block are xored in their correct position
    static private func xorHash0(_ fileBytes:Data,fileBytesLen:Int, blockSize:Int) -> [UInt8]
    {
        let bytesPerBlockUnit = blockSize * BITS_IN_BYTE
        
        let hashedFile = readfileIntoXorBlocks(fileBytes, fileBytesLen: fileBytesLen, blockSize: blockSize)
        
        // convert int64 file block to byte file block
        var hashedByteBlock = convertUInt64ArrayToUInt8Array(hashedFile)

        // arrange all the 160 bytes in 8 blocks, each block index according to it's bit shift over byte alignment
        var blocksToHash:[[UInt8]] = Array(repeating: Array(repeating: 0, count: blockSize), count: 8)
        for i in 0..<bytesPerBlockUnit
        {
            let originalBytePos = (i * SHIFT_BITS) % bytesPerBlockUnit
            let missalignedBits = originalBytePos % BITS_IN_BYTE
            let byteAlignIndex = originalBytePos / BITS_IN_BYTE
            blocksToHash[missalignedBits][byteAlignIndex] = hashedByteBlock[i]
        }
        
        // shift each block by it's index to get the correct position of bytes and xor all the blocks
        var returnBlock = blocksToHash[0]
        for i in 1..<blocksToHash.count
        {
            rotateLeftByBits(&blocksToHash[i], bitsLen: i)
            xor(&returnBlock, otherBlock:blocksToHash[i])
        }
        
        return returnBlock.reversed()
    }
    
    
    static private func xorHash(_ fileBytes:Data, fileBytesLen:Int, blockSize:Int) -> [UInt8]
    {
        var extendedLength = extend64(blockSize, lowerBytes: UInt64(fileBytesLen))
        xor(&extendedLength, otherBlock: xorHash0(fileBytes, fileBytesLen:fileBytesLen, blockSize: blockSize))
        return extendedLength
    }
    
    static public func generate(_ fileURL:URL) -> [UInt8]
    {
        let BLOCK_SIZE:Int = 20
        guard let fileData = try? Data(contentsOf: fileURL) else {
            return zeroUInt8(BLOCK_SIZE)
        }

        return xorHash(fileData, fileBytesLen:fileData.count ,blockSize: BLOCK_SIZE)
    }
}

*)

// Dumb copy of QuickXorHash.cs, not really idiomatic F# - but it works!
// https://gist.github.com/rgregg/c07a91964300315c6c3e77f7b5b861e4
type QuickXorHash() =
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

type SuperQuickXorHash () = 

    let BLOCK_LEN = 20
    let BITS_IN_BYTE = 8
    let SHIFT_BITS = 11

    let longToBlock (long : int64) =
        let bytes = BitConverter.GetBytes long
        Array.append (Array.zeroCreate (BLOCK_LEN - bytes.Length)) bytes

    let rotateLeft bitsLen (block : byte[]) = 
        if bitsLen = 0 then
            block
        else
            let mutable (lastCarry : System.Byte) = 0uy
            let shiftCarry = BITS_IN_BYTE - bitsLen

            for i in 0 .. block.Length - 1 do
                let mutable (b : System.Byte) = block.[i]
                block.[i] <- (b <<< bitsLen ||| lastCarry)
                lastCarry <- (b >>> shiftCarry)

            block.[0] <- block.[0] ||| lastCarry
            block

    let xor (block : byte[]) (otherBlock : byte[]) = 
        let foo = new BitArray(block)
        foo.Xor(new BitArray(otherBlock)).CopyTo(block, 0)

    let chunkUp size (stream : Stream) = seq {
        let mutable reading = true
        while reading do
            let fileBytes = Array.zeroCreate size
            let cnt = stream.Read(fileBytes, 0, size)
            yield fileBytes
            reading <- cnt = size
    }

    let readFileInBlocks (stream : Stream) = 
        let blockLength = BLOCK_LEN * BITS_IN_BYTE
        let hashedFile = Array.zeroCreate blockLength
        for chunk in chunkUp blockLength stream do
            xor hashedFile chunk
        hashedFile

    let hashInternal (stream : Stream) = 
        let blockLength = BLOCK_LEN * BITS_IN_BYTE
        let hashedFile = readFileInBlocks stream
        
        let blocksToHash = Array2D.zeroCreate 8 20

        for i in 0 .. blockLength - 1 do
            let originalBytePos = i * SHIFT_BITS % blockLength
            let missalignedBits = originalBytePos % BITS_IN_BYTE
            let byteAlignIndex = originalBytePos / BITS_IN_BYTE
            blocksToHash.[missalignedBits,byteAlignIndex] <- hashedFile.[i]

        let arrayLine line mda =
            [for i in 0 .. Array2D.length2 mda - 1 do yield mda.[line,i]] |> List.toArray

        let returnBlock = blocksToHash |> arrayLine 0
        for i in 1 .. Array2D.length1 blocksToHash - 1 do
            xor returnBlock (blocksToHash |> arrayLine i |> rotateLeft i)

        returnBlock

    member __.GetHashForFile (file : FileInfo) = 
        let hashResult = longToBlock file.Length
        use stream = new BufferedStream((file.OpenRead ()), 1024 * 1024)
        xor hashResult (hashInternal stream)
        Convert.ToBase64String hashResult

    member __.GetHashForBytes (bytes : byte array) = 
        let hashResult = longToBlock bytes.LongLength
        use stream = new MemoryStream(bytes)
        xor hashResult (hashInternal stream)
        Convert.ToBase64String hashResult

type HashType = SHA1 | QuickXOR

type private HashMsg = HashType * FileInfo * AsyncReplyChannel<string>

let private actor = MailboxProcessor<HashMsg>.Start(fun inbox -> 
    let rec loop () = async {
        let! (typ, file, reply) = inbox.Receive ()
        let hashFile = file.OpenRead()
        let hashAlgo : HashAlgorithm = match typ with SHA1 -> SHA1Managed.Create() :> _ | QuickXOR -> new QuickXorHash() :> _
        Debug.WriteLine(file.FullName)
        reply.Reply (hashAlgo.ComputeHash(hashFile) |> System.Convert.ToBase64String)
        return! loop ()
    }
    loop ()
)

let get typ localFile = 
    actor.PostAndAsyncReply (fun reply -> (typ, localFile.FileInfo, reply))
        