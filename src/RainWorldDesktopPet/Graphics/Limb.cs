using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Graphics
{
    public enum LimbKind
    {
        Arm,
        Leg
    }

    // Names and semantics mirror Limb.Mode in Assembly-CSharp.dll.
    public enum LimbMode
    {
        HuntRelativePosition,
        HuntAbsolutePosition,
        Retracted,
        Dangle
    }

    public sealed class Limb
    {
        private readonly double defaultHuntSpeed;
        private readonly double defaultQuickness;
        private readonly int limbNumber;
        private bool wasCrawling;

        public Limb(LimbKind kind, int side, Vec2 initialPosition, double length)
        {
            Kind = kind;
            Side = side;
            limbNumber = side < 0 ? 0 : 1;
            Length = length;
            End = new BodyPart(initialPosition, kind == LimbKind.Arm ? 3.0 : 2.5, 0.8, 1.0);
            defaultHuntSpeed = kind == LimbKind.Arm ? 7.0 : 5.0;
            defaultQuickness = 0.5;
            HuntSpeed = defaultHuntSpeed;
            Quickness = defaultQuickness;
            Mode = LimbMode.HuntRelativePosition;
            AbsoluteHuntPosition = initialPosition;
            TargetPosition = initialPosition;
            ConnectionPosition = initialPosition;
            LastConnectionPosition = initialPosition;
        }

        public readonly LimbKind Kind;
        public readonly int Side;
        public readonly double Length;
        public readonly BodyPart End;
        public LimbMode Mode;
        public Vec2 RelativeHuntPosition;
        public Vec2 AbsoluteHuntPosition;
        public Vec2 TargetPosition;
        public Vec2 ConnectionPosition;
        public Vec2 LastConnectionPosition;
        public double HuntSpeed;
        public double Quickness;
        public bool ReachedSnapPosition;
        public bool Retract;
        public int RetractCounter;
        public long GripSurfaceId;
        public DesktopSurfaceKind GripSurfaceKind = DesktopSurfaceKind.ScreenEdge;
        public bool IsPlanted { get { return ReachedSnapPosition && Mode == LimbMode.HuntAbsolutePosition; } }
        public int LimbNumber { get { return limbNumber; } }
        public bool MovementEngagedThisTick { get; private set; }

        // SlugcatHand.Update ordering: update using the previous tick's target,
        // constrain to the upper BodyChunk, then select the target for next tick.
        public void Step(Slugcat player, Vec2 connection, Vec2 rotationChunk,
            Vec2 connectionVelocity, DesktopCollisionWorld world)
        {
            Step(player, connection, rotationChunk, connectionVelocity, world, null);
        }

        public void Step(Slugcat player, Vec2 connection, Vec2 rotationChunk,
            Vec2 connectionVelocity, DesktopCollisionWorld world, Limb leadLimb)
        {
            Step(player, connection, rotationChunk, connectionVelocity, world, leadLimb, 0.0);
        }

        public void Step(Slugcat player, Vec2 connection, Vec2 rotationChunk,
            Vec2 connectionVelocity, DesktopCollisionWorld world, Limb leadLimb,
            double airborneCounter)
        {
            LastConnectionPosition = ConnectionPosition;
            ConnectionPosition = connection;
            bool crawling = player.State.BodyMode == BodyModeIndex.Crawl;
            if (crawling && !wasCrawling)
            {
                // Do this before UpdateLimb consumes the previous frame's
                // target. Otherwise a retracted or raised standing hand can be
                // retained indefinitely by FindCrawlGrip's 29-unit keep zone.
                Vec2 normalizedVelocity = connectionVelocity.Normalized;
                Mode = LimbMode.HuntAbsolutePosition;
                AbsoluteHuntPosition = connection + new Vec2(
                    -6.0 + 12.0 * limbNumber + normalizedVelocity.X * 20.0,
                    Math.Abs(normalizedVelocity.Y) * 20.0);
                TargetPosition = AbsoluteHuntPosition;
                GripSurfaceId = 0;
                RetractCounter = 0;
                ReachedSnapPosition = false;
            }
            if (GripSurfaceId != 0 && !world.ContainsSurface(GripSurfaceId,
                GripSurfaceKind, AbsoluteHuntPosition, 3.0))
            {
                ReleaseSurfaceGrip();
            }
            UpdateLimb(connection, rotationChunk, connectionVelocity);
            End.ConnectToPoint(connection, Length, false, 0.0,
                connectionVelocity, 0.0, 0.0);

            bool retractWhenUnused = EngageInMovement(player, world, leadLimb, airborneCounter);
            MovementEngagedThisTick = !retractWhenUnused;
            if (player.State.Animation == AnimationIndex.Sleep)
            {
                Vec2 center = (player.BodyChunks[0].Position + player.BodyChunks[1].Position) * 0.5;
                Mode = LimbMode.HuntAbsolutePosition;
                AbsoluteHuntPosition = center + new Vec2(player.State.Facing * 10.0, 20.0);
                GripSurfaceId = 0;
                retractWhenUnused = false;
            }

            if (retractWhenUnused && Mode != LimbMode.Retracted)
            {
                RetractCounter++;
                if (RetractCounter > 5)
                {
                    Mode = LimbMode.HuntAbsolutePosition;
                    End.Position = Vec2.Lerp(End.Position, connection,
                        MathUtil.Clamp((RetractCounter - 5.0) * 0.05, 0.0, 1.0));
                    if (Vec2.Distance(End.Position, connection) < 2.0 && ReachedSnapPosition)
                        Mode = LimbMode.Retracted;
                    AbsoluteHuntPosition = connection;
                    HuntSpeed = 1.0 + RetractCounter * 0.2;
                    Quickness = 1.0;
                }
            }
            else
            {
                RetractCounter = Math.Max(0, RetractCounter - 10);
            }

            TargetPosition = Mode == LimbMode.HuntRelativePosition
                ? connection + RotateRelative(RelativeHuntPosition, rotationChunk, connection)
                : AbsoluteHuntPosition;
            wasCrawling = crawling;
        }

        private void UpdateLimb(Vec2 connection, Vec2 rotationChunk, Vec2 connectionVelocity)
        {
            End.LastPosition = End.Position;
            if (Retract && Mode != LimbMode.Retracted)
            {
                Mode = LimbMode.HuntAbsolutePosition;
                AbsoluteHuntPosition = connection;
                if (Vec2.Distance(AbsoluteHuntPosition, End.Position) < HuntSpeed)
                    Mode = LimbMode.Retracted;
            }

            if (Mode == LimbMode.HuntRelativePosition)
                AbsoluteHuntPosition = connection + RotateRelative(RelativeHuntPosition, rotationChunk, connection);

            if (Mode == LimbMode.HuntRelativePosition || Mode == LimbMode.HuntAbsolutePosition)
            {
                if (Vec2.Distance(AbsoluteHuntPosition, End.Position) < HuntSpeed)
                {
                    End.Velocity = AbsoluteHuntPosition - End.Position;
                    ReachedSnapPosition = true;
                }
                else
                {
                    Vec2 desiredVelocity = (AbsoluteHuntPosition - End.Position).Normalized * HuntSpeed;
                    End.Velocity = Vec2.Lerp(End.Velocity, desiredVelocity, Quickness);
                    ReachedSnapPosition = false;
                }
            }
            else if (Mode == LimbMode.Retracted)
            {
                End.Velocity = connectionVelocity;
                End.Position = connection;
                ReachedSnapPosition = true;
            }
            else
            {
                ReachedSnapPosition = false;
            }

            Quickness = defaultQuickness;
            HuntSpeed = defaultHuntSpeed;
            if (Mode != LimbMode.Retracted)
            {
                End.Position += End.Velocity;
                if (Mode == LimbMode.HuntRelativePosition) End.Position += connectionVelocity;
                End.Velocity *= End.AirFriction;
            }
        }

        private bool EngageInMovement(Slugcat player, DesktopCollisionWorld world, Limb leadLimb,
            double airborneCounter)
        {
            SlugcatState state = player.State;
            Vec2 connection = player.BodyChunks[0].Position;
            Vec2 velocity = player.BodyChunks[0].Velocity;
            bool unused = true;

            if (state.BodyMode == BodyModeIndex.Crawl)
            {
                unused = false;
                Mode = LimbMode.HuntAbsolutePosition;
                HuntSpeed = 12.0;
                Quickness = 0.7;
                FindCrawlGrip(player, world, leadLimb);
            }
            else if (state.BodyMode == BodyModeIndex.CorridorClimb)
            {
                unused = false;
                Mode = LimbMode.HuntAbsolutePosition;
                Vec2 bodyDirection = (player.BodyChunks[0].Position - player.BodyChunks[1].Position).Normalized;
                Vec2 input = new Vec2(player.LastInput.X, player.LastInput.Y).Normalized;
                Vec2 goal = connection + (bodyDirection + input * 1.5).Normalized * 20.0 +
                    bodyDirection.Perpendicular * (6.0 - 12.0 * limbNumber);
                if (!FindGrip(world, connection, goal, 20.0)) Mode = LimbMode.Dangle;
            }
            else if (state.BodyMode == BodyModeIndex.WallClimb)
            {
                unused = false;
                Mode = LimbMode.HuntAbsolutePosition;
                double wallX;
                long wallId;
                DesktopSurfaceKind wallKind;
                if (!world.TryGetWall(connection.X, connection.Y, state.Facing, 30.0,
                    out wallX, out wallId, out wallKind))
                {
                    wallX = connection.X + state.Facing * 10.0;
                    GripSurfaceId = 0;
                }
                else
                {
                    GripSurfaceId = wallId;
                    GripSurfaceKind = wallKind;
                }
                AbsoluteHuntPosition.X = wallX;
                bool lowHand = (limbNumber == 0) == (state.Facing == -1);
                AbsoluteHuntPosition.Y = connection.Y + (lowHand ? 7.0 : -3.0);
            }
            else if (state.BodyMode == BodyModeIndex.Default &&
                     velocity.Length > 4.0 &&
                     airborneCounter > 180.0)
            {
                unused = false;
                RetractCounter = 0;
                Mode = LimbMode.HuntRelativePosition;
                GripSurfaceId = 0;
                bool chestPastHips = player.BodyChunks[0].Position.Y <
                    player.BodyChunks[1].Position.Y - 5.0;
                double originalRelativeY = -velocity.Y *
                    (chestPastHips ? -3.0 : -0.9) +
                    Math.Abs(velocity.X * 0.6) + 1.0;
                RelativeHuntPosition = new Vec2(
                    (Math.Abs(velocity.X) + 4.0) * (-1.0 + 2.0 * limbNumber),
                    -originalRelativeY);
                HuntSpeed = 8.0;
                Quickness = 0.6;
            }

            if (state.Animation == AnimationIndex.DownOnFours ||
                state.Animation == AnimationIndex.CrawlTurn)
            {
                unused = false;
                Mode = LimbMode.HuntAbsolutePosition;
                GripSurfaceId = 0;
                Vec2 normalizedVelocity = velocity.Normalized;
                AbsoluteHuntPosition = connection + new Vec2(
                    -6.0 + 12.0 * limbNumber + normalizedVelocity.X * 20.0,
                    Math.Abs(normalizedVelocity.Y) * 20.0);
            }

            return unused;
        }

        private void FindCrawlGrip(Slugcat player, DesktopCollisionWorld world, Limb leadLimb)
        {
            Vec2 connection = player.BodyChunks[0].Position;
            if (player.State.Animation == AnimationIndex.DownOnFours ||
                player.State.Animation == AnimationIndex.CrawlTurn)
            {
                GripSurfaceId = 0;
                return;
            }
            // SlugcatHand.EngageInMovement lets hand 0 establish the next
            // Crawl grip. Hand 1 may search only after hand 0 is planted near
            // the upper chunk; both retain their previous target within 29u.
            if (limbNumber != 0 && (leadLimb == null ||
                Math.Abs(leadLimb.End.Position.X - connection.X) >= 10.0 ||
                !leadLimb.ReachedSnapPosition)) return;
            if (Vec2.Distance(connection, AbsoluteHuntPosition) < 29.0) return;

            Vec2 goal = new Vec2(connection.X + player.State.Facing * 28.0, connection.Y + 10.0);
            FindGrip(world, connection, goal, 100.0);
        }

        // Desktop Room adapter for Limb.FindGrip: choose the nearest exposed
        // horizontal/vertical surface point to goal within the arm constraint.
        private bool FindGrip(DesktopCollisionWorld world, Vec2 attachedPosition,
            Vec2 goalPosition, double maximumRadius)
        {
            Vec2 best = AbsoluteHuntPosition;
            double bestDistance = double.MaxValue;
            DesktopSurface bestSurface = null;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                Vec2 candidate;
                if (surface.IsHorizontal)
                {
                    candidate = new Vec2(MathUtil.Clamp(goalPosition.X, surface.Left, surface.Right), surface.Top);
                }
                else
                {
                    candidate = new Vec2(surface.Left,
                        MathUtil.Clamp(goalPosition.Y, surface.Top, surface.Bottom));
                }
                if (Vec2.Distance(attachedPosition, candidate) > maximumRadius) continue;
                double distance = (candidate - goalPosition).LengthSquared;
                if (distance >= bestDistance) continue;
                best = candidate;
                bestDistance = distance;
                bestSurface = surface;
            }
            if (bestDistance < double.MaxValue)
            {
                Mode = LimbMode.HuntAbsolutePosition;
                AbsoluteHuntPosition = best;
                GripSurfaceId = bestSurface.Id;
                GripSurfaceKind = bestSurface.Kind;
                return true;
            }
            GripSurfaceId = 0;
            return false;
        }

        private void ReleaseSurfaceGrip()
        {
            GripSurfaceId = 0;
            GripSurfaceKind = DesktopSurfaceKind.ScreenEdge;
            ReachedSnapPosition = false;
            Mode = LimbMode.Dangle;
            AbsoluteHuntPosition = End.Position;
        }

        private static Vec2 RotateRelative(Vec2 relative, Vec2 rotationChunk, Vec2 connection)
        {
            double angle = AimScreen(rotationChunk, connection);
            double radians = angle * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new Vec2(relative.X * cosine - relative.Y * sine,
                relative.X * sine + relative.Y * cosine);
        }

        private static double AimScreen(Vec2 from, Vec2 to)
        {
            return Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI + 90.0;
        }

        public Vec2 RenderPosition(double interpolation)
        {
            return End.RenderPosition(interpolation);
        }

        public Vec2 ComputeJoint(Vec2 start, Vec2 end, double interpolation)
        {
            Vec2 delta = end - start;
            double distance = Math.Max(0.001, Math.Min(delta.Length, Length * 0.995));
            Vec2 direction = delta / distance;
            double half = Length * 0.5;
            double height = Math.Sqrt(Math.Max(0.0, half * half - distance * distance * 0.25));
            return start + direction * (distance * 0.5) + direction.Perpendicular * height * Side;
        }

        public void Translate(Vec2 delta)
        {
            End.Translate(delta);
            AbsoluteHuntPosition += delta;
            TargetPosition += delta;
            ConnectionPosition += delta;
            LastConnectionPosition += delta;
        }
    }
}
