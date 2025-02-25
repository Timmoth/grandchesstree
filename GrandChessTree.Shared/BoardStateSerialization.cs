using System.Buffers.Binary;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared
{
    public static class BoardStateSerialization
    {
        public const int StructSize = 25;

        // Serialize the struct into a Base64 string
        public static string Serialize(this Board board, bool whiteToMove)
        {
            Span<byte> buffer = stackalloc byte[StructSize];
            WriteToSpan(board, buffer, whiteToMove);
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

        private static PieceType GetPieceType(this Board board, ulong occupiedSquare)
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

        // Write struct data to a Span<byte>
        private static void WriteToSpan(this Board board, Span<byte> span, bool whiteToMove)
        {
            // 16 bytes piece / colour type [piece2 - 4 bits][piece1 - 4 bits]
            // 8 bytes ulong occupancy [bitboard - 64 bits]
            // 1 byte castle rights + enpassant + white to move [white to move - 1 bit][enpassant file - 3 bits][castle rights - 4 bits]
           
            var occupancy = (board.White | board.Black);
            int bufferIndex = 0;
            while (occupancy != 0)
            {
                var pieceType1 = (byte)board.GetPieceType(1ul << occupancy.PopLSB());
                var pieceType2 = occupancy != 0 ? (byte)board.GetPieceType(1ul << occupancy.PopLSB()) : 0;
                span[bufferIndex++] = (byte)((byte)(pieceType2 << 4) | pieceType1);
            }

            // skip piece bytes
            bufferIndex = 16;
            // write occupancy
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(bufferIndex, 8), board.White | board.Black);
            bufferIndex = 24;
            // write rights
            // [white to move - 1 bit][enpassant file - 3 bits][castle rights - 4 bits]
            span[bufferIndex] = (byte)(
                ((whiteToMove ? 0 : (1 << 7))) |           // Bit 7 = turn (0 = white, 1 = black)
                (((board.EnPassantFile-1) & 0x7) << 4) |   // Bits 6-4 = en passant file (0–7)
                (((byte)board.CastleRights) & 0xF)           // Bits 3-0 = castle rights
            );
        }

        private static (Board board, bool whiteToMove) ReadFromSpan(ReadOnlySpan<byte> span)
        {
            if (span.Length < 25)
                throw new ArgumentException("Compressed board state must be at least 25 bytes.");

            Board board = default;

            // 1. Read the 16 bytes that encode up to 32 piece nibbles.
            ReadOnlySpan<byte> pieceData = span.Slice(0, 16);

            // 2. Read the occupancy bitboard (8 bytes).
            ulong occupancy = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));

            // 3. Read the flags (1 byte).
            byte flags = span[24];
            bool whiteToMove = (flags & 0x80) == 0;             // Bit 7: 0 means white to move.
            byte enPassantFile = (byte)((flags >> 4) & 0x7);      // Bits 6-4.
            enPassantFile++;
            CastleRights castleRights = (CastleRights)(flags & 0xF); // Bits 3-0.
            board.CastleRights = castleRights;
            board.EnPassantFile = enPassantFile;

            // 4. Decode the piece information.
            // The pieces were stored in the order in which occupancy bits were popped.
            // When decoding, we iterate over squares 0-63 in increasing order.
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
                        case 1: // White Pawn
                            board.Pawn |= mask;
                            board.White |= mask;
                            break;
                        case 2: // White Knight
                            board.Knight |= mask;
                            board.White |= mask;
                            break;
                        case 3: // White Bishop
                            board.Bishop |= mask;
                            board.White |= mask;
                            break;
                        case 4: // White Rook
                            board.Rook |= mask;
                            board.White |= mask;
                            break;
                        case 5: // White Queen
                            board.Queen |= mask;
                            board.White |= mask;
                            break;
                        case 6: // White King
                            board.White |= mask;
                            board.WhiteKingPos = (byte)square;
                            break;
                        case 7: // Black Pawn
                            board.Pawn |= mask;
                            board.Black |= mask;
                            break;
                        case 8: // Black Knight
                            board.Knight |= mask;
                            board.Black |= mask;
                            break;
                        case 9: // Black Bishop
                            board.Bishop |= mask;
                            board.Black |= mask;
                            break;
                        case 10: // Black Rook
                            board.Rook |= mask;
                            board.Black |= mask;
                            break;
                        case 11: // Black Queen
                            board.Queen |= mask;
                            board.Black |= mask;
                            break;
                        case 12: // Black King
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
