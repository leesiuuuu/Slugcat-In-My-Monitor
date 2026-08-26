using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    public enum DesktopFoodKind
    {
        DangleFruit,
        EggBugEgg
    }

    public enum DesktopFoodState
    {
        Free,
        Claimed,
        Held,
        Biting,
        Dragged,
        Ignored,
        Consumed,
        Expired
    }

    // A deliberately small desktop equivalent of Rain World's IPlayerEdible
    // contract. It preserves the item's visible and edible behavior without
    // importing Room/AbstractPhysicalObject/Creature graphs into the overlay.
    public sealed class DesktopFood
    {
        private static readonly string[] FrontElements =
            { "DangleFruit0A", "DangleFruit1A", "DangleFruit2A" };
        private static readonly string[] BackElements =
            { "DangleFruit0B", "DangleFruit1B", "DangleFruit2B" };
        public const int DangleFruitInitialBites = 3;
        public const int DangleFruitFoodPoints = 1;
        public const double DangleFruitRadius = 8.0;
        public const double DangleFruitVisualReach = 13.0;
        public const int EggBugEggInitialBites = 2;
        public const int EggBugEggFoodPoints = 1;
        public const double EggBugEggRadius = 4.6;
        public const int EggBugEggTailSegmentCount = 5;
        // Includes the flexible tail, whose final point extends about 22
        // simulation units from the BodyChunk center.
        public const double EggBugEggVisualReach = 23.0;
        // Kept for API/test compatibility. Shared desktop food no longer expires
        // merely because it has been left on the desktop for this many ticks.
        public const int DefaultLifetimeTicks = 1200;

        private const double Gravity = 0.9;
        private const double AirFriction = 0.999;
        private const double SurfaceFriction = 0.7;
        private const double Bounce = 0.2;
        private Vec2 rotation;
        private Vec2 lastRotation;
        private double rotationVelocity;
        private DesktopFoodState stateBeforeDrag;
        private readonly Vec2[] eggTailPositions;
        private readonly Vec2[] eggTailLastPositions;
        private readonly Vec2[] eggTailVelocities;

        public DesktopFood(DesktopFoodKind kind, Vec2 position)
            : this(kind, position, 0.13)
        {
        }

        public DesktopFood(DesktopFoodKind kind, Vec2 position, double visualHue)
            : this(kind, position, visualHue, Vec2.Up)
        {
        }

        public DesktopFood(DesktopFoodKind kind, Vec2 position, double visualHue,
            Vec2 initialRotation)
        {
            if (kind != DesktopFoodKind.DangleFruit &&
                kind != DesktopFoodKind.EggBugEgg)
                throw new ArgumentOutOfRangeException("kind", kind,
                    "Unknown desktop food kind.");
            Kind = kind;
            bool egg = kind == DesktopFoodKind.EggBugEgg;
            Chunk = new BodyChunk(0, position,
                egg ? EggBugEggRadius : DangleFruitRadius, 0.2);
            State = DesktopFoodState.Free;
            InitialBites = egg ? EggBugEggInitialBites : DangleFruitInitialBites;
            BitesRemaining = InitialBites;
            FoodPoints = egg ? EggBugEggFoodPoints : DangleFruitFoodPoints;
            VisualHue = visualHue - Math.Floor(visualHue);
            rotation = initialRotation.LengthSquared > 0.000001
                ? initialRotation.Normalized : Vec2.Up;
            lastRotation = rotation;
            if (egg)
            {
                eggTailPositions = new Vec2[EggBugEggTailSegmentCount];
                eggTailLastPositions = new Vec2[EggBugEggTailSegmentCount];
                eggTailVelocities = new Vec2[EggBugEggTailSegmentCount];
                ResetEggTail();
            }
        }

        public DesktopFoodKind Kind { get; private set; }
        public readonly BodyChunk Chunk;
        public DesktopFoodState State { get; private set; }
        public int InitialBites { get; private set; }
        public int BitesRemaining { get; private set; }
        public int FoodPoints { get; private set; }
        public double VisualHue { get; private set; }
        public int AgeTicks { get; private set; }
        public Vec2 Rotation { get { return rotation; } }
        public Vec2 LastRotation { get { return lastRotation; } }
        public bool HasVisibleEggTail
        {
            get
            {
                return Kind == DesktopFoodKind.EggBugEgg &&
                    BitesRemaining == EggBugEggInitialBites;
            }
        }
        public double VisualReach
        {
            get
            {
                return Kind == DesktopFoodKind.EggBugEgg
                    ? EggBugEggVisualReach : DangleFruitVisualReach;
            }
        }
        public bool IsActive
        {
            get
            {
                return State != DesktopFoodState.Consumed &&
                    State != DesktopFoodState.Expired;
            }
        }
        public bool IsPhysical
        {
            get
            {
                return State == DesktopFoodState.Free ||
                    State == DesktopFoodState.Claimed ||
                    State == DesktopFoodState.Ignored;
            }
        }
        public bool IsDraggable
        {
            get
            {
                return State == DesktopFoodState.Free ||
                    State == DesktopFoodState.Claimed ||
                    State == DesktopFoodState.Ignored;
            }
        }
        public int SpriteFrame
        {
            get { return MathUtil.Clamp(InitialBites - BitesRemaining, 0, InitialBites - 1); }
        }
        public string FrontElement
        {
            get
            {
                return Kind == DesktopFoodKind.EggBugEgg
                    ? (SpriteFrame == 0 ? "DangleFruit0A" : "DangleFruit1A")
                    : FrontElements[SpriteFrame];
            }
        }
        public string BackElement
        {
            get
            {
                return Kind == DesktopFoodKind.EggBugEgg
                    ? (SpriteFrame == 0 ? "EggBugEggColor" : "EggBugEggColorEaten")
                    : BackElements[SpriteFrame];
            }
        }
        public string DetailElement
        {
            get { return Kind == DesktopFoodKind.EggBugEgg ? "JetFishEyeA" : null; }
        }

        public Vec2 EggTailPosition(int index, double interpolation)
        {
            ValidateEggTailIndex(index);
            return Vec2.Lerp(eggTailLastPositions[index],
                eggTailPositions[index], interpolation);
        }

        public Vec2 EggTailVelocity(int index)
        {
            ValidateEggTailIndex(index);
            return eggTailVelocities[index];
        }

        public void SetCreationVelocity(Vec2 velocity)
        {
            Chunk.Velocity = velocity;
        }

        public bool BeginDrag()
        {
            if (!IsDraggable) return false;
            stateBeforeDrag = State;
            State = DesktopFoodState.Dragged;
            Chunk.BeginTick();
            Chunk.LastPosition = Chunk.Position;
            Chunk.Velocity = Vec2.Zero;
            return true;
        }

        public void DragTo(Vec2 position)
        {
            if (State != DesktopFoodState.Dragged) return;
            Chunk.LastPosition = Chunk.Position;
            Chunk.Position = position;
            Chunk.Velocity = Vec2.Zero;
        }

        public bool EndDrag(Vec2 velocity)
        {
            if (State != DesktopFoodState.Dragged) return false;
            State = stateBeforeDrag;
            Chunk.LastPosition = Chunk.Position;
            Chunk.Velocity = velocity;
            return true;
        }

        public bool Claim()
        {
            if (State != DesktopFoodState.Free) return false;
            State = DesktopFoodState.Claimed;
            return true;
        }

        // Shared-food reservation is external to the edible object. When a
        // seeker abandons an item, make the physical object globally available
        // again without recreating it or changing its bite state.
        public bool ReleaseClaim()
        {
            if (State != DesktopFoodState.Claimed &&
                State != DesktopFoodState.Ignored) return false;
            State = DesktopFoodState.Free;
            return true;
        }

        public bool Ignore()
        {
            if (State != DesktopFoodState.Free) return false;
            State = DesktopFoodState.Ignored;
            return true;
        }

        public bool PickUp(Vec2 position)
        {
            if (State != DesktopFoodState.Free &&
                State != DesktopFoodState.Claimed) return false;
            State = DesktopFoodState.Held;
            HoldAt(position);
            return true;
        }

        public void HoldAt(Vec2 position)
        {
            HoldAt(position, null, false);
        }

        public void HoldAt(Vec2 position, Vec2 holderPosition)
        {
            HoldAt(position, (Vec2?)holderPosition, true);
        }

        private void HoldAt(Vec2 position, Vec2? holderPosition,
            bool advanceEggTail)
        {
            if (State != DesktopFoodState.Held &&
                State != DesktopFoodState.Biting) return;
            Chunk.LastPosition = Chunk.Position;
            Chunk.Position = position;
            Chunk.Velocity = Vec2.Zero;
            if (holderPosition.HasValue)
            {
                Vec2 towardHolder = holderPosition.Value - position;
                if (towardHolder.LengthSquared > 0.000001)
                {
                    lastRotation = rotation;
                    // Y-down form of Perpendicular(DirVec(item, grabber)) with
                    // the original positive-Y clamp applied before conversion.
                    rotation = -towardHolder.Normalized.Perpendicular;
                    rotation.Y = -Math.Abs(rotation.Y);
                    rotation = rotation.Normalized;
                }
            }
            ApplyEggRotationVelocity();
            if (advanceEggTail) StepEggTail();
        }

        public bool BeginBiting()
        {
            if (State != DesktopFoodState.Held) return false;
            State = DesktopFoodState.Biting;
            return true;
        }

        public bool Bite()
        {
            if (State != DesktopFoodState.Biting || BitesRemaining <= 0) return false;
            BitesRemaining--;
            if (BitesRemaining == 0) State = DesktopFoodState.Consumed;
            return true;
        }

        public void Drop(Vec2 velocity)
        {
            if (State != DesktopFoodState.Held &&
                State != DesktopFoodState.Biting) return;
            // Keep the previous appetite decision after an interrupted bite.
            // The owning manager can reacquire a dropped accepted item without
            // rerolling it into an ignored one.
            State = DesktopFoodState.Claimed;
            Chunk.Velocity = velocity;
        }

        public void StepPhysics(DesktopCollisionWorld world)
        {
            if (world == null) throw new ArgumentNullException("world");
            if (!IsPhysical)
            {
                if (State == DesktopFoodState.Dragged) StepEggTail();
                return;
            }

            // Food remains in the shared desktop pool until it is eaten or the
            // user explicitly clears it. Age is still tracked for procedural
            // animation/debugging, but it no longer turns the item Expired.
            AgeTicks++;

            lastRotation = rotation;
            Chunk.BeginTick();
            Chunk.Integrate(Gravity, AirFriction);
            world.Resolve(Chunk, world.CurrentSnapshot, 0, SurfaceFriction, Bounce);

            // DangleFruit.Update and EggBugEgg.Update leave airborne rotation
            // untouched. Both rotate only from their floor-contact branch and
            // apply the same object-specific 0.8 horizontal damping after the
            // shared BodyChunk collision response.
            if (Chunk.ContactFloor)
            {
                if (Kind == DesktopFoodKind.DangleFruit)
                {
                    rotation = (rotation + rotation.Perpendicular *
                        (0.1 * Chunk.Velocity.X)).Normalized;
                }
                else
                {
                    rotationVelocity = MathUtil.Lerp(rotationVelocity,
                        0.12 * Chunk.Velocity.X, 0.8);
                }
                Chunk.Velocity.X *= 0.8;
            }

            // The retail branch only advances positive rotVel. On a desktop
            // floor that makes leftward eggs visually lock while rightward eggs
            // roll, so preserve the same signed equation in both directions.
            ApplyEggRotationVelocity();
            StepEggTail();
        }

        private void ApplyEggRotationVelocity()
        {
            if (Kind == DesktopFoodKind.EggBugEgg &&
                Math.Abs(rotationVelocity) > 0.000001)
                rotation = (rotation + rotation.Perpendicular *
                    rotationVelocity).Normalized;
        }

        private void ResetEggTail()
        {
            if (Kind != DesktopFoodKind.EggBugEgg) return;
            for (int i = 0; i < EggBugEggTailSegmentCount; i++)
            {
                eggTailPositions[i] = Chunk.Position + rotation * i;
                eggTailLastPositions[i] = eggTailPositions[i];
                eggTailVelocities[i] = Vec2.Zero;
            }
        }

        private void StepEggTail()
        {
            if (Kind != DesktopFoodKind.EggBugEgg) return;
            for (int i = 0; i < EggBugEggTailSegmentCount; i++)
            {
                double value = i / (double)(EggBugEggTailSegmentCount - 1);
                eggTailLastPositions[i] = eggTailPositions[i];
                eggTailPositions[i] += eggTailVelocities[i];
                eggTailVelocities[i] *= 0.995;
                eggTailVelocities[i].Y += Gravity *
                    MathUtil.InverseLerp(0.5, 1.0, value);
                eggTailVelocities[i] += rotation * (5.0 *
                    MathUtil.InverseLerp(0.5, 0.0, value));
                if (i > 1)
                {
                    Vec2 separation = MathUtil.Direction(
                        eggTailPositions[i - 2], eggTailPositions[i]);
                    eggTailVelocities[i] += separation;
                    eggTailVelocities[i - 2] -= separation;
                }
                ConnectEggTailSegment(i);
            }
            for (int i = EggBugEggTailSegmentCount - 1; i >= 0; i--)
                ConnectEggTailSegment(i);
            for (int i = 0; i < EggBugEggTailSegmentCount; i++)
                ConnectEggTailSegment(i);
        }

        private void ConnectEggTailSegment(int index)
        {
            if (index == 0)
            {
                Vec2 target = Chunk.Position + rotation * (7.0 * 1.15);
                Vec2 direction = MathUtil.Direction(eggTailPositions[index], target);
                double distance = Vec2.Distance(eggTailPositions[index], target);
                Vec2 correction = direction * (2.0 - distance);
                eggTailPositions[index] -= correction;
                eggTailVelocities[index] -= correction;
                return;
            }

            Vec2 towardPrevious = MathUtil.Direction(eggTailPositions[index],
                eggTailPositions[index - 1]);
            double segmentDistance = Vec2.Distance(eggTailPositions[index],
                eggTailPositions[index - 1]);
            Vec2 sharedCorrection = towardPrevious *
                ((2.0 - segmentDistance) * 0.5);
            eggTailPositions[index] -= sharedCorrection;
            eggTailVelocities[index] -= sharedCorrection;
            eggTailPositions[index - 1] += sharedCorrection;
            eggTailVelocities[index - 1] += sharedCorrection;
        }

        private void ValidateEggTailIndex(int index)
        {
            if (Kind != DesktopFoodKind.EggBugEgg)
                throw new InvalidOperationException(
                    "Only EggBugEgg has procedural tail segments.");
            if (index < 0 || index >= EggBugEggTailSegmentCount)
                throw new ArgumentOutOfRangeException("index", index,
                    "EggBugEgg tail segment index is out of range.");
        }

        public void ApplyMovingSurfaceDelta(DesktopCollisionWorld world)
        {
            if (world == null || !IsPhysical || Chunk.SupportingSurfaceId == 0) return;
            Vec2 delta = world.GetSurfaceMovement(Chunk.SupportingSurfaceId,
                Chunk.SupportingSurfaceKind);
            Chunk.Position += delta;
            Chunk.LastPosition += delta;
            if (Kind == DesktopFoodKind.EggBugEgg)
            {
                for (int i = 0; i < EggBugEggTailSegmentCount; i++)
                {
                    eggTailPositions[i] += delta;
                    eggTailLastPositions[i] += delta;
                }
            }
        }
    }
}
