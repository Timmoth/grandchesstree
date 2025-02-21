namespace GrandChessTree.Shared.Helpers;

using System;
using System.Linq;
using System.Numerics;

public class HyperLogLog
{
    private readonly int _m;
    private readonly byte[] _registers;
    private readonly int _precision;

    public HyperLogLog(int precision = 16)
    {
        if (precision < 4 || precision > 18)
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be between 4 and 18.");

        _precision = precision;
        _m = 1 << precision;  // Number of registers (2^precision)
        _registers = new byte[_m];
    }

    public void Merge(HyperLogLog other)
    {
        if (other._precision != this._precision)
            throw new InvalidOperationException("HyperLogLog precision must match to merge");

        for (int i = 0; i < _m; i++)
        {
            _registers[i] = Math.Max(_registers[i], other._registers[i]);
        }
    }

    public void Add(ulong hashedValue)
    {
        // Stronger hash mixing using Murmur-like finalizer
        hashedValue ^= hashedValue >> 33;
        hashedValue *= 0xff51afd7ed558ccdL;
        hashedValue ^= hashedValue >> 33;
        hashedValue *= 0xc4ceb9fe1a85ec53L;
        hashedValue ^= hashedValue >> 33;

        // Extract bucket from highest _precision bits
        int bucket = (int)(hashedValue >> (64 - _precision));

        // Remove bucket bits, keeping remaining bits for leading zero calculation
        ulong remainingBits = hashedValue << _precision;

        // Count leading zeros in remaining bits
        int leadingZeros = BitOperations.LeadingZeroCount(remainingBits) + 1;
        leadingZeros = Math.Min(leadingZeros, 64 - _precision); // Avoid extreme values

        // Update register with the maximum observed leading zero value
        _registers[bucket] = Math.Max(_registers[bucket], (byte)leadingZeros);
    }

    public double Count()
    {
        // Compute sum using Kahan summation for numerical stability
        double sum = 0.0;
        double c = 0.0; // Kahan correction
        foreach (byte r in _registers)
        {
            double y = Math.Pow(2.0, -r) - c;
            double t = sum + y;
            c = (t - sum) - y;
            sum = t;
        }

        double alphaM = (_m == 16) ? 0.673 :
                        (_m == 32) ? 0.697 :
                        (_m == 64) ? 0.709 :
                        (0.7213 / (1 + 1.079 / _m));

        double rawEstimate = alphaM * _m * _m / sum;

        // Small range correction (Linear Counting)
        int zeroCount = _registers.Count(r => r == 0);
        if (rawEstimate <= 2.5 * _m && zeroCount > 0)
        {
            rawEstimate = _m * Math.Log((double)_m / zeroCount);
        }

        // Large range correction (HyperLogLog++ bias correction)
        double threshold = (1L << 32) / 30.0; // ~143M, threshold for overestimation
        if (rawEstimate > threshold)
        {
            // Avoid log(0) issues by ensuring rawEstimate isn't too close to 2^64
            double ratio = rawEstimate / Math.Pow(2, 64);
            if (ratio < 1.0)
            {
                rawEstimate = -Math.Pow(2, 64) * Math.Log(1 - ratio);
            }
            else
            {
                rawEstimate = Math.Pow(2, 64); // Cap the estimate at 2^64
            }
        }

        return rawEstimate;
    }
}
