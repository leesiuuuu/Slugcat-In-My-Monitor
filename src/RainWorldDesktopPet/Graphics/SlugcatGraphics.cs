using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class SlugcatGraphics
    {
        private readonly Slugcat slugcat;
        private readonly Limb[] arms;
        private readonly BodyPart head;
        private readonly BodyPart legs;
        private ProceduralTail tail;
        private SlugcatGraphicsProfile graphicsProfile;
        private ISlugcatGraphicsExtension[] extensions;
        private RainWorldAtlasSet atlas;
        private readonly SlugcatPose renderPose;
        private readonly Random graphicsRandom = new Random();
        private readonly Vec2[,] drawPositions = new Vec2[2, 2];
        private readonly Vec2[] crawlEdibleStart = new Vec2[2];
        private readonly bool[] hasCrawlEdibleStart = new bool[2];
        private Vec2 lookDirection;
        private Vec2 lastLookDirection;
        private Vec2 originalLookDirection;
        private Vec2 lastOriginalLookDirection;
        private bool mouseAttentionActive;
        private Vec2 legsDirection = Vec2.Down;
        private Vec2 lastLegsDirection = Vec2.Down;
        private Vec2 legsTargetPosition;
        private Vec2 headTargetPosition;
        private double breath;
        private double lastBreath;
        private int blink;
        private double airborneCounter;
        private double spearDirection;
        private bool leftFoot;
        private int previousAnimationFrame;
        private readonly Dictionary<string, Color> partColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        public SlugcatGraphics(Slugcat slugcat)
            : this(slugcat, slugcat.SelectedSlugcat.Graphics, null)
        {
        }

        public SlugcatGraphics(Slugcat slugcat, SlugcatGraphicsProfile profile,
            RainWorldAtlasSet atlas)
        {
            this.slugcat = slugcat;
            Vec2 chest = slugcat.BodyChunks[0].Position;
            Vec2 hips = slugcat.BodyChunks[1].Position;
            drawPositions[0, 0] = drawPositions[0, 1] = chest;
            drawPositions[1, 0] = drawPositions[1, 1] = hips;
            head = new BodyPart(chest + new Vec2(0.0, -7.0), 4.0, 0.8, 0.99);
            legs = new BodyPart(hips + new Vec2(0.0, 5.0), 1.0, 0.8, 0.99);
            legsTargetPosition = hips;
            arms = new Limb[2];
            arms[0] = new Limb(LimbKind.Arm, -1, chest + new Vec2(-4.0, 8.0), 20.0);
            arms[1] = new Limb(LimbKind.Arm, 1, chest + new Vec2(4.0, 8.0), 20.0);
            renderPose = new SlugcatPose();
            SetGraphicsProfile(profile ?? SlugcatGraphicsProfiles.White, atlas);
        }

        public ProceduralTail Tail { get { return tail; } }
        public Limb[] Arms { get { return arms; } }
        public BodyPart Legs { get { return legs; } }
        public BodyPart Head { get { return head; } }
        public SlugcatGraphicsProfile GraphicsProfile { get { return graphicsProfile; } }
        public SlugcatVisualProfile VisualProfile
        {
            get { return SlugcatVisualProfiles.FromGraphics(graphicsProfile); }
        }
        public ISlugcatGraphicsExtension[] Extensions { get { return extensions; } }

        public Color GetPartColor(string part)
        {
            Color color;
            if (partColors.TryGetValue(part, out color)) return color;
            SlugcatVisualProfile compatibility =
                SlugcatVisualProfiles.FromGraphics(graphicsProfile);
            Color body = compatibility != null
                ? compatibility.ResolveBodyColor(slugcat.Appearance)
                : graphicsProfile.BodyColor;
            return string.Equals(part, "Face", StringComparison.OrdinalIgnoreCase)
                ? graphicsProfile.EyeColor : body;
        }

        public void SetPartColor(string part, Color color)
        {
            if (string.IsNullOrWhiteSpace(part)) return;
            partColors[part] = Color.FromArgb(255, color.R, color.G, color.B);
        }

        public void ClearPartColors()
        {
            partColors.Clear();
        }

        public void SetGraphicsProfile(SlugcatGraphicsProfile profile, RainWorldAtlasSet sourceAtlas)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            ProceduralTail previous = tail;
            graphicsProfile = profile;
            atlas = sourceAtlas;
            tail = new ProceduralTail(slugcat.BodyChunks[1].Position, profile.Tail);
            if (previous != null && previous.Segments.Length == tail.Segments.Length)
            {
                for (int i = 0; i < tail.Segments.Length; i++)
                {
                    tail.Segments[i].Position = previous.Segments[i].Position;
                    tail.Segments[i].LastPosition = previous.Segments[i].LastPosition;
                    tail.Segments[i].Velocity = previous.Segments[i].Velocity;
                }
            }

            extensions = SlugcatGraphicsExtensionFactory.Create(profile, slugcat, atlas);
            int count = tail.Segments.Length;
            renderPose.Tail = new Vec2[count];
            renderPose.TailLast = new Vec2[count];
            renderPose.TailCurrent = new Vec2[count];
            renderPose.TailRadii = new double[count];
            int extraCount = 0;
            for (int i = 0; i < extensions.Length; i++) extraCount += extensions[i].SpriteCount;
            renderPose.ExtraParts = new ExtraGraphicsPartPose[extraCount];
            for (int i = 0; i < extraCount; i++) renderPose.ExtraParts[i] = new ExtraGraphicsPartPose();
        }

        public void SetVisualProfile(SlugcatVisualProfile profile, RainWorldAtlasSet sourceAtlas)
        {
            SetGraphicsProfile(profile, sourceAtlas);
        }

        // Called after Player/PhysicalObject update, matching GraphicsModule.Update.
        public void Step(AttentionSystem attention, DesktopCollisionWorld world)
        {
            Step(attention, attention.Target, false, world);
        }

        public void Step(AttentionSystem attention, Vec2 originalAttentionTarget,
            bool isMouseAttentionActive, DesktopCollisionWorld world)
        {
            lastBreath = breath;
            breath += slugcat.State.Animation == AnimationIndex.Sleep
                ? 0.0125
                : 1.0 / MathUtil.Lerp(60.0, 15.0, Math.Pow(slugcat.State.AerobicLevel, 1.5));
            lastLookDirection = lookDirection;
            lastOriginalLookDirection = originalLookDirection;
            originalLookDirection = (originalAttentionTarget - head.Position).Normalized;
            lookDirection = (attention.Smoothed - head.Position).Normalized;
            mouseAttentionActive = isMouseAttentionActive && slugcat.State.Conscious &&
                !slugcat.State.Dead && slugcat.State.StunCounter < 1;
            if (!slugcat.State.Conscious)
            {
                // PlayerGraphics clears objectLooker and zeroes lookDirection
                // whenever Creature.Consious is false.
                originalLookDirection = Vec2.Zero;
                lookDirection = Vec2.Zero;
                blink = 10;
            }
            blink--;
            if (blink < -graphicsRandom.Next(2, 1800))
            {
                int blinkUpper = graphicsRandom.Next(3, 10);
                blink = blinkUpper <= 3 ? 3 : graphicsRandom.Next(3, blinkUpper);
            }
            if (slugcat.State.Animation == AnimationIndex.Sleep)
                blink = Math.Max(2, blink);
            blink = Math.Max(blink, slugcat.State.ImpactBlinkTicks);
            lastLegsDirection = legsDirection;

            if (slugcat.State.BodyMode == BodyModeIndex.Stand && slugcat.LastInput.X != 0)
                spearDirection = MathUtil.Clamp(
                    spearDirection + slugcat.LastInput.X * 0.1, -1.0, 1.0);
            else
                spearDirection = MathUtil.MoveTowards(spearDirection, 0.0, 0.05);
            if (slugcat.State.BodyMode == BodyModeIndex.Stand &&
                slugcat.LastInput.X != 0 && slugcat.State.AnimationFrame == 0 &&
                previousAnimationFrame > 0)
                leftFoot = !leftFoot;
            previousAnimationFrame = slugcat.State.AnimationFrame;

            for (int i = 0; i < 2; i++)
            {
                drawPositions[i, 1] = drawPositions[i, 0];
                drawPositions[i, 0] = slugcat.BodyChunks[i].Position;
            }
            ApplyOriginalBodyModeOffsets();

            bool noChunkContact = !slugcat.BodyChunks[0].ContactFloor &&
                !slugcat.BodyChunks[0].ContactLeft && !slugcat.BodyChunks[0].ContactRight &&
                !slugcat.BodyChunks[1].ContactFloor &&
                !slugcat.BodyChunks[1].ContactLeft && !slugcat.BodyChunks[1].ContactRight;
            if (slugcat.State.BodyMode == BodyModeIndex.Default &&
                slugcat.State.Animation == AnimationIndex.None && noChunkContact)
                airborneCounter += slugcat.BodyChunks[0].Velocity.Length;
            else
                airborneCounter = 0.0;

            Vec2 upper = drawPositions[0, 0];
            Vec2 lower = drawPositions[1, 0];
            Vec2 bodyUp = (upper - lower).Normalized;
            if (bodyUp.LengthSquared < 0.1) bodyUp = Vec2.Up;

            if (slugcat.State.BodyMode == BodyModeIndex.Stand)
            {
                if (slugcat.LastInput.X == 0) head.Velocity -= lookDirection * 0.5;
                upper -= lookDirection * 2.0;
                drawPositions[0, 0] = upper;
            }
            else
            {
                head.Velocity += lookDirection;
            }

            tail.Step(upper, lower, slugcat.BodyChunks[1].Velocity,
                slugcat.State.Facing, slugcat.State.BodyMode, world);

            SpearmasterAbilityController extraction =
                slugcat.AbilityController as SpearmasterAbilityController;
            if (extraction != null)
            {
                if (tail.Segments.Length > 2)
                {
                    extraction.SetTailNeedlePosition(tail.Segments[2].Position);
                    for (int i = 0; i < slugcat.Spears.Count; i++)
                    {
                        if (slugcat.Spears[i].NeedleHasConnection)
                            slugcat.Spears[i].SetConnectionAnchor(
                                tail.Segments[2].Position);
                    }
                }
                head.Velocity += extraction.ConsumeGraphicsHeadImpulse();
                if (extraction.SpearProgress > 0.1) blink = Math.Max(blink, 5);
            }
            head.Update();
            world.PushOutOfTerrain(head, slugcat.BodyChunks[0].Position);
            Vec2 neckDirection = bodyUp * 3.0;
            if (slugcat.State.BodyMode == BodyModeIndex.Crawl) neckDirection.X *= 2.5;
            Vec2 headTarget = Vec2.Lerp(upper, lower, 0.2) + neckDirection;
            headTargetPosition = headTarget;
            head.ConnectToPoint(headTarget, 3.0, false, 0.2,
                slugcat.BodyChunks[0].Velocity, 0.7, 0.1);

            legs.Update();
            world.PushOutOfTerrain(legs, slugcat.BodyChunks[1].Position);
            bool grounded = slugcat.BodyChunks[1].ContactFloor;
            Vec2 legsTarget = grounded
                ? slugcat.BodyChunks[1].Position + new Vec2(legsDirection.X * 8.0, -1.0)
                : slugcat.BodyChunks[1].Position + new Vec2(legsDirection.X * 8.0, 2.0);
            legsTargetPosition = legsTarget;
            legs.ConnectToPoint(legsTarget, grounded ? 5.0 : 4.0, false, 0.25,
                new Vec2(slugcat.BodyChunks[1].Velocity.X, 10.0), 0.5, 0.1);
            if (grounded)
            {
                if (slugcat.BodyChunks[1].ContactLeft) legsDirection.X += 1.0;
                if (slugcat.BodyChunks[1].ContactRight) legsDirection.X -= 1.0;
                legsDirection.Y += 1.0;
            }
            else
            {
                legsDirection += slugcat.BodyChunks[1].Velocity * 0.01;
                legsDirection.Y += 0.05;
            }
            legsDirection = legsDirection.Normalized;

            for (int i = 0; i < 2; i++)
            {
                arms[i].Step(slugcat, slugcat.BodyChunks[0].Position,
                    slugcat.BodyChunks[1].Position, slugcat.BodyChunks[0].Velocity,
                    world, i == 0 ? null : arms[0], airborneCounter);
            }
            SpearmasterAbilityController spearAbility = extraction;
            if (spearAbility != null && spearAbility.HeldSpear != null)
            {
                int hand = spearAbility.HeldHand;
                if (!arms[hand].MovementEngagedThisTick &&
                    slugcat.State.Animation != AnimationIndex.Sleep)
                {
                    Vec2 relative = new Vec2(-20.0 + 40.0 * hand, 12.0);
                    if (spearDirection != 0.0 &&
                        slugcat.State.BodyMode == BodyModeIndex.Stand)
                    {
                        Vec2 standingTarget = DegreesToScreenDirection(
                            180.0 + (hand == 0 ? -1.0 : 1.0) * 8.0 +
                            slugcat.LastInput.X * 4.0) * 12.0;
                        double frameCycle = slugcat.State.AnimationFrame / 6.0 *
                            Math.PI * 2.0;
                        standingTarget.Y -= Math.Sin(frameCycle) * 2.0;
                        standingTarget.X -= Math.Cos(
                            (slugcat.State.AnimationFrame + (!leftFoot ? 6 : 0)) /
                            12.0 * Math.PI * 2.0) * 4.0 * slugcat.LastInput.X;
                        standingTarget.X += slugcat.LastInput.X * 2.0;
                        relative = Vec2.Lerp(relative, standingTarget,
                            Math.Abs(spearDirection));
                    }
                    arms[hand].Mode = LimbMode.HuntRelativePosition;
                    arms[hand].RelativeHuntPosition = relative;
                    arms[hand].GripSurfaceId = 0;
                    arms[hand].RetractCounter = Math.Max(0,
                        arms[hand].RetractCounter - 10);
                }

                Vec2 heldDirection = GetHeldSpearDirection(
                    spearAbility.HeldSpear, hand);
                if (slugcat.State.BodyMode == BodyModeIndex.Stand)
                {
                    spearAbility.HeldSpear.SetOverlap(
                        (spearDirection > -0.4 && hand == 0) ||
                        (spearDirection < 0.4 && hand == 1));
                }
                spearAbility.HeldSpear.HoldAt(arms[hand].End.Position,
                    heldDirection, arms[hand].End.Velocity);
                spearAbility.HeldSpear.SetConnectionAnchor(
                    tail.Segments.Length > 2 ? tail.Segments[2].Position :
                    slugcat.BodyChunks[1].Position);
            }
            else if (spearAbility != null && spearAbility.ThrowFollowTicks > 0 &&
                spearAbility.ThrownSpear != null && slugcat.State.Conscious)
            {
                int hand = spearAbility.HeldHand;
                DesktopSpear thrown = spearAbility.ThrownSpear;
                arms[hand].Mode = LimbMode.HuntAbsolutePosition;
                arms[hand].AbsoluteHuntPosition = thrown.Chunk.Position;
                arms[hand].GripSurfaceId = 0;
                Vec2 direction = MathUtil.Direction(
                    arms[hand].End.Position, thrown.Chunk.Position);
                if (Vec2.Distance(arms[hand].End.Position,
                    thrown.Chunk.Position) < 40.0)
                    arms[hand].End.Position = thrown.Chunk.Position;
                else
                    arms[hand].End.Velocity += direction * 6.0;
                arms[1 - hand].End.Velocity -= direction * 3.0;
                spearAbility.AdvanceThrowFollowThrough();
            }
            if (slugcat.State.Animation == AnimationIndex.Sleep)
            {
                Vec2 center = (upper + lower) * 0.5;
                head.Position = Vec2.Lerp(head.Position,
                    center + new Vec2(slugcat.State.Facing * 5.0, 3.0), 0.35);
                tail.CurlAround(lower, slugcat.State.Facing, 1.0);
            }
            for (int i = 0; i < extensions.Length; i++)
                extensions[i].Step(slugcat, lookDirection);
        }

        // SlugcatHand.Update's normal one-hand grasp pose. While eatCounter falls
        // from 40 to 20, the original scales this target from the ordinary held
        // position toward the raised eating position; values clamp after 20.
        public void SetEdibleHandPose(int handIndex, int eatCounter)
        {
            if (handIndex < 0 || handIndex >= arms.Length)
                throw new ArgumentOutOfRangeException("handIndex");
            Limb hand = arms[handIndex];
            if (slugcat.State.BodyMode == BodyModeIndex.Crawl)
            {
                if (!hasCrawlEdibleStart[handIndex] || eatCounter >= 40)
                {
                    crawlEdibleStart[handIndex] = hand.End.Position;
                    hasCrawlEdibleStart[handIndex] = true;
                }
                double mouthProgress = MathUtil.InverseLerp(40.0, 20.0,
                    eatCounter);
                Vec2 mouthTarget = drawPositions[0, 0];
                hand.Mode = LimbMode.HuntAbsolutePosition;
                hand.AbsoluteHuntPosition = Vec2.Lerp(
                    crawlEdibleStart[handIndex], mouthTarget, mouthProgress);
                hand.TargetPosition = hand.AbsoluteHuntPosition;
                hand.GripSurfaceId = 0;
                hand.RetractCounter = Math.Max(0, hand.RetractCounter - 10);
                return;
            }
            hasCrawlEdibleStart[handIndex] = false;
            double scale = 1.0;
            double verticalRaise = 0.0;
            double horizontalSpread = 1.0;
            if (eatCounter < 40)
            {
                double progress = MathUtil.InverseLerp(40.0, 20.0, eatCounter);
                scale = MathUtil.Lerp(0.9, 0.7, progress);
                verticalRaise = MathUtil.Lerp(2.0, 4.0, progress);
                horizontalSpread = MathUtil.Lerp(1.0, 1.2, progress);
            }
            hand.Mode = LimbMode.HuntRelativePosition;
            hand.RelativeHuntPosition = new Vec2(
                (-20.0 + 40.0 * handIndex) * scale * horizontalSpread,
                12.0 * scale - verticalRaise);
            hand.GripSurfaceId = 0;
            hand.RetractCounter = Math.Max(0, hand.RetractCounter - 10);
        }

        // A held item must not replace a crawl hand's low planted pose with the
        // standing relative grasp target. Spearmaster keeps the item at the hand
        // until its delayed toss; standing characters still use SlugcatHand's
        // ordinary one-hand grasp.
        public void SetHeldFoodPose(int handIndex)
        {
            if (handIndex < 0 || handIndex >= arms.Length)
                throw new ArgumentOutOfRangeException("handIndex");
            if (slugcat.State.BodyMode == BodyModeIndex.Crawl) return;
            SetEdibleHandPose(handIndex, 40);
        }

        // PlayerGraphics.BiteFly runs before PlayerGraphics.Update in Rain World.
        // GameLoop updates graphics first, so apply the already-integrated head
        // impulse and the post-decrement blink value here to preserve the same
        // visible bite frame.
        public void ApplyEdibleBiteAfterGraphicsStep(int handIndex)
        {
            if (handIndex < 0 || handIndex >= arms.Length)
                throw new ArgumentOutOfRangeException("handIndex");
            Vec2 impulse = MathUtil.Direction(head.Position,
                arms[handIndex].End.Position) * 2.0;
            head.Position += impulse;
            head.Velocity += impulse * head.AirFriction;
            drawPositions[0, 0].Y -= 1.0;
            arms[handIndex].End.Position = drawPositions[0, 0];
            arms[handIndex].End.LastPosition = drawPositions[0, 0];
            blink = Math.Max(blink, 4);
        }

        private Vec2 GetHeldSpearDirection(DesktopSpear spear, int hand)
        {
            Vec2 direction = MathUtil.Direction(
                slugcat.BodyChunks[0].Position, spear.Chunk.Position) *
                (hand == 0 ? -1.0 : 1.0);
            if (slugcat.State.Animation != AnimationIndex.HangFromBeam)
                direction = -direction.Perpendicular;
            if (slugcat.State.BodyMode == BodyModeIndex.Crawl)
            {
                direction = MathUtil.Direction(slugcat.BodyChunks[1].Position,
                    Vec2.Lerp(spear.Chunk.Position,
                        slugcat.BodyChunks[0].Position, 0.8));
            }
            else if (slugcat.State.Animation == AnimationIndex.ClimbOnBeam)
            {
                direction.Y = -Math.Abs(direction.Y);
                direction = MathUtil.SlerpDirection(direction,
                    MathUtil.Direction(slugcat.BodyChunks[1].Position,
                        slugcat.BodyChunks[0].Position), 0.75);
            }
            else
            {
                double phase = (slugcat.State.AnimationFrame + (leftFoot ? 9 : 3)) /
                    12.0 * Math.PI * 2.0;
                double degrees = (80.0 + Math.Cos(phase) * 4.0 * spearDirection) *
                    spearDirection;
                direction = MathUtil.SlerpDirection(direction,
                    DegreesToScreenDirection(degrees), Math.Abs(spearDirection));
            }
            return direction.LengthSquared > 0.000001
                ? direction.Normalized : new Vec2(slugcat.State.Facing, 0.0);
        }

        private static Vec2 DegreesToScreenDirection(double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            return new Vec2(Math.Sin(radians), -Math.Cos(radians));
        }

        private void ApplyOriginalBodyModeOffsets()
        {
            int frame = slugcat.State.AnimationFrame;
            int facing = slugcat.State.Facing;
            Vec2 upper = drawPositions[0, 0];
            Vec2 lower = drawPositions[1, 0];
            if (slugcat.State.BodyMode == BodyModeIndex.Stand)
            {
                double cycle = frame / 6.0 * Math.PI * 2.0;
                upper.X += facing * 6.0 * MathUtil.Clamp(Math.Abs(slugcat.BodyChunks[1].Velocity.X) - 0.2, 0.0, 1.0);
                upper.Y -= Math.Cos(cycle) * 2.0;
                lower.X -= facing * (1.5 - frame / 6.0);
                lower.Y -= 2.0 + Math.Sin(cycle) * 4.0;
            }
            else if (slugcat.State.BodyMode == BodyModeIndex.Crawl)
            {
                double sin = Math.Sin(frame / 21.0 * Math.PI * 2.0);
                double cos = Math.Cos(frame / 14.0 * Math.PI * 2.0);
                upper.X += cos * facing * 2.0;
                upper.Y += -sin * 1.5 - 3.0;
                head.Velocity.Y -= sin * 0.5 + 0.5;
                head.Velocity.X += upper.X < lower.X ? -1.0 : 1.0;
                lower.X += -3.0 * sin * facing;
                lower.Y += cos * 1.5 - 7.0;
            }
            else if (slugcat.State.BodyMode == BodyModeIndex.WallClimb)
            {
                legsDirection.Y += 1.0;
                upper.Y -= 2.0;
                upper.X -= facing * (slugcat.BodyChunks[1].ContactFloor ? 3.0 : 5.0);
                head.Velocity.Y += facing * 5.0;
            }
            if (slugcat.State.Animation == AnimationIndex.Sleep)
            {
                Vec2 middle = (upper + lower) * 0.5;
                upper = Vec2.Lerp(upper, middle, 0.35);
                lower = Vec2.Lerp(lower, middle, 0.35);
            }
            drawPositions[0, 0] = upper;
            drawPositions[1, 0] = lower;
        }

        public SlugcatPose BuildPose(double interpolation, AttentionSystem attention)
        {
            return BuildPose(interpolation, attention, 0);
        }

        public SlugcatPose BuildPose(double interpolation, AttentionSystem attention, long simulationTick)
        {
            return BuildPose(interpolation, attention, simulationTick, true);
        }

        public SlugcatPose BuildPose(double interpolation, AttentionSystem attention,
            long simulationTick, bool includeDebugText)
        {
            double timeStacker = MathUtil.Clamp01(interpolation);
            SlugcatPose pose = renderPose;
            pose.SimulationTick = simulationTick;
            pose.TimeStacker = timeStacker;
            pose.SelectedSlugcat = slugcat.SelectedSlugcat.Id;
            SlugcatVisualProfile compatibilityProfile =
                SlugcatVisualProfiles.FromGraphics(graphicsProfile);
            pose.CurrentSkin = compatibilityProfile.Skin;
            pose.OriginalSlugcatId = compatibilityProfile.ResolveOriginalSlugcatId(
                slugcat.Appearance);
            pose.VisualProfileName = graphicsProfile.DisplayName;
            pose.BaseSpriteCount = graphicsProfile.BaseSpriteCount;
            pose.ExtraSpriteCount = graphicsProfile.ExtraSpriteCount;
            pose.GraphicsExtensions = includeDebugText
                ? (graphicsProfile.ExtensionNames.Length == 0
                    ? "none" : string.Join(", ", graphicsProfile.ExtensionNames))
                : string.Empty;
            pose.TailProfileName = graphicsProfile.Tail.Name;
            pose.TailRootRadius = graphicsProfile.Tail.RootRadius;
            pose.VisualBodyColor = GetPartColor("Body");
            pose.VisualEyeColor = GetPartColor("Face");
            pose.VisualHeadColor = GetPartColor("Head");
            pose.VisualArmColor = GetPartColor("Arms");
            pose.VisualHipsColor = GetPartColor("Hips");
            pose.VisualLegsColor = GetPartColor("Legs");
            pose.VisualTailColor = GetPartColor("Tail");
            pose.BodyElement = graphicsProfile.BodyElement;
            pose.HipsElement = graphicsProfile.HipsElement;
            pose.VisualBodyScale = compatibilityProfile.ResolveBodyScale(slugcat.Appearance);
            pose.VisualHipsScale = compatibilityProfile.ResolveHipsScale(slugcat.Appearance);
            pose.VisualHeadScale = graphicsProfile.HeadScale;
            pose.ArmShoulderScale = graphicsProfile.ArmShoulderScale;
            if (includeDebugText)
            {
                pose.MovementProfileDebug = string.Format("run:{0:0.##} weight:{1:0.##} throw:{2:0.##} pole:{3:0.##} corridor:{4:0.##}",
                    slugcat.SelectedSlugcat.Movement.RunSpeedFactor,
                    slugcat.SelectedSlugcat.Movement.BodyWeightFactor,
                    slugcat.SelectedSlugcat.Movement.ThrowingSkill,
                    slugcat.SelectedSlugcat.Movement.PoleClimbSpeedFactor,
                    slugcat.SelectedSlugcat.Movement.CorridorClimbSpeedFactor);
                pose.AbilityDebug = slugcat.AbilityController.DebugState;
            }
            else
            {
                pose.MovementProfileDebug = pose.AbilityDebug = string.Empty;
            }
            for (int i = 0; i < 2; i++)
            {
                pose.ChunkLast[i] = slugcat.BodyChunks[i].LastPosition;
                pose.ChunkCurrent[i] = slugcat.BodyChunks[i].Position;
                pose.ChunkRender[i] = slugcat.BodyChunks[i].RenderPosition(timeStacker);
                pose.DrawLast[i] = drawPositions[i, 1];
                pose.DrawCurrent[i] = drawPositions[i, 0];
            }
            pose.Chest = Vec2.Lerp(drawPositions[0, 1], drawPositions[0, 0], timeStacker);
            pose.Hips = Vec2.Lerp(drawPositions[1, 1], drawPositions[1, 0], timeStacker);
            pose.BodyUp = (pose.Chest - pose.Hips).Normalized;
            if (pose.BodyUp.LengthSquared < 0.1) pose.BodyUp = Vec2.Up;
            pose.BodyRight = pose.BodyUp.Perpendicular;
            pose.HeadLast = head.LastPosition;
            pose.HeadCurrent = head.Position;
            pose.Head = head.RenderPosition(timeStacker);
            pose.HeadTarget = headTargetPosition;
            pose.HeadVelocity = head.Velocity;
            pose.LookDirection = Vec2.Lerp(lastLookDirection, lookDirection, timeStacker);
            pose.OriginalLookDirection = Vec2.Lerp(lastOriginalLookDirection,
                originalLookDirection, timeStacker);
            pose.MouseAttentionActive = mouseAttentionActive;
            pose.HeadDirection = (pose.Head - Vec2.Lerp(pose.Hips, pose.Chest, 0.5)).Normalized;
            pose.LegsLast = legs.LastPosition;
            pose.LegsCurrent = legs.Position;
            pose.Legs = legs.RenderPosition(timeStacker);
            pose.LegsDirection = Vec2.Lerp(lastLegsDirection, legsDirection, timeStacker).Normalized;
            pose.Facing = slugcat.State.Facing;
            pose.Animation = slugcat.State.Animation;
            pose.BodyMode = slugcat.State.BodyMode;
            pose.AnimationFrame = slugcat.State.AnimationFrame;
            pose.InputX = slugcat.LastInput.X;
            VirtualInput[] inputHistory = slugcat.Movement.InputHistoryForRead;
            pose.PreviousInputX = inputHistory[1].X;
            pose.InputY = slugcat.LastInput.Y;
            pose.InputJump = slugcat.LastInput.Jump;
            pose.Conscious = slugcat.State.Conscious;
            pose.Dead = slugcat.State.Dead;
            pose.Blink = blink > 0;
            pose.IsAirborne = !slugcat.State.Grounded &&
                slugcat.State.BodyMode == BodyModeIndex.Default;
            double verticalVelocity = (slugcat.BodyChunks[0].Velocity.Y +
                slugcat.BodyChunks[1].Velocity.Y) * 0.5;
            pose.IsRising = pose.IsAirborne && verticalVelocity < 0.0;
            pose.IsFalling = pose.IsAirborne && verticalVelocity >= 0.0;
            pose.AirborneCounter = airborneCounter;
            pose.AirMovementContribution[0] = slugcat.Movement.LastAirMovementContribution[0];
            pose.AirMovementContribution[1] = slugcat.Movement.LastAirMovementContribution[1];
            pose.AirHorizontalVelocityBefore[0] = slugcat.Movement.LastAirHorizontalVelocityBefore[0];
            pose.AirHorizontalVelocityBefore[1] = slugcat.Movement.LastAirHorizontalVelocityBefore[1];
            pose.AirHorizontalVelocityAfter[0] = slugcat.Movement.LastAirHorizontalVelocityAfter[0];
            pose.AirHorizontalVelocityAfter[1] = slugcat.Movement.LastAirHorizontalVelocityAfter[1];
            pose.AirControlBranch = slugcat.Movement.LastAirControlBranch;
            pose.IsStunned = slugcat.State.IsStunned;
            pose.StunCounter = slugcat.State.StunCounter;
            pose.InitialStunValue = slugcat.State.InitialStunValue;
            pose.Standing = slugcat.State.Standing;
            TerrainImpactData impact = slugcat.LastTerrainImpact;
            pose.TerrainImpactSequence = slugcat.TerrainImpactSequence;
            pose.ImpactBodyChunk = impact.BodyChunkIndex;
            pose.PreImpactVelocity = impact.PreImpactVelocity;
            pose.PostImpactVelocity = impact.PostImpactVelocity;
            pose.ImpactDirection = impact.ImpactDirection;
            pose.ImpactCollisionNormal = impact.CollisionNormal;
            pose.ImpactSpeed = impact.ImpactSpeed;
            pose.ImpactSurfaceId = impact.SurfaceId;
            pose.ImpactSurfaceKind = impact.SurfaceKind;
            pose.ImpactFirstContact = impact.FirstContact;
            pose.TerrainImpactTriggered = impact.TerrainImpactTriggered;
            pose.CalculatedImpactStun = impact.CalculatedStun;
            pose.AppliedImpactStun = impact.AppliedStun;
            pose.ImpactWasOriginallyLethal = impact.WasOriginallyLethal;
            pose.ImpactSafetyOverrideApplied = impact.SafetyOverrideApplied;
            pose.DesktopImpactResult = impact.DesktopResult;
            pose.ImpactStunDeadlineTick = impact.ImpactStunDeadlineTick;
            pose.ImpactCausedDeath = impact.CausedDeath;
            pose.Breath = 0.5 + 0.5 * Math.Sin(MathUtil.Lerp(lastBreath, breath, timeStacker) * Math.PI * 2.0);
            pose.LandingCompression = slugcat.State.LandingCompression;
            for (int i = 0; i < 2; i++)
            {
                pose.HandLast[i] = arms[i].End.LastPosition;
                pose.HandCurrent[i] = arms[i].End.Position;
                pose.Hands[i] = arms[i].RenderPosition(timeStacker);
                pose.HandTargets[i] = arms[i].TargetPosition;
                pose.ArmConnectionLast[i] = arms[i].LastConnectionPosition;
                pose.ArmConnectionCurrent[i] = arms[i].ConnectionPosition;
                pose.ArmConnections[i] = Vec2.Lerp(arms[i].LastConnectionPosition,
                    arms[i].ConnectionPosition, timeStacker);
                pose.ArmMaxLengths[i] = arms[i].Length;
                pose.ArmRetractCounters[i] = arms[i].RetractCounter;
                pose.ArmModes[i] = arms[i].Mode;
                pose.ArmGripSurfaceIds[i] = arms[i].GripSurfaceId;
                pose.ArmVisible[i] = arms[i].Mode != LimbMode.Retracted;
                pose.Elbows[i] = arms[i].ComputeJoint(pose.Chest, pose.Hands[i], timeStacker);
                double side = i == 0 ? -2.0 : 2.0;
                Vec2 lastBodyUp = (drawPositions[0, 1] - drawPositions[1, 1]).Normalized;
                if (lastBodyUp.LengthSquared < 0.1) lastBodyUp = Vec2.Up;
                Vec2 currentBodyUp = (drawPositions[0, 0] - drawPositions[1, 0]).Normalized;
                if (currentBodyUp.LengthSquared < 0.1) currentBodyUp = Vec2.Up;
                pose.FootLast[i] = pose.LegsLast + lastBodyUp.Perpendicular * side;
                pose.FootCurrent[i] = pose.LegsCurrent + currentBodyUp.Perpendicular * side;
                pose.Feet[i] = Vec2.Lerp(pose.FootLast[i], pose.FootCurrent[i], timeStacker);
                pose.FootTargets[i] = legsTargetPosition;
                pose.Knees[i] = Vec2.Lerp(pose.Hips, pose.Feet[i], 0.5);
            }
            TailSegment[] segments = tail.Segments;
            for (int i = 0; i < segments.Length; i++)
            {
                pose.TailLast[i] = segments[i].LastPosition;
                pose.TailCurrent[i] = segments[i].Position;
                pose.Tail[i] = segments[i].RenderPosition(timeStacker);
                double stretched = MathUtil.Lerp(segments[i].LastStretched, segments[i].Stretched, timeStacker);
                pose.TailRadii[i] = segments[i].Radius * stretched;
            }
            pose.CharacterOrigin = (pose.Chest + pose.Hips) * 0.5;
            pose.CharacterRenderScale = SimulationConstants.CharacterRenderScale;
            pose.TailRoot = (pose.Hips * 3.0 + pose.Chest) / 4.0;
            OriginalFaceState face = SpriteRenderer.ResolveOriginalFaceState(pose);
            pose.SelectedFaceElement = face.FaceElement;
            pose.FacePosition = face.FacePosition;
            pose.FaceRotation = face.FaceRotation;
            pose.FaceScaleX = face.FaceScaleX;
            pose.FaceSelectionReason = face.Reason;
            pose.HeadElement = face.HeadElement;
            pose.HeadSpritePosition = face.HeadPosition;
            pose.HeadRotation = face.HeadRotation;
            pose.HeadScaleX = face.HeadScaleX;
            pose.TailRenderMode = "OriginalTriangleMesh";
            pose.TailMeshVertexCount = 15;
            for (int i = 0; i < 2; i++)
            {
                pose.ArmShoulders[i] = SpriteRenderer.ComputeArmShoulder(pose, i);
                pose.ArmDirections[i] = (pose.ArmShoulders[i] - pose.Hands[i]).Normalized;
                pose.ArmRotations[i] = SpriteRenderer.ComputeArmRotation(pose, i);
                pose.ArmScaleY[i] = SpriteRenderer.ComputeArmScaleY(pose, i);
            }
            int extraIndex = 0;
            for (int i = 0; i < extensions.Length; i++)
                extraIndex = extensions[i].BuildPose(pose, extraIndex, timeStacker);
            if (extraIndex != pose.ExtraParts.Length)
                throw new InvalidOperationException("Graphics extension sprite allocation did not match its declared count.");
            pose.UpdateGraphicsBounds();
            return pose;
        }

        public void ApplyMovingSurfaceDelta(Vec2 delta)
        {
            if (delta.LengthSquared < 0.000001) return;
            head.Translate(delta);
            legs.Translate(delta);
            for (int i = 0; i < 2; i++) arms[i].Translate(delta);
            tail.Translate(delta);
            for (int i = 0; i < extensions.Length; i++) extensions[i].Translate(delta);
            for (int i = 0; i < 2; i++)
            {
                drawPositions[i, 0] += delta;
                drawPositions[i, 1] += delta;
            }
        }
    }
}
