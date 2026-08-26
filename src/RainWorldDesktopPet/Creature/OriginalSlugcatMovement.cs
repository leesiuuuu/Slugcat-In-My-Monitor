using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Creature
{
    // Desktop movement state for the Y-down Windows coordinate system.
    // Input updates intent/counters, BodyChunks resolve contact state, and the
    // graphics layer consumes the resulting simulation state.
    public sealed partial class SlugcatMovement
    {
        private readonly string rollLoopKey = "movement:roll:" + Guid.NewGuid().ToString("N");
        private readonly string bellySlideLoopKey = "movement:belly:" + Guid.NewGuid().ToString("N");
        private bool rollLoopPlaying;
        private bool bellySlideLoopPlaying;

        public bool LaunchedThisTick { get; private set; }
        public bool Rolling { get { return owner.State.Animation == AnimationIndex.Roll && owner.State.RollDirection != 0; } }
        public bool Sliding { get { return owner.State.Animation == AnimationIndex.BellySlide && owner.State.RollDirection != 0; } }

        private void ApplyOriginalInput(VirtualInput input, DesktopCollisionWorld world)
        {
            VirtualInput previous = inputHistory[0];
            RecordInput(input);
            previousJump = input.Jump;
            LaunchedThisTick = false;
            if (dropThroughTicks > 0) dropThroughTicks--;

            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            SlugcatState state = owner.State;
            bool grounded = chest.ContactFloor || hips.ContactFloor;
            bool wallContact = chest.ContactLeft || chest.ContactRight ||
                hips.ContactLeft || hips.ContactRight;
            BodyModeIndex previousBodyMode = state.BodyMode;
            AnimationIndex previousAnimation = state.Animation;

            ClearMovementDiagnostics(chest, hips);
            UpdateOriginalContactState(grounded, chest, hips);
            state.LowerBodyFramesOnGround = hips.ContactFloor
                ? state.LowerBodyFramesOnGround + 1 : 0;
            state.UpperBodyFramesOffGround = !chest.ContactFloor
                ? state.UpperBodyFramesOffGround + 1 : 0;
            UpdateOriginalCounters(input, previous, grounded, hips);
            UpdateStandingIntent(input, previous, previousBodyMode);
            RecoverOriginalAerobic(input);

            if (input.DropThrough && grounded && owner.PrimarySupportingSurfaceId > 0)
            {
                dropThroughSurfaceId = owner.PrimarySupportingSurfaceId;
                dropThroughTicks = 12;
                chest.Velocity.Y = Math.Max(chest.Velocity.Y, 2.5);
                hips.Velocity.Y = Math.Max(hips.Velocity.Y, 2.5);
                state.Grounded = false;
                state.BodyMode = BodyModeIndex.Default;
                state.Animation = AnimationIndex.None;
                StopOriginalMovementLoops();
                FinishOriginalTick(input, grounded);
                return;
            }

            if (Rolling)
            {
                UpdateOriginalRoll(input);
                FinishOriginalTick(input, grounded);
                return;
            }
            if (Sliding)
            {
                UpdateOriginalBellySlide(input);
                FinishOriginalTick(input, grounded);
                return;
            }

            SelectOriginalBodyMode(input, grounded, wallContact);
            UpdateOriginalSkid(input, grounded, previousBodyMode);
            if (TryOriginalBufferedJump(input, grounded, wallContact))
            {
                FinishOriginalTick(input, grounded);
                return;
            }

            ApplyOriginalHorizontalInput(input, grounded);
            UpdateOriginalPosture(input, grounded, previousAnimation);
            if (state.Animation == AnimationIndex.Flip) ApplyOriginalFlipRotation();

            if (!grounded && input.Jump && jumpBoost > 0.0)
            {
                jumpBoost = Math.Max(0.0, jumpBoost - 1.5);
                double boost = (jumpBoost + 1.0) * 0.3;
                chest.Velocity.Y -= boost;
                hips.Velocity.Y -= boost;
                lastAirMovementContribution[0].Y -= boost;
                lastAirMovementContribution[1].Y -= boost;
            }
            else if (!input.Jump) jumpBoost = 0.0;
            FinishOriginalTick(input, grounded);
        }

        public void TerrainImpact(TerrainImpactData impact)
        {
            SlugcatState state = owner.State;
            int downDiagonal = OriginalDownDiagonal(owner.LastInput);
            if (downDiagonal == 0 || state.Animation == AnimationIndex.Roll ||
                impact.ImpactDirection.Y >= 0.0 || state.AllowRoll <= 0 ||
                state.ConsistentDownDiagonal <= (impact.ImpactSpeed > 24.0 ? 1 : 6) ||
                (impact.ImpactSpeed <= 12.0 && state.Animation != AnimationIndex.Flip))
                return;

            state.Animation = AnimationIndex.Roll;
            state.RollDirection = downDiagonal;
            state.RollCounter = 0;
            state.StopRollingCounter = 0;
            state.Standing = false;
            double target = 9.0 * owner.LastInput.X;
            for (int i = 0; i < owner.BodyChunks.Length; i++)
                owner.BodyChunks[i].Velocity.X = MathUtil.Lerp(
                    owner.BodyChunks[i].Velocity.X, target, 0.7);
            owner.EmitSound("Slugcat_Roll_Init", owner.Center, 1.0, 1.0, 1);
            StartOriginalRollLoop();
        }

        public void Reset()
        {
            StopOriginalMovementLoops();
            jumpBoost = 0.0;
            dropThroughTicks = 0;
            LaunchedThisTick = false;
            for (int i = 0; i < inputHistory.Length; i++)
                inputHistory[i] = VirtualInput.Neutral;
            SlugcatState state = owner.State;
            state.WantToJump = state.CanJump = state.CanWallJump = 0;
            state.SuperLaunchJump = state.KillSuperLaunchJumpCounter = 0;
            state.AllowRoll = state.RollDirection = state.RollCounter = 0;
            state.SlideCounter = state.InitSlideCounter = 0;
            state.CrawlTurnDelay = state.ExitBellySlideCounter = 0;
            state.StopRollingCounter = state.ConsistentDownDiagonal = 0;
            state.LowerBodyFramesOnGround = state.UpperBodyFramesOffGround = 0;
            state.FlipFromSlide = false;
        }

        private void UpdateOriginalCounters(VirtualInput input, VirtualInput previous,
            bool grounded, BodyChunk hips)
        {
            SlugcatState state = owner.State;
            if (input.JumpPressed) state.WantToJump = 5;
            else if (state.WantToJump > 0) state.WantToJump--;
            if (grounded) state.CanJump = 5;
            else if (state.CanJump > 0) state.CanJump--;

            if (state.AllowRoll > 0) state.AllowRoll--;
            if (!hips.ContactFloor) state.AllowRoll = 15;
            int diagonal = OriginalDownDiagonal(input);
            if (diagonal != 0 && diagonal == OriginalDownDiagonal(previous))
                state.ConsistentDownDiagonal++;
            else state.ConsistentDownDiagonal = 0;

            bool charging = state.BodyMode == BodyModeIndex.Crawl &&
                owner.BodyChunks[0].ContactFloor && owner.BodyChunks[1].ContactFloor &&
                input.X == 0 && input.Y == 0 && input.Jump;
            if (charging)
            {
                state.WantToJump = 0;
                state.SuperLaunchJump = Math.Min(20, state.SuperLaunchJump + 1);
                if (state.SuperLaunchJump >= 20)
                    state.KillSuperLaunchJumpCounter = 15;
            }
            else if (!input.Jump && previous.Jump && state.SuperLaunchJump > 0)
                state.WantToJump = 1;
            else if ((input.X != 0 || input.Y != 0) && state.SuperLaunchJump < 20)
                state.SuperLaunchJump = 0;

            if (state.KillSuperLaunchJumpCounter > 0)
            {
                state.KillSuperLaunchJumpCounter--;
                if (state.KillSuperLaunchJumpCounter == 0 && input.Jump)
                    state.SuperLaunchJump = 0;
            }
        }

        private void UpdateStandingIntent(VirtualInput input, VirtualInput previous,
            BodyModeIndex previousBodyMode)
        {
            SlugcatState state = owner.State;
            bool waking = previous.Posture != VirtualPosture.None &&
                input.Posture == VirtualPosture.None;
            if (waking || (input.Y < 0 && previous.Y >= 0)) state.Standing = true;
            if (input.Y > 0 && previous.Y <= 0)
            {
                if (state.Standing && previousBodyMode == BodyModeIndex.Stand)
                    owner.EmitSound("Slugcat_Down_On_Fours", owner.Center, 1.0, 1.0, 2);
                state.Standing = false;
            }
            if (input.Posture != VirtualPosture.None) state.Standing = false;
        }

        private void SelectOriginalBodyMode(VirtualInput input, bool grounded,
            bool wallContact)
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            if (wallContact && input.Y < 0 && !grounded)
            {
                if (state.BodyMode != BodyModeIndex.WallClimb)
                    owner.EmitSound("Slugcat_Enter_Wall_Slide", owner.Center, 1.0, 1.0, 4);
                state.BodyMode = BodyModeIndex.WallClimb;
                state.Animation = AnimationIndex.None;
                state.CanWallJump = ((chest.ContactRight || hips.ContactRight) ? -1 : 1) * 15;
                return;
            }
            if (state.CanWallJump > 0) state.CanWallJump--;
            else if (state.CanWallJump < 0) state.CanWallJump++;
            if (!grounded)
            {
                state.BodyMode = BodyModeIndex.Default;
                return;
            }
            bool upright = chest.Position.Y < hips.Position.Y - 3.0;
            state.BodyMode = upright && state.Animation != AnimationIndex.CrawlTurn
                ? BodyModeIndex.Stand : BodyModeIndex.Crawl;
        }

        private void UpdateOriginalSkid(VirtualInput input, bool grounded,
            BodyModeIndex previousBodyMode)
        {
            SlugcatState state = owner.State;
            if (!grounded || previousBodyMode != BodyModeIndex.Stand ||
                state.Animation == AnimationIndex.Flip)
            {
                if (!grounded) state.InitSlideCounter = 0;
                return;
            }
            if (state.SlideCounter > 0)
            {
                state.SlideCounter++;
                if (state.SlideCounter > 20 || input.X != -state.SlideDirection)
                    state.SlideCounter = 0;
                else
                {
                    double skid = Math.Sin(state.SlideCounter / 20.0 * Math.PI);
                    owner.BodyChunks[0].Velocity.X += state.SlideDirection * skid * 1.5;
                    owner.BodyChunks[1].Velocity.X += state.SlideDirection * skid;
                }
                return;
            }
            if (input.X == 0) return;
            if (state.SlideDirection == 0) state.SlideDirection = input.X;
            if (input.X == state.SlideDirection)
            {
                state.InitSlideCounter = Math.Min(29, state.InitSlideCounter + 1);
                return;
            }
            int threshold = owner.SelectedSlugcat.Id == SlugcatId.Rivulet ? 5 : 10;
            double velocity = owner.BodyChunks[0].Velocity.X;
            if (state.InitSlideCounter > threshold && Math.Abs(velocity) > 1.0 &&
                Math.Sign(velocity) == state.SlideDirection)
            {
                state.SlideCounter = 1;
                owner.EmitSound("Slugcat_Skid_On_Ground_Init", owner.Center, 1.0, 1.0, 2);
            }
            else
            {
                state.SlideDirection = input.X;
                state.InitSlideCounter = 1;
            }
        }

        private bool TryOriginalBufferedJump(VirtualInput input, bool grounded,
            bool wallContact)
        {
            SlugcatState state = owner.State;
            if (state.WantToJump <= 0) return false;
            if (!grounded && (state.CanWallJump != 0 || wallContact))
            {
                int direction = state.CanWallJump != 0
                    ? Math.Sign(state.CanWallJump)
                    : ((owner.BodyChunks[0].ContactRight || owner.BodyChunks[1].ContactRight)
                        ? -1 : 1);
                OriginalWallJump(direction);
                state.WantToJump = 0;
                return true;
            }
            if (state.CanJump <= 0) return false;
            state.CanJump = state.WantToJump = 0;
            if (state.Animation == AnimationIndex.DownOnFours &&
                OriginalDownDiagonal(input) == state.Facing &&
                owner.BodyChunks[1].ContactFloor)
            {
                StartOriginalBellySlide(state.Facing);
                return true;
            }
            if (state.Standing && state.SlideCounter > 0 && state.SlideCounter < 10)
            {
                OriginalBackflip();
                return true;
            }
            OriginalGroundJump();
            return true;
        }

        private void OriginalGroundJump()
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            double factor = OriginalJumpFactor();
            if (!state.Standing)
            {
                double horizontal = state.SuperLaunchJump >= 20
                    ? (owner.SelectedSlugcat.Id == SlugcatId.Rivulet ? 12.0 : 9.0)
                    : 1.5;
                int direction = Math.Abs(chest.Position.X - hips.Position.X) > 0.5
                    ? Math.Sign(chest.Position.X - hips.Position.X) : state.Facing;
                chest.Position.Y -= 6.0;
                chest.Velocity.Y = -3.0 * factor;
                hips.Velocity.Y = -4.0 * factor;
                chest.Velocity.X += direction * horizontal * factor;
                hips.Velocity.X += direction * horizontal * factor;
                state.SuperLaunchJump = 0;
                jumpBoost = 6.0;
            }
            else
            {
                chest.Velocity.Y = -owner.SelectedSlugcat.Movement.StandingJumpChest * factor;
                hips.Velocity.Y = -owner.SelectedSlugcat.Movement.StandingJumpHips * factor;
                // Keep the regular floor-jump boost independent from Rivulet's
                // corridor-specific movement tuning so ordinary jumps do not
                // inherit the climbing multiplier.
                jumpBoost = 8.0;
            }
            state.AerobicLevel = MathUtil.Clamp01(state.AerobicLevel + 0.75 / 9.0);
            state.Animation = AnimationIndex.None;
            state.BodyMode = BodyModeIndex.Default;
            state.Grounded = false;
            LaunchedThisTick = true;
            owner.EmitSound(owner.SelectedSlugcat.Audio.Jump, owner.Center, 1.0, 1.0, 3);
        }

        private void OriginalWallJump(int direction)
        {
            bool rivulet = owner.SelectedSlugcat.Id == SlugcatId.Rivulet;
            double factor = OriginalJumpFactor();
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            chest.Velocity.Y = (rivulet ? -10.0 : -8.0) * factor;
            hips.Velocity.Y = (rivulet ? -9.0 : -7.0) * factor;
            chest.Velocity.X = (rivulet ? 9.0 : 6.0) * direction * factor;
            hips.Velocity.X = (rivulet ? 7.0 : 5.0) * direction * factor;
            jumpBoost = rivulet ? 4.0 : 0.0;
            owner.State.BodyMode = BodyModeIndex.Default;
            owner.State.Animation = AnimationIndex.None;
            owner.State.Standing = true;
            owner.State.JumpStun = 8 * direction;
            owner.State.CanWallJump = 0;
            owner.State.Grounded = false;
            LaunchedThisTick = true;
            owner.EmitSound("Slugcat_Wall_Jump", owner.Center, 1.0, 1.0, 1);
        }

        private void OriginalBackflip()
        {
            SlugcatState state = owner.State;
            bool rivulet = owner.SelectedSlugcat.Id == SlugcatId.Rivulet;
            double factor = OriginalJumpFactor();
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            chest.Velocity.Y = (rivulet ? -12.0 : -9.0) * factor;
            hips.Velocity.Y = (rivulet ? -10.0 : -7.0) * factor;
            chest.Velocity.X *= 0.5;
            hips.Velocity.X *= 0.5;
            chest.Velocity.X -= state.SlideDirection * 4.0 * factor;
            jumpBoost = rivulet ? 9.0 : 5.0;
            state.Animation = AnimationIndex.Flip;
            state.FlipFromSlide = false;
            state.BodyMode = BodyModeIndex.Default;
            state.Standing = false;
            state.SlideCounter = 0;
            state.Grounded = false;
            LaunchedThisTick = true;
            owner.EmitSound("Slugcat_Flip_Jump", owner.Center, 1.0, 1.0, 1);
        }

        private void StartOriginalBellySlide(int direction)
        {
            SlugcatState state = owner.State;
            state.Animation = AnimationIndex.BellySlide;
            state.BodyMode = BodyModeIndex.Default;
            state.RollDirection = direction;
            state.RollCounter = state.ExitBellySlideCounter = 0;
            state.Standing = false;
            owner.EmitSound("Slugcat_Belly_Slide_Init", owner.Center, 1.0, 1.0, 1);
            StartOriginalBellyLoop();
        }

        private void UpdateOriginalBellySlide(VirtualInput input)
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            int direction = state.RollDirection;
            state.BodyMode = BodyModeIndex.Default;
            state.RollCounter++;
            state.Standing = false;
            if (!bellySlideLoopPlaying) StartOriginalBellyLoop();
            bool rivulet = owner.SelectedSlugcat.Id == SlugcatId.Rivulet;
            GourmandAbilityController gourmand = owner.AbilityController as GourmandAbilityController;
            if (state.RollCounter < 6 && !rivulet)
            {
                hips.Velocity.Y -= 2.7;
                hips.Velocity.X -= 9.1 * direction;
            }
            double force = rivulet ? 25.0 :
                (gourmand != null ? (gourmand.Exhausted ? 14.0 : 45.0) : 18.1);
            int duration = rivulet ? 20 : 34;
            chest.Velocity.X += force * direction *
                Math.Sin(state.RollCounter / 15.0 * Math.PI);
            if (!chest.ContactFloor) chest.Velocity.X *= 0.5;
            if (!hips.ContactFloor) hips.Velocity.X *= 0.5;

            if (input.JumpPressed && state.RollCounter > 0 &&
                state.RollCounter < (rivulet ? 6 : 12))
            {
                StopOriginalBellyLoop();
                double jumpFactor = OriginalJumpFactor();
                if (input.X == -direction)
                {
                    double reverseX = rivulet ? 11.0 : 7.0;
                    chest.Velocity = new Vec2(-direction * reverseX,
                        rivulet ? -12.0 : -10.0) * jumpFactor;
                    hips.Velocity = new Vec2(-direction * reverseX,
                        rivulet ? -13.0 : -11.0) * jumpFactor;
                    state.RollDirection = -direction;
                    state.SlideDirection = -direction;
                    state.Animation = AnimationIndex.Flip;
                    state.FlipFromSlide = true;
                    state.Standing = true;
                    owner.EmitSound("Slugcat_Sectret_Super_Wall_Jump",
                        owner.Center, 1.0, 1.0, 1);
                }
                else
                {
                    hips.Position += new Vec2(5.0 * direction, -5.0);
                    chest.Position = hips.Position +
                        new Vec2(5.0 * direction, -5.0);
                    Vec2 rocket = new Vec2(direction * (rivulet ? 18.0 : 9.0),
                        -(rivulet ? 10.0 : 8.5)) * jumpFactor;
                    chest.Velocity = rocket;
                    hips.Velocity = rocket;
                    state.RollDirection = 0;
                    state.Animation = AnimationIndex.RocketJump;
                    state.Standing = false;
                    owner.EmitSound("Slugcat_Rocket_Jump", owner.Center,
                        1.0, 1.0, 1);
                }
                state.RollCounter = 0;
                jumpBoost = 0.0;
                state.Grounded = false;
                LaunchedThisTick = true;
                return;
            }
            int diagonal = OriginalDownDiagonal(input);
            if (input.X != direction && diagonal != direction)
                state.ExitBellySlideCounter++;
            else state.ExitBellySlideCounter = 0;
            bool leftGround = state.RollCounter > 6 &&
                !chest.ContactFloor && !hips.ContactFloor;
            if ((state.RollCounter > 8 && state.ExitBellySlideCounter >
                    (rivulet ? 6 : 12)) || state.RollCounter > duration || leftGround)
                FinishOriginalBellySlide(input.Y < 0);
        }

        private void FinishOriginalBellySlide(bool success)
        {
            StopOriginalBellyLoop();
            SlugcatState state = owner.State;
            state.SlowMovementStun = success ? 20 : 40;
            owner.EmitSound(success ? "Slugcat_Belly_Slide_Finish_Success" :
                "Slugcat_Belly_Slide_Finish_Fail", owner.Center, 1.0, 1.0, 1);
            state.RollDirection = state.RollCounter = state.ExitBellySlideCounter = 0;
            state.Animation = AnimationIndex.None;
            state.Standing = success;
        }

        private void UpdateOriginalRoll(VirtualInput input)
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            if (!rollLoopPlaying) StartOriginalRollLoop();
            state.RollCounter++;
            state.BodyMode = BodyModeIndex.Default;
            state.Standing = false;
            state.CanJump = Math.Max(state.CanJump, 5);
            if (state.WantToJump > 0)
            {
                OriginalRollJump();
                state.WantToJump = 0;
                return;
            }

            Vec2 bodyDirection = (chest.Position - hips.Position).Normalized;
            Vec2 perpendicular = new Vec2(bodyDirection.Y, -bodyDirection.X);
            chest.Velocity *= 0.9;
            hips.Velocity *= 0.9;
            Vec2 force = perpendicular * (2.0 * state.RollDirection);
            chest.Velocity += force;
            hips.Velocity -= force;
            state.AerobicLevel = MathUtil.Clamp01(state.AerobicLevel + 0.01);
            bool blocked = (state.RollDirection > 0 &&
                (chest.ContactRight || hips.ContactRight)) ||
                (state.RollDirection < 0 && (chest.ContactLeft || hips.ContactLeft));
            if (!chest.ContactFloor && !hips.ContactFloor) blocked = true;
            else
            {
                chest.Velocity.X += 1.1 * state.RollDirection;
                hips.Velocity.X += 1.1 * state.RollDirection;
            }
            state.StopRollingCounter = blocked ? state.StopRollingCounter + 1 : 0;
            bool inputExit = state.RollCounter > 15 &&
                (OriginalDownDiagonal(input) == 0 || input.X == -state.RollDirection);
            GourmandAbilityController gourmand = owner.AbilityController as GourmandAbilityController;
            int maximum = gourmand != null && !gourmand.Exhausted
                ? 140 : 60 + (int)(80.0 * state.AerobicLevel);
            if ((inputExit && chest.Position.Y < hips.Position.Y) ||
                state.RollCounter > maximum || state.StopRollingCounter > 6)
                StopOriginalRoll(true);
        }

        private void OriginalRollJump()
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            hips.Velocity = Vec2.Zero;
            hips.Position += new Vec2(5.0 * state.RollDirection, -5.0);
            chest.Position = hips.Position +
                new Vec2(5.0 * state.RollDirection, -5.0);
            double amount = MathUtil.InverseLerp(0.0, 25.0, state.RollCounter);
            double angle = MathUtil.Lerp(60.0, 35.0, amount) * Math.PI / 180.0;
            double speed = MathUtil.Lerp(9.5, 13.1, amount) * OriginalJumpFactor();
            Vec2 velocity = new Vec2(Math.Cos(angle) * state.RollDirection,
                -Math.Sin(angle)) * speed;
            if (owner.SelectedSlugcat.Id == SlugcatId.Rivulet) velocity.X *= 1.5;
            chest.Velocity = velocity;
            hips.Velocity = velocity;
            state.Animation = AnimationIndex.RocketJump;
            state.BodyMode = BodyModeIndex.Default;
            state.RollDirection = state.RollCounter = 0;
            state.Grounded = false;
            jumpBoost = 0.0;
            LaunchedThisTick = true;
            StopOriginalRollLoop();
            owner.EmitSound("Slugcat_Rocket_Jump", owner.Center, 1.0, 1.0, 1);
        }

        private void StopOriginalRoll(bool sound)
        {
            StopOriginalRollLoop();
            owner.State.RollDirection = owner.State.RollCounter = 0;
            owner.State.StopRollingCounter = 0;
            owner.State.Animation = AnimationIndex.None;
            if (sound)
                owner.EmitSound("Slugcat_Roll_Finish", owner.Center, 1.0, 1.0, 1);
        }

        private void ApplyOriginalHorizontalInput(VirtualInput input, bool grounded)
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            SlugcatMovementProfile movement = owner.SelectedSlugcat.Movement;
            int crawlAxis = Math.Abs(chest.Position.X - hips.Position.X) > 0.5
                ? Math.Sign(chest.Position.X - hips.Position.X) : state.Facing;
            if (input.X != 0) state.Facing = input.X;
            if (state.BodyMode == BodyModeIndex.WallClimb)
            {
                // Wall contact here is a slide state, not a climbing drive.
                // Gravity remains active and only a jump may add upward speed;
                // applying climb tuning here makes fast variants rise by holding up.
                chest.Velocity.X *= 0.5;
                hips.Velocity.X *= 0.5;
                return;
            }
            bool crawl = state.BodyMode == BodyModeIndex.Crawl;
            double mainSpeed = grounded
                ? (crawl ? (input.Y != 0 ? 1.0 : movement.CrawlSpeed)
                    : 4.2 * movement.RunSpeedFactor)
                : (input.Y != 0 ? movement.CrawlSpeed : movement.AirRunSpeed);
            // Crawling into the body's current axis turns more slowly than
            // moving with it; the animation applies its own rotational forces.
            if (grounded && crawl && input.X != 0 && input.X != crawlAxis)
                mainSpeed *= 0.75;
            double hipsSpeed = grounded
                ? (crawl ? mainSpeed : (input.Y != 0 ? 2.0 :
                    4.0 * movement.RunSpeedFactor)) : mainSpeed;
            double slow = MathUtil.Lerp(1.0, 0.5,
                MathUtil.Clamp01(state.SlowMovementStun / 10.0));
            lastAirHorizontalVelocityBefore[0] = chest.Velocity.X;
            lastAirHorizontalVelocityBefore[1] = hips.Velocity.X;
            double chestAir = ApplyOriginalHorizontalMovement(
                chest, input.X, mainSpeed * slow, grounded);
            double hipsAir = ApplyOriginalHorizontalMovement(
                hips, input.X, hipsSpeed * slow, grounded);
            lastAirHorizontalVelocityAfter[0] = chest.Velocity.X;
            lastAirHorizontalVelocityAfter[1] = hips.Velocity.X;
            if (!grounded)
            {
                lastAirMovementContribution[0].X = chestAir;
                lastAirMovementContribution[1].X = hipsAir;
                lastAirControlBranch = "air-control no-contact";
            }
            else lastAirControlBranch = "grounded contact";
        }

        private void UpdateOriginalPosture(VirtualInput input, bool grounded,
            AnimationIndex previousAnimation)
        {
            SlugcatState state = owner.State;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            if (state.BodyMode == BodyModeIndex.Stand && grounded)
            {
                chest.Velocity.Y -= 1.5;
                hips.Velocity.Y += 4.5;
                if (!state.Standing && state.LowerBodyFramesOnGround >= 5 &&
                    state.UpperBodyFramesOffGround >= 5 &&
                    state.Animation != AnimationIndex.DownOnFours)
                {
                    state.Animation = AnimationIndex.DownOnFours;
                    state.AnimationFrame = 0;
                }
            }
            else if (state.BodyMode == BodyModeIndex.Crawl && grounded)
            {
                if (state.Standing && state.Animation != AnimationIndex.StandUp)
                {
                    state.Animation = AnimationIndex.StandUp;
                    state.AnimationFrame = 0;
                    owner.EmitSound("Slugcat_Stand_Up", owner.Center, 1.0, 1.0, 2);
                }
                int axis = Math.Abs(chest.Position.X - hips.Position.X) > 0.5
                    ? Math.Sign(chest.Position.X - hips.Position.X) : state.Facing;
                state.CrawlTurnDelay = input.X != 0 && input.X != axis
                    ? state.CrawlTurnDelay + 1 : 0;
                if (state.CrawlTurnDelay > 5 && state.Animation == AnimationIndex.None)
                {
                    state.Animation = AnimationIndex.CrawlTurn;
                    state.CrawlTurnDelay = 0;
                }
            }

            if (state.Animation == AnimationIndex.DownOnFours)
            {
                if (state.Standing) state.Animation = AnimationIndex.StandUp;
                else
                {
                    chest.Velocity.Y += 2.0;
                    chest.Velocity.X += state.Facing;
                    hips.Velocity.X -= state.Facing;
                    if (chest.ContactFloor || chest.Position.Y >= hips.Position.Y)
                        state.Animation = AnimationIndex.None;
                }
            }
            else if (state.Animation == AnimationIndex.StandUp)
            {
                if (!state.Standing) state.Animation = AnimationIndex.DownOnFours;
                else
                {
                    chest.Velocity.X *= 0.7;
                    chest.Velocity.Y -= 2.0;
                    hips.Velocity.Y += 1.0;
                    if (chest.Position.Y < hips.Position.Y - 3.0)
                    {
                        state.BodyMode = BodyModeIndex.Stand;
                        state.Animation = AnimationIndex.None;
                        owner.EmitSound("Slugcat_Regain_Footing", owner.Center, 1.0, 1.0, 2);
                    }
                }
            }
            else if (state.Animation == AnimationIndex.CrawlTurn)
            {
                state.BodyMode = BodyModeIndex.Default;
                chest.Velocity.X += state.Facing;
                hips.Velocity.X -= 2.0 * state.Facing;
                bool rotating = input.X > 0 != chest.Position.X < hips.Position.X;
                if (rotating)
                {
                    chest.Velocity.Y += 3.0;
                    if (chest.Position.Y > hips.Position.Y - 2.0)
                    {
                        state.Animation = AnimationIndex.None;
                        chest.Velocity.Y += 1.0;
                    }
                }
                else chest.Velocity.Y -= 2.0;
                if (input.X == 0) state.Animation = AnimationIndex.None;
            }

            // Sit/Sleep are stationary rest intents. A locomotion/action input
            // wakes the pet instead of reapplying a curled rest animation while
            // its body chunks are already moving.
            bool activeInput = input.X != 0 || input.Y != 0 || input.Jump ||
                input.Pickup || input.Throw || input.DropThrough;
            bool resting = input.Posture != VirtualPosture.None && !activeInput;
            if (grounded && resting)
                state.Animation = input.Posture == VirtualPosture.Sleep
                    ? AnimationIndex.Sleep : AnimationIndex.Sit;
            else if (grounded && activeInput &&
                (state.Animation == AnimationIndex.Sleep ||
                 state.Animation == AnimationIndex.Sit))
            {
                state.Standing = input.Y <= 0;
                state.Animation = state.Standing
                    ? AnimationIndex.StandUp : AnimationIndex.None;
                state.AnimationFrame = 0;
            }
            else if (!grounded && state.Animation != AnimationIndex.Flip &&
                state.Animation != AnimationIndex.RocketJump &&
                state.Animation != AnimationIndex.DownOnFours &&
                state.Animation != AnimationIndex.StandUp)
                state.Animation = AnimationIndex.None;
            else if (previousAnimation == AnimationIndex.RocketJump && grounded)
                state.Animation = AnimationIndex.None;
        }

        private void ApplyOriginalFlipRotation()
        {
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            Vec2 axis = (chest.Position - hips.Position).Normalized;
            Vec2 perpendicular = new Vec2(-axis.Y, axis.X);
            int direction = owner.State.SlideDirection == 0
                ? owner.State.Facing : owner.State.SlideDirection;
            double force = MathUtil.Lerp(0.38, 0.8, owner.State.AerobicLevel) *
                (owner.State.FlipFromSlide ? 2.5 : 1.0);
            chest.Velocity -= perpendicular * (direction * force);
            hips.Velocity += perpendicular * (direction * force);
            owner.State.Standing = false;
            if (chest.ContactFloor || hips.ContactFloor || chest.ContactLeft ||
                chest.ContactRight || hips.ContactLeft || hips.ContactRight)
            {
                owner.State.Animation = AnimationIndex.None;
                owner.State.Standing = chest.Position.Y < hips.Position.Y - 3.0;
                owner.State.RollDirection = 0;
                owner.State.FlipFromSlide = false;
            }
        }

        private void FinishOriginalTick(VirtualInput input, bool groundedAtStart)
        {
            SlugcatState state = owner.State;
            owner.BodyConnection.Distance = MathUtil.Lerp(owner.BodyConnection.Distance,
                state.RollDirection != 0 ? 10.0 : SimulationConstants.BodyConnectionDistance,
                0.25);
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            bool crawl = state.BodyMode == BodyModeIndex.Crawl;
            double speed = Math.Abs((chest.Velocity.X + hips.Velocity.X) * 0.5);
            state.RunCycle += speed * (crawl ? 0.07 : 0.11);
            bool resting = input.Posture != VirtualPosture.None;
            if (groundedAtStart && !LaunchedThisTick && !Rolling && !Sliding)
            {
                if (input.X == 0 || resting)
                {
                    state.AnimationFrame = 0;
                    state.Stillness = MathUtil.Clamp01(state.Stillness + 0.035);
                }
                else
                {
                    state.Stillness = MathUtil.Clamp01(state.Stillness - 0.12);
                    state.AnimationFrame++;
                    int lastFrame = crawl ? 10 : 6;
                    if (state.AnimationFrame > lastFrame) state.AnimationFrame = 0;
                    if (state.AnimationFrame == 0)
                    {
                        string sound = crawl ? "Slugcat_Crawling_Step" :
                            ((((int)Math.Floor(state.RunCycle)) & 1) == 0
                                ? owner.SelectedSlugcat.Audio.FootstepA
                                : owner.SelectedSlugcat.Audio.FootstepB);
                        owner.EmitSound(sound, hips.Position, crawl ? 0.65 : 1.0, 1.0, 2);
                    }
                }
            }
            else
            {
                state.AnimationFrame++;
                state.Stillness = MathUtil.Clamp01(state.Stillness - 0.12);
            }
        }

        private void RecoverOriginalAerobic(VirtualInput input)
        {
            SlugcatState state = owner.State;
            GourmandAbilityController gourmand = owner.AbilityController as GourmandAbilityController;
            bool exhausted = gourmand != null &&
                (gourmand.Exhausted || state.AerobicLevel >= 0.95);
            double idle = exhausted ? 200.0 : 400.0;
            double moving = exhausted ? 800.0 : 1100.0;
            if (exhausted && state.BodyMode == BodyModeIndex.Crawl)
            {
                idle = 125.0;
                moving = 400.0;
            }
            double denominator = (input.X == 0 && input.Y == 0 ? idle : moving) *
                (1.0 + 3.0 * MathUtil.InverseLerp(0.9, 1.0, state.AerobicLevel));
            state.AerobicLevel = Math.Max(0.0, state.AerobicLevel - 1.0 / denominator);
            if (state.SlowMovementStun > 0) state.SlowMovementStun--;
        }

        private void UpdateOriginalContactState(bool grounded, BodyChunk chest, BodyChunk hips)
        {
            SlugcatState state = owner.State;
            state.JustLanded = !state.Grounded && grounded;
            state.Grounded = grounded;
            if (state.JustLanded)
            {
                landingCounter = 6;
                double speed = Math.Max(chest.FloorImpactSpeed, hips.FloorImpactSpeed);
                state.LandingCompression = MathUtil.Clamp(speed / 12.0, 0.25, 1.0);
            }
            else if (landingCounter > 0)
            {
                landingCounter--;
                state.LandingCompression *= 0.72;
            }
        }

        private void ClearMovementDiagnostics(BodyChunk chest, BodyChunk hips)
        {
            lastAirMovementContribution[0] = lastAirMovementContribution[1] = Vec2.Zero;
            lastAirHorizontalVelocityBefore[0] = chest.Velocity.X;
            lastAirHorizontalVelocityBefore[1] = hips.Velocity.X;
            lastAirHorizontalVelocityAfter[0] = chest.Velocity.X;
            lastAirHorizontalVelocityAfter[1] = hips.Velocity.X;
        }

        private static int OriginalDownDiagonal(VirtualInput input)
        {
            return input.X != 0 && input.Y > 0 ? input.X : 0;
        }

        private double OriginalJumpFactor()
        {
            return MathUtil.Lerp(1.0, 1.15,
                MathUtil.Clamp01(owner.State.Adrenaline));
        }

        private void StartOriginalRollLoop()
        {
            if (rollLoopPlaying) return;
            rollLoopPlaying = true;
            owner.StartSoundLoop("Slugcat_Roll_LOOP", rollLoopKey, owner.Center, 0.8, 1.0);
        }

        private void StopOriginalRollLoop()
        {
            if (!rollLoopPlaying) return;
            rollLoopPlaying = false;
            owner.StopSoundLoop("Slugcat_Roll_LOOP", rollLoopKey, owner.Center);
        }

        private void StartOriginalBellyLoop()
        {
            if (bellySlideLoopPlaying) return;
            bellySlideLoopPlaying = true;
            owner.StartSoundLoop("Slugcat_Belly_Slide_LOOP", bellySlideLoopKey,
                owner.Center, 0.8, 1.0);
        }

        private void StopOriginalBellyLoop()
        {
            if (!bellySlideLoopPlaying) return;
            bellySlideLoopPlaying = false;
            owner.StopSoundLoop("Slugcat_Belly_Slide_LOOP", bellySlideLoopKey, owner.Center);
        }

        private void StopOriginalMovementLoops()
        {
            StopOriginalRollLoop();
            StopOriginalBellyLoop();
        }
    }
}
