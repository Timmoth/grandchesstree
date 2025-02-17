using System.ComponentModel.Design;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public partial struct Board
{
    internal unsafe void BlackPawn_Enpassant(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        var captureSquare = 1UL << (fromSquare.GetRankIndex() * 8 + EnPassantFile);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhitePawn + (fromSquare.GetRankIndex() * 8 + EnPassantFile))
                ^ Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= moveMask ^ captureSquare;
        Black ^= moveMask;
        White ^= captureSquare;

        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_Capture_KnightPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7){

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Knight |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);
        White &= ~captureMask;

        EnPassantFile = 8;
    }


    internal unsafe void BlackPawn_Capture_BishopPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Bishop |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);
        White &= ~captureMask;

        EnPassantFile = 8;
    }
    internal unsafe void BlackPawn_Capture_RookPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Rook |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);
        White &= ~captureMask;

        EnPassantFile = 8;
    }


    internal unsafe void BlackPawn_Capture_QueenPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Queen |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);
        White &= ~captureMask;

        EnPassantFile = 8;
    }

 
    internal unsafe void BlackPawn_Capture(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Pawn & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            zobristOffset = Zobrist.WhitePawn;
        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= moveMask;
        Black ^= moveMask;
        White &= ~captureMask;

        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_KnightPromotion(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= 1UL << fromSquare;
        Knight |= 1UL << toSquare;
        Black ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);


        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_BishopPromotion(int fromSquare, int toSquare)
    {
        Pawn ^= 1UL << fromSquare;
        Bishop |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_RookPromotion(int fromSquare, int toSquare)
    {
        Pawn ^= 1UL << fromSquare;
        Rook |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_QueenPromotion(int fromSquare, int toSquare)
    {
        Pawn ^= 1UL << fromSquare;
        Queen |= 1UL << toSquare;
        Black ^= (1UL << fromSquare) | (1UL << toSquare);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= moveMask;
        Black ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void BlackPawn_DoublePush(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= moveMask;
        Black ^= moveMask;
        byte enPassantFile = (byte)(fromSquare % 8);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackPawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + enPassantFile);
        EnPassantFile = enPassantFile;
    }

    internal unsafe void BlackKnight_Capture(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Pawn & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            zobristOffset = Zobrist.WhitePawn;
        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKnight + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Knight ^= moveMask;
        Black ^= moveMask;
        White &= ~captureMask;

        EnPassantFile = 8;
    }

    internal unsafe void BlackKnight_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Knight ^= moveMask;
        Black ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKnight + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void BlackBishop_Capture(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Pawn & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            zobristOffset = Zobrist.WhitePawn;
        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.WhiteKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.WhiteBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.WhiteRook;
            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.WhiteQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackBishop + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Bishop ^= moveMask;
        Black ^= moveMask;
        White &= ~captureMask;

        EnPassantFile = 8;
    }

    internal unsafe void BlackBishop_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Bishop ^= moveMask;
        Black ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackBishop + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void BlackRook_Capture(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        var prevCastleRights = CastleRights;
        if (fromSquare == 56)
            CastleRights &= ~CastleRights.BlackQueenSide;
        else if (fromSquare == 63)
            CastleRights &= ~CastleRights.BlackKingSide;

        var captureMask = (1UL << toSquare);
        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Rook & captureMask) != 0)
        {
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }

            Rook &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        Rook ^= moveMask;
        Black ^= moveMask;
        White &= ~captureMask;

        
        EnPassantFile = 8;
    }

    internal unsafe void BlackRook_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Rook ^= moveMask;
        Black ^= moveMask;
        var prevCastleRights = CastleRights;

        if (fromSquare == 56)
        {
            CastleRights &= ~CastleRights.BlackQueenSide;
        }
        else if (fromSquare == 63)
        {
            CastleRights &= ~CastleRights.BlackKingSide;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackRook + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        EnPassantFile = 8;
    }

    internal unsafe void BlackQueen_Capture(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        var captureMask = (1UL << toSquare);
        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

            var prevCastleRights = CastleRights;
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {

                CastleRights &= ~CastleRights.WhiteKingSide;
            }
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);
        }

        Queen ^= moveMask;
        Black ^= moveMask;
        White &= ~captureMask;

        
        EnPassantFile = 8;
    }

    internal unsafe void BlackQueen_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Queen ^= moveMask;
        Black ^= moveMask;


        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackQueen + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void BlackKing_Capture(int fromSquare, int toSquare)
    {
        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.BlackKingSide | CastleRights.BlackQueenSide);

        var captureMask = (1UL << toSquare);
        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Rook & captureMask) != 0)
        {
            if (toSquare == 0)
            {
                CastleRights &= ~CastleRights.WhiteQueenSide;
            }
            else if (toSquare == 7)
            {
                CastleRights &= ~CastleRights.WhiteKingSide;
            }

            Rook &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }

        Black ^= (1UL << fromSquare) | (1UL << toSquare);
        White &= ~captureMask;

        
        EnPassantFile = 8;
        BlackKingPos = (byte)toSquare;
    }

    internal unsafe void BlackKing_Move(int fromSquare, int toSquare)
    {
        Black ^= (1UL << fromSquare) | (1UL << toSquare);
        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.BlackKingSide | CastleRights.BlackQueenSide);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKing + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        
        EnPassantFile = 8;
        BlackKingPos = (byte)toSquare;
    }

    internal unsafe void BlackKing_KingSideCastle()
    {
        Rook ^= (1UL << 63) | (1UL << 61);
        Black ^= (1UL << 60) | (1UL << 63);
        Black |= (1UL << 62) | (1UL << 61);

        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.BlackKingSide | CastleRights.BlackQueenSide);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + 60) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKing + 62) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + 63) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + 61) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        
        EnPassantFile = 8;
        BlackKingPos = 62;
    }

    internal unsafe void BlackKing_QueenSideCastle()
    {
        Rook ^= (1UL << 56) | (1UL << 59);
        Black ^= (1UL << 60) | (1UL << 56);
        Black |= (1UL << 58) | (1UL << 59);

        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.BlackKingSide | CastleRights.BlackQueenSide);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.BlackKing + 60) ^
                *(Zobrist.PiecesArray + Zobrist.BlackKing + 58) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + 56) ^
                *(Zobrist.PiecesArray + Zobrist.BlackRook + 59) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        
        EnPassantFile = 8;
        BlackKingPos = 58;
    }

  

  
}