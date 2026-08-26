using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Core
{
    public enum FoodInteractionState
    {
        None,
        Seeking,
        Holding,
        Eating
    }

    internal sealed class DesktopFoodPool
    {
        private readonly List<DesktopFood> foods = new List<DesktopFood>();
        private readonly IList<DesktopFood> foodView;
        private readonly Dictionary<DesktopFood, WeakReference> reservations =
            new Dictionary<DesktopFood, WeakReference>();
        private readonly List<WeakReference> managers = new List<WeakReference>();
        private long physicsSerial;
        private long lastSurfaceSerial = long.MinValue;

        public DesktopFoodPool(bool sharedWorld)
        {
            IsSharedWorld = sharedWorld;
            foodView = foods.AsReadOnly();
        }

        public bool IsSharedWorld { get; private set; }
        public IList<DesktopFood> View { get { return foodView; } }
        public List<DesktopFood> Items { get { return foods; } }
        public long PhysicsSerial { get { return physicsSerial; } }
        public int Count { get { return foods.Count; } }

        public void Register(DesktopFoodManager manager)
        {
            if (manager == null) return;
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                DesktopFoodManager existing = managers[i].Target as DesktopFoodManager;
                if (existing == null)
                {
                    managers.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(existing, manager)) return;
            }
            managers.Add(new WeakReference(manager));
        }

        public void ImportFrom(DesktopFoodPool source)
        {
            if (source == null || ReferenceEquals(source, this)) return;
            source.RemoveInactive();
            for (int i = 0; i < source.foods.Count &&
                foods.Count < DesktopFoodManager.MaximumActiveFoods; i++)
            {
                DesktopFood food = source.foods[i];
                if (!food.IsActive) continue;
                // A per-Slugcat appetite decision must not leak into the shared
                // world. Once the manager joins the desktop pool, unheld food is
                // available to every hungry Slugcat again.
                if (food.State == DesktopFoodState.Claimed ||
                    food.State == DesktopFoodState.Ignored)
                    food.ReleaseClaim();
                foods.Add(food);
            }
            source.foods.Clear();
            source.reservations.Clear();
        }

        public bool Contains(DesktopFood food)
        {
            return food != null && foods.Contains(food);
        }

        public bool Add(DesktopFood food)
        {
            if (food == null || foods.Count >= DesktopFoodManager.MaximumActiveFoods)
                return false;
            foods.Add(food);
            return true;
        }

        public void Remove(DesktopFood food)
        {
            if (food == null) return;
            foods.Remove(food);
            reservations.Remove(food);
        }

        public void ClearAll()
        {
            foods.Clear();
            reservations.Clear();
        }

        public void RemoveInactive()
        {
            for (int i = foods.Count - 1; i >= 0; i--)
            {
                if (foods[i].IsActive) continue;
                reservations.Remove(foods[i]);
                foods.RemoveAt(i);
            }
        }

        public bool TryAdvancePhysics(long serial)
        {
            if (serial <= physicsSerial) return false;
            physicsSerial = serial;
            return true;
        }

        public bool TryBeginSurfaceApply(long serial)
        {
            if (!IsSharedWorld) return true;
            if (serial == lastSurfaceSerial) return false;
            lastSurfaceSerial = serial;
            return true;
        }

        public bool IsDisplayOwner(DesktopFoodManager manager)
        {
            if (!IsSharedWorld) return true;
            DesktopFoodManager best = null;
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                DesktopFoodManager candidate = managers[i].Target as DesktopFoodManager;
                if (candidate == null || !ReferenceEquals(candidate.Pool, this))
                {
                    managers.RemoveAt(i);
                    continue;
                }
                if (best == null || candidate.LastPoolStepSerial > best.LastPoolStepSerial ||
                    (candidate.LastPoolStepSerial == best.LastPoolStepSerial &&
                        candidate.ManagerId < best.ManagerId))
                    best = candidate;
            }
            return best == null || ReferenceEquals(best, manager);
        }

        // Arbitration happens before a reservation is made. The currently
        // hungriest active Slugcat wins; equal fullness is broken by manager id.
        // A Slugcat that already has a food target does not block another food.
        public bool CanReserveFor(DesktopFoodManager manager)
        {
            if (!IsSharedWorld || manager == null) return true;
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                DesktopFoodManager candidate = managers[i].Target as DesktopFoodManager;
                if (candidate == null || !ReferenceEquals(candidate.Pool, this))
                {
                    managers.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(candidate, manager) || !candidate.WantsSharedFood)
                    continue;
                if (candidate.LastPoolStepSerial + 4 < physicsSerial) continue;
                if (candidate.Fullness < manager.Fullness - 0.000001) return false;
                if (Math.Abs(candidate.Fullness - manager.Fullness) <= 0.000001 &&
                    candidate.ManagerId < manager.ManagerId) return false;
            }
            return true;
        }

        public bool TryReserve(DesktopFood food, DesktopFoodManager manager)
        {
            if (food == null || manager == null || !Contains(food) || !food.IsActive)
                return false;
            DesktopFoodManager owner = ReservationOwner(food);
            if (owner != null) return ReferenceEquals(owner, manager);
            if (food.State == DesktopFoodState.Claimed)
                food.ReleaseClaim();
            if (food.State != DesktopFoodState.Free || !food.Claim()) return false;
            reservations[food] = new WeakReference(manager);
            return true;
        }

        public bool IsReservedBy(DesktopFood food, DesktopFoodManager manager)
        {
            return ReferenceEquals(ReservationOwner(food), manager);
        }

        public void ReleaseReservation(DesktopFood food, DesktopFoodManager manager,
            bool makeFree)
        {
            if (food == null) return;
            DesktopFoodManager owner = ReservationOwner(food);
            if (owner != null && !ReferenceEquals(owner, manager)) return;
            reservations.Remove(food);
            if (!makeFree) return;
            if (food.State == DesktopFoodState.Held ||
                food.State == DesktopFoodState.Biting)
                food.Drop(Vec2.Zero);
            if (food.State == DesktopFoodState.Claimed ||
                food.State == DesktopFoodState.Ignored)
                food.ReleaseClaim();
        }

        public void ForceReleaseForDrag(DesktopFood food)
        {
            if (food == null) return;
            reservations.Remove(food);
            if (food.State == DesktopFoodState.Claimed ||
                food.State == DesktopFoodState.Ignored)
                food.ReleaseClaim();
        }

        private DesktopFoodManager ReservationOwner(DesktopFood food)
        {
            WeakReference reference;
            if (!reservations.TryGetValue(food, out reference)) return null;
            DesktopFoodManager owner = reference.Target as DesktopFoodManager;
            if (owner == null || !ReferenceEquals(owner.Pool, this))
            {
                reservations.Remove(food);
                if (food.State == DesktopFoodState.Held ||
                    food.State == DesktopFoodState.Biting)
                    food.Drop(Vec2.Zero);
                if (food.State == DesktopFoodState.Claimed)
                    food.ReleaseClaim();
                return null;
            }

            // A removed/paused pet must not reserve a floor item forever. Held
            // and biting food stays protected until its owner actually vanishes,
            // which prevents another Slugcat from stealing it out of a hand.
            if (food.State == DesktopFoodState.Claimed &&
                owner.LastPoolStepSerial + 4 < physicsSerial)
            {
                reservations.Remove(food);
                food.ReleaseClaim();
                return null;
            }
            return owner;
        }
    }

    // Per-Slugcat hunger/animation state lives here, while managers attached to
    // the same DesktopCollisionWorld share one DesktopFoodPool. This keeps food
    // global without merging each Slugcat's fullness or hand animation state.
    public sealed class DesktopFoodManager
    {
        public const int MaximumActiveFoods = 12;
        public const double MaximumFullness = 3.0;
        public const int DigestionTicksPerFoodPoint = 3600;
        private const double SharedHungerThreshold = 0.5;
        private const double ApproachDistance = 17.0;
        private const double PickupDistance = 25.0;
        private const double PickupVerticalTolerance = 32.0;
        private const int InitialEatCounter = 40;
        private const int BiteIntervalTicks = 15;
        private const int SpearmasterMinimumHoldTicks =
            (int)SimulationConstants.LogicTicksPerSecond;
        private const int SpearmasterMaximumHoldTicks =
            (int)(SimulationConstants.LogicTicksPerSecond * 3.0);
        private const double TossAngleDegrees = 60.0;
        private const double TossSpeed = 12.5;
        private const double FoodHandReachDistance = 34.0;
        private const double SeekingHandBlend = 0.34;

        private static readonly ConditionalWeakTable<DesktopCollisionWorld, DesktopFoodPool>
            SharedPools = new ConditionalWeakTable<DesktopCollisionWorld, DesktopFoodPool>();
        private static readonly IList<DesktopFood> EmptyFoodView = new DesktopFood[0];
        private static int nextManagerId;

        private DesktopFoodPool pool;
        private readonly Random random;
        private readonly Random rotationRandom;
        private readonly HashSet<DesktopFood> spearmasterRejectedFoods =
            new HashSet<DesktopFood>();
        private DesktopFood target;
        private DesktopFood draggedFood;
        private Vec2 dragOffset;
        private int interactionCountdown;
        private int foodHand = -1;
        private double fullness;
        private bool sharedHungry = true;
        private bool placementDropActive;
        private bool placementReadyForClick;
        private bool placementLastLeftDown;
        private bool spearmasterFoodRulesActive;
        private long physicsSerial;
        private long lastPoolStepSerial;
        private readonly int managerId;

        public DesktopFoodManager()
            : this(Environment.TickCount)
        {
        }

        public DesktopFoodManager(int randomSeed)
        {
            managerId = Interlocked.Increment(ref nextManagerId);
            pool = new DesktopFoodPool(false);
            pool.Register(this);
            random = new Random(randomSeed);
            rotationRandom = new Random(unchecked(randomSeed ^ 0x53C7A1));
        }

        internal DesktopFoodPool Pool { get { return pool; } }
        internal long LastPoolStepSerial { get { return lastPoolStepSerial; } }
        internal int ManagerId { get { return managerId; } }
        internal bool WantsSharedFood
        {
            get
            {
                return pool.IsSharedWorld && sharedHungry && target == null &&
                    draggedFood == null && (!spearmasterFoodRulesActive ||
                        HasAvailableSharedFood());
            }
        }

        // Only one manager exposes the shared list to composition. Logic always
        // uses pool.Items internally, so every Slugcat still sees every food.
        // This avoids drawing/stepping the same shared object once per pet.
        public IList<DesktopFood> Foods
        {
            get
            {
                return !pool.IsSharedWorld || pool.IsDisplayOwner(this)
                    ? pool.View : EmptyFoodView;
            }
        }
        public DesktopFood Target { get { return target; } }
        public DesktopFood DraggedFood { get { return draggedFood; } }
        public bool IsDragging { get { return draggedFood != null; } }
        public FoodInteractionState InteractionState { get; private set; }
        public int FoodPointsEaten { get; private set; }
        public int TotalBites { get; private set; }
        public string LastEvent { get; private set; }
        public bool LastSpawnAccepted { get; private set; }
        public double Fullness { get { return fullness; } }
        public double FullnessRatio { get { return fullness / MaximumFullness; } }

        public bool TryAddDangleFruit(Vec2 position)
        {
            RemoveInactive();
            if (pool.Count >= MaximumActiveFoods) return false;
            DesktopFood fruit = new DesktopFood(DesktopFoodKind.DangleFruit,
                position, 0.13, RandomItemRotation());
            if (!pool.Add(fruit)) return false;
            LastEvent = "DangleFruit_Spawn";
            return true;
        }

        public bool TryAddEggBugEgg(Vec2 position)
        {
            RemoveInactive();
            if (pool.Count >= MaximumActiveFoods) return false;
            DesktopFood egg = new DesktopFood(DesktopFoodKind.EggBugEgg, position,
                FoodRenderPalette.CreateNormalEggHue(random), RandomItemRotation());
            if (!pool.Add(egg)) return false;
            LastEvent = "EggBugEgg_Spawn";
            return true;
        }

        public bool TrySpawnDangleFruit(Slugcat slugcat, DesktopCollisionWorld world)
        {
            return TrySpawnFood(DesktopFoodKind.DangleFruit, slugcat, world);
        }

        public bool TrySpawnEggBugEgg(Slugcat slugcat, DesktopCollisionWorld world)
        {
            return TrySpawnFood(DesktopFoodKind.EggBugEgg, slugcat, world);
        }

        private bool TrySpawnFood(DesktopFoodKind kind, Slugcat slugcat,
            DesktopCollisionWorld world)
        {
            if (slugcat == null || world == null) return false;

            // Active GameLoops have already joined their shared world from
            // StepPhysics. In that mode the tray action creates a cursor-held
            // shared item; the next distinct left click drops it.
            if (pool.IsSharedWorld)
                return TryBeginCursorPlacement(kind, slugcat.Center);

            // Keep the old isolated-manager behavior for previews/unit tests and
            // callers that are not attached to a running desktop world.
            return TrySpawnLegacyFood(kind, slugcat, world);
        }

        private bool TryBeginCursorPlacement(DesktopFoodKind kind, Vec2 fallbackPosition)
        {
            RemoveInactive();
            if (draggedFood != null || pool.Count >= MaximumActiveFoods) return false;
            for (int i = 0; i < pool.Items.Count; i++)
                if (pool.Items[i].State == DesktopFoodState.Dragged) return false;

            NativeMethods.Point point;
            Vec2 position = NativeMethods.GetCursorPos(out point)
                ? DesktopWorldTransform.ToSimulation(new Vec2(point.X, point.Y))
                : fallbackPosition;
            double visualHue = kind == DesktopFoodKind.EggBugEgg
                ? FoodRenderPalette.CreateNormalEggHue(random) : 0.0;
            DesktopFood food = new DesktopFood(kind, position, visualHue,
                RandomItemRotation());
            if (!pool.Add(food) || !food.BeginDrag())
            {
                pool.Remove(food);
                return false;
            }

            draggedFood = food;
            dragOffset = Vec2.Zero;
            placementDropActive = true;
            placementLastLeftDown = IsLeftMouseDown();
            placementReadyForClick = !placementLastLeftDown;
            LastSpawnAccepted = true;
            LastEvent = FoodEventName(food, "Spawn_PendingDrop");
            return true;
        }

        private bool TrySpawnLegacyFood(DesktopFoodKind kind, Slugcat slugcat,
            DesktopCollisionWorld world)
        {
            RemoveInactive();
            if (pool.Count >= MaximumActiveFoods) return false;

            double radius = kind == DesktopFoodKind.EggBugEgg
                ? DesktopFood.EggBugEggRadius : DesktopFood.DangleFruitRadius;
            double minimumDistance = DesktopWorldTransform.ToSimulationLength(140.0);
            double maximumDistance = DesktopWorldTransform.ToSimulationLength(360.0);
            double distance = MathUtil.Lerp(minimumDistance, maximumDistance,
                random.NextDouble());
            int facing = slugcat.State.Facing == 0 ? 1 : slugcat.State.Facing;
            int direction = random.NextDouble() < 0.68 ? facing : -facing;
            double x = slugcat.Center.X + direction * distance;
            double y;
            double left;
            double right;
            DesktopSurface surface;
            if (slugcat.PrimarySupportingSurfaceId != 0 && world.TryGetSurface(
                slugcat.PrimarySupportingSurfaceId, slugcat.PrimarySupportingSurfaceKind,
                out surface) && surface.IsHorizontal)
            {
                left = surface.Left + radius + 3.0;
                right = surface.Right - radius - 3.0;
                y = surface.Top - radius;
            }
            else
            {
                MonitorInfo monitor = world.FindMonitor(slugcat.Center);
                left = DesktopWorldTransform.ToSimulationLength(monitor.WorkArea.Left) +
                    radius + 3.0;
                right = DesktopWorldTransform.ToSimulationLength(monitor.WorkArea.Right) -
                    radius - 3.0;
                y = DesktopWorldTransform.ToSimulationLength(monitor.FloorY) - radius;
            }

            if (right <= left) return false;
            x = MathUtil.Clamp(x, left, right);
            if (Math.Abs(x - slugcat.Center.X) < minimumDistance)
            {
                double opposite = MathUtil.Clamp(slugcat.Center.X - direction * distance,
                    left, right);
                if (Math.Abs(opposite - slugcat.Center.X) > Math.Abs(x - slugcat.Center.X))
                    x = opposite;
            }

            double dropHeight = DesktopWorldTransform.ToSimulationLength(
                MathUtil.Lerp(45.0, 120.0, random.NextDouble()));
            double visualHue = kind == DesktopFoodKind.EggBugEgg
                ? FoodRenderPalette.CreateNormalEggHue(random) : 0.0;
            DesktopFood food = new DesktopFood(kind,
                new Vec2(x, y - dropHeight), visualHue, RandomItemRotation());
            food.SetCreationVelocity(new Vec2(direction *
                MathUtil.Lerp(0.15, 0.75, random.NextDouble()), 0.0));
            if (!pool.Add(food)) return false;
            LastSpawnAccepted = ConsiderFood(food);
            if (LastSpawnAccepted && target == null) target = food;
            LastEvent = FoodEventName(food, LastSpawnAccepted
                ? "Spawn_Accepted" : "Spawn_Ignored");
            return true;
        }

        public void StepPhysics(DesktopCollisionWorld world)
        {
            if (world == null) throw new ArgumentNullException("world");
            EnsureSharedPool(world);
            StepMetabolism();
            physicsSerial++;
            lastPoolStepSerial = physicsSerial;
            if (!pool.TryAdvancePhysics(physicsSerial))
            {
                RemoveInactive();
                return;
            }
            pool.RemoveInactive();
            for (int i = 0; i < pool.Items.Count; i++)
                pool.Items[i].StepPhysics(world);
            pool.RemoveInactive();
            RemoveInactive();
        }

        public void StepMetabolism()
        {
            fullness = Math.Max(0.0, fullness -
                1.0 / DigestionTicksPerFoodPoint);
            if (pool.IsSharedWorld && fullness <= SharedHungerThreshold)
                sharedHungry = true;
        }

        public bool TryProduceInput(Slugcat slugcat, SlugcatGraphics graphics,
            AttentionSystem attention, out VirtualInput input)
        {
            input = VirtualInput.Neutral;
            if (slugcat == null || graphics == null) return false;
            bool useSpearmasterRules = IsSpearmaster(slugcat);
            if (useSpearmasterRules && !spearmasterFoodRulesActive &&
                target != null && (target.State == DesktopFoodState.Held ||
                    target.State == DesktopFoodState.Biting))
            {
                interactionCountdown = random.Next(SpearmasterMinimumHoldTicks,
                    SpearmasterMaximumHoldTicks + 1);
            }
            spearmasterFoodRulesActive = useSpearmasterRules;
            if (draggedFood != null) return false;

            if (pool.IsSharedWorld) SelectSharedTarget(slugcat);
            else SelectLegacyTarget(slugcat);
            if (target == null)
            {
                InteractionState = FoodInteractionState.None;
                return false;
            }

            if (slugcat.IsGrabbed || !slugcat.State.Conscious || slugcat.State.Dead ||
                slugcat.State.StunCounter > 0)
            {
                DropTarget(slugcat);
                return false;
            }

            if (pool.IsSharedWorld && !pool.IsReservedBy(target, this))
            {
                ResetTargetState();
                return false;
            }

            if (attention != null)
                attention.SetTarget(AttentionKind.Food, target.Chunk.Position);

            if (target.State == DesktopFoodState.Held ||
                target.State == DesktopFoodState.Biting)
            {
                EnsureFoodHand(slugcat);
                InteractionState = target.State == DesktopFoodState.Biting
                    ? FoodInteractionState.Eating : FoodInteractionState.Holding;
                return true;
            }

            if (!pool.IsSharedWorld) target.Claim();
            InteractionState = FoodInteractionState.Seeking;
            Vec2 offset = target.Chunk.Position - slugcat.Center;
            if (Math.Abs(offset.X) > ApproachDistance)
            {
                input = new VirtualInput(offset.X < 0.0 ? -1 : 1, 0, false, false);
                return true;
            }

            if (offset.Length <= PickupDistance &&
                Math.Abs(offset.Y) <= PickupVerticalTolerance && slugcat.State.Grounded)
            {
                if (target.PickUp(target.Chunk.Position))
                {
                    EnsureFoodHand(slugcat);
                    interactionCountdown = IsSpearmaster(slugcat)
                        ? random.Next(SpearmasterMinimumHoldTicks,
                            SpearmasterMaximumHoldTicks + 1)
                        : 0;
                    InteractionState = FoodInteractionState.Holding;
                    LastEvent = FoodEventName(target, "PickUp");
                }
            }
            return true;
        }

        public void StepInteraction(Slugcat slugcat, SlugcatGraphics graphics)
        {
            if (slugcat == null || graphics == null)
            {
                if (pool.IsSharedWorld && target != null)
                    pool.ReleaseReservation(target, this, true);
                ResetTargetState();
                return;
            }

            bool biteOccurred = false;
            DesktopFood animatedFood = target;
            try
            {
                if (target == null || !target.IsActive || !pool.Contains(target)) return;
                if (pool.IsSharedWorld && !pool.IsReservedBy(target, this))
                {
                    ResetTargetState();
                    return;
                }
                if (slugcat.IsGrabbed || !slugcat.State.Conscious || slugcat.State.Dead ||
                    slugcat.State.StunCounter > 0)
                {
                    DropTarget(slugcat);
                    return;
                }
                if (target.State != DesktopFoodState.Held &&
                    target.State != DesktopFoodState.Biting) return;

                EnsureFoodHand(slugcat);

                if (IsSpearmaster(slugcat))
                {
                    // Spearmaster has no mouth and never enters the edible bite
                    // path. Keep the grasp for one to three seconds, then use
                    // Player.TossObject's light-item direction and speed.
                    InteractionState = FoodInteractionState.Holding;
                    if (interactionCountdown > 0) interactionCountdown--;
                    if (interactionCountdown <= 0) TossUneatenTarget(slugcat);
                    return;
                }

                if (target.State == DesktopFoodState.Held)
                {
                    target.BeginBiting();
                    InteractionState = FoodInteractionState.Eating;
                    interactionCountdown = InitialEatCounter;
                    return;
                }

                if (interactionCountdown > 0)
                {
                    interactionCountdown--;
                    return;
                }

                if (!target.Bite()) return;
                biteOccurred = true;
                TotalBites++;
                LastEvent = FoodEventName(target, "Bite");
                if (target.State == DesktopFoodState.Consumed)
                {
                    FoodPointsEaten += target.FoodPoints;
                    fullness = Math.Min(MaximumFullness,
                        fullness + target.FoodPoints);
                    if (pool.IsSharedWorld)
                    {
                        sharedHungry = false;
                        pool.ReleaseReservation(target, this, false);
                    }
                    LastEvent = FoodEventName(target, "Eaten");
                    target = null;
                    interactionCountdown = 0;
                    foodHand = -1;
                    InteractionState = FoodInteractionState.None;
                    return;
                }
                interactionCountdown = BiteIntervalTicks - 1;
            }
            finally
            {
                ApplyFoodAnimation(slugcat, graphics, animatedFood, biteOccurred);
            }
        }

        public void ApplyMovingSurfaceDelta(DesktopCollisionWorld world)
        {
            if (world == null) return;
            EnsureSharedPool(world);
            if (!pool.TryBeginSurfaceApply(physicsSerial)) return;
            for (int i = 0; i < pool.Items.Count; i++)
                pool.Items[i].ApplyMovingSurfaceDelta(world);
        }

        public bool HitTest(Vec2 point)
        {
            return FindDraggableFood(point) != null;
        }

        public bool TryBeginDrag(Vec2 point)
        {
            if (draggedFood != null) return false;
            DesktopFood food = FindDraggableFood(point);
            if (food == null) return false;
            if (pool.IsSharedWorld) pool.ForceReleaseForDrag(food);
            if (!food.BeginDrag()) return false;

            draggedFood = food;
            dragOffset = food.Chunk.Position - point;
            placementDropActive = false;
            placementReadyForClick = false;
            if (ReferenceEquals(target, food)) ResetTargetState();
            LastEvent = FoodEventName(food, "MouseGrab");
            return true;
        }

        public void MoveDraggedFood(Vec2 pointerPosition)
        {
            if (draggedFood == null) return;
            draggedFood.DragTo(pointerPosition + dragOffset);
            if (!placementDropActive) return;

            bool leftDown = IsLeftMouseDown();
            if (!placementReadyForClick)
            {
                if (!leftDown) placementReadyForClick = true;
                placementLastLeftDown = leftDown;
                return;
            }
            if (leftDown && !placementLastLeftDown)
            {
                EndDrag(Vec2.Zero);
                return;
            }
            placementLastLeftDown = leftDown;
        }

        public bool EndDrag(Vec2 velocity)
        {
            DesktopFood food = draggedFood;
            bool wasPlacement = placementDropActive;
            draggedFood = null;
            dragOffset = Vec2.Zero;
            placementDropActive = false;
            placementReadyForClick = false;
            placementLastLeftDown = false;
            if (food == null || !food.EndDrag(velocity)) return false;
            LastEvent = FoodEventName(food,
                wasPlacement ? "DropAtCursor" : "MouseRelease");
            return true;
        }

        public void Clear()
        {
            pool.ClearAll();
            target = null;
            draggedFood = null;
            dragOffset = Vec2.Zero;
            interactionCountdown = 0;
            foodHand = -1;
            placementDropActive = false;
            placementReadyForClick = false;
            placementLastLeftDown = false;
            spearmasterFoodRulesActive = false;
            spearmasterRejectedFoods.Clear();
            InteractionState = FoodInteractionState.None;
            LastSpawnAccepted = false;
            LastEvent = "Food_Clear";
        }

        private void EnsureSharedPool(DesktopCollisionWorld world)
        {
            DesktopFoodPool shared = SharedPools.GetValue(world, CreateSharedPool);
            if (ReferenceEquals(pool, shared)) return;
            if (!pool.IsSharedWorld) shared.ImportFrom(pool);
            pool = shared;
            pool.Register(this);
            physicsSerial = pool.PhysicsSerial;
            lastPoolStepSerial = physicsSerial;
            sharedHungry = fullness <= SharedHungerThreshold;
            if (target != null && !pool.Contains(target)) ResetTargetState();
            if (draggedFood != null && !pool.Contains(draggedFood))
            {
                draggedFood = null;
                dragOffset = Vec2.Zero;
                placementDropActive = false;
            }
        }

        private static DesktopFoodPool CreateSharedPool(DesktopCollisionWorld world)
        {
            return new DesktopFoodPool(true);
        }

        private void SelectSharedTarget(Slugcat slugcat)
        {
            if (target != null)
            {
                if (target.IsActive && pool.Contains(target) &&
                    target.State != DesktopFoodState.Dragged &&
                    pool.IsReservedBy(target, this)) return;
                pool.ReleaseReservation(target, this,
                    target.State == DesktopFoodState.Claimed);
                ResetTargetState();
            }
            if (!sharedHungry || !pool.CanReserveFor(this)) return;

            DesktopFood closest = null;
            double closestDistance = double.MaxValue;
            for (int i = 0; i < pool.Items.Count; i++)
            {
                DesktopFood food = pool.Items[i];
                if (!food.IsActive || food.State != DesktopFoodState.Free) continue;
                if (IsSpearmaster(slugcat) && spearmasterRejectedFoods.Contains(food))
                    continue;
                double distance = Vec2.Distance(slugcat.Center, food.Chunk.Position);
                if (distance >= closestDistance) continue;
                closest = food;
                closestDistance = distance;
            }
            if (closest == null || !pool.TryReserve(closest, this)) return;
            target = closest;
            InteractionState = FoodInteractionState.Seeking;
            LastEvent = FoodEventName(target, "ClaimShared");
        }

        private void SelectLegacyTarget(Slugcat slugcat)
        {
            if (target != null && target.IsActive && pool.Contains(target)) return;
            target = null;
            for (int i = 0; i < pool.Items.Count; i++)
            {
                DesktopFood food = pool.Items[i];
                if (!food.IsActive || food.State == DesktopFoodState.Ignored ||
                    food.State == DesktopFoodState.Dragged) continue;
                if (IsSpearmaster(slugcat) && spearmasterRejectedFoods.Contains(food))
                    continue;
                if (food.State == DesktopFoodState.Free && !ConsiderFood(food)) continue;
                target = food;
                break;
            }
        }

        private void DropTarget(Slugcat slugcat)
        {
            DesktopFood dropping = target;
            if (dropping != null && (dropping.State == DesktopFoodState.Held ||
                dropping.State == DesktopFoodState.Biting))
            {
                Vec2 velocity = slugcat == null
                    ? Vec2.Zero : slugcat.BodyChunks[0].Velocity * 0.5;
                dropping.Drop(velocity);
                LastEvent = FoodEventName(dropping, "Drop");
            }
            if (pool.IsSharedWorld && dropping != null)
                pool.ReleaseReservation(dropping, this, true);
            ResetTargetState();
        }

        private void TossUneatenTarget(Slugcat slugcat)
        {
            DesktopFood tossed = target;
            if (tossed == null || (tossed.State != DesktopFoodState.Held &&
                tossed.State != DesktopFoodState.Biting)) return;

            int facing = slugcat.State.Facing == 0 ? 1 : slugcat.State.Facing;
            double radians = TossAngleDegrees * facing * Math.PI / 180.0;
            Vec2 direction = new Vec2(Math.Sin(radians), -Math.Cos(radians));
            // Player.TossObject for a 0.2-mass item first carries 60% of the
            // main chunk velocity, then adds a 12.5-unit 60-degree toss.
            Vec2 velocity = slugcat.BodyChunks[0].Velocity * 0.6 +
                direction * TossSpeed;
            tossed.Drop(velocity);
            spearmasterRejectedFoods.Add(tossed);
            if (pool.IsSharedWorld)
                pool.ReleaseReservation(tossed, this, true);
            else
                tossed.ReleaseClaim();
            LastEvent = FoodEventName(tossed, "TossUneaten");
            ResetTargetState();
        }

        private bool HasAvailableSharedFood()
        {
            for (int i = 0; i < pool.Items.Count; i++)
            {
                DesktopFood food = pool.Items[i];
                if (food.IsActive && food.State == DesktopFoodState.Free &&
                    !spearmasterRejectedFoods.Contains(food)) return true;
            }
            return false;
        }

        private static bool IsSpearmaster(Slugcat slugcat)
        {
            return slugcat != null && slugcat.SelectedSlugcat != null &&
                slugcat.SelectedSlugcat.Id == SlugcatId.SpearMaster;
        }

        private void ResetTargetState()
        {
            target = null;
            interactionCountdown = 0;
            foodHand = -1;
            InteractionState = FoodInteractionState.None;
        }

        private void RemoveInactive()
        {
            pool.RemoveInactive();
            spearmasterRejectedFoods.RemoveWhere(delegate(DesktopFood food)
            {
                return food == null || !food.IsActive || !pool.Contains(food);
            });
            if (target != null && (!target.IsActive || !pool.Contains(target)))
                ResetTargetState();
            if (draggedFood != null && (!draggedFood.IsActive || !pool.Contains(draggedFood)))
            {
                draggedFood = null;
                dragOffset = Vec2.Zero;
                placementDropActive = false;
                placementReadyForClick = false;
                placementLastLeftDown = false;
            }
        }

        private DesktopFood FindDraggableFood(Vec2 point)
        {
            DesktopFood closest = null;
            double closestDistance = double.MaxValue;
            for (int i = pool.Items.Count - 1; i >= 0; i--)
            {
                DesktopFood food = pool.Items[i];
                if (!food.IsActive || !food.IsDraggable) continue;
                double distance = Vec2.Distance(point, food.Chunk.Position);
                if (distance > food.VisualReach + 5.0 || distance >= closestDistance)
                    continue;
                closest = food;
                closestDistance = distance;
            }
            return closest;
        }

        private void EnsureFoodHand(Slugcat slugcat)
        {
            if (foodHand >= 0) return;
            foodHand = 0;
            SpearmasterAbilityController spear =
                slugcat.AbilityController as SpearmasterAbilityController;
            if (spear != null && spear.HeldSpear != null && spear.HeldHand == foodHand)
                foodHand = 1;
        }

        private void ApplyFoodAnimation(Slugcat slugcat, SlugcatGraphics graphics,
            DesktopFood animatedFood, bool biteOccurred)
        {
            DesktopFood food = animatedFood != null ? animatedFood : target;
            if (food == null) return;
            bool carrying = food.State == DesktopFoodState.Held ||
                food.State == DesktopFoodState.Biting || biteOccurred;
            if (!carrying && InteractionState != FoodInteractionState.Seeking) return;

            Vec2 connection = slugcat.BodyChunks[0].Position;
            if (!carrying && Vec2.Distance(connection, food.Chunk.Position) >
                FoodHandReachDistance) return;

            EnsureFoodHand(slugcat);
            int handIndex = foodHand;
            Limb hand = graphics.Arms[handIndex];
            if (carrying)
            {
                if (IsSpearmaster(slugcat))
                    graphics.SetHeldFoodPose(handIndex);
                else
                    graphics.SetEdibleHandPose(handIndex, interactionCountdown);
                if (biteOccurred)
                {
                    graphics.ApplyEdibleBiteAfterGraphicsStep(handIndex);
                    if (food.IsActive)
                    {
                        food.HoldAt(slugcat.BodyChunks[0].Position,
                            slugcat.BodyChunks[0].Position);
                        food.Chunk.LastPosition = food.Chunk.Position;
                    }
                }
                else
                {
                    food.HoldAt(hand.End.Position,
                        slugcat.BodyChunks[0].Position);
                    food.Chunk.LastPosition = food.Chunk.Position;
                }
                return;
            }

            Vec2 handTarget = food.Chunk.Position;
            Vec2 offset = handTarget - connection;
            double maximumReach = hand.Length * 0.95;
            if (offset.Length > maximumReach)
                handTarget = connection + offset.Normalized * maximumReach;

            hand.Mode = LimbMode.HuntAbsolutePosition;
            hand.AbsoluteHuntPosition = handTarget;
            hand.TargetPosition = handTarget;
            hand.GripSurfaceId = 0;
            hand.RetractCounter = 0;
            hand.HuntSpeed = 9.0;
            hand.Quickness = 0.65;

            Vec2 previous = hand.End.Position;
            hand.End.Position = Vec2.Lerp(previous, handTarget, SeekingHandBlend);
            hand.End.LastPosition = previous;
            hand.End.Velocity = hand.End.Position - previous;
        }

        private bool ConsiderFood(DesktopFood food)
        {
            if (food.State == DesktopFoodState.Claimed) return true;
            if (food.State == DesktopFoodState.Ignored) return false;
            if (food.State != DesktopFoodState.Free) return true;

            double projected = ProjectedFullness();
            bool accepted;
            if (projected <= 0.001)
                accepted = true;
            else if (projected >= MaximumFullness)
                accepted = false;
            else
            {
                double chance = MathUtil.Lerp(0.78, 0.12,
                    projected / MaximumFullness);
                accepted = random.NextDouble() < chance;
            }

            if (accepted) food.Claim();
            else food.Ignore();
            return accepted;
        }

        private double ProjectedFullness()
        {
            double projected = fullness;
            for (int i = 0; i < pool.Items.Count; i++)
            {
                DesktopFood food = pool.Items[i];
                if (food.State == DesktopFoodState.Claimed ||
                    food.State == DesktopFoodState.Held ||
                    food.State == DesktopFoodState.Biting)
                    projected += food.FoodPoints;
            }
            return projected;
        }

        private Vec2 RandomItemRotation()
        {
            double angle = rotationRandom.NextDouble() * Math.PI * 2.0;
            return new Vec2(Math.Cos(angle), Math.Sin(angle));
        }

        private static bool IsLeftMouseDown()
        {
            return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        }

        private static string FoodEventName(DesktopFood food, string action)
        {
            return food.Kind + "_" + action;
        }
    }
}
