using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Desktop
{
    internal struct MouseHookHitCircle
    {
        internal MouseHookHitCircle(Vec2 center, double radius)
        {
            Center = center;
            Radius = radius;
        }

        internal readonly Vec2 Center;
        internal readonly double Radius;

        internal bool Contains(Vec2 point)
        {
            Vec2 delta = point - Center;
            return delta.LengthSquared <= Radius * Radius;
        }
    }

    internal sealed class MouseHookHitTarget
    {
        internal MouseHookHitTarget(object value, int firstCircle, int circleCount)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (firstCircle < 0) throw new ArgumentOutOfRangeException("firstCircle");
            if (circleCount < 0) throw new ArgumentOutOfRangeException("circleCount");
            Value = value;
            FirstCircle = firstCircle;
            CircleCount = circleCount;
        }

        internal readonly object Value;
        internal readonly int FirstCircle;
        internal readonly int CircleCount;
    }

    // Published as one immutable reference after a simulation frame. The hook
    // thread never traverses collections that the UI thread may be mutating.
    internal sealed class MouseHookHitSnapshot
    {
        internal static readonly MouseHookHitSnapshot Empty =
            new MouseHookHitSnapshot(new MouseHookHitTarget[0],
                new MouseHookHitCircle[0]);

        internal MouseHookHitSnapshot(MouseHookHitTarget[] targets,
            MouseHookHitCircle[] circles)
        {
            Targets = targets ?? new MouseHookHitTarget[0];
            Circles = circles ?? new MouseHookHitCircle[0];
        }

        internal readonly MouseHookHitTarget[] Targets;
        internal readonly MouseHookHitCircle[] Circles;

        internal object HitTest(Vec2 point)
        {
            for (int i = Targets.Length - 1; i >= 0; i--)
            {
                MouseHookHitTarget target = Targets[i];
                int end = target.FirstCircle + target.CircleCount;
                for (int circle = target.FirstCircle; circle < end; circle++)
                    if (Circles[circle].Contains(point)) return target.Value;
            }
            return null;
        }
    }
}
