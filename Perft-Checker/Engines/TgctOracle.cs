using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PerftChecker.Engines;

/// <summary>
/// Reference oracle backed by TGCT (GrandChessTree.Engine). Drives one
/// long-lived TGCT subprocess via its `divide:<depth>:<mb>:<fen>[:<moves>]`
/// command — that single round-trip returns the per-move divide, the
/// total node count, and the resulting FEN after applying the optional
/// move sequence. Used by drill-down to find the first move at which the
/// test engine's move-gen diverges from truth.
/// </summary>
public sealed class TgctOracle : IAsyncDisposable
{
    readonly string _enginePath;
    Process?        _process;

    static readonly Regex EndRe    = new(@"^-{15,}$", RegexOptions.Compiled);
    static readonly Regex NodesRe  = new(@"^nodes:\s*(\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex FenRe    = new(@"^fen:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DivideRe = new(@"^([a-h][1-8][a-h][1-8][rnbq]?)\s+(\d+)\s*$",
        RegexOptions.Compiled);
    static readonly Regex ErrorRe  = new(@"^error:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TgctOracle(string enginePath) => _enginePath = enginePath;

    public Task StartAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = _enginePath,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch oracle '{_enginePath}'.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send `divide:<depth>:<mb>:<fen>:<moves>` and parse the response.
    /// `moves` is a space-separated list of UCI moves (empty for no moves).
    /// Returns the per-move divide, total node count, and the FEN of the
    /// position after applying <paramref name="moves"/>.
    /// </summary>
    public async Task<DivideResult> DivideAsync(
        string fen, string moves, int depth, int timeoutMs, CancellationToken ct)
    {
        if (_process is null) throw new InvalidOperationException("Oracle not started.");
        if (_process.HasExited)
            throw new EngineProtocolException("Oracle exited.");

        // moves can be empty — TGCT treats no-trailing-field as no moves.
        string cmd = string.IsNullOrEmpty(moves)
            ? $"divide:{depth}:0:{fen}"
            : $"divide:{depth}:0:{fen}:{moves}";

        await _process.StandardInput.WriteLineAsync(cmd.AsMemory(), ct).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var divide = new Dictionary<string, ulong>(StringComparer.Ordinal);
        ulong total = 0;
        bool totalSeen = false;
        string? newFen = null;
        string? error  = null;
        var capture = new StringBuilder();

        string? line;
        while ((line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false))
               is not null)
        {
            if (capture.Length < 2048) { capture.Append(line); capture.Append('\n'); }

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var em = ErrorRe.Match(trimmed);
            if (em.Success) { error = em.Groups[1].Value.Trim(); continue; }

            var dm = DivideRe.Match(trimmed);
            if (dm.Success) { divide[dm.Groups[1].Value] = ulong.Parse(dm.Groups[2].Value); continue; }

            var nm = NodesRe.Match(trimmed);
            if (nm.Success) { total = ulong.Parse(nm.Groups[1].Value); totalSeen = true; continue; }

            var fm = FenRe.Match(trimmed);
            if (fm.Success) { newFen = fm.Groups[1].Value.Trim(); continue; }

            if (EndRe.IsMatch(trimmed)) break;
        }

        if (error is not null)
            throw new EngineProtocolException(
                $"TGCT rejected divide request: {error}. raw={capture.ToString().TrimEnd()}");
        if (!totalSeen)
            throw new EngineProtocolException(
                $"TGCT did not return a 'nodes:' line. raw={capture.ToString().TrimEnd()}");
        if (newFen is null)
            throw new EngineProtocolException(
                $"TGCT did not return a 'fen:' line. raw={capture.ToString().TrimEnd()}");

        return new DivideResult(total, divide, newFen);
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteLineAsync("quit").ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                catch { /* ignore */ }
                if (!_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
            }
        }
        catch { /* ignore */ }
        finally { _process.Dispose(); _process = null; }
    }
}

public sealed record DivideResult(
    ulong Total,
    IReadOnlyDictionary<string, ulong> Divide,
    string ResultFen);
