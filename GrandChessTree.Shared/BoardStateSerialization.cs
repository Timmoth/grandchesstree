using System.Buffers.Binary;
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
        public static void WriteToSpan(ref Board board, Span<byte> span, bool whiteToMove)
        {
            if (span.Length < 26)
                throw new ArgumentException("Span must be at least 26 bytes.");

            // 1. Write piece/colour type information in 16 bytes.
            //    Each byte holds two 4-bit nibbles:
            //      [piece2 (high nibble)][piece1 (low nibble)]
            ulong occupancy = board.White | board.Black;
            int bufferIndex = 0;
            while (occupancy != 0)
            {
                // PopLSB returns the index of the least-significant set bit and clears it.
                // GetPieceType returns a value between 1 and 12 (or 0 if something’s wrong).
                byte pieceType1 = (byte)board.GetPieceType(1UL << occupancy.PopLSB());
                byte pieceType2 = occupancy != 0
                    ? (byte)board.GetPieceType(1UL << occupancy.PopLSB())
                    : (byte)0;
                span[bufferIndex++] = (byte)((pieceType2 << 4) | pieceType1);
            }
            // If fewer than 16 bytes were written, the remaining bytes in the span will be ignored.

            // 2. Write the occupancy bitboard (8 bytes) at offset 16.
            bufferIndex = 16;
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(bufferIndex, 8), board.White | board.Black);

            // 3. Write en passant and castle rights in 1 byte at offset 24.
            //    High nibble (bits 7-4): board.EnPassantFile (0-8) [requires 4 bits].
            //    Low nibble (bits 3-0): castle rights (4 bits).
            bufferIndex = 24;
            span[24] = (byte)(
              ((((byte)board.EnPassantFile) & 0xF) << 4) |   // high nibble: en passant file (4 bits)
              (((byte)board.CastleRights) & 0xF)               // low nibble: castle rights (4 bits)
          );

            // 4. Write the whiteToMove flag in 1 byte at offset 25.
            //    0 means white to move, 1 means black.
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
