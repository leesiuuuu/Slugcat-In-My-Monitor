using System;
using System.Collections.Generic;
using System.Drawing;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class CompositionBatch
    {
        internal CompositionBatch()
        {
            SurfaceIndices = new List<int>(8);
        }

        public Rectangle Bounds { get; internal set; }
        public List<int> SurfaceIndices { get; private set; }

        internal void Reset(Rectangle bounds, int surfaceIndex)
        {
            Bounds = bounds;
            SurfaceIndices.Clear();
            SurfaceIndices.Add(surfaceIndex);
        }
    }

    public sealed class CompositionBatchPlanner
    {
        private readonly List<CompositionBatch> batches = new List<CompositionBatch>(8);
        private readonly Stack<CompositionBatch> available = new Stack<CompositionBatch>(8);

        public IList<CompositionBatch> Plan(IList<Rectangle> surfaceBounds,
            int sizeQuantum)
        {
            if (surfaceBounds == null) throw new ArgumentNullException("surfaceBounds");
            if (sizeQuantum < 1) throw new ArgumentOutOfRangeException("sizeQuantum");

            for (int i = 0; i < batches.Count; i++) available.Push(batches[i]);
            batches.Clear();
            for (int i = 0; i < surfaceBounds.Count; i++)
            {
                CompositionBatch batch = available.Count == 0 ?
                    new CompositionBatch() : available.Pop();
                batch.Reset(surfaceBounds[i], i);
                batches.Add(batch);
            }

            while (TryMergeBestPair(batches, sizeQuantum)) { }
            return batches;
        }

        private bool TryMergeBestPair(List<CompositionBatch> values, int sizeQuantum)
        {
            int bestLeft = -1;
            int bestRight = -1;
            Rectangle bestBounds = Rectangle.Empty;
            long bestSaving = 0;
            bool bestIsRequiredOverlap = false;

            for (int left = 0; left < values.Count; left++)
            {
                for (int right = left + 1; right < values.Count; right++)
                {
                    Rectangle union = Rectangle.Union(values[left].Bounds,
                        values[right].Bounds);
                    Rectangle rounded = RoundAroundCenter(union, sizeQuantum);
                    long separateArea = Area(values[left].Bounds) +
                        Area(values[right].Bounds);
                    long saving = separateArea - Area(rounded);
                    Rectangle intersection = Rectangle.Intersect(
                        values[left].Bounds, values[right].Bounds);
                    bool requiredOverlap = intersection.Width > 0 &&
                        intersection.Height > 0;
                    if (requiredOverlap)
                    {
                        if (bestIsRequiredOverlap && saving <= bestSaving) continue;
                    }
                    else
                    {
                        if (bestIsRequiredOverlap || saving <= bestSaving) continue;
                    }
                    bestSaving = saving;
                    bestLeft = left;
                    bestRight = right;
                    bestBounds = rounded;
                    bestIsRequiredOverlap = requiredOverlap;
                }
            }

            if (bestLeft < 0) return false;
            CompositionBatch target = values[bestLeft];
            CompositionBatch source = values[bestRight];
            target.Bounds = bestBounds;
            target.SurfaceIndices.AddRange(source.SurfaceIndices);
            target.SurfaceIndices.Sort();
            values.RemoveAt(bestRight);
            available.Push(source);
            return true;
        }

        private static Rectangle RoundAroundCenter(Rectangle bounds, int quantum)
        {
            int width = RoundUp(bounds.Width, quantum);
            int height = RoundUp(bounds.Height, quantum);
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            return new Rectangle(centerX - width / 2, centerY - height / 2,
                width, height);
        }

        private static int RoundUp(int value, int quantum)
        {
            return ((Math.Max(1, value) + quantum - 1) / quantum) * quantum;
        }

        private static long Area(Rectangle bounds)
        {
            return (long)Math.Max(0, bounds.Width) * Math.Max(0, bounds.Height);
        }
    }
}
