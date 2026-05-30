using System.Runtime.InteropServices;

namespace GrandChessTree.Shared;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public partial struct Board
{
    public ulong Pawn;    
    public ulong Knight;  
    public ulong Bishop; 
    public ulong Rook;   
    public ulong Queen;  
    public ulong White;  
    public ulong Black;
    public ulong MoveMask;
    public ulong Hash;

    public CastleRights CastleRights;
    public byte EnPassantFile;
    public byte WhiteKingPos; 
    public byte BlackKingPos;
}


[Flags]
public enum CastleRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8
}