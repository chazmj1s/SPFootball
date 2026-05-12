using System;
using System.Collections.Generic;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Static utility container that exposes a reusable FIFO queue for doubles with fixed capacity = 10.
    /// Use the nested <see cref="FifoDoubleQueue"/> instance in non-static classes.
    /// </summary>
    public static class FifoDoubleQueueUtility
    {
        public const int Capacity = 10;

        /// <summary>
        /// Instance FIFO queue (capacity 10) for doubles.
        /// Most recent values receive higher weight in <see cref="GetWeightedAverage"/>.
        /// </summary>
        public sealed class FifoDoubleQueue
        {
            private readonly double[] _buffer = new double[Capacity];
            private int _start;
            private int _count;
            private readonly Lock _sync = new();

            public int Count
            {
                get { lock (_sync) { return _count; } }
            }

            /// <summary>
            /// Enqueue a value. If full, evicts the oldest value.
            /// </summary>
            public void Enqueue(double value)
            {
                lock (_sync)
                {
                    if (_count < Capacity)
                    {
                        int insertIndex = (_start + _count) % Capacity;
                        _buffer[insertIndex] = value;
                        _count++;
                    }
                    else
                    {
                        // overwrite oldest and advance start
                        _buffer[_start] = value;
                        _start = (_start + 1) % Capacity;
                    }
                }
            }

            /// <summary>
            /// Returns values from most recent to oldest.
            /// </summary>
            public List<double> GetValuesMostRecentFirst()
            {
                lock (_sync)
                {
                    var list = new List<double>(_count);
                    for (int i = 0; i < _count; i++)
                    {
                        int idx = (_start + _count - 1 - i + Capacity) % Capacity;
                        list.Add(_buffer[idx]);
                    }
                    return list;
                }
            }

            /// <summary>
            /// Calculates weighted average where the most recent element has the highest linear weight.
            /// Weighting: most recent = Count, next = Count-1, ..., oldest = 1.
            /// Returns 0.0 if the queue is empty.
            /// </summary>
            public double GetWeightedAverage()
            {
                lock (_sync)
                {
                    if (_count == 0)
                        return 0.0;

                    int n = _count;
                    long weightSum = (long)n * (n + 1) / 2;
                    double weightedTotal = 0.0;

                    for (int i = 0; i < n; i++)
                    {
                        int weight = n - i; // i=0 => most recent => weight=n
                        int idx = (_start + _count - 1 - i + Capacity) % Capacity;
                        weightedTotal += _buffer[idx] * weight;
                    }

                    return weightedTotal / weightSum;
                }
            }

            /// <summary>
            /// Clears the queue.
            /// </summary>
            public void Clear()
            {
                lock (_sync)
                {
                    Array.Clear(_buffer, 0, Capacity);
                    _start = 0;
                    _count = 0;
                }
            }
        }
    }
}