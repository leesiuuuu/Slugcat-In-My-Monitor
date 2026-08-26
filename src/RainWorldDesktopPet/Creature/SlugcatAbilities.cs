using System;
using System.Collections.Generic;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Creature
{
    public enum AbilityEffectKind
    {
        Explosion,
        ExplosionLight,
        ExplosionSpikes,
        SootMark,
        ShockWave,
        Smoke,
        FlashingSmoke,
        Spark,
        WaterDrip
    }

    public sealed class AbilityEffect
    {
        private readonly Random random;
        // Ability particles are presentation state rather than physical actors.
        // Keeping them as lightweight trajectories avoids allocating BodyChunks
        // and running desktop terrain resolution for every visual particle.

        public AbilityEffect(AbilityEffectKind kind, Vec2 position, Vec2 velocity,
            int lifetime, double radius)
        {
            Kind = kind;
            Position = position;
            LastPosition = position;
            Velocity = velocity;
            Lifetime = lifetime;
            InitialLifetime = lifetime;
            Radius = radius;
            Life = 1.0;
            LastLife = 1.0;
            LifeTime = Math.Max(1, lifetime);
            PreviousPreviousPosition = position;
            PreviousPreviousPreviousPosition = position;
            TargetPosition = position;
            random = new Random(unchecked((int)(position.X * 31.0 + position.Y * 17.0) ^
                lifetime ^ ((int)kind * 7919)));
        }

        public readonly AbilityEffectKind Kind;
        public Vec2 LastPosition;
        public Vec2 Position;
        public Vec2 Velocity;
        public int Lifetime;
        public readonly int InitialLifetime;
        public readonly double Radius;
        public double Gravity;
        public double Intensity = 1.0;
        public double Life;
        public double LastLife;
        public double LifeTime;
        public double Rotation;
        public double LastRotation;
        public double RotationVelocity;
        public Vec2 TargetPosition;
        public Vec2 PreviousPreviousPosition;
        public Vec2 PreviousPreviousPreviousPosition;
        public bool IsAlive
        {
            get
            {
                // Different visual kinds use different edge rules so flashes,
                // expanding waves, and smoke can finish on their intended frame.
                if (Kind == AbilityEffectKind.Smoke ||
                    Kind == AbilityEffectKind.FlashingSmoke) return LastLife > 0.0;
                if (Kind == AbilityEffectKind.ExplosionLight) return LastLife >= 0.0;
                if (Kind == AbilityEffectKind.ShockWave) return LastLife <= 1.0;
                return Life > 0.0 && Lifetime > 0;
            }
        }

        public static AbilityEffect CreateExplosionSmoke(Vec2 position, Vec2 velocity,
            double size, Random source)
        {
            Vec2 target = position + new Vec2(
                MathUtil.Lerp(-50.0, 50.0, source.NextDouble()),
                MathUtil.Lerp(-400.0, 100.0, source.NextDouble()));
            Vec2 initialPosition = position + velocity.Normalized *
                (60.0 * source.NextDouble());
            double radius = MathUtil.Lerp(0.6, 1.5, source.NextDouble()) * size;
            double rotation = source.NextDouble() * 360.0;
            double rotationVelocity = MathUtil.Lerp(-6.0, 6.0,
                source.NextDouble());
            double lifeTime = MathUtil.Lerp(170.0, 400.0,
                source.NextDouble());
            AbilityEffect effect = new AbilityEffect(AbilityEffectKind.Smoke,
                initialPosition, velocity, (int)Math.Ceiling(lifeTime), radius);
            effect.LifeTime = lifeTime;
            effect.Lifetime = (int)Math.Ceiling(lifeTime);
            effect.Rotation = rotation;
            effect.LastRotation = effect.Rotation;
            effect.RotationVelocity = rotationVelocity;
            // Target motion is stored directly in the desktop Y-down space.
            effect.TargetPosition = target;
            return effect;
        }

        public static AbilityEffect CreateExplosionLight(Vec2 position, double radius,
            double alpha, int lifeTime)
        {
            AbilityEffect effect = new AbilityEffect(AbilityEffectKind.ExplosionLight,
                position, Vec2.Zero, lifeTime, radius);
            effect.Intensity = alpha;
            // Start interpolation from zero so the first rendered frame eases
            // into the flash instead of appearing as an instantaneous pop.
            effect.LastLife = 0.0;
            return effect;
        }

        public static AbilityEffect CreateSpark(Vec2 position, Vec2 velocity,
            int standardLifeTime, int exceptionalLifeTime, Random source)
        {
            Vec2 initialPosition = position + velocity.Normalized *
                (30.0 * source.NextDouble());
            double gravity = MathUtil.Lerp(0.4, 0.9, source.NextDouble());
            int lifeTime = source.Next(0, Math.Max(1, standardLifeTime));
            if (source.NextDouble() < 0.1)
                lifeTime = source.Next(standardLifeTime,
                    Math.Max(standardLifeTime + 1, exceptionalLifeTime));
            // Zero lifetime remains a valid single-frame burst; clamping it to
            // one would make short spark groups visibly denser than intended.
            AbilityEffect effect = new AbilityEffect(AbilityEffectKind.Spark,
                initialPosition, velocity, lifeTime, 1.0);
            // Trail history starts at the unshifted spawn point so the first
            // segment connects cleanly to the emitter.
            effect.LastPosition = position;
            effect.PreviousPreviousPosition = position;
            effect.PreviousPreviousPreviousPosition = position;
            effect.Gravity = gravity;
            return effect;
        }

        public static AbilityEffect CreateShockWave(Vec2 position, double radius,
            double intensity, int lifeTime)
        {
            AbilityEffect effect = new AbilityEffect(AbilityEffectKind.ShockWave,
                position, Vec2.Zero, lifeTime, radius);
            effect.Intensity = intensity;
            effect.Life = 0.0;
            effect.LastLife = 0.0;
            return effect;
        }

        public void Step()
        {
            Step(null);
        }

        public void Step(DesktopCollisionWorld world)
        {
            LastLife = Life;
            PreviousPreviousPreviousPosition = PreviousPreviousPosition;
            PreviousPreviousPosition = LastPosition;
            LastPosition = Position;
            LastRotation = Rotation;
            if (Kind == AbilityEffectKind.Smoke || Kind == AbilityEffectKind.FlashingSmoke)
            {
                Velocity *= 0.9;
                Vec2 towardTarget = (TargetPosition - Position).Normalized;
                Velocity += towardTarget * (random.NextDouble() * 0.04);
                Rotation += RotationVelocity * Velocity.Length;
                IntegrateWithTerrain(world, 1.0);
                Life -= 1.0 / Math.Max(1.0, LifeTime);
            }
            else if (Kind == AbilityEffectKind.Spark)
            {
                Velocity.Y += Gravity;
                IntegrateWithTerrain(world, 0.5);
                Life -= 1.0 / Math.Max(1.0, LifeTime);
            }
            else if (Kind == AbilityEffectKind.WaterDrip)
            {
                Velocity.Y += Gravity;
                Position += Velocity;
                Life -= 1.0 / Math.Max(1.0, LifeTime);
            }
            else if (Kind == AbilityEffectKind.ShockWave)
            {
                Life += 1.0 / Math.Max(1.0, LifeTime);
            }
            else
            {
                Position += Velocity;
                Life -= 1.0 / Math.Max(1.0, LifeTime);
            }
            Lifetime = Math.Max(0, (int)Math.Ceiling(Math.Max(0.0, Life) * LifeTime));
        }

        private void IntegrateWithTerrain(DesktopCollisionWorld world, double bounce)
        {
            // These particles are decorative and do not participate in desktop
            // collision gameplay, so integrating their visual trajectory is
            // enough and avoids per-particle BodyChunk work.
            Position += Velocity;
        }
    }

    public interface ISlugcatAbilityController
    {
        string Name { get; }
        string DebugState { get; }
        void UpdateBeforeMovement(ref VirtualInput input, DesktopCollisionWorld world);
        void UpdateAfterMovement(VirtualInput input, DesktopCollisionWorld world);
        void TerrainImpact(TerrainImpactData impact);
        void Reset();
    }

    public class DefaultAbilityController : ISlugcatAbilityController
    {
        protected readonly Slugcat Owner;

        public DefaultAbilityController(Slugcat owner)
        {
            Owner = owner;
        }

        public virtual string Name { get { return "None"; } }
        public virtual string DebugState { get { return "ready"; } }
        public virtual void UpdateBeforeMovement(ref VirtualInput input, DesktopCollisionWorld world) { }
        public virtual void UpdateAfterMovement(VirtualInput input, DesktopCollisionWorld world) { }
        public virtual void TerrainImpact(TerrainImpactData impact) { }
        public virtual void Reset() { }
    }

    public sealed class ArtificerAbilityController : DefaultAbilityController
    {
        private const int Capacity = 10;
        private readonly Random random = new Random(0xA471F1C);
        private int explosiveJumpCounter;
        private int cooldown;
        private int parryCooldown;
        private int jumpDropLock;
        private int wantToJump;
        private bool pyroJumped;

        public ArtificerAbilityController(Slugcat owner) : base(owner) { }

        public override string Name { get { return "Explosive jump"; } }
        public int ExplosiveJumpCounter { get { return explosiveJumpCounter; } }
        public int Cooldown { get { return cooldown; } }
        public int ParryCooldown { get { return parryCooldown; } }
        public override string DebugState
        {
            get { return string.Format("pyro:{0}/{1} cooldown:{2} parry:{3}",
                explosiveJumpCounter, Capacity, cooldown, parryCooldown); }
        }

        public override void UpdateAfterMovement(VirtualInput input, DesktopCollisionWorld world)
        {
            if (jumpDropLock > 0) jumpDropLock--;
            if (parryCooldown > 0) parryCooldown--;
            if (input.JumpPressed) wantToJump = 5;
            else if (wantToJump > 0) wantToJump--;

            int safeThreshold = Math.Max(1, Capacity - 5);
            if (explosiveJumpCounter > 0 && (Owner.State.Conscious || Owner.State.Dead))
            {
                cooldown--;
                if (cooldown <= 0)
                {
                    cooldown = explosiveJumpCounter >= safeThreshold ? 40 : 60;
                    explosiveJumpCounter--;
                }
            }

            if (explosiveJumpCounter >= safeThreshold && random.NextDouble() < 0.25)
            {
                Owner.AddEffect(AbilityEffect.CreateExplosionSmoke(Owner.BodyChunks[0].Position,
                    RandomUnit() * (2.0 * random.NextDouble()), 1.0, random));
            }
            if (explosiveJumpCounter >= safeThreshold && random.NextDouble() < 0.5)
                Owner.AddEffect(AbilityEffect.CreateSpark(Owner.BodyChunks[0].Position, RandomUnit(),
                    4, 8, random));

            if (Owner.State.Grounded || !Owner.State.Conscious ||
                Owner.State.BodyMode == BodyModeIndex.CorridorClimb ||
                Owner.State.BodyMode == BodyModeIndex.WallClimb ||
                Owner.State.BodyMode == BodyModeIndex.Swimming ||
                Owner.State.BodyMode == BodyModeIndex.ZeroG)
                pyroJumped = false;

            bool requested = wantToJump > 0 && input.Pickup &&
                !Owner.Movement.LaunchedThisTick;
            bool validMode = Owner.State.Conscious && !Owner.State.Grounded &&
                Owner.State.BodyMode != BodyModeIndex.Crawl &&
                Owner.State.BodyMode != BodyModeIndex.CorridorClimb &&
                Owner.State.BodyMode != BodyModeIndex.WallClimb &&
                Owner.State.BodyMode != BodyModeIndex.Swimming &&
                Owner.State.BodyMode != BodyModeIndex.ZeroG &&
                Owner.State.Animation != AnimationIndex.HangFromBeam &&
                Owner.State.Animation != AnimationIndex.ClimbOnBeam;

            if (requested && !pyroJumped && validMode && input.Y <= 0)
            {
                ExplosiveJump(input.X, input.Y < 0);
                wantToJump = 0;
                return;
            }

            bool parryDirection = input.Y > 0 || Owner.State.BodyMode == BodyModeIndex.Crawl;
            if (requested && !pyroJumped && Owner.State.Conscious &&
                parryDirection && (Owner.State.Grounded || input.Y > 0) &&
                parryCooldown <= 0)
            {
                ExplosiveParry();
                wantToJump = 0;
            }
        }

        private void ExplosiveJump(int x, bool aimingUp)
        {
            BodyChunk chest = Owner.BodyChunks[0];
            BodyChunk hips = Owner.BodyChunks[1];
            int dangerThreshold = Math.Max(1, Capacity - 3);
            pyroJumped = true;
            jumpDropLock = 40;
            if (x != 0)
            {
                chest.Velocity.Y = Math.Max(chest.Velocity.Y, 0.0) - 8.0;
                hips.Velocity.Y = Math.Max(hips.Velocity.Y, 0.0) - 7.0;
                Owner.Movement.SetJumpBoost(6.0);
            }
            if (x == 0 || aimingUp)
            {
                double chestLift = explosiveJumpCounter >= dangerThreshold ? 16.0 : 11.0;
                double hipsLift = explosiveJumpCounter >= dangerThreshold ? 15.0 : 10.0;
                chest.Velocity.Y = Math.Max(chest.Velocity.Y, 0.0) - chestLift;
                hips.Velocity.Y = Math.Max(hips.Velocity.Y, 0.0) - hipsLift;
                Owner.Movement.SetJumpBoost(explosiveJumpCounter >= dangerThreshold
                    ? 10.0 : 8.0);
            }
            if (aimingUp)
            {
                chest.Velocity.X = 10.0 * x;
                hips.Velocity.X = 8.0 * x;
            }
            else
            {
                chest.Velocity.X = 15.0 * x;
                hips.Velocity.X = 13.0 * x;
            }

            Owner.State.Animation = AnimationIndex.Flip;
            Owner.State.FlipFromSlide = false;
            Owner.State.BodyMode = BodyModeIndex.Default;
            Owner.State.Grounded = false;
            explosiveJumpCounter++;
            cooldown = 150;
            EmitJumpEffects(false);
            ApplyOverheat(dangerThreshold);
        }

        private void ExplosiveParry()
        {
            if (!Owner.State.Grounded)
            {
                pyroJumped = true;
                Owner.BodyChunks[0].Velocity.Y = Math.Max(
                    Owner.BodyChunks[0].Velocity.Y, 0.0) - 8.0;
                Owner.BodyChunks[1].Velocity.Y = Math.Max(
                    Owner.BodyChunks[1].Velocity.Y, 0.0) - 6.0;
                Owner.Movement.SetJumpBoost(6.0);
            }
            int safeThreshold = Math.Max(1, Capacity - 5);
            int dangerThreshold = Math.Max(1, Capacity - 3);
            explosiveJumpCounter += explosiveJumpCounter <= safeThreshold ? 2 : 1;
            parryCooldown = 40;
            cooldown = 150;
            EmitJumpEffects(true);
            ApplyOverheat(dangerThreshold);
        }

        private void EmitJumpEffects(bool parry)
        {
            Vec2 effectPosition = Owner.BodyChunks[0].Position;
            for (int i = 0; i < 8; i++)
            {
                Owner.AddEffect(AbilityEffect.CreateExplosionSmoke(effectPosition,
                    RandomUnit() * (5.0 * random.NextDouble()), 1.0, random));
            }
            Owner.AddEffect(AbilityEffect.CreateExplosionLight(effectPosition,
                160.0, 1.0, 3));
            for (int i = 0; i < 10; i++)
            {
                Vec2 at = effectPosition + RandomUnit() * (40.0 * random.NextDouble());
                Owner.AddEffect(AbilityEffect.CreateSpark(at,
                    RandomUnit() * MathUtil.Lerp(4.0, 30.0, random.NextDouble()),
                    4, 18, random));
            }
            if (parry)
            {
                Owner.AddEffect(AbilityEffect.CreateShockWave(effectPosition,
                    200.0, 0.2, 6));
            }
            Owner.EmitSound("Fire_Spear_Explode", effectPosition,
                0.3 + random.NextDouble() * 0.3,
                0.5 + random.NextDouble() * 2.0, 1);
        }

        private void ApplyOverheat(int dangerThreshold)
        {
            if (explosiveJumpCounter >= dangerThreshold)
                Owner.Stun(60 * (explosiveJumpCounter - (dangerThreshold - 1)));
            if (explosiveJumpCounter >= Capacity) PyroDeath();
        }

        private void PyroDeath()
        {
            explosiveJumpCounter = Capacity;
            Vec2 deathPosition = Vec2.Lerp(Owner.BodyChunks[0].Position,
                Owner.BodyChunks[0].LastPosition, 0.35);
            Owner.AddEffect(new AbilityEffect(AbilityEffectKind.SootMark,
                deathPosition, Vec2.Zero, 400, 80.0));
            Owner.AddEffect(new AbilityEffect(AbilityEffectKind.Explosion,
                deathPosition, Vec2.Zero, 7, 350.0));
            Owner.AddEffect(AbilityEffect.CreateExplosionLight(deathPosition,
                280.0, 1.0, 7));
            Owner.AddEffect(AbilityEffect.CreateExplosionLight(deathPosition,
                230.0, 1.0, 3));
            Owner.AddEffect(new AbilityEffect(AbilityEffectKind.ExplosionSpikes,
                deathPosition, Vec2.Zero, 7, 170.0));
            Owner.AddEffect(AbilityEffect.CreateShockWave(deathPosition,
                430.0, 0.045, 5));
            for (int i = 0; i < 25; i++)
            {
                Vec2 direction = RandomUnit();
                for (int j = 0; j < 3; j++)
                {
                    Vec2 at = deathPosition + direction *
                        MathUtil.Lerp(30.0, 60.0, random.NextDouble());
                    Vec2 sparkVelocity = direction *
                        MathUtil.Lerp(7.0, 38.0, random.NextDouble()) +
                        RandomUnit() * (20.0 * random.NextDouble());
                    Owner.AddEffect(CreateSpark(at, sparkVelocity, 11, 28));
                }
                Vec2 smokePosition = deathPosition + direction *
                    (40.0 * random.NextDouble());
                Vec2 smokeVelocity = direction * MathUtil.Lerp(4.0, 20.0,
                    Math.Pow(random.NextDouble(), 2.0));
                AbilityEffect smoke = CreateSmoke(smokePosition, smokeVelocity,
                    1.0 + 0.05 * random.NextDouble());
                Owner.AddEffect(new AbilityEffect(AbilityEffectKind.FlashingSmoke,
                    smoke.Position, smoke.Velocity, smoke.Lifetime, smoke.Radius));
            }
            Owner.EmitSound("Bomb_Explode", deathPosition, 1.0, 1.0, 1);
            Owner.Die();
        }

        private AbilityEffect CreateSmoke(Vec2 at, Vec2 smokeVelocity, double size)
        {
            return AbilityEffect.CreateExplosionSmoke(at, smokeVelocity, size, random);
        }

        private AbilityEffect CreateSpark(Vec2 at, Vec2 sparkVelocity,
            int standardLifetime, int exceptionalLifetime)
        {
            return AbilityEffect.CreateSpark(at, sparkVelocity, standardLifetime,
                exceptionalLifetime, random);
        }

        private Vec2 RandomUnit()
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            return new Vec2(Math.Cos(angle), Math.Sin(angle));
        }

        public override void Reset()
        {
            explosiveJumpCounter = 0;
            cooldown = 0;
            parryCooldown = 0;
            jumpDropLock = 0;
            wantToJump = 0;
            pyroJumped = false;
        }
    }

    public enum SpearmasterActionState
    {
        Idle,
        Moving,
        PreparingSpear,
        PullingSpear,
        HoldingSpear,
        Aiming,
        Throwing,
        Recovering
    }

    public sealed class SpearmasterAbilityController : DefaultAbilityController
    {
        private const int ThrownSpearLifetimeTicks =
            (int)(15.0 * SimulationConstants.LogicTicksPerSecond);
        private const int ThrownSpearFadeTicks =
            (int)(0.5 * SimulationConstants.LogicTicksPerSecond);
        private readonly Random random = new Random(0x5EA2);
        private double spearProgress;
        private bool pullSoundPlayed;
        private DesktopSpear heldSpear;
        private DesktopSpear thrownSpear;
        private int throwFollowTicks;
        private int spearType;
        private int spearLine;
        private int spearRow;
        private Vec2 graphicsHeadImpulse;
        private Vec2 tailNeedlePosition;
        private SpearmasterActionState actionState;
        private Vec2 aimTarget;

        public SpearmasterAbilityController(Slugcat owner) : base(owner)
        {
            tailNeedlePosition = owner.BodyChunks[1].Position;
        }

        public override string Name { get { return "Needle spear"; } }
        public double SpearProgress { get { return spearProgress; } }
        public DesktopSpear HeldSpear { get { return heldSpear; } }
        public DesktopSpear ThrownSpear { get { return thrownSpear; } }
        public int ThrowFollowTicks { get { return throwFollowTicks; } }
        public int HeldHand { get { return 0; } }
        public int SpearType { get { return spearType; } }
        public int SpearLine { get { return spearLine; } }
        public int SpearRow { get { return spearRow; } }
        public SpearmasterActionState ActionState { get { return actionState; } }
        public Vec2 AimTarget { get { return aimTarget; } }
        public void SetActionState(SpearmasterActionState state, Vec2 target)
        {
            actionState = state;
            aimTarget = target;
        }
        public Vec2 ConsumeGraphicsHeadImpulse()
        {
            Vec2 result = graphicsHeadImpulse;
            graphicsHeadImpulse = Vec2.Zero;
            return result;
        }
        public Vec2 SpearCreationPosition
        {
            get
            {
                Vec2 chest = Owner.BodyChunks[0].Position;
                Vec2 hips = Owner.BodyChunks[1].Position;
                Vec2 direction = (chest - hips).Normalized;
                Vec2 result = chest;
                if (Math.Abs(chest.Y - hips.Y) > Math.Abs(chest.X - hips.X) &&
                    chest.Y < hips.Y)
                {
                    result += direction * 5.0;
                    direction *= -1.0;
                    direction.X += 0.4 * Owner.State.Facing;
                }
                return result;
            }
        }
        public override string DebugState
        {
            get { return heldSpear != null
                ? string.Format("spear:{0} type:{1}", actionState, heldSpear.NeedleType)
                : string.Format("spear:{0} create {1:0}%", actionState,
                    spearProgress * 100.0); }
        }

        public override void UpdateAfterMovement(VirtualInput input, DesktopCollisionWorld world)
        {
            graphicsHeadImpulse = Vec2.Zero;
            if (!Owner.State.Conscious)
            {
                RetractSpearProgress();
                return;
            }

            if (heldSpear != null)
            {
                Vec2 direction = ResolveAimDirection(input);
                bool activelyAiming = actionState == SpearmasterActionState.Aiming ||
                    actionState == SpearmasterActionState.Throwing || input.ThrowPressed;
                if (activelyAiming && Math.Abs(direction.X) > 0.001)
                    Owner.State.Facing = direction.X < 0.0 ? -1 : 1;
                heldSpear.SetConnectionAnchor(TailConnectionAnchor());
                if (input.ThrowPressed)
                {
                    bool vertical = Owner.State.Animation == AnimationIndex.Flip &&
                        input.X == 0 && input.Y != 0;
                    direction = vertical
                        ? new Vec2(0.0, input.Y)
                        : new Vec2(Owner.State.Facing, 0.0);
                    Vec2 playerVelocity = Owner.BodyChunks[0].Velocity;
                    Vec2 velocity = !vertical
                        ? new Vec2(playerVelocity.X * 0.2 + direction.X * 40.0,
                            playerVelocity.Y * 0.5 - 1.5)
                        : new Vec2(playerVelocity.X * 0.5, direction.Y * 40.0);
                    if (!vertical) velocity.X *= 1.2;
                    Vec2 throwPosition = Owner.BodyChunks[0].Position +
                        direction * 10.0 + new Vec2(0.0, -4.0);
                    heldSpear.HoldAt(throwPosition, direction);
                    heldSpear.SetConnectionAnchor(TailConnectionAnchor());
                    heldSpear.Throw(velocity, direction);
                    heldSpear.SetDespawnAfterTicks(ThrownSpearLifetimeTicks,
                        ThrownSpearFadeTicks);
                    Owner.BodyChunks[0].Velocity += direction * 8.0;
                    Owner.BodyChunks[1].Velocity -= direction * 4.0;
                    Owner.EmitSound("Slugcat_Throw_Spear", throwPosition,
                        1.0, 1.0, 0);
                    actionState = SpearmasterActionState.Recovering;
                    thrownSpear = heldSpear;
                    throwFollowTicks = 5;
                    heldSpear = null;
                }
                return;
            }

            bool neutral = input.X == 0 && input.Y == 0 &&
                !input.Jump && !input.Throw;
            if (!input.Pickup)
            {
                RetractSpearProgress();
                return;
            }
            // Keep partial extraction progress while pickup stays held even if
            // another action temporarily breaks the neutral extraction gate.
            if (!neutral) return;

            if (actionState != SpearmasterActionState.PreparingSpear)
                actionState = SpearmasterActionState.PullingSpear;

            if (spearProgress == 0.0)
            {
                // Choose a visible tail lane, row, and needle appearance.
                spearLine = random.Next(0, 2);
                spearRow = random.Next(0, 4);
                spearType = random.Next(3);
            }

            if (spearProgress < 0.1)
                spearProgress = MathUtil.Lerp(spearProgress, 0.11, 0.1);
            else
            {
                if (!pullSoundPlayed)
                {
                    Owner.EmitSound("SM_Spear_Pull", Owner.Center, 1.0,
                        1.0 + random.NextDouble() * 0.5, 1);
                    pullSoundPlayed = true;
                }
                spearProgress = MathUtil.Lerp(spearProgress, 1.0, 0.05);
            }
            if (spearProgress > 0.6)
                graphicsHeadImpulse += RandomUnit() *
                    ((spearProgress - 0.6) / 0.4 * 2.0);
            if (spearProgress <= 0.95) return;

            spearProgress = 0.0;
            pullSoundPlayed = false;
            Vec2 creationPosition = SpearCreationPosition;
            Vec2 creationDirection = (Owner.BodyChunks[0].Position -
                Owner.BodyChunks[1].Position).Normalized;
            if (Math.Abs(Owner.BodyChunks[0].Position.Y - Owner.BodyChunks[1].Position.Y) >
                Math.Abs(Owner.BodyChunks[0].Position.X - Owner.BodyChunks[1].Position.X) &&
                Owner.BodyChunks[0].Position.Y < Owner.BodyChunks[1].Position.Y)
            {
                creationDirection *= -1.0;
                creationDirection.X += 0.4 * Owner.State.Facing;
                creationDirection = creationDirection.Normalized;
            }
            Vec2 initialVelocity = Vec2.ClampMagnitude(
                (creationDirection * 2.0 + RandomUnit() * random.NextDouble()) / 0.07, 6.0);
            heldSpear = new DesktopSpear(creationPosition, spearType);
            heldSpear.SetCreationVelocity(initialVelocity);
            heldSpear.SetConnectionAnchor(TailConnectionAnchor());
            Owner.AddSpear(heldSpear);
            Owner.EmitSound("SM_Spear_Grab", Owner.Center, 1.0,
                0.5 + random.NextDouble() * 1.5, 1);
            Vec2 tailEffectPosition = tailNeedlePosition;
            Vec2 towardHips = (Owner.BodyChunks[1].Position - tailEffectPosition).Normalized;
            for (int i = 0; i < 4; i++)
            {
                AbilityEffect drip = new AbilityEffect(AbilityEffectKind.WaterDrip,
                    tailEffectPosition + RandomUnit() * (random.NextDouble() * 1.5),
                    RandomUnit() * (3.0 * random.NextDouble()) + towardHips *
                        MathUtil.Lerp(2.0, 6.0, random.NextDouble()),
                    random.Next(10, 120), 1.0);
                drip.Gravity = 0.9;
                Owner.AddEffect(drip);
            }
            for (int i = 0; i < 5; i++)
            {
                Vec2 sparkDirection = RandomUnit();
                Vec2 sparkVelocity = sparkDirection *
                    MathUtil.Lerp(4.0, 30.0, random.NextDouble());
                Owner.AddEffect(AbilityEffect.CreateSpark(
                    tailEffectPosition + sparkDirection * (random.NextDouble() * 40.0),
                    sparkVelocity, 4, 18, random));
            }
            actionState = SpearmasterActionState.HoldingSpear;
        }

        private Vec2 ResolveAimDirection(VirtualInput input)
        {
            if (Owner.State.Animation == AnimationIndex.Flip &&
                input.X == 0 && input.Y != 0)
                return new Vec2(0.0, input.Y);
            if ((actionState == SpearmasterActionState.Aiming ||
                actionState == SpearmasterActionState.Throwing) &&
                (aimTarget - Owner.Center).LengthSquared > 1.0)
            {
                Vec2 toward = (aimTarget - Owner.Center).Normalized;
                if (Math.Abs(toward.Y) > Math.Abs(toward.X) * 1.5 &&
                    Owner.State.Animation == AnimationIndex.Flip)
                    return new Vec2(0.0, toward.Y < 0.0 ? -1.0 : 1.0);
                return new Vec2(toward.X < 0.0 ? -1.0 : 1.0, 0.0);
            }
            return new Vec2(Owner.State.Facing, -0.15).Normalized;
        }

        private Vec2 TailConnectionAnchor()
        {
            Vec2 hips = Owner.BodyChunks[1].Position;
            Vec2 awayFromChest = (hips - Owner.BodyChunks[0].Position).Normalized;
            return hips + awayFromChest * 8.0;
        }

        public void SetTailNeedlePosition(Vec2 position)
        {
            tailNeedlePosition = position;
        }

        public void AdvanceThrowFollowThrough()
        {
            if (throwFollowTicks > 0) throwFollowTicks--;
            if (throwFollowTicks <= 0) thrownSpear = null;
        }

        private void RetractSpearProgress()
        {
            spearProgress = MathUtil.Lerp(spearProgress, 0.0, 0.05);
            if (spearProgress < 0.025) spearProgress = 0.0;
            if (spearProgress == 0.0) pullSoundPlayed = false;
        }

        private Vec2 RandomUnit()
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            return new Vec2(Math.Cos(angle), Math.Sin(angle));
        }

        public override void Reset()
        {
            spearProgress = 0.0;
            pullSoundPlayed = false;
            heldSpear = null;
            thrownSpear = null;
            throwFollowTicks = 0;
            spearType = 0;
            spearLine = 0;
            spearRow = 0;
            graphicsHeadImpulse = Vec2.Zero;
            tailNeedlePosition = Owner.BodyChunks[1].Position;
            actionState = SpearmasterActionState.Idle;
            aimTarget = Vec2.Zero;
        }
    }

    public enum SaintTongueMode
    {
        Retracted,
        ShootingOut,
        AttachedToTerrain,
        AttachedToObject,
        Retracting
    }

    public sealed class SaintAbilityController : DefaultAbilityController
    {
        private const int RopeSegments = 20;
        private readonly Vec2[] rope = new Vec2[RopeSegments];
        private readonly Vec2[] lastRope = new Vec2[RopeSegments];
        private readonly Vec2[] ropeVelocity = new Vec2[RopeSegments];
        private readonly bool[] ropeClaimed = new bool[RopeSegments];
        private readonly DesktopRope desktopRope;
        private SaintTongueMode mode;
        private Vec2 position;
        private Vec2 lastPosition;
        private Vec2 velocity;
        private Vec2 anchor;
        private double requestedLength = 140.0;
        private double idealLength = 150.0;
        private double elastic = 1.0;
        private int attachedTicks;
        private bool returning;
        private long attachedSurfaceId;
        private DesktopSurfaceKind attachedSurfaceKind = DesktopSurfaceKind.ScreenEdge;

        public SaintAbilityController(Slugcat owner) : base(owner)
        {
            position = owner.BodyChunks[0].Position;
            lastPosition = position;
            desktopRope = new DesktopRope(position, position);
            FillRope(position, position, null);
        }

        public override string Name { get { return "Tongue / rope"; } }
        public SaintTongueMode Mode { get { return mode; } }
        public Vec2 TonguePosition { get { return position; } }
        public Vec2[] Rope { get { return (Vec2[])rope.Clone(); } }
        public Vec2[] LastRope { get { return (Vec2[])lastRope.Clone(); } }
        internal Vec2[] RopeForRender { get { return rope; } }
        internal Vec2[] LastRopeForRender { get { return lastRope; } }
        public double RopeTotalLength { get { return desktopRope.TotalLength; } }
        public double RequestedRopeLength { get { return requestedLength; } }
        public double LastElasticityExcess { get; private set; }
        public double LastElasticityTargetLength { get; private set; }
        public double LastElasticityRequestLength { get; private set; }
        public double RopeStretchFactor
        {
            get
            {
                double stretch = MathUtil.Lerp(200.0,
                    Math.Min(requestedLength, 200.0), 0.5) /
                    (desktopRope.TotalLength + 80.0);
                stretch = Math.Pow(stretch, stretch >= 1.0 ? 0.4 : 1.6);
                if (mode == SaintTongueMode.AttachedToTerrain)
                    stretch = MathUtil.Lerp(stretch, 1.0, 0.5);
                return stretch;
            }
        }
        public long AttachedSurfaceId { get { return attachedSurfaceId; } }
        public bool CanJumpReleaseAttachedTongue
        {
            get
            {
                if (mode != SaintTongueMode.AttachedToTerrain ||
                    !Owner.State.Conscious || Owner.State.Grounded ||
                    Owner.State.CanJump > 0 || Owner.Movement.LaunchedThisTick)
                    return false;
                if (Owner.State.BodyMode == BodyModeIndex.CorridorClimb ||
                    Owner.State.BodyMode == BodyModeIndex.ClimbIntoShortCut ||
                    Owner.State.BodyMode == BodyModeIndex.WallClimb ||
                    Owner.State.BodyMode == BodyModeIndex.Swimming ||
                    Owner.State.BodyMode == BodyModeIndex.ZeroG)
                    return false;
                for (int i = 0; i < Owner.BodyChunks.Length; i++)
                {
                    BodyChunk chunk = Owner.BodyChunks[i];
                    if (chunk.ContactFloor || chunk.ContactLeft || chunk.ContactRight)
                        return false;
                }
                return true;
            }
        }
        public override string DebugState
        {
            get { return string.Format("tongue:{0} rope:{1:0}/{2:0}", mode,
                Vec2.Distance(Owner.BodyChunks[0].Position, position), idealLength); }
        }

        public override void UpdateAfterMovement(VirtualInput input, DesktopCollisionWorld world)
        {
            Vec2 mouth = Owner.BodyChunks[0].Position;
            if (mode == SaintTongueMode.AttachedToTerrain)
            {
                attachedTicks++;
                if (Owner.State.StunCounter > 0)
                {
                    Release();
                    FinishRetraction(mouth);
                    return;
                }
                if (input.Y < 0) idealLength = Math.Max(50.0, idealLength - 3.0);
                if (input.Y > 0) idealLength = Math.Min(170.0, idealLength + 3.0);
                if (input.JumpPressed && attachedTicks >= 2 &&
                    CanJumpReleaseAttachedTongue)
                {
                    Release();
                    double jumpFactor = MathUtil.Lerp(1.0, 1.15,
                        MathUtil.Clamp01(Owner.State.Adrenaline));
                    Owner.BodyChunks[0].Velocity.Y = -8.0 * jumpFactor;
                    Owner.BodyChunks[1].Velocity.Y = -7.0 * jumpFactor;
                    Owner.Movement.SetJumpBoost(8.0);
                    Owner.EmitSound("Slugcat_Normal_Jump", mouth, 1.0, 1.0, 3);
                    return;
                }
                position = anchor;
                velocity = Vec2.Zero;
                FillRope(mouth, anchor, world);
                ApplyElasticity();
                elastic = Math.Max(0.0, elastic - 0.05);
                requestedLength = MathUtil.MoveTowards(requestedLength, idealLength,
                    (1.0 - elastic) * 2.0);
                double distance = desktopRope.TotalLength;
                if (distance > 500.0 ||
                    !world.ContainsSurface(attachedSurfaceId, attachedSurfaceKind, anchor, 5.0))
                {
                    Release();
                }
                return;
            }

            if (mode == SaintTongueMode.ShootingOut)
            {
                lastPosition = position;
                position += velocity;
                requestedLength = Math.Max(0.0, requestedLength - 4.0);
                velocity.Y += 0.9 - 0.8 *
                    MathUtil.InverseLerp(0.0, 1.0, elastic);
                DesktopSurface hit;
                Vec2 hitPoint;
                if (FindFirstSurfaceHit(world, lastPosition, position, out hit, out hitPoint))
                {
                    mode = SaintTongueMode.AttachedToTerrain;
                    anchor = position = hitPoint;
                    velocity = Vec2.Zero;
                    attachedSurfaceId = hit.Id;
                    attachedSurfaceKind = hit.Kind;
                    attachedTicks = 0;
                    requestedLength = Vec2.Distance(mouth, anchor);
                    elastic = 1.0;
                    Owner.EmitSound("Tube_Worm_Tongue_Hit_Terrain", anchor, 1.0, 1.0, 4);
                }
                else
                {
                    if (returning && Vec2.Distance(mouth, position) < 40.0)
                        mode = SaintTongueMode.Retracted;
                    else if (Vec2.Dot((position - mouth).Normalized,
                        velocity.Normalized) < 0.0)
                        returning = true;
                }
                FillRope(mouth, position, world);
                if (mode != SaintTongueMode.Retracted) ApplyElasticity();
                return;
            }

            if (mode == SaintTongueMode.Retracting)
            {
                FinishRetraction(mouth);
                return;
            }

            position = lastPosition = mouth;
            FillRope(mouth, mouth, world);
            bool valid = Owner.State.Conscious && !Owner.State.Grounded &&
                Owner.State.CanJump <= 0 && !Owner.Movement.LaunchedThisTick &&
                Owner.State.BodyMode != BodyModeIndex.Crawl &&
                Owner.State.BodyMode != BodyModeIndex.CorridorClimb &&
                Owner.State.BodyMode != BodyModeIndex.ClimbIntoShortCut &&
                Owner.State.BodyMode != BodyModeIndex.WallClimb &&
                Owner.State.BodyMode != BodyModeIndex.Swimming &&
                Owner.State.BodyMode != BodyModeIndex.ZeroG &&
                Owner.State.Animation != AnimationIndex.ClimbOnBeam &&
                Owner.State.Animation != AnimationIndex.HangFromBeam &&
                Owner.State.Animation != AnimationIndex.AntlerClimb &&
                Owner.State.Animation != AnimationIndex.VineGrab &&
                Owner.State.Animation != AnimationIndex.ZeroGPoleGrab;
            if (!input.JumpPressed || input.Pickup || !valid) return;
            Vec2 direction;
            if (input.Y < 0)
                direction = Vec2.Up;
            else
                direction = new Vec2(Owner.State.Facing, -0.7).Normalized;
            direction = (direction + Owner.BodyChunks[0].Velocity.Normalized * 0.2).Normalized;
            direction = AutoAim(world, mouth, direction);
            mode = SaintTongueMode.ShootingOut;
            position = mouth + direction * 5.0;
            velocity = direction * 70.0;
            requestedLength = 140.0;
            elastic = 1.0;
            returning = false;
            Owner.EmitSound("Tube_Worm_Shoot_Tongue", mouth, 1.0, 1.0, 4);
            input.Jump = false;
        }

        private void Release()
        {
            if (mode == SaintTongueMode.AttachedToTerrain)
                Owner.EmitSound("Tube_Worm_Detach_Tongue_Terrain", anchor, 1.0, 1.0, 4);
            mode = SaintTongueMode.Retracting;
            attachedSurfaceId = 0;
            attachedSurfaceKind = DesktopSurfaceKind.ScreenEdge;
            returning = false;
        }

        private void FinishRetraction(Vec2 mouth)
        {
            mode = SaintTongueMode.Retracted;
            position = lastPosition = mouth;
            velocity = Owner.BodyChunks[0].Velocity;
            FillRope(mouth, mouth, Owner.World);
        }

        public override void Reset()
        {
            mode = SaintTongueMode.Retracted;
            attachedSurfaceId = 0;
            attachedSurfaceKind = DesktopSurfaceKind.ScreenEdge;
            position = Owner.BodyChunks[0].Position;
            lastPosition = position;
            returning = false;
            LastElasticityExcess = 0.0;
            LastElasticityTargetLength = 0.0;
            LastElasticityRequestLength = 0.0;
            FillRope(position, position, null);
        }

        private static Vec2 AutoAim(DesktopCollisionWorld world, Vec2 from, Vec2 direction)
        {
            if (RayIsClear(world, from, from + direction * 230.0)) return direction;
            for (int angle = 5; angle <= 25; angle += 5)
            {
                Vec2 left = Rotate(direction, -angle * Math.PI / 180.0);
                if (RayIsClear(world, from, from + left * 230.0)) return left;
                Vec2 right = Rotate(direction, angle * Math.PI / 180.0);
                if (RayIsClear(world, from, from + right * 230.0)) return right;
            }
            return direction;
        }

        private static bool RayIsClear(DesktopCollisionWorld world, Vec2 from, Vec2 to)
        {
            DesktopSurface hit;
            Vec2 point;
            return !FindFirstSurfaceHit(world, from, to, out hit, out point);
        }

        private static Vec2 Rotate(Vec2 value, double radians)
        {
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new Vec2(value.X * cosine - value.Y * sine,
                value.X * sine + value.Y * cosine);
        }

        private void FillRope(Vec2 from, Vec2 to, DesktopCollisionWorld world)
        {
            if (world == null)
            {
                for (int i = 0; i < rope.Length; i++)
                {
                    rope[i] = Vec2.Lerp(to, from, i / (double)(rope.Length - 1));
                    lastRope[i] = rope[i];
                    ropeVelocity[i] = Vec2.Zero;
                    ropeClaimed[i] = false;
                }
                return;
            }
            desktopRope.Update(world, from, to, attachedSurfaceId);
            if (desktopRope.TotalLength < 0.0001)
            {
                for (int i = 0; i < rope.Length; i++)
                {
                    lastRope[i] = rope[i];
                    rope[i] = from;
                    ropeVelocity[i] = Vec2.Zero;
                    ropeClaimed[i] = false;
                }
                return;
            }
            for (int i = 0; i < rope.Length; i++)
            {
                lastRope[i] = rope[i];
                ropeClaimed[i] = false;
            }

            IList<Vec2> path = desktopRope.Path;
            double walked = 0.0;
            for (int index = path.Count - 1; index >= 0; index--)
            {
                if (index < path.Count - 1)
                    walked += Vec2.Distance(path[index + 1], path[index]);
                double fraction = desktopRope.TotalLength < 0.0001
                    ? 0.0 : walked / desktopRope.TotalLength;
                AlignRope(fraction, path[index]);
            }

            for (int i = 0; i < rope.Length; i++)
            {
                if (ropeClaimed[i]) continue;
                rope[i] += ropeVelocity[i];
                ropeVelocity[i] *= 0.98;
                int before = i;
                int after = i;
                while (before > 0 && !ropeClaimed[before]) before--;
                while (after < rope.Length - 1 && !ropeClaimed[after]) after++;
                Vec2 target = Vec2.Lerp(rope[before], rope[after],
                    MathUtil.InverseLerp(before, after, i));
                if (mode == SaintTongueMode.Retracted)
                    rope[i] = target;
                else
                {
                    ropeVelocity[i] += (target - rope[i]) * 0.2;
                    rope[i] = Vec2.Lerp(rope[i], target, 0.4);
                }
            }

            for (int i = 1; i < rope.Length; i++) ConnectRopeSegments(i, i - 1);
            for (int i = 0; i < rope.Length; i++) ropeClaimed[i] = false;
        }

        private void AlignRope(double fraction, Vec2 alignPosition)
        {
            int index = MathUtil.Clamp((int)(fraction * rope.Length), 0, rope.Length - 1);
            lastRope[index] = rope[index];
            rope[index] = alignPosition;
            ropeVelocity[index] = Vec2.Zero;
            ropeClaimed[index] = true;
        }

        private void ConnectRopeSegments(int first, int second)
        {
            Vec2 direction = (rope[second] - rope[first]).Normalized;
            double distance = Vec2.Distance(rope[first], rope[second]);
            double target = desktopRope.TotalLength / rope.Length * 0.1;
            Vec2 correction = direction * ((distance - target) * 0.5);
            if (!ropeClaimed[first])
            {
                rope[first] += correction;
                ropeVelocity[first] += correction;
            }
            if (!ropeClaimed[second])
            {
                rope[second] -= correction;
                ropeVelocity[second] -= correction;
            }
        }

        private void ApplyElasticity()
        {
            double terrainMassShare = mode == SaintTongueMode.AttachedToTerrain ? 1.0 : 0.0;
            Vec2 baseDirection = (desktopRope.AConnect -
                Owner.BodyChunks[0].Position).Normalized;
            // Clamp the requested rope length to the active rope span, then
            // relax toward the target as elasticity decays. A larger attached
            // factor prevents a visibly slack rope from pulling every tick.
            const double onRopePosition = 1.0;
            const double totalRope = 200.0;
            double requestRope = Math.Min(requestedLength,
                onRopePosition * totalRope);
            LastElasticityRequestLength = requestRope;
            double attachedFactor = MathUtil.Lerp(1.1, 0.7,
                MathUtil.InverseLerp(0.5, 0.4,
                    Math.Abs(0.5 - onRopePosition)));
            double targetLength = requestRope * MathUtil.Lerp(
                mode == SaintTongueMode.AttachedToTerrain ? attachedFactor : 0.7,
                1.0, elastic);
            LastElasticityTargetLength = targetLength;
            double strength = MathUtil.Lerp(0.85, 0.25, elastic);
            double excess = desktopRope.TotalLength - targetLength;
            LastElasticityExcess = excess;
            if (excess <= 0.0) return;
            Owner.BodyChunks[0].Velocity += baseDirection *
                (excess * strength * terrainMassShare);
            Owner.BodyChunks[0].Position += baseDirection *
                (excess * strength * terrainMassShare *
                 MathUtil.Lerp(1.0, 0.5, elastic));
            if (mode == SaintTongueMode.ShootingOut ||
                mode == SaintTongueMode.Retracting)
            {
                Vec2 tongueDirection = (desktopRope.BConnect - position).Normalized;
                position += tongueDirection * (excess * strength *
                    (1.0 - terrainMassShare) * MathUtil.Lerp(1.0, 0.5, elastic));
                velocity += tongueDirection *
                    (excess * strength * (1.0 - terrainMassShare));
            }
        }

        private static bool FindFirstSurfaceHit(DesktopCollisionWorld world, Vec2 from,
            Vec2 to, out DesktopSurface hit, out Vec2 point)
        {
            hit = null;
            point = to;
            double best = double.MaxValue;
            Vec2 delta = to - from;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                double t;
                if (surface.IsHorizontal)
                {
                    if (Math.Abs(delta.Y) < 0.0001) continue;
                    t = (surface.Top - from.Y) / delta.Y;
                    if (t < 0.0 || t > 1.0) continue;
                    double x = from.X + delta.X * t;
                    if (x < surface.Left || x > surface.Right) continue;
                }
                else
                {
                    if (Math.Abs(delta.X) < 0.0001) continue;
                    t = (surface.WallX - from.X) / delta.X;
                    if (t < 0.0 || t > 1.0) continue;
                    double y = from.Y + delta.Y * t;
                    if (y < surface.Top || y > surface.Bottom) continue;
                }
                if (t >= best) continue;
                best = t;
                hit = surface;
                point = from + delta * t;
            }
            return hit != null;
        }
    }

    public interface IGourmandEdible
    {
        string FoodId { get; }
    }

    public interface IGourmandCraftingRecipe
    {
        string ResultId { get; }
        bool Matches(IGourmandEdible first, IGourmandEdible second);
    }

    public sealed class GourmandCraftingFramework
    {
        private readonly List<IGourmandCraftingRecipe> recipes =
            new List<IGourmandCraftingRecipe>();

        public IList<IGourmandCraftingRecipe> Recipes { get { return recipes.AsReadOnly(); } }
        public void Register(IGourmandCraftingRecipe recipe)
        {
            if (recipe != null && !recipes.Contains(recipe)) recipes.Add(recipe);
        }

        public IGourmandCraftingRecipe Find(IGourmandEdible first, IGourmandEdible second)
        {
            for (int i = 0; i < recipes.Count; i++)
                if (recipes[i].Matches(first, second)) return recipes[i];
            return null;
        }
    }

    public sealed class GourmandAbilityController : DefaultAbilityController
    {
        private bool exhausted;
        private int rollCounter;
        private int consistentDownDiagonal;
        private int lastDownDiagonal;
        private int rollDirection;
        private int stopRollingCounter;
        private int allowRoll;
        private int exitBellySlideCounter;

        public GourmandAbilityController(Slugcat owner) : base(owner)
        {
            Crafting = new GourmandCraftingFramework();
        }

        public readonly GourmandCraftingFramework Crafting;
        public bool Exhausted { get { return exhausted; } }
        public bool Rolling { get { return Owner.Movement.Rolling; } }
        public bool Sliding { get { return Owner.Movement.Sliding; } }
        public int RollCounter { get { return Owner.State.RollCounter; } }
        public int AllowRoll { get { return Owner.State.AllowRoll; } }
        public int ConsistentDownDiagonal { get { return Owner.State.ConsistentDownDiagonal; } }
        public override string Name { get { return "Roll / exhaustion / crafting"; } }
        public override string DebugState
        {
            get { return string.Format("roll:{0} exhausted:{1} aerobic:{2:0.00}",
                rollCounter, exhausted, Owner.State.AerobicLevel); }
        }

        public override void UpdateBeforeMovement(ref VirtualInput input, DesktopCollisionWorld world)
        {
            if (Owner.State.AerobicLevel >= 0.95) exhausted = true;
            else if (Owner.State.AerobicLevel < 0.4) exhausted = false;
            if (exhausted)
            {
                int slow = (int)MathUtil.Lerp(6.0, 0.0,
                    MathUtil.InverseLerp(0.7, 0.4, Owner.State.AerobicLevel));
                Owner.State.SlowMovementStun = Math.Max(
                    Owner.State.SlowMovementStun, slow);
            }

            // Shared movement handles roll and belly-slide transitions. Keep
            // the legacy controller path only as a defensive fallback.
            if (Owner.Movement != null) return;

            if (allowRoll > 0) allowRoll--;
            if (!Owner.BodyChunks[1].ContactFloor) allowRoll = 15;

            int downDiagonal = input.X != 0 && input.Y > 0 ? input.X : 0;
            if (downDiagonal != 0 && downDiagonal == lastDownDiagonal)
                consistentDownDiagonal++;
            else
                consistentDownDiagonal = 0;
            lastDownDiagonal = downDiagonal;

            if (!Owner.State.Conscious)
            {
                rollDirection = 0;
                rollCounter = 0;
                stopRollingCounter = 0;
                return;
            }

            if (rollDirection == 0 &&
                Owner.State.Animation == AnimationIndex.DownOnFours &&
                Owner.BodyChunks[1].ContactFloor && downDiagonal == Owner.State.Facing)
            {
                rollDirection = Owner.State.Facing;
                rollCounter = 0;
                Owner.State.Animation = AnimationIndex.BellySlide;
                Owner.State.Standing = false;
                Owner.EmitSound("Slugcat_Belly_Slide_Init", Owner.Center, 1.0, 1.0, 1);
            }

            if (rollDirection == 0) return;
            rollCounter++;
            Owner.BodyConnection.Distance = 10.0;
            if (Owner.State.BodyMode != BodyModeIndex.Default &&
                Owner.State.BodyMode != BodyModeIndex.Stand &&
                Owner.State.BodyMode != BodyModeIndex.Crawl || rollCounter > 200)
            {
                StopRolling(false);
                return;
            }

            if (Owner.State.Animation == AnimationIndex.BellySlide)
            {
                UpdateBellySlide(input);
                return;
            }

            Owner.State.Animation = AnimationIndex.Roll;
            Owner.State.BodyMode = BodyModeIndex.Default;
            BodyChunk chest = Owner.BodyChunks[0];
            BodyChunk hips = Owner.BodyChunks[1];
            Vec2 bodyDirection = (chest.Position - hips.Position).Normalized;
            Vec2 perpendicular = new Vec2(bodyDirection.Y, -bodyDirection.X);
            chest.Velocity *= 0.9;
            hips.Velocity *= 0.9;
            Vec2 rotationForce = perpendicular * (2.0 * rollDirection);
            chest.Velocity += rotationForce;
            hips.Velocity -= rotationForce;
            Owner.State.AerobicLevel = MathUtil.Clamp01(
                Owner.State.AerobicLevel + 0.01 / 9.0);

            bool blocked = (rollDirection > 0 && (chest.ContactRight || hips.ContactRight)) ||
                (rollDirection < 0 && (chest.ContactLeft || hips.ContactLeft));
            if (!chest.ContactFloor && !hips.ContactFloor)
                blocked = true;
            else
            {
                chest.Velocity.X += 1.1 * rollDirection;
                hips.Velocity.X += 1.1 * rollDirection;
            }

            stopRollingCounter = blocked ? stopRollingCounter + 1 : 0;
            double upright = MathUtil.InverseLerp(0.0, 1.0,
                Math.Abs(bodyDirection.Y));
            Owner.EmitSound("Slugcat_Roll_LOOP", Owner.Center,
                MathUtil.Lerp(0.5, 1.0, upright),
                MathUtil.Lerp(0.85, 1.15, upright), 1);

            bool chestAboveHips = chest.Position.Y < hips.Position.Y;
            bool inputExit = rollCounter > 15 && input.Y < 1 && downDiagonal == 0;
            bool exhaustedShortExit = rollCounter > 30 && exhausted;
            bool opposite = input.X == -rollDirection;
            if (((inputExit || exhaustedShortExit || opposite) && chestAboveHips) ||
                (rollCounter > 60 && exhausted) || stopRollingCounter > 6)
            {
                StopRolling(true);
            }
        }

        public override void TerrainImpact(TerrainImpactData impact)
        {
            // Shared movement normally handles roll entry before this
            // character-specific fallback callback runs.
            if (Owner.Movement != null) return;
            int downDiagonal = Owner.LastInput.X != 0 && Owner.LastInput.Y > 0
                ? Owner.LastInput.X : 0;
            if (downDiagonal == 0 || Owner.State.Animation == AnimationIndex.Roll ||
                impact.ImpactDirection.Y >= 0.0 || allowRoll <= 0 ||
                consistentDownDiagonal <= (impact.ImpactSpeed > 24.0 ? 1 : 6) ||
                (impact.ImpactSpeed <= 12.0 &&
                    Owner.State.Animation != AnimationIndex.Flip)) return;
            Owner.EmitSound("Slugcat_Roll_Init", Owner.Center, 1.0, 1.0, 1);
            Owner.State.Animation = AnimationIndex.Roll;
            rollDirection = downDiagonal;
            rollCounter = 0;
            Owner.State.Standing = false;
            double target = 9.0 * Owner.LastInput.X;
            for (int i = 0; i < Owner.BodyChunks.Length; i++)
                Owner.BodyChunks[i].Velocity.X = MathUtil.Lerp(
                    Owner.BodyChunks[i].Velocity.X, target, 0.7);
        }

        private void UpdateBellySlide(VirtualInput input)
        {
            Owner.State.BodyMode = BodyModeIndex.Default;
            BodyChunk chest = Owner.BodyChunks[0];
            BodyChunk hips = Owner.BodyChunks[1];
            if (rollCounter < 6)
            {
                hips.Velocity.Y -= 2.7;
                hips.Velocity.X -= 9.1 * rollDirection;
            }
            double startForce = exhausted ? 14.0 : 45.0;
            chest.Velocity.X += startForce * rollDirection *
                Math.Sin(rollCounter / 15.0 * Math.PI);
            if (!chest.ContactFloor) chest.Velocity.X *= 0.5;
            if (!hips.ContactFloor) hips.Velocity.X *= 0.5;

            int downDiagonal = input.X != 0 && input.Y > 0 ? input.X : 0;
            if (input.X != rollDirection && downDiagonal != rollDirection)
                exitBellySlideCounter++;
            else
                exitBellySlideCounter = 0;

            bool jumpExit = input.JumpPressed && rollCounter > 0 && rollCounter < 12;
            bool leftGround = rollCounter > 6 && !chest.ContactFloor && !hips.ContactFloor;
            if ((rollCounter > 8 && exitBellySlideCounter > 6) ||
                rollCounter > 15 || leftGround || jumpExit)
            {
                chest.Velocity.Y = 0.0;
                hips.Velocity.Y = 0.0;
                bool success = input.Y < 0;
                if (Math.Abs(chest.Velocity.X) > 8.0) chest.Velocity *= 0.5;
                if (Math.Abs(hips.Velocity.X) > 8.0) hips.Velocity *= 0.5;
                Owner.State.SlowMovementStun = success ? 20 : 40;
                Owner.EmitSound(success
                    ? "Slugcat_Belly_Slide_Finish_Success"
                    : "Slugcat_Belly_Slide_Finish_Fail",
                    Owner.Center, 1.0, 1.0, 1);
                rollDirection = 0;
                rollCounter = 0;
                exitBellySlideCounter = 0;
                Owner.State.Animation = AnimationIndex.None;
                Owner.State.Standing = success;
            }
            else
                Owner.State.Standing = false;
        }

        private void StopRolling(bool sound)
        {
            rollDirection = 0;
            rollCounter = 0;
            stopRollingCounter = 0;
            Owner.State.Animation = AnimationIndex.None;
            if (sound)
                Owner.EmitSound("Slugcat_Roll_Finish", Owner.Center, 1.0, 1.0, 1);
        }

        public override void Reset()
        {
            exhausted = false;
            rollDirection = 0;
            rollCounter = 0;
            consistentDownDiagonal = 0;
            lastDownDiagonal = 0;
            stopRollingCounter = 0;
            allowRoll = 0;
            exitBellySlideCounter = 0;
        }
    }
}
