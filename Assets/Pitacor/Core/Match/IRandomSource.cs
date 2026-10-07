using System;

namespace Pitacor.Core.Match
{
    /// <summary>Injected randomness so the engine stays deterministic in tests.</summary>
    public interface IRandomSource
    {
        /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
        int Next(int minInclusive, int maxExclusive);
    }

    public sealed class SeededRandom : IRandomSource
    {
        private readonly Random _random;

        public SeededRandom(int seed)
        {
            _random = new Random(seed);
        }

        public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
    }
}
