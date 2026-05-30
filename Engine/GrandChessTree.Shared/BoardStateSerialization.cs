using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared
{
    public static class BoardStateSerialization
    {
        public const int StructSize = 26;

        // Serialize the struct into a Base64 string
        public static string Serialize(this ref Board board, bool whiteToMove)
        {
            Span<byte> buffer = stackalloc byte[StructSize];
            buffer.Clear();
            WriteToSpan(ref board, buffer, whiteToMove);
            return Convert.ToBase64String(buffer);
        }

        public static byte[] ToByteArray(this ref Board board, bool whiteToMove)
        {
            var buffer = new byte[StructSize];
            WriteToSpan(ref board, buffer, whiteToMove);
            return buffer;
        }
        public static (Board board, bool whiteToMove) FromByteArray(byte[] buffer)
        {
            return ReadFromSpan(buffer);
        }
        // Deserialize from a Base64 string
        public static (Board board, bool whiteToMove) Deserialize(string base64)
        {
            Span<byte> buffer = Convert.FromBase64String(base64);
            return ReadFromSpan(buffer);
        }

        private enum PieceType : byte
        {
            None = 0,
            WhitePawn = 1,
            WhiteKnight = 2,
            WhiteBishop = 3,
            WhiteRook = 4,
            WhiteQueen = 5,
            WhiteKing = 6,
            BlackPawn = 7,
            BlackKnight = 8,
            BlackBishop = 9,
            BlackRook = 10,
            BlackQueen = 11,
            BlackKing = 12,
        }

        private static PieceType GetPieceType(this ref Board board, ulong occupiedSquare)
        {
            // Check if the square is occupied by a white piece.
            if ((board.White & occupiedSquare) != 0)
            {
                if ((board.Pawn & occupiedSquare) != 0)
                    return PieceType.WhitePawn;
                if ((board.Knight & occupiedSquare) != 0)
                    return PieceType.WhiteKnight;
                if ((board.Bishop & occupiedSquare) != 0)
                    return PieceType.WhiteBishop;
                if ((board.Rook & occupiedSquare) != 0)
                    return PieceType.WhiteRook;
                if ((board.Queen & occupiedSquare) != 0)
                    return PieceType.WhiteQueen;
                return PieceType.WhiteKing; // if none of the above, it must be the king.
            }
            else
            {
                // Otherwise, it’s a black piece.
                if ((board.Pawn & occupiedSquare) != 0)
                    return PieceType.BlackPawn;
                if ((board.Knight & occupiedSquare) != 0)
                    return PieceType.BlackKnight;
                if ((board.Bishop & occupiedSquare) != 0)
                    return PieceType.BlackBishop;
                if ((board.Rook & occupiedSquare) != 0)
                    return PieceType.BlackRook;
                if ((board.Queen & occupiedSquare) != 0)
                    return PieceType.BlackQueen;
                return PieceType.BlackKing;
            }
        }

        // Write the board state into a 26-byte span.
        //
        // Bytes 0..15 (piece data): per-square nibbles in pop-LSB occupancy order.
        //   Each byte holds two 4-bit nibbles: [piece2 (high)][piece1 (low)].
        //   Piece codes: 1..6 = White {Pawn,Knight,Bishop,Rook,Queen,King},
        //                7..12 = Black equivalents.
        // Bytes 16..23: occupancy bitboard, little-endian u64.
        // Byte 24: high nibble = EnPassantFile, low nibble = CastleRights.
        // Byte 25: side-to-move (0 = white, 1 = black).
        //
        // Fast path uses BMI2 PEXT/PDEP to construct the nibble stream branch-free.
        // For each piece type, PEXT(piece, occupancy) yields a packed bit vector
        // (bit i = 1 iff the i-th occupied square in pop-LSB order has that piece).
        // PDEP into 0x1111…1 nibble slots places those bits into per-nibble low bit.
        // Multiplying by the piece-type code (1..6) populates the nibble's low bits;
        // ORing the six pieces' contributions gives the piece-kind nibble (each
        // square has exactly one piece type). Finally, +6 added to black-piece
        // nibbles via a PDEP-of-black-bits * 6, addition non-carrying because
        // each nibble's max becomes 12 (0b1100) < 16.
        public static void WriteToSpan(ref Board board, Span<byte> span, bool whiteToMove)
        {
            if (span.Length < 26)
                throw new ArgumentException("Span must be at least 26 bytes.");

            ulong occupancy = board.White | board.Black;

            if (Bmi2.X64.IsSupported)
            {
                int popCount = BitOperations.PopCount(occupancy);
                ulong kingBitboard = (1UL << board.WhiteKingPos) | (1UL << board.BlackKingPos);

                // PEXT each piece-type bitboard at occupancy positions.
                ulong pawnBits   = Bmi2.X64.ParallelBitExtract(board.Pawn,   occupancy);
                ulong knightBits = Bmi2.X64.ParallelBitExtract(board.Knight, occupancy);
                ulong bishopBits = Bmi2.X64.ParallelBitExtract(board.Bishop, occupancy);
                ulong rookBits   = Bmi2.X64.ParallelBitExtract(board.Rook,   occupancy);
                ulong queenBits  = Bmi2.X64.ParallelBitExtract(board.Queen,  occupancy);
                ulong kingBits   = Bmi2.X64.ParallelBitExtract(kingBitboard, occupancy);
                ulong whiteBits  = Bmi2.X64.ParallelBitExtract(board.White,  occupancy);

                const ulong nibbleMask = 0x1111111111111111UL;

                // Lower 16 occupied squares → bytes 0..7
                ulong low = Bmi2.X64.ParallelBitDeposit(pawnBits,   nibbleMask) * 1UL
                          | Bmi2.X64.ParallelBitDeposit(knightBits, nibbleMask) * 2UL
                          | Bmi2.X64.ParallelBitDeposit(bishopBits, nibbleMask) * 3UL
                          | Bmi2.X64.ParallelBitDeposit(rookBits,   nibbleMask) * 4UL
                          | Bmi2.X64.ParallelBitDeposit(queenBits,  nibbleMask) * 5UL
                          | Bmi2.X64.ParallelBitDeposit(kingBits,   nibbleMask) * 6UL;
                // Color: nibble i += 6 iff square i is black.
                int validLow = popCount < 16 ? popCount : 16;
                ulong validMaskLow = validLow == 64 ? ~0UL : (1UL << validLow) - 1;
                ulong blackBitsLow = ~whiteBits & validMaskLow;
                low += Bmi2.X64.ParallelBitDeposit(blackBitsLow, nibbleMask) * 6UL;

                BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(0, 8), low);

                // Upper 16 occupied squares (if any) → bytes 8..15
                ulong high = 0;
                if (popCount > 16)
                {
                    ulong pawnHi   = pawnBits   >> 16;
                    ulong knightHi = knightBits >> 16;
                    ulong bishopHi = bishopBits >> 16;
                    ulong rookHi   = rookBits   >> 16;
                    ulong queenHi  = queenBits  >> 16;
                    ulong kingHi   = kingBits   >> 16;
                    ulong whiteHi  = whiteBits  >> 16;

                    high = Bmi2.X64.ParallelBitDeposit(pawnHi,   nibbleMask) * 1UL
                         | Bmi2.X64.ParallelBitDeposit(knightHi, nibbleMask) * 2UL
                         | Bmi2.X64.ParallelBitDeposit(bishopHi, nibbleMask) * 3UL
                         | Bmi2.X64.ParallelBitDeposit(rookHi,   nibbleMask) * 4UL
                         | Bmi2.X64.ParallelBitDeposit(queenHi,  nibbleMask) * 5UL
                         | Bmi2.X64.ParallelBitDeposit(kingHi,   nibbleMask) * 6UL;
                    int validHi = popCount - 16;
                    ulong validMaskHi = validHi == 64 ? ~0UL : (1UL << validHi) - 1;
                    ulong blackBitsHi = ~whiteHi & validMaskHi;
                    high += Bmi2.X64.ParallelBitDeposit(blackBitsHi, nibbleMask) * 6UL;
                }
                BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8, 8), high);
            }
            else
            {
                // Fallback: iterate occupancy bitboard and call GetPieceType per square.
                ulong occ = occupancy;
                int bufferIndex = 0;
                // Zero the piece-data region first; the original loop may leave
                // trailing bytes untouched when popCount < 32.
                span.Slice(0, 16).Clear();
                while (occ != 0)
                {
                    byte pieceType1 = (byte)board.GetPieceType(1UL << occ.PopLSB());
                    byte pieceType2 = occ != 0
                        ? (byte)board.GetPieceType(1UL << occ.PopLSB())
                        : (byte)0;
                    span[bufferIndex++] = (byte)((pieceType2 << 4) | pieceType1);
                }
            }

            // Bytes 16..23: occupancy bitboard, little-endian.
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(16, 8), occupancy);

            // Byte 24: high nibble = EnPassantFile (0..8), low nibble = CastleRights.
            span[24] = (byte)(
              ((((byte)board.EnPassantFile) & 0xF) << 4) |
              (((byte)board.CastleRights) & 0xF)
          );

            // Byte 25: side-to-move (0 = white, 1 = black).
            span[25] = (byte)(whiteToMove ? 0 : 1);
        }

        // Read the board state and whiteToMove flag from a 26-byte span.
        public static (Board board, bool whiteToMove) ReadFromSpan(ReadOnlySpan<byte> span)
        {
            if (span.Length < 26)
                throw new ArgumentException("Compressed board state must be at least 26 bytes.");

            Board board = default;

            // 1. Read piece data (16 bytes).
            ReadOnlySpan<byte> pieceData = span.Slice(0, 16);

            // 2. Read occupancy bitboard (8 bytes) from offset 16.
            ulong occupancy = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));

            // 3. Read en passant and castle rights from byte at offset 24.
            byte flags = span[24];
            // High nibble (bits 7-4): en passant file.
            byte enPassantFile = (byte)((flags >> 4) & 0xF);
            // Low nibble (bits 3-0): castle rights.
            CastleRights castleRights = (CastleRights)(flags & 0xF);
            board.CastleRights = castleRights;
            board.EnPassantFile = enPassantFile;

            // 4. Read whiteToMove flag from byte at offset 25.
            //    0 means white to move, 1 means black.
            bool whiteToMove = (span[25] == 0);

            // 5. Decode piece information.
            //    The pieces were stored in the order in which occupancy bits were popped.
            //    When decoding, iterate over squares 0-63 in increasing order.
            int pieceIndex = 0; // Index into the sequence of nibbles (0..31)
            for (int square = 0; square < 64; square++)
            {
                ulong mask = 1UL << square;
                if ((occupancy & mask) != 0)
                {
                    // Determine which nibble to read.
                    int byteIndex = pieceIndex / 2;
                    bool lowNibble = (pieceIndex % 2) == 0;
                    byte nibble = lowNibble ? (byte)(pieceData[byteIndex] & 0x0F)
                                            : (byte)(pieceData[byteIndex] >> 4);
                    pieceIndex++;

                    // Map the nibble to a piece and update the corresponding bitboards.
                    switch (nibble)
                    {
                        case (byte)PieceType.WhitePawn: // White Pawn
                            board.Pawn |= mask;
                            board.White |= mask;
                            break;
                        case (byte)PieceType.WhiteKnight: // White Knight
                            board.Knight |= mask;
                            board.White |= mask;
                            break;
                        case (byte)PieceType.WhiteBishop: // White Bishop
                            board.Bishop |= mask;
                            board.White |= mask;
                            break;
                        case (byte)PieceType.WhiteRook: // White Rook
                            board.Rook |= mask;
                            board.White |= mask;
                            break;
                        case (byte)PieceType.WhiteQueen: // White Queen
                            board.Queen |= mask;
                            board.White |= mask;
                            break;
                        case (byte)PieceType.WhiteKing: // White King
                            board.White |= mask;
                            board.WhiteKingPos = (byte)square;
                            break;
                        case (byte)PieceType.BlackPawn: // Black Pawn
                            board.Pawn |= mask;
                            board.Black |= mask;
                            break;
                        case (byte)PieceType.BlackKnight: // Black Knight
                            board.Knight |= mask;
                            board.Black |= mask;
                            break;
                        case (byte)PieceType.BlackBishop: // Black Bishop
                            board.Bishop |= mask;
                            board.Black |= mask;
                            break;
                        case (byte)PieceType.BlackRook: // Black Rook
                            board.Rook |= mask;
                            board.Black |= mask;
                            break;
                        case (byte)PieceType.BlackQueen: // Black Queen
                            board.Queen |= mask;
                            board.Black |= mask;
                            break;
                        case (byte)PieceType.BlackKing: // Black King
                            board.Black |= mask;
                            board.BlackKingPos = (byte)square;
                            break;
                        default:
                            throw new InvalidOperationException("Invalid piece type nibble encountered.");
                    }
                }
            }

            board.Hash = Zobrist.CalculateZobristKey(ref board, whiteToMove);

            return (board, whiteToMove);
        }
    }
}
