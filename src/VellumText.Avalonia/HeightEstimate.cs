namespace VellumText.Avalonia;

/// <summary>
/// The height to assume for something nobody has measured yet — a block, or a table row — drawn
/// from the ones that have been.
/// </summary>
/// <remarks>
/// <para>
/// The mean of the measured heights, each counted at no more than <c>OutlierFactor</c> times
/// their median (or <see cref="Seed"/>, if that is larger); <see cref="Seed"/> until anything has
/// been measured.
/// </para>
/// <para>
/// A plain mean let one item set the height of all the others. A 20,000-character paragraph is
/// hundreds of thousands of pixels tall, and beside twenty ordinary ones it priced ten thousand
/// unmeasured paragraphs at eleven thousand pixels each — a document of a hundred million
/// pixels, whose extent then collapsed by millions with every few small blocks measured, so
/// scrolling moved the thumb and not the text. A table row holding a cell of thousands of words
/// did the same to every one-line row. Ordinary variety (headings, short lines, long
/// paragraphs, a few tall rows) stays well inside the bound, and for it this is the plain mean,
/// bit for bit: it comes from the same running total it always did, because a position on an
/// item's exact boundary is decided by the last bits of a sum.
/// </para>
/// <para>
/// The median comes from a histogram, an eighth of an octave per bucket, rather than a sorted
/// list, so a measurement stays a constant-time update; it is the mean of the bucket the middle
/// measurement falls in, which is the height itself when the heights are alike. Worked out when
/// first asked for after a change, since it is read far more often than measurements change.
/// </para>
/// </remarks>
internal sealed class HeightEstimate
{
    private const int Buckets = 256;
    private const double BucketsPerOctave = 8;

    /// <summary>How many times the typical measured height one item may count for, at most.</summary>
    private const double OutlierFactor = 16;

    private readonly int[] _bucketCount = new int[Buckets];
    private readonly double[] _bucketSum = new double[Buckets];
    private int _count;
    private double _total;
    private double _seed;
    private double _value;
    private bool _stale = true;

    /// <summary>Creates an estimate with nothing measured.</summary>
    /// <param name="seed">The height to assume until something has been measured.</param>
    public HeightEstimate(double seed) => _seed = seed;

    /// <summary>The height assumed while nothing has been measured, and the floor of the bound.</summary>
    public double Seed
    {
        get => _seed;
        set
        {
            _seed = value;
            _stale = true;
        }
    }

    /// <summary>How many measurements there are.</summary>
    public int Count => _count;

    /// <summary>The height to assume for an item nobody has measured; see the remarks on the class.</summary>
    public double Value => _count == 0 ? _seed : _stale ? Recompute() : _value;

    /// <summary>Records a measurement.</summary>
    public void Add(double height)
    {
        var bucket = BucketOf(height);

        _count++;
        _total += height;
        _bucketCount[bucket]++;
        _bucketSum[bucket] += height;
        _stale = true;
    }

    /// <summary>Takes back a measurement recorded earlier.</summary>
    public void Remove(double height)
    {
        var bucket = BucketOf(height);

        _count--;
        _total -= height;

        // Emptied buckets restart from zero, so subtraction never leaves rounding behind.
        _bucketSum[bucket] = --_bucketCount[bucket] == 0 ? 0 : _bucketSum[bucket] - height;
        _stale = true;
    }

    /// <summary>
    /// Forgets every measurement, keeping what they added up to as the new <see cref="Seed"/>:
    /// still the best guess about how tall an item here tends to be.
    /// </summary>
    public void Restart()
    {
        _seed = Value;
        _count = 0;
        _total = 0;
        Array.Clear(_bucketCount);
        Array.Clear(_bucketSum);
        _stale = true;
    }

    private static int BucketOf(double height) =>
        Math.Min(Buckets - 1, (int)(Math.Log2(1 + height) * BucketsPerOctave));

    /// <summary>The smallest and largest height a bucket can hold.</summary>
    private static double Lower(int bucket) => Math.Pow(2, bucket / BucketsPerOctave) - 1;

    private static double Upper(int bucket) =>
        bucket == Buckets - 1 ? double.PositiveInfinity : Math.Pow(2, (bucket + 1) / BucketsPerOctave) - 1;

    private double Recompute()
    {
        var middle = (_count + 1) / 2;
        var seen = 0;
        var median = 0d;

        for (var bucket = 0; bucket < Buckets; bucket++)
        {
            seen += _bucketCount[bucket];

            if (seen >= middle)
            {
                median = _bucketSum[bucket] / _bucketCount[bucket];
                break;
            }
        }

        // Floored at the seed: with a handful of measurements, or many empty paragraphs, the
        // median can be next to nothing, and a bound that small would clip ordinary lines rather
        // than outliers.
        var cap = OutlierFactor * Math.Max(median, _seed);
        var highest = Buckets - 1;

        while (_bucketCount[highest] == 0)
        {
            highest--;
        }

        if (Upper(highest) <= cap)
        {
            _value = _total / _count;
        }
        else
        {
            var total = 0d;

            for (var bucket = 0; bucket < Buckets; bucket++)
            {
                var count = _bucketCount[bucket];

                if (count == 0)
                {
                    continue;
                }

                // A bucket wholly under the bound counts in full and one wholly over it counts at
                // the bound; one the bound passes through counts in full unless that exceeds it.
                total += Upper(bucket) <= cap ? _bucketSum[bucket]
                    : Lower(bucket) >= cap ? count * cap
                    : Math.Min(_bucketSum[bucket], count * cap);
            }

            _value = total / _count;
        }

        _stale = false;

        return _value;
    }
}
