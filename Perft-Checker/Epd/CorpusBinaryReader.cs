using System.IO.Compression;
using System.Text;

namespace PerftChecker.Epd;

/// <summary>
/// Reads the gzipped binary corpus produced by
/// <c>fen_corpus/scripts/09_pack.py</c> and yields one <see cref="EpdCase"/>
/// per (FEN, depth) pair with a non-zero perft total — exactly matching the
/// shape <see cref="StaticAnalysisReader"/> yields from the JSONL.
///
/// Format spec lives in 09_pack.py. Summary:
///   "GCTC" u16(version) u32(position_count)
///   varint(url_count)  for each: varint(len) utf8[len]
///   u8(tag_count)      for each: u8(len) utf8[len]
///   for each position:
///     varint(fen_len) utf8[fen_len]
///     u8(context_quality)
///     varint(tag_count_here) u8[]
///     varint(url_count_here) varint[]
///     varint × 7 (d1..d7 totals)
///     for d in 1..7:
///       varint(divide_count)
///       for each move:
///         u16 LE (6b from | 6b to | 4b promo)
///         varint(node_count)
///
/// Streaming reader: only the URL + tag tables stay resident; positions are
/// yielded record-by-record without materialising the full corpus in RAM.
/// </summary>
public static class CorpusBinaryReader
{
    static readonly byte[] Magic = { (byte)'G', (byte)'C', (byte)'T', (byte)'C' };
    const ushort Version = 1;

    static readonly string?[] QualityTable = { "high", "medium", "low", null };

    /// <summary>
    /// Logical name of the corpus resource embedded in the perftcheck
    /// assembly. <see cref="ReadEmbedded"/> looks it up via reflection.
    /// </summary>
    public const string EmbeddedResourceName = "PerftChecker.fen_corpus.corpus.gctc.gz";

    /// <summary>
    /// Yields one <see cref="EpdCase"/> per (FEN, depth) where d_n &gt; 0
    /// from a corpus file on disk.
    /// </summary>
    public static IEnumerable<EpdCase> ReadFile(string path, bool includeDivides = false)
        => ReadStreamCore(File.OpenRead(path), Path.GetFileName(path), includeDivides);

    /// <summary>
    /// Yields cases from the corpus embedded in the perftcheck assembly
    /// (the default when no <c>--static-analysis</c> path is given).
    /// </summary>
    public static IEnumerable<EpdCase> ReadEmbedded(bool includeDivides = false)
    {
        var asm = typeof(CorpusBinaryReader).Assembly;
        Stream stream =
            asm.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded corpus '{EmbeddedResourceName}' not found in {asm.FullName}. "
                + "Did you rebuild after running 09_pack.py?");
        return ReadStreamCore(stream, "<embedded:corpus.gctc.gz>", includeDivides);
    }

    static IEnumerable<EpdCase> ReadStreamCore(
        Stream backing, string label, bool includeDivides)
    {
        int lineNum = 0;  // synthetic "line" counter for FailureEntry.Source

        using var fs = backing;
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var br = new BinaryReader(gz, Encoding.UTF8, leaveOpen: false);

        // Header
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 ||
            magic[0] != Magic[0] || magic[1] != Magic[1] ||
            magic[2] != Magic[2] || magic[3] != Magic[3])
        {
            throw new InvalidDataException(
                $"{label}: bad magic; expected 'GCTC', got '{Encoding.ASCII.GetString(magic)}'.");
        }
        ushort version = br.ReadUInt16();
        if (version != Version)
        {
            throw new InvalidDataException(
                $"{label}: unsupported version {version}, expected {Version}.");
        }
        uint positionCount = br.ReadUInt32();

        // URL table
        int urlCount = (int)ReadVarUInt(br);
        var urls = new string[urlCount];
        for (int i = 0; i < urlCount; i++)
        {
            int len = (int)ReadVarUInt(br);
            urls[i] = Encoding.UTF8.GetString(br.ReadBytes(len));
        }

        // Tag vocab (u8 count, u8 lengths)
        int tagCount = br.ReadByte();
        var tags = new string[tagCount];
        for (int i = 0; i < tagCount; i++)
        {
            int len = br.ReadByte();
            tags[i] = Encoding.UTF8.GetString(br.ReadBytes(len));
        }

        // Positions
        for (uint p = 0; p < positionCount; p++)
        {
            lineNum++;

            int fenLen = (int)ReadVarUInt(br);
            string fen = Encoding.UTF8.GetString(br.ReadBytes(fenLen));

            int qId = br.ReadByte();
            string? quality = qId < QualityTable.Length ? QualityTable[qId] : null;

            int nTags = (int)ReadVarUInt(br);
            List<string>? rowTags = nTags > 0 ? new List<string>(nTags) : null;
            for (int i = 0; i < nTags; i++)
                rowTags!.Add(tags[br.ReadByte()]);

            int nUrls = (int)ReadVarUInt(br);
            List<string>? rowUrls = nUrls > 0 ? new List<string>(nUrls) : null;
            for (int i = 0; i < nUrls; i++)
                rowUrls!.Add(urls[(int)ReadVarUInt(br)]);

            var totals = new ulong[7];
            for (int d = 0; d < 7; d++) totals[d] = ReadVarUInt(br);

            // Divides — read every depth even if we won't materialise it, so
            // the stream stays aligned. Materialise only when includeDivides.
            var divides = new IReadOnlyDictionary<string, ulong>?[7];
            for (int d = 0; d < 7; d++)
            {
                int nMoves = (int)ReadVarUInt(br);
                Dictionary<string, ulong>? map =
                    (includeDivides && nMoves > 0)
                        ? new Dictionary<string, ulong>(nMoves, StringComparer.Ordinal)
                        : null;
                for (int m = 0; m < nMoves; m++)
                {
                    ushort packed = br.ReadUInt16();
                    ulong count = ReadVarUInt(br);
                    if (map is not null)
                        map[DecodeMove(packed)] = count;
                }
                divides[d] = map;
            }

            // Yield one EpdCase per (fen, depth) where d > 0.
            for (int d = 1; d <= 7; d++)
            {
                ulong expected = totals[d - 1];
                if (expected == 0) continue;
                yield return new EpdCase(
                    Fen:            fen,
                    Depth:          d,
                    Expected:       expected,
                    SourceFile:     label,
                    SourceLine:     lineNum,
                    SourceUrls:     rowUrls,
                    Tags:           rowTags,
                    ExpectedDivide: includeDivides ? divides[d - 1] : null,
                    ContextQuality: quality);
            }
        }

    }

    static ulong ReadVarUInt(BinaryReader br)
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = br.ReadByte();
            result |= ((ulong)(b & 0x7F)) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("varint > 64 bits");
        }
    }

    static readonly char[] PromoChar = { 'q', 'r', 'b', 'n' };

    static string DecodeMove(ushort packed)
    {
        int from  = packed & 0x3F;
        int to    = (packed >> 6) & 0x3F;
        int promo = (packed >> 12) & 0xF;
        Span<char> buf = stackalloc char[5];
        int len = 0;
        buf[len++] = (char)('a' + (from % 8));
        buf[len++] = (char)('1' + (from / 8));
        buf[len++] = (char)('a' + (to   % 8));
        buf[len++] = (char)('1' + (to   / 8));
        if (promo >= 1 && promo <= 4) buf[len++] = PromoChar[promo - 1];
        return new string(buf[..len]);
    }
}
