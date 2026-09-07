using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Creature
{
    public sealed partial class SlugcatMovement
    {
        private readonly Slugcat owner;
        private bool previousJump;
        private int landingCounter;
        private double jumpBoost;
        private long dropThroughSurfaceId;
        private int dropThroughTicks;
        private readonly VirtualInput[] inputHistory = new VirtualInput[4];
        private readonly Vec2[] lastAirMovementContribution = new Vec2[2];
        private readonly double[] lastAirHorizontalVelocityBefore = new double[2];
        private readonly double[] lastAirHorizontalVelocityAfter = new double[2];
        private string lastAirControlBranch = "Grounded";

        public SlugcatMovement(Slugcat owner)
        {
            this.owner = owner;
        }

        public long IgnoredSurfaceId { get { return dropThroughTicks > 0 ? dropThroughSurfaceId : 0; } }
        public VirtualInput[] InputHistory { get { return (VirtualInput[])inputHistory.Clone(); } }
        internal VirtualInput[] InputHistoryForRead { get { return inputHistory; } }
        public Vec2[] LastAirMovementContribution { get { return lastAirMovementContribution; } }
        public double[] LastAirHorizontalVelocityBefore { get { return lastAirHorizontalVelocityBefore; } }
        public double[] LastAirHorizontalVelocityAfter { get { return lastAirHorizontalVelocityAfter; } }
        public string LastAirControlBranch { get { return lastAirControlBranch; } }
        public double JumpBoost { get { return jumpBoost; } }

        internal void SetJumpBoost(double value)
        {
            jumpBoost = Math.Max(jumpBoost, value);
        }

        public void ApplyInput(VirtualInput input, DesktopCollisionWorld world)
        {
            // The retail-state adapter lives in a separate partial file so this
            // compatibility implementation remains available for diagnostics.
            if (owner.SelectedSlugcat != null)
            {
                ApplyOriginalInput(input, world);
                return;
            }
            RecordInput(input);
            if (dropThroughTicks > 0) dropThroughTicks--;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            SlugcatState state = owner.State;
            GourmandAbilityController gourmand = owner.AbilityController as GourmandAbilityController;
            bool gourmandExhausted = gourmand != null &&
                (gourmand.Exhausted || state.AerobicLevel >= 0.95);
            double idleRecovery = gourmandExhausted ? 200.0 : 400.0;
            double movingRecovery = gourmandExhausted ? 800.0 : 1100.0;
            if (gourmandExhausted && state.BodyMode == BodyModeIndex.Crawl)
            {
                idleRecovery = 125.0;
                movingRecovery = 400.0;
            }
            double recoveryDenominator = (input.X == 0 && input.Y == 0 ? idleRecovery : movingRecovery) *
                (1.0 + 3.0 * MathUtil.InverseLerp(0.9, 1.0, state.AerobicLevel));
            state.AerobicLevel = Math.Max(0.0, state.AerobicLevel - 1.0 / recoveryDenominator);
            if (state.SlowMovementStun > 0) state.SlowMovementStun--;
            bool wasGrounded = chest.ContactFloor || hips.ContactFloor;
            bool wallContact = chest.ContactLeft || chest.ContactRight || hips.ContactLeft || hips.ContactRight;
            BodyModeIndex previousBodyMode = state.BodyMode;
            int previousFacing = state.Facing;
            lastAirMovementContribution[0] = Vec2.Zero;
            lastAirMovementContribution[1] = Vec2.Zero;

            state.JustLanded = !state.Grounded && wasGrounded;
            state.Grounded = wasGrounded;
            if (state.JustLanded)
            {
                landingCounter = 6;
                double impactSpeed = Math.Max(chest.FloorImpactSpeed, hips.FloorImpactSpeed);
                state.LandingCompression = MathUtil.Clamp(impactSpeed / 12.0, 0.25, 1.0);
            }
            else if (landingCounter > 0)
            {
                landingCounter--;
                state.LandingCompression *= 0.72;
            }

            if (input.X != 0)
            {
                state.Facing = input.X;
            }
            int bodyAxis = Math.Abs(chest.Position.X - hips.Position.X) > 0.5
                ? Math.Sign(chest.Position.X - hips.Position.X)
                : previousFacing;
            bool startCrawlTurn = previousBodyMode == BodyModeIndex.Crawl &&
                wasGrounded && input.X != 0 && input.X != bodyAxis;

            if (input.DropThrough && wasGrounded && owner.PrimarySupportingSurfaceId > 0)
            {
                dropThroughSurfaceId = owner.PrimarySupportingSurfaceId;
                dropThroughTicks = 12;
                chest.Velocity.Y = Math.Max(chest.Velocity.Y, 2.5);
                hips.Velocity.Y = Math.Max(hips.Velocity.Y, 2.5);
                state.Grounded = false;
                state.BodyMode = BodyModeIndex.Default;
                state.Animation = AnimationIndex.None;
                state.AnimationFrame++;
                previousJump = input.Jump;
                return;
            }

            bool resting = input.Posture == VirtualPosture.Sit || input.Posture == VirtualPosture.Sleep;
            bool crawl = input.Y > 0 || resting;
            SlugcatMovementProfile movement = owner.SelectedSlugcat.Movement;
            bool crawlBodyMode = wasGrounded &&
                (crawl || previousBodyMode == BodyModeIndex.Crawl);
            double mainRunSpeed = wasGrounded
                ? (crawlBodyMode
                    ? (input.Y != 0 ? 1.0 : movement.CrawlSpeed)
                    : 4.2 * movement.RunSpeedFactor)
                : (input.Y != 0 ? movement.CrawlSpeed : movement.AirRunSpeed);
            double hipsRunSpeed = wasGrounded
                ? (crawlBodyMode ? mainRunSpeed :
                    (input.Y != 0 ? 2.0 : 4.0 * movement.RunSpeedFactor))
                : mainRunSpeed;
            double slowMovementFactor = MathUtil.Lerp(1.0, 0.5,
                MathUtil.Clamp01(state.SlowMovementStun / 10.0));
            mainRunSpeed *= slowMovementFactor;
            hipsRunSpeed *= slowMovementFactor;
            lastAirHorizontalVelocityBefore[0] = chest.Velocity.X;
            lastAirHorizontalVelocityBefore[1] = hips.Velocity.X;
            double chestAirX = ApplyOriginalHorizontalMovement(
                chest, input.X, mainRunSpeed, wasGrounded);
            double hipsAirX = ApplyOriginalHorizontalMovement(
                hips, input.X, hipsRunSpeed, wasGrounded);
            lastAirHorizontalVelocityAfter[0] = chest.Velocity.X;
            lastAirHorizontalVelocityAfter[1] = hips.Velocity.X;
            if (!wasGrounded)
            {
                lastAirMovementContribution[0].X = chestAirX;
                lastAirMovementContribution[1].X = hipsAirX;
                lastAirControlBranch = "Player.MovementUpdate Default+None no-contact";
            }
            else lastAirControlBranch = "Player.MovementUpdate grounded contact";

            if (input.X == 0 && wasGrounded)
            {
                state.Stillness = MathUtil.Clamp01(state.Stillness + 0.035);
            }
            else
            {
                state.Stillness = MathUtil.Clamp01(state.Stillness - 0.12);
            }

            if (wallContact && input.Y < 0 && !wasGrounded)
            {
                state.BodyMode = BodyModeIndex.WallClimb;
                state.Animation = AnimationIndex.None;
                // Compatibility path: match Player's gravity-driven wall
                // slide. There is no continuous upward movement in WallClimb.
                chest.Velocity.X *= 0.5;
                hips.Velocity.X *= 0.5;
            }
            else if (wasGrounded)
            {
                state.BodyMode = crawl ? BodyModeIndex.Crawl : BodyModeIndex.Stand;
            }
            else
            {
                state.BodyMode = BodyModeIndex.Default;
            }

            if (startCrawlTurn || state.Animation == AnimationIndex.CrawlTurn)
            {
                state.BodyMode = BodyModeIndex.Default;
                state.Animation = AnimationIndex.CrawlTurn;
            }

            bool launchedThisTick = false;
            if (input.Jump && !previousJump && !wasGrounded &&
                (previousBodyMode == BodyModeIndex.WallClimb || wallContact))
            {
                int direction = (chest.ContactRight || hips.ContactRight) ? -1 :
                    ((chest.ContactLeft || hips.ContactLeft) ? 1 : -state.Facing);
                bool rivulet = owner.SelectedSlugcat.Id == SlugcatId.Rivulet;
                chest.Velocity.Y = rivulet ? -10.0 : -8.0;
                hips.Velocity.Y = rivulet ? -9.0 : -7.0;
                chest.Velocity.X = (rivulet ? 9.0 : 6.0) * direction;
                hips.Velocity.X = (rivulet ? 7.0 : 5.0) * direction;
                jumpBoost = rivulet ? 4.0 : 0.0;
                state.BodyMode = BodyModeIndex.Default;
                state.Animation = AnimationIndex.None;
                state.Standing = true;
                launchedThisTick = true;
                owner.EmitSound("Slugcat_Wall_Jump", owner.Center, 1.0, 1.0, 1);
            }
            else if (input.Jump && !previousJump && wasGrounded)
            {
                // Player.Jump's ordinary standing branch assigns 4/3 in
                // Rain World's y-up coordinates and leaves animation=None.
                chest.Velocity.Y = -movement.StandingJumpChest;
                hips.Velocity.Y = -movement.StandingJumpHips;
                jumpBoost = 8.0;
                state.AerobicLevel = MathUtil.Clamp01(state.AerobicLevel + 0.75 / 9.0);
                state.Animation = AnimationIndex.None;
                state.Grounded = false;
                state.BodyMode = BodyModeIndex.Default;
                launchedThisTick = true;
                owner.EmitSound(owner.SelectedSlugcat.Audio.Jump, owner.Center, 1.0, 1.0, 3);
            }
            // Player keeps the normal body connection at 17 in Stand, Crawl,
            // ordinary air, and landing. Only specialized Roll/Corridor modes
            // replace it, and those are not synthesized here.
            double targetDistance = SimulationConstants.BodyConnectionDistance;
            owner.BodyConnection.Distance = MathUtil.Lerp(
                owner.BodyConnection.Distance, targetDistance, 0.25);

            if (!launchedThisTick && !wasGrounded && input.Jump && jumpBoost > 0.0)
            {
                jumpBoost = Math.Max(0.0, jumpBoost - 1.5);
                double boost = (jumpBoost + 1.0) * 0.3;
                chest.Velocity.Y -= boost;
                hips.Velocity.Y -= boost;
                lastAirMovementContribution[0].Y -= boost;
                lastAirMovementContribution[1].Y -= boost;
            }
            else if (!input.Jump)
            {
                jumpBoost = 0.0;
            }

            if (!launchedThisTick)
            {
                ApplyOriginalBodyModeForces(input, wasGrounded, input.Posture);
            }

            if (!launchedThisTick)
            {
                if (wasGrounded)
                {
                    if (input.Posture == VirtualPosture.Sleep)
                    {
                        state.Animation = AnimationIndex.Sleep;
                    }
                    else if (input.Posture == VirtualPosture.Sit)
                    {
                        state.Animation = AnimationIndex.Sit;
                    }
                    else if (crawl && previousBodyMode != BodyModeIndex.Crawl)
                    {
                        state.Animation = AnimationIndex.DownOnFours;
                    }
                    else if (!crawl && previousBodyMode == BodyModeIndex.Crawl)
                    {
                        state.Animation = AnimationIndex.StandUp;
                    }
                    else if (state.Animation != AnimationIndex.Sit &&
                             state.Animation != AnimationIndex.Sleep &&
                             state.Animation != AnimationIndex.DownOnFours &&
                             state.Animation != AnimationIndex.StandUp &&
                             state.Animation != AnimationIndex.CrawlTurn)
                    {
                        state.Animation = AnimationIndex.None;
                    }
                }
                else if (state.BodyMode != BodyModeIndex.WallClimb)
                {
                    // Ordinary rising and falling both use AnimationIndex.None
                    // in Player. Velocity and body orientation carry the phase.
                    if (state.Animation != AnimationIndex.Flip &&
                        state.Animation != AnimationIndex.Roll &&
                        state.Animation != AnimationIndex.BellySlide)
                        state.Animation = AnimationIndex.None;
                }
            }

            if (gourmand != null && gourmand.Rolling)
            {
                state.Animation = AnimationIndex.Roll;
                state.BodyMode = BodyModeIndex.Default;
                state.Standing = false;
            }
            else if (gourmand != null && gourmand.Sliding)
            {
                state.Animation = AnimationIndex.BellySlide;
                state.BodyMode = BodyModeIndex.Default;
                state.Standing = false;
            }

            double speed = Math.Abs((chest.Velocity.X + hips.Velocity.X) * 0.5);
            state.RunCycle += speed * (crawl ? 0.07 : 0.11);
            if (wasGrounded && !launchedThisTick)
            {
                if (input.X == 0 || resting)
                {
                    state.AnimationFrame = 0;
                }
                else
                {
                    state.AnimationFrame++;
                    int lastFrame = crawl ? 10 : 6;
                    if (state.AnimationFrame > lastFrame) state.AnimationFrame = 0;
                    if (state.AnimationFrame == 0 &&
                        state.Animation != AnimationIndex.CrawlTurn &&
                        !IsPressingIntoWall(input.X))
                    {
                        string step = ((int)Math.Floor(state.RunCycle) & 1) == 0
                            ? owner.SelectedSlugcat.Audio.FootstepA
                            : owner.SelectedSlugcat.Audio.FootstepB;
                        if (crawl) step = "Slugcat_Crawling_Step";
                        owner.EmitSound(step, owner.BodyChunks[1].Position,
                            crawl ? 0.65 : 1.0, 1.0, 2);
                    }
                    if (state.AnimationFrame == 0 && state.AerobicLevel < 0.7)
                        state.AerobicLevel = MathUtil.Clamp01(state.AerobicLevel + 0.05 / 9.0);
                }
            }
            else
            {
                state.AnimationFrame++;
            }
            previousJump = input.Jump;
        }

        // Player.checkInput still advances input history while stun prevents
        // MovementUpdate. BodyChunk physics/contact continues, but no movement
        // force is synthesized and recovery is not forced to Stand or Idle.
        public void ApplyDisabledInput(VirtualInput input)
        {
            LaunchedThisTick = false;
            StopOriginalMovementLoops();
            RecordInput(input);
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            bool grounded = chest.ContactFloor || hips.ContactFloor;
            owner.State.JustLanded = !owner.State.Grounded && grounded;
            owner.State.Grounded = grounded;
            if (owner.State.JustLanded)
            {
                landingCounter = 6;
                double impactSpeed = Math.Max(chest.FloorImpactSpeed, hips.FloorImpactSpeed);
                owner.State.LandingCompression = MathUtil.Clamp(impactSpeed / 12.0, 0.25, 1.0);
            }
            else if (landingCounter > 0)
            {
                landingCounter--;
                owner.State.LandingCompression *= 0.72;
            }
            lastAirMovementContribution[0] = Vec2.Zero;
            lastAirMovementContribution[1] = Vec2.Zero;
            lastAirHorizontalVelocityBefore[0] = chest.Velocity.X;
            lastAirHorizontalVelocityBefore[1] = hips.Velocity.X;
            lastAirHorizontalVelocityAfter[0] = chest.Velocity.X;
            lastAirHorizontalVelocityAfter[1] = hips.Velocity.X;
            lastAirControlBranch = owner.State.Dead
                ? "Player.Update dead: MovementUpdate skipped"
                : "Player.Update stun>0: MovementUpdate skipped";
            owner.BodyConnection.Distance = MathUtil.Lerp(owner.BodyConnection.Distance,
                SimulationConstants.BodyConnectionDistance, 0.25);
            previousJump = input.Jump;
        }

        private void RecordInput(VirtualInput input)
        {
            for (int history = inputHistory.Length - 1; history > 0; history--)
                inputHistory[history] = inputHistory[history - 1];
            inputHistory[0] = input;
        }

        private static double ApplyOriginalHorizontalMovement(BodyChunk chunk, int direction,
            double dynamicRunSpeed, bool grounded)
        {
            double before = chunk.Velocity.X;
            const double acceleration = 2.4 * SimulationConstants.SurfaceFriction;
            if (direction < 0)
            {
                double amount = acceleration;
                if (chunk.Velocity.X - amount < -dynamicRunSpeed)
                    amount = dynamicRunSpeed + chunk.Velocity.X;
                if (amount > 0.0) chunk.Velocity.X -= amount;
            }
            else if (direction > 0)
            {
                double amount = acceleration;
                if (chunk.Velocity.X + amount > dynamicRunSpeed)
                    amount = dynamicRunSpeed - chunk.Velocity.X;
                if (amount > 0.0) chunk.Velocity.X += amount;
            }

            if (!grounded) return chunk.Velocity.X - before;
            double target = direction == 0
                ? 0.0
                : MathUtil.Clamp(chunk.Velocity.X, -dynamicRunSpeed, dynamicRunSpeed);
            chunk.Velocity.X += (target - chunk.Velocity.X) *
                Math.Pow(SimulationConstants.SurfaceFriction, 1.5);
            return chunk.Velocity.X - before;
        }

        private void ApplyOriginalBodyModeForces(VirtualInput input, bool grounded,
            VirtualPosture posture)
        {
            if (!grounded)
            {
                return;
            }

            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];

            if (owner.State.Animation == AnimationIndex.CrawlTurn)
            {
                // Player.UpdateAnimation CrawlTurn. X is unchanged; Rain
                // World's y-up forces are inverted for desktop y-down.
                chest.Velocity.X += owner.State.Facing;
                hips.Velocity.X -= 2.0 * owner.State.Facing;
                bool rotatingTowardInput = input.X > 0 != chest.Position.X < hips.Position.X;
                if (rotatingTowardInput)
                {
                    chest.Velocity.Y += 3.0;
                    if (chest.Position.Y > hips.Position.Y - 2.0)
                    {
                        owner.State.Animation = AnimationIndex.None;
                        chest.Velocity.Y += 1.0;
                    }
                }
                else
                {
                    chest.Velocity.Y -= 2.0;
                }
                if (input.X == 0) owner.State.Animation = AnimationIndex.None;
            }
            else if (owner.State.BodyMode == BodyModeIndex.Stand && posture == VirtualPosture.None)
            {
                // Player.UpdateBodyMode's Stand branch applies +1.5 to body chunk 0
                // and -4.5 to body chunk 1 in Rain World's y-up coordinates.
                // The desktop collision world uses y-down screen coordinates.
                chest.Velocity.Y -= 1.5;
                hips.Velocity.Y += 4.5;
            }
            else if (owner.State.Animation == AnimationIndex.DownOnFours)
            {
                // Player.UpdateAnimation DownOnFours, converted from Rain
                // World's y-up coordinates to desktop y-down coordinates.
                chest.Velocity.Y += 2.0;
                chest.Velocity.X += owner.State.Facing;
                hips.Velocity.X -= owner.State.Facing;
                if (chest.ContactFloor || chest.Position.Y > hips.Position.Y)
                    owner.State.Animation = AnimationIndex.None;
            }
            else if (owner.State.Animation == AnimationIndex.StandUp)
            {
                chest.Velocity.X *= 0.7;
                if (chest.Position.Y < hips.Position.Y - 3.0)
                    owner.State.Animation = AnimationIndex.None;
            }
        }
    }
}
