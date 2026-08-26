using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class OriginalFaceState
    {
        public string HeadElement;
        public Vec2 HeadPosition;
        public double HeadRotation;
        public double HeadScaleX;
        public string FaceElement;
        public Vec2 FacePosition;
        public double FaceRotation;
        public double FaceScaleX;
        public string Reason;
    }

    public sealed class SpriteRenderer : IDisposable
    {
        private static readonly Color OutlineColor = Color.FromArgb(255, 28, 39, 51);
        private static readonly Color EyeColor = Color.FromArgb(255, 23, 32, 42);
        // Desktop has no RoomPalette, so use its fixed equivalent of the
        // black/fog palette already used by the original-effect renderer.
        private static readonly Color OriginalUmbilicalFog = Color.FromArgb(255, 92, 98, 105);
        private static readonly Color OriginalUmbilicalThread = LerpColor(
            Color.FromArgb(255, 242, 204, 140), OriginalUmbilicalFog, 0.2);
        private readonly RainWorldAtlasSet atlas;
        private readonly Font debugFont = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Dictionary<int, ImageAttributes> tintAttributes = new Dictionary<int, ImageAttributes>();
        private readonly Dictionary<int, ImageAttributes> effectTintAttributes =
            new Dictionary<int, ImageAttributes>();
        private readonly Dictionary<int, SolidBrush> bodyBrushes = new Dictionary<int, SolidBrush>();
        private readonly GdiSpriteCanvas gdiCanvas;
        private readonly Dictionary<SlugcatId, bool> profileAtlasAvailability =
            new Dictionary<SlugcatId, bool>();
        private readonly PointF[] destinationPoints = new PointF[3];
        private readonly PointF[] effectDestinationPoints = new PointF[3];
        private readonly Vec2[] tailMeshVertices = new Vec2[15];
        private readonly PointF[] tailMeshPoints = new PointF[15];
        private readonly PointF[] tailTrianglePoints = new PointF[3];
        private readonly PointF[] tailRasterDestinationPoints = new PointF[3];
        private readonly PointF[] tailTextureCoordinates = new PointF[15];
        private readonly PointF[] tailTextureSourceTriangle = new PointF[3];
        private readonly PointF[] tailTextureDestinationTriangle = new PointF[3];
        private readonly PointF[] abilityQuad = new PointF[4];
        private readonly PointF[] abilityTriangle = new PointF[3];
        private readonly Vec2[] eggTailCenters =
            new Vec2[DesktopFood.EggBugEggTailSegmentCount + 1];
        private readonly PointF[] eggTailOutline =
            new PointF[(DesktopFood.EggBugEggTailSegmentCount + 1) * 2];
        private readonly Bitmap tailRaster;
        private readonly System.Drawing.Graphics tailRasterGraphics;
        private readonly Bitmap flatLightShaderMask;
        private readonly Bitmap lightSourceShaderMask;
        private readonly Bitmap shockWaveShaderMask;
        private const int TailRasterSize = 128;
        private const int ResourceCacheLimit = 1024;
        public const int OriginalTailMeshVertexCount = 15;
        public const int OriginalTailMeshTriangleCount = 13;
        private static readonly int[,] TailTriangles =
        {
            { 0, 1, 2 }, { 1, 2, 3 }, { 4, 5, 6 }, { 5, 6, 7 },
            { 8, 9, 10 }, { 9, 10, 11 }, { 12, 13, 14 },
            { 2, 3, 4 }, { 3, 4, 5 }, { 6, 7, 8 }, { 7, 8, 9 },
            { 10, 11, 12 }, { 11, 12, 13 }
        };
        private static readonly int[] TailLeftEdge =
        {
            0, 2, 4, 6, 8, 10, 12, 14
        };
        private static readonly int[] TailRightEdge =
        {
            1, 3, 5, 7, 9, 11, 13, 14
        };
        private SlugcatPose activePose;
        private RenderSpace activeRenderSpace;
        private readonly Dictionary<string, DmsSkinDefinition> dmsParts =
            new Dictionary<string, DmsSkinDefinition>(StringComparer.OrdinalIgnoreCase);

        public SpriteRenderer(RainWorldAtlasSet atlas)
        {
            this.atlas = atlas;
            tailRaster = new Bitmap(TailRasterSize, TailRasterSize,
                PixelFormat.Format32bppPArgb);
            tailRasterGraphics = System.Drawing.Graphics.FromImage(tailRaster);
            tailRasterGraphics.SmoothingMode = SmoothingMode.None;
            tailRasterGraphics.PixelOffsetMode = PixelOffsetMode.Half;
            tailRasterGraphics.CompositingMode = CompositingMode.SourceCopy;
            tailRasterGraphics.CompositingQuality = CompositingQuality.HighSpeed;
            flatLightShaderMask = CreateEffectShaderMask(EffectShaderMask.FlatLight);
            lightSourceShaderMask = CreateEffectShaderMask(EffectShaderMask.LightSource);
            shockWaveShaderMask = CreateEffectShaderMask(EffectShaderMask.ShockWave);
            gdiCanvas = new GdiSpriteCanvas(GetTintAttributes);
        }

        public bool UsesLocalAtlas { get { return atlas != null; } }
        public DmsSkinDefinition ActiveDmsSkin
        {
            get
            {
                foreach (DmsSkinDefinition skin in dmsParts.Values) return skin;
                return null;
            }
        }

        public DmsSkinDefinition GetDmsPart(string part)
        {
            DmsSkinDefinition skin;
            return !string.IsNullOrWhiteSpace(part) && dmsParts.TryGetValue(part, out skin)
                ? skin : null;
        }

        public void SetDmsPart(string part, DmsSkinDefinition skin)
        {
            if (string.IsNullOrWhiteSpace(part)) return;
            if (skin == null) dmsParts.Remove(part);
            else dmsParts[part] = skin;
        }

        public void ClearDmsParts()
        {
            dmsParts.Clear();
        }

        public void InvalidateAtlasAvailability()
        {
            profileAtlasAvailability.Clear();
        }

        // Kept for the command-line preview utility. Runtime UI uses only
        // explicit per-part selections through SetDmsPart.
        public void SetDmsSkin(DmsSkinDefinition skin)
        {
            dmsParts.Clear();
            if (skin == null) return;
            for (int i = 0; i < DmsSpriteGroups.SelectableParts.Length; i++)
            {
                string part = DmsSpriteGroups.SelectableParts[i];
                if (skin.HasPart(part)) dmsParts[part] = skin;
            }
        }

        public void Render(System.Drawing.Graphics graphics, SlugcatPose pose,
            Vec2 windowOrigin, bool debug, DesktopCollisionWorld world,
            Slugcat slugcat, DesktopPetAI ai, string assetStatus,
            SlugcatAppearance appearance)
        {
            Render(graphics, pose, windowOrigin, debug, world, slugcat, ai,
                assetStatus, slugcat.SelectedSlugcat);
        }

        public void Render(System.Drawing.Graphics graphics, SlugcatPose pose,
            RenderSpace renderSpace, bool debug, DesktopCollisionWorld world,
            Slugcat slugcat, DesktopPetAI ai, string assetStatus,
            SlugcatAppearance appearance)
        {
            Render(graphics, pose, renderSpace, debug, world, slugcat, ai,
                assetStatus, slugcat.SelectedSlugcat);
        }

        public void Render(
            System.Drawing.Graphics graphics,
            SlugcatPose pose,
            Vec2 windowOrigin,
            bool debug,
            DesktopCollisionWorld world,
            Slugcat slugcat,
            DesktopPetAI ai,
            string assetStatus,
            SlugcatProfile selectedSlugcat)
        {
            Render(graphics, pose, new RenderSpace(new Rectangle((int)windowOrigin.X,
                (int)windowOrigin.Y, 1, 1)), debug, world, slugcat, ai, assetStatus, selectedSlugcat);
        }

        public void Render(
            System.Drawing.Graphics graphics,
            SlugcatPose pose,
            RenderSpace renderSpace,
            bool debug,
            DesktopCollisionWorld world,
            Slugcat slugcat,
            DesktopPetAI ai,
            string assetStatus,
            SlugcatProfile selectedSlugcat)
        {
            gdiCanvas.Begin(graphics);
            try
            {
                RenderCore(gdiCanvas, pose, renderSpace, debug, world, slugcat,
                    ai, assetStatus, selectedSlugcat);
            }
            finally
            {
                gdiCanvas.End();
            }
        }

        internal void RenderGpu(GpuSpriteCanvas canvas, SlugcatPose pose,
            RenderSpace renderSpace, DesktopCollisionWorld world, Slugcat slugcat,
            DesktopPetAI ai, string assetStatus, SlugcatProfile selectedSlugcat)
        {
            if (canvas == null) throw new ArgumentNullException("canvas");
            RenderCore(canvas, pose, renderSpace, false, world, slugcat, ai,
                assetStatus, selectedSlugcat);
        }

        private void RenderCore(ISpriteCanvas graphics, SlugcatPose pose,
            RenderSpace renderSpace, bool debug, DesktopCollisionWorld world,
            Slugcat slugcat, DesktopPetAI ai, string assetStatus,
            SlugcatProfile selectedSlugcat)
        {
            pose.SpritePlacements.Clear();
            pose.OverlayBounds = renderSpace.VirtualDesktopBounds;
            activePose = pose;
            // SpritePlacement is debug-overlay metadata. Building one heap
            // object per atlas sprite at compositor FPS was the renderer's
            // largest steady allocation source.
            activeRenderSpace = debug ? renderSpace : null;
            graphics.Save();
            try
            {
                double scale = pose.CharacterRenderScale;
                graphics.SetTransform((float)scale, 0.0f, 0.0f, (float)scale,
                    (float)-renderSpace.WorldOrigin.X,
                    (float)-renderSpace.WorldOrigin.Y);

                bool profileAtlasAvailable = IsProfileAtlasAvailable(pose.SelectedSlugcat);
                DrawSpears(graphics, slugcat, pose, pose.TimeStacker, false);
                if (profileAtlasAvailable)
                {
                    // Dress My Slugcat reorders PlayerGraphics' Futile nodes
                    // after AddToContainer: tail(2) behind legs(4) behind
                    // hips(1), then one arm behind the body and the other
                    // behind the head according to flipDirection.  Applying
                    // an individual DMS part must preserve that exact order.
                    if (pose.Facing >= 0) DrawAtlasArm(graphics, pose, 1, pose.VisualArmColor); // 6
                    else DrawAtlasArm(graphics, pose, 0, pose.VisualArmColor); // 5
                    DrawAtlasBody(graphics, pose); // 0
                    DrawTail(graphics, pose, pose.VisualTailColor); // 2
                    DrawAtlasLegs(graphics, pose); // 4
                    DrawAtlasHips(graphics, pose); // 1
                    // Spearmaster's TailSpeckles are inserted at original
                    // sprite index 3, before the front arm and head.
                    DrawExtraGraphics(graphics, pose, ExtraGraphicsLayer.AfterTailBeforeHead);
                    if (pose.Facing >= 0) DrawAtlasArm(graphics, pose, 0, pose.VisualArmColor); // 5
                    else DrawAtlasArm(graphics, pose, 1, pose.VisualArmColor); // 6
                    DrawAtlasHeadPart(graphics, pose, pose.VisualHeadColor, false); // 3
                    DrawExtraGraphics(graphics, pose, ExtraGraphicsLayer.BehindFace);
                    // The DMS hook moves Rivulet's gills directly behind FaceA.
                    DrawExtraGraphics(graphics, pose, ExtraGraphicsLayer.InFront);
                    DrawAtlasHeadPart(graphics, pose, pose.VisualHeadColor, true); // 9
                }
                else
                {
                    DrawTail(graphics, pose, pose.VisualTailColor);
                    DrawLimbs(graphics, pose, 0, pose.VisualArmColor);
                    DrawProceduralBody(graphics, pose);
                    DrawLimbs(graphics, pose, 1, pose.VisualArmColor);
                    DrawHead(graphics, pose, false, pose.VisualHeadColor);
                }
                DrawAbilityObjects(graphics, slugcat, pose, pose.TimeStacker);

                if (debug)
                {
                    graphics.SetTransform(1.0f, 0.0f, 0.0f, 1.0f,
                        (float)-renderSpace.WorldOrigin.X,
                        (float)-renderSpace.WorldOrigin.Y);
                    GdiSpriteCanvas debugCanvas = graphics as GdiSpriteCanvas;
                    if (debugCanvas == null)
                        throw new InvalidOperationException(
                            "Debug rendering requires the GDI fallback canvas.");
                    DrawDebugWorld(debugCanvas.Target, pose, world, slugcat, ai);
                }
            }
            finally
            {
                graphics.Restore();
                activePose = null;
                activeRenderSpace = null;
            }

            if (debug)
            {
                StringBuilder builder = new StringBuilder(3400);
                builder.AppendFormat("sim {0:0.#} Hz tick {1} step {2:0.000000}s time {3:0.000}s steps/frame {4}\n",
                    pose.LogicTicksPerSecond, pose.SimulationTick, pose.LogicStepSeconds,
                    pose.SimulationTimeSeconds, pose.SimulationStepsLastFrame);
                builder.AppendFormat("accumulator {0:0.000000}s timeStacker {1:0.000} | render {2:0.0} FPS monitor {3:0.#} Hz\n",
                    pose.AccumulatorSeconds, pose.TimeStacker, pose.RenderFramesPerSecond, pose.MonitorRefreshRate);
                builder.AppendFormat("AI {0} input {1} | body {2} animation {3} frame {4} facing/flip {5}\n",
                    ai.Behavior, slugcat.LastInput, pose.BodyMode, pose.Animation,
                    pose.AnimationFrame, pose.Facing);
                PlatformTransitionPlan transition = ai.TransitionPlan;
                builder.AppendFormat("transition {0} valid={1} surface {2}->{3} target={4} dx/dy={5:0.0}/{6:0.0}\n",
                    transition.Mode, transition.IsValid, transition.SourceSurfaceId,
                    transition.TargetSurfaceId, transition.TargetPoint,
                    transition.HorizontalDistance, transition.VerticalDistance);
                builder.AppendFormat("slugcat {0} originalID={1} profile={2} sprites base={3} extra={4}\n",
                    pose.SelectedSlugcat, pose.OriginalSlugcatId, pose.VisualProfileName,
                    pose.BaseSpriteCount, pose.ExtraSpriteCount);
                builder.AppendFormat("movement {0}\nability {1}\n",
                    pose.MovementProfileDebug, pose.AbilityDebug);
                builder.AppendFormat("DMS parts {0}\n", dmsParts.Count);
                builder.AppendFormat("face {0} tail={1} extensions={2}\n",
                    pose.SelectedFaceElement, pose.TailProfileName, pose.GraphicsExtensions);
                for (int i = 0; i < pose.ExtraParts.Length; i++)
                {
                    ExtraGraphicsPartPose extra = pose.ExtraParts[i];
                    builder.AppendFormat("  extra[{0}] #{1} {2}/{3} last={4} pos={5} render={6} rot={7:0.##} layer={8} visible={9}\n",
                        i, extra.OriginalSpriteIndex, extra.ExtensionName, extra.Element,
                        extra.LastPosition, extra.CurrentPosition, extra.RenderPosition,
                        extra.Rotation, extra.Layer, extra.Visible);
                }
                VirtualInput[] inputHistory = slugcat.Movement.InputHistoryForRead;
                builder.AppendFormat("input history now/1/2/3: {0} | {1} | {2} | {3}\n",
                    inputHistory[0], inputHistory[1], inputHistory[2], inputHistory[3]);
                builder.AppendFormat("physics gravity {0:0.###}/tick air {1:0.###} maxFall none connection {2:0.###} world x{3:0.00} snapshot {4}\n",
                    SimulationConstants.GravityPerTick, SimulationConstants.AirFriction,
                    slugcat.BodyConnection.Distance, SimulationConstants.DesktopWorldScale,
                    world.CurrentSnapshot.Version);
                builder.AppendFormat("monitor {0} id={1} bounds={2} work={3} taskbar={4}/{5} floorY={6:0.###}\n",
                    pose.CurrentMonitorName, pose.CurrentMonitorId, pose.CurrentMonitorBounds,
                    pose.CurrentMonitorWorkArea, pose.CurrentTaskbarEdge,
                    pose.CurrentTaskbarBounds, pose.CurrentMonitorFloorY);
                builder.AppendFormat("current surface {0}/{1} left={2:0.###} right={3:0.###} top={4:0.###}\n",
                    pose.CurrentSurfaceId, pose.CurrentSurfaceKind, pose.CurrentSurfaceLeft,
                    pose.CurrentSurfaceRight, pose.CurrentSurfaceTop);
                for (int i = 0; i < slugcat.BodyChunks.Length; i++)
                {
                    BodyChunk chunk = slugcat.BodyChunks[i];
                    builder.AppendFormat("chunk{0} pos {1} last {2} render {3} vel {4} contact F/L/R={5}/{6}/{7} surface={8} wall={9}\n",
                        i, chunk.Position, chunk.LastPosition, pose.ChunkRender[i], chunk.Velocity,
                        chunk.ContactFloor, chunk.ContactLeft, chunk.ContactRight,
                        chunk.SupportingSurfaceId, chunk.WallSurfaceId);
                    AppendSurfaceDebug(builder, world, chunk);
                }
                builder.AppendFormat("head {0}->{1}->{2} target {3} vel {4} originalLook {5} finalLook {6} dir {7}\n",
                    pose.HeadLast, pose.HeadCurrent, pose.Head, pose.HeadTarget,
                    pose.HeadVelocity, pose.OriginalLookDirection, pose.LookDirection,
                    pose.HeadDirection);
                builder.AppendFormat("face animation={0} body={1} facing/flip={2} blink={3} head={4} at {5} rot={6:0.###} scaleX={7:0.###}\n",
                    pose.Animation, pose.BodyMode, pose.Facing, pose.Blink, pose.HeadElement,
                    pose.HeadSpritePosition, pose.HeadRotation, pose.HeadScaleX);
                builder.AppendFormat("face element={0} at {1} rot={2:0.###} scaleX={3:0.###} reason={4}\n",
                    pose.SelectedFaceElement, pose.FacePosition, pose.FaceRotation,
                    pose.FaceScaleX, pose.FaceSelectionReason);
                builder.AppendFormat("mouse pos={0} headDistance={1:0.###} radius={2:0.###} lastRelevantClick={3:0.###} since={4:0.###} timeout={5:0.###} active={6}\n",
                    pose.MousePosition, pose.MouseDistanceToHead, pose.MouseAttentionRadius,
                    pose.LastRelevantMouseClickTime, pose.TimeSinceRelevantMouseClick,
                    pose.MouseAttentionTimeout, pose.MouseAttentionActive);
                builder.AppendFormat("air input x prev={0}/{1} y/jump={2}/{3} vx before={4:0.###},{5:0.###} after={6:0.###},{7:0.###}\n",
                    pose.InputX, pose.PreviousInputX, pose.InputY, pose.InputJump,
                    pose.AirHorizontalVelocityBefore[0], pose.AirHorizontalVelocityBefore[1],
                    pose.AirHorizontalVelocityAfter[0], pose.AirHorizontalVelocityAfter[1]);
                builder.AppendFormat("air gravity={0:0.###} contribution c0={1} c1={2} body={3} animation={4} airborne={5} rising={6} falling={7} counter={8:0.###} branch={9}\n",
                    SimulationConstants.GravityPerTick, pose.AirMovementContribution[0],
                    pose.AirMovementContribution[1], pose.BodyMode, pose.Animation, pose.IsAirborne,
                    pose.IsRising, pose.IsFalling, pose.AirborneCounter,
                    pose.AirControlBranch);
                builder.AppendFormat("impact seq={0} chunk={1} pre={2} post={3} direction={4} normal={5} speed={6:0.###} surface={7}/{8} first={9} triggered={10}\n",
                    pose.TerrainImpactSequence, pose.ImpactBodyChunk, pose.PreImpactVelocity,
                    pose.PostImpactVelocity, pose.ImpactDirection,
                    pose.ImpactCollisionNormal, pose.ImpactSpeed, pose.ImpactSurfaceId,
                    pose.ImpactSurfaceKind, pose.ImpactFirstContact,
                    pose.TerrainImpactTriggered);
                builder.AppendFormat("impact safety result={0} originallyLethal={1} originalStun={2} applied={3} override={4} deadline={5} max={6} ticks/{7:0.0}s death={8}\n",
                    pose.DesktopImpactResult, pose.ImpactWasOriginallyLethal,
                    pose.CalculatedImpactStun, pose.AppliedImpactStun,
                    pose.ImpactSafetyOverrideApplied, pose.ImpactStunDeadlineTick,
                    SimulationConstants.MaxImpactStunTicks,
                    SimulationConstants.MaxImpactStunDurationSeconds,
                    pose.ImpactCausedDeath);
                builder.AppendFormat("stun active={0} counter={1} initial={2} conscious={3} dead={4} body={5} animation={6} standing={7} face={8}\n",
                    pose.IsStunned, pose.StunCounter, pose.InitialStunValue,
                    pose.Conscious, pose.Dead, pose.BodyMode, pose.Animation,
                    pose.Standing, pose.SelectedFaceElement);
                for (int i = 0; i < 2; i++)
                {
                    builder.AppendFormat("hand{0} {1}->{2}->{3} shoulder {4} dir {5} rot {6:0.###} scaleY {7:0.###} target {8} mode {9} grip {10}; foot {11}->{12}->{13} target {14}\n",
                        i, pose.HandLast[i], pose.HandCurrent[i], pose.Hands[i],
                        pose.ArmShoulders[i], pose.ArmDirections[i], pose.ArmRotations[i],
                        pose.ArmScaleY[i], pose.HandTargets[i], pose.ArmModes[i],
                        pose.ArmGripSurfaceIds[i], pose.FootLast[i], pose.FootCurrent[i],
                        pose.Feet[i], pose.FootTargets[i]);
                }
                Vec2 tailPrevious = pose.TailRoot;
                builder.AppendFormat("tail root={0} tip={1} mode={2} meshVertices={3}\n",
                    pose.TailRoot, pose.TailTip, pose.TailRenderMode,
                    pose.TailMeshVertexCount);
                for (int i = 0; i < pose.Tail.Length; i++)
                {
                    builder.AppendFormat("tail{0} last={1} current={2} render={3} radius={4:0.###} distance={5:0.###} tangent={6} perp={7}\n",
                        i, pose.TailLast[i], pose.TailCurrent[i], pose.Tail[i],
                        pose.TailRadii[i], Vec2.Distance(tailPrevious, pose.Tail[i]),
                        pose.TailTangents[i], pose.TailPerpendiculars[i]);
                    tailPrevious = pose.Tail[i];
                }
                builder.AppendFormat("tail mesh L/R root={0}/{1} joint0={2}/{3} joint1={4}/{5} joint2={6}/{7} tip={8} | graphics {9} overlay {10}\n",
                    pose.TailMeshVertices[0], pose.TailMeshVertices[1],
                    pose.TailMeshVertices[4], pose.TailMeshVertices[5],
                    pose.TailMeshVertices[8], pose.TailMeshVertices[9],
                    pose.TailMeshVertices[12], pose.TailMeshVertices[13],
                    pose.TailMeshVertices[14], pose.GraphicsBounds, pose.OverlayBounds);
                builder.AppendFormat("renderScale {0:0.00} | attention final={1}/{2} original={3}/{4}\n{5}",
                    pose.CharacterRenderScale, ai.Attention.Kind, ai.Attention.Target,
                    ai.OriginalAttentionKind, ai.OriginalAttentionTarget, assetStatus);
                string text = builder.ToString();
                graphics.DrawString(text, debugFont,
                    Color.FromArgb(210, 0, 0, 0), new PointF(9.0f, 9.0f));
                graphics.DrawString(text, debugFont,
                    Color.FromArgb(255, 235, 255, 235), new PointF(8.0f, 8.0f));
            }
        }

        private bool IsProfileAtlasAvailable(SlugcatId id)
        {
            bool available;
            if (profileAtlasAvailability.TryGetValue(id, out available)) return available;
            string missing;
            available = atlas != null && SlugcatGraphicsProfiles.Get(id).IsAvailable(
                atlas, out missing);
            profileAtlasAvailability[id] = available;
            return available;
        }

        private static void AppendSurfaceDebug(StringBuilder builder, DesktopCollisionWorld world, BodyChunk chunk)
        {
            long id = chunk.SupportingSurfaceId != 0 ? chunk.SupportingSurfaceId : chunk.WallSurfaceId;
            if (id == 0) return;
            DesktopSurfaceKind kind = chunk.SupportingSurfaceId != 0
                ? chunk.SupportingSurfaceKind
                : chunk.WallSurfaceKind;
            DesktopSurface surface;
            if (!world.TryGetSurface(id, kind, out surface))
            {
                builder.AppendFormat("  surface {0}/{1} MISSING\n", id, kind);
                return;
            }
            builder.AppendFormat("  surface {0}/{1} LTRB={2},{3},{4},{5} prev={6} current={7} velocity={8} missed={9}\n",
                surface.Id, surface.Kind, surface.Left, surface.Top, surface.Right, surface.Bottom,
                surface.PreviousWindowBounds, surface.CurrentWindowBounds,
                surface.MovementVelocity, surface.MissingRefreshes);
        }

        private void DrawTail(ISpriteCanvas graphics, SlugcatPose pose, Color bodyColor)
        {
            if (pose.Tail == null || pose.Tail.Length != SimulationConstants.TailSegmentCount)
                return;
            // TailSegment is simulation-only. Atlas and procedural body paths
            // both submit this one continuous PlayerGraphics-equivalent mesh;
            // there is no segmented line/sprite fallback.
            DrawOriginalTailMesh(graphics, pose, bodyColor);
        }

        private void DrawOriginalTailMesh(ISpriteCanvas graphics, SlugcatPose pose, Color bodyColor)
        {
            pose.TailRenderMode = null;
            PopulateOriginalTailMeshVertices(pose, tailMeshVertices);
            for (int i = 0; i < tailMeshVertices.Length; i++)
                tailMeshPoints[i] = tailMeshVertices[i].ToPointF();

            // Rain World draws this TriangleMesh into its 1:1 internal render
            // target with MSAA disabled, then point-filters that target to the
            // display. Rasterize the DLL's 13 triangles at simulation-pixel
            // resolution first so the tail shares the atlas' pixel grid.
            float minX = tailMeshPoints[0].X;
            float minY = tailMeshPoints[0].Y;
            float maxX = minX;
            float maxY = minY;
            for (int i = 1; i < tailMeshPoints.Length; i++)
            {
                minX = Math.Min(minX, tailMeshPoints[i].X);
                minY = Math.Min(minY, tailMeshPoints[i].Y);
                maxX = Math.Max(maxX, tailMeshPoints[i].X);
                maxY = Math.Max(maxY, tailMeshPoints[i].Y);
            }

            int rasterLeft = (int)Math.Floor(minX) - 2;
            int rasterTop = (int)Math.Floor(minY) - 2;
            int rasterWidth = (int)Math.Ceiling(maxX) + 2 - rasterLeft;
            int rasterHeight = (int)Math.Ceiling(maxY) + 2 - rasterTop;
            AtlasSprite dmsTail = null;
            DmsSkinDefinition dmsTailSkin = GetDmsPart("TAIL");
            bool textured = dmsTailSkin != null && dmsTailSkin.TryGetSprite(
                "TailTexture", pose.OriginalSlugcatId, DmsSpriteSide.None,
                out dmsTail);
            Color tailColor = dmsTailSkin != null &&
                dmsTailSkin.DefaultTail.Color.A > 0
                ? dmsTailSkin.DefaultTail.Color : bodyColor;

            // Rain World rasterizes PlayerGraphics' TriangleMesh at its 1:1
            // internal pixel resolution before point-filtering the result to
            // the display. Keep that exact ordering even on the GPU canvas:
            // build only this tiny tail bitmap with aliased GDI triangles,
            // then let Direct2D upload and scale it with nearest-neighbor.
            // This avoids both per-triangle GPU AA seams and smooth vector
            // edges that do not match the game's pixel-art presentation.
            if (rasterWidth <= TailRasterSize && rasterHeight <= TailRasterSize)
            {
                tailRasterGraphics.Clear(Color.Transparent);
                if (textured)
                {
                    RasterizeDmsTail(dmsTail, tailColor, rasterLeft, rasterTop);
                    pose.TailRenderMode = "DMS-UV-TriangleMesh";
                }
                else
                {
                    for (int i = 0; i < TailTriangles.GetLength(0); i++)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            PointF point = tailMeshPoints[TailTriangles[i, j]];
                            tailTrianglePoints[j] = new PointF(
                                point.X - rasterLeft, point.Y - rasterTop);
                        }
                        tailRasterGraphics.FillPolygon(GetBodyBrush(tailColor),
                            tailTrianglePoints, FillMode.Winding);
                    }
                }

                tailRasterDestinationPoints[0] = new PointF(rasterLeft, rasterTop);
                tailRasterDestinationPoints[1] = new PointF(
                    rasterLeft + rasterWidth, rasterTop);
                tailRasterDestinationPoints[2] = new PointF(
                    rasterLeft, rasterTop + rasterHeight);
                graphics.DrawImage(tailRaster, tailRasterDestinationPoints,
                    new RectangleF(0.0f, 0.0f, rasterWidth, rasterHeight),
                    Color.White, true);
            }
            else
            {
                for (int i = 0; i < TailTriangles.GetLength(0); i++)
                {
                    for (int j = 0; j < 3; j++)
                        tailTrianglePoints[j] = tailMeshPoints[TailTriangles[i, j]];
                    graphics.FillPolygon(bodyColor, tailTrianglePoints);
                }
            }
            Array.Copy(tailMeshVertices, pose.TailMeshVertices,
                OriginalTailMeshVertexCount);
            if (string.IsNullOrEmpty(pose.TailRenderMode)) pose.TailRenderMode = "OriginalTriangleMesh";
            pose.TailMeshVertexCount = tailMeshVertices.Length;
        }

        private void RasterizeDmsTail(AtlasSprite sprite, Color tint, int rasterLeft, int rasterTop)
        {
            PopulateTailTextureCoordinates(sprite.Element, tailTextureCoordinates);
            GraphicsState baseState = tailRasterGraphics.Save();
            try
            {
                tailRasterGraphics.CompositingMode = CompositingMode.SourceOver;
                for (int triangle = 0; triangle < TailTriangles.GetLength(0); triangle++)
                {
                    for (int point = 0; point < 3; point++)
                    {
                        int vertex = TailTriangles[triangle, point];
                        tailTextureSourceTriangle[point] = tailTextureCoordinates[vertex];
                        tailTextureDestinationTriangle[point] = new PointF(
                            tailMeshPoints[vertex].X - rasterLeft,
                            tailMeshPoints[vertex].Y - rasterTop);
                    }
                    using (Matrix transform = CreateTriangleTransform(
                        tailTextureSourceTriangle, tailTextureDestinationTriangle))
                    {
                        if (transform == null) continue;
                        GraphicsState state = tailRasterGraphics.Save();
                        try
                        {
                            using (GraphicsPath clip = new GraphicsPath())
                            {
                                clip.AddPolygon(tailTextureDestinationTriangle);
                                tailRasterGraphics.SetClip(clip, CombineMode.Replace);
                            }
                            tailRasterGraphics.Transform = transform;
                            tailRasterGraphics.DrawImage(sprite.Atlas.Image,
                                new Rectangle(0, 0, sprite.Atlas.Image.Width, sprite.Atlas.Image.Height),
                                0, 0, sprite.Atlas.Image.Width, sprite.Atlas.Image.Height,
                                GraphicsUnit.Pixel, GetTintAttributes(tint));
                        }
                        finally
                        {
                            tailRasterGraphics.Restore(state);
                        }
                    }
                }
            }
            finally
            {
                tailRasterGraphics.Restore(baseState);
            }
        }

        private static void PopulateTailTextureCoordinates(AtlasElement element,
            PointF[] result)
        {
            for (int index = 0; index < result.Length; index++)
            {
                double u;
                double v;
                if (index == result.Length - 1)
                {
                    u = 1.0;
                    v = 0.5;
                }
                else
                {
                    u = (index / 2) / 7.0;
                    v = index % 2 == 0 ? 0.0 : 1.0;
                }
                result[index] = new PointF(
                    element.Frame.Left + (float)(u * element.Frame.Width),
                    element.Frame.Top + (float)(v * element.Frame.Height));
            }
        }

        private static Matrix CreateTriangleTransform(PointF[] source, PointF[] destination)
        {
            double denominator = source[0].X * (source[1].Y - source[2].Y) +
                source[1].X * (source[2].Y - source[0].Y) +
                source[2].X * (source[0].Y - source[1].Y);
            if (Math.Abs(denominator) < 0.000001) return null;
            double m11 = (destination[0].X * (source[1].Y - source[2].Y) +
                destination[1].X * (source[2].Y - source[0].Y) +
                destination[2].X * (source[0].Y - source[1].Y)) / denominator;
            double m21 = (destination[0].X * (source[2].X - source[1].X) +
                destination[1].X * (source[0].X - source[2].X) +
                destination[2].X * (source[1].X - source[0].X)) / denominator;
            double dx = (destination[0].X * (source[1].X * source[2].Y - source[2].X * source[1].Y) +
                destination[1].X * (source[2].X * source[0].Y - source[0].X * source[2].Y) +
                destination[2].X * (source[0].X * source[1].Y - source[1].X * source[0].Y)) / denominator;
            double m12 = (destination[0].Y * (source[1].Y - source[2].Y) +
                destination[1].Y * (source[2].Y - source[0].Y) +
                destination[2].Y * (source[0].Y - source[1].Y)) / denominator;
            double m22 = (destination[0].Y * (source[2].X - source[1].X) +
                destination[1].Y * (source[0].X - source[2].X) +
                destination[2].Y * (source[1].X - source[0].X)) / denominator;
            double dy = (destination[0].Y * (source[1].X * source[2].Y - source[2].X * source[1].Y) +
                destination[1].Y * (source[2].X * source[0].Y - source[0].X * source[2].Y) +
                destination[2].Y * (source[0].X * source[1].Y - source[1].X * source[0].Y)) / denominator;
            return new Matrix((float)m11, (float)m12, (float)m21, (float)m22,
                (float)dx, (float)dy);
        }

        public static Vec2[] BuildOriginalTailMeshVertices(SlugcatPose pose)
        {
            if (pose == null) throw new ArgumentNullException("pose");
            if (pose.Tail == null || pose.Tail.Length < 4 ||
                pose.TailRadii == null || pose.TailRadii.Length < 4)
                throw new ArgumentException("The original PlayerGraphics tail requires four segments.", "pose");

            Vec2[] vertices = new Vec2[OriginalTailMeshVertexCount];
            PopulateOriginalTailMeshVertices(pose, vertices);
            return vertices;
        }

        private static void PopulateOriginalTailMeshVertices(SlugcatPose pose, Vec2[] vertices)
        {
            Vec2 previous = (pose.Hips * 3.0 + pose.Chest) / 4.0;
            pose.TailRoot = previous;
            double previousRadius = pose.TailRootRadius;
            for (int i = 0; i < 4; i++)
            {
                Vec2 current = pose.Tail[i];
                Vec2 direction = (current - previous).Normalized;
                Vec2 perpendicular = direction.Perpendicular;
                pose.TailCrossSectionCenters[i] = previous;
                pose.TailTangents[i] = direction;
                pose.TailPerpendiculars[i] = perpendicular;
                double halfAdvance = i == 0 ? 0.0 : Vec2.Distance(current, previous) / 5.0;
                Vec2 previousWidth = perpendicular * previousRadius;
                vertices[i * 4] = previous - previousWidth + direction * halfAdvance;
                vertices[i * 4 + 1] = previous + previousWidth + direction * halfAdvance;
                if (i < 3)
                {
                    Vec2 currentWidth = perpendicular * pose.TailRadii[i];
                    vertices[i * 4 + 2] = current - currentWidth - direction * halfAdvance;
                    vertices[i * 4 + 3] = current + currentWidth - direction * halfAdvance;
                }
                else
                {
                    vertices[14] = current;
                }
                previousRadius = pose.TailRadii[i];
                previous = current;
            }
            pose.TailTip = pose.Tail[3];
            Array.Copy(vertices, pose.TailMeshVertices,
                OriginalTailMeshVertexCount);
        }

        private SolidBrush GetBodyBrush(Color color)
        {
            SolidBrush brush;
            if (bodyBrushes.TryGetValue(color.ToArgb(), out brush)) return brush;
            if (bodyBrushes.Count >= ResourceCacheLimit)
            {
                foreach (KeyValuePair<int, SolidBrush> item in bodyBrushes)
                    item.Value.Dispose();
                bodyBrushes.Clear();
            }
            brush = new SolidBrush(color);
            bodyBrushes[color.ToArgb()] = brush;
            return brush;
        }

        private static void DrawLimbs(ISpriteCanvas graphics, SlugcatPose pose, int layer, Color bodyColor)
        {
            int sideIndex = layer == 0 ? 0 : 1;
            DrawLimb(graphics, pose.Chest, pose.Elbows[sideIndex], pose.Hands[sideIndex], 5.0f, bodyColor);
            DrawLimb(graphics, pose.Hips, pose.Knees[sideIndex], pose.Feet[sideIndex], 5.5f, bodyColor);
        }

        private static void DrawLimb(ISpriteCanvas graphics, Vec2 start, Vec2 joint, Vec2 end, float width, Color bodyColor)
        {
            PointF[] points = { start.ToPointF(), joint.ToPointF(), end.ToPointF() };
            graphics.DrawLines(OutlineColor, width + 4.0f, points);
            graphics.DrawLines(bodyColor, width, points);
            FillCircle(graphics, end, width * 0.65, bodyColor);
        }

        private static void DrawProceduralBody(ISpriteCanvas graphics, SlugcatPose pose)
        {
            float bodyWidth = (float)((18.0 - pose.LandingCompression * 2.5) * pose.VisualBodyScale);
            graphics.DrawLine(OutlineColor, bodyWidth + 6.0f,
                pose.Chest.ToPointF(), pose.Hips.ToPointF());
            graphics.DrawLine(pose.VisualBodyColor, bodyWidth,
                pose.Chest.ToPointF(), pose.Hips.ToPointF());
            FillCircle(graphics, pose.Chest, 10.3, OutlineColor);
            FillCircle(graphics, pose.Hips, 10.0, OutlineColor);
            FillCircle(graphics, pose.Chest, 7.4 * pose.VisualBodyScale, pose.VisualBodyColor);
            FillCircle(graphics, pose.Hips, 7.1 * pose.VisualHipsScale, Shade(pose.VisualHipsColor));
        }

        private void DrawAtlasBody(ISpriteCanvas graphics, SlugcatPose pose)
        {
            double bodyAngle = AimScreen(pose.Hips, pose.Chest);
            double verticality = MathUtil.InverseLerp(0.3, 0.5, Math.Abs(pose.BodyUp.Y));
            double bodyWidth = pose.VisualBodyScale + MathUtil.Lerp(-0.05, 0.05, pose.Breath) * verticality;

            Vec2 bodyPosition = pose.Chest + new Vec2(0.0, -0.5 * pose.Breath * (1.0 - verticality));
            DrawElement(graphics, pose.BodyElement, bodyPosition, bodyAngle, bodyWidth, 1.0,
                0.5, 0.7894737, pose.VisualBodyColor, SelectTorsoSide(pose));
        }

        private void DrawAtlasHips(ISpriteCanvas graphics, SlugcatPose pose)
        {
            double hipsWidth = pose.VisualHipsScale + 0.05 * pose.Breath;
            Vec2 hipsPosition = (pose.Hips * 2.0 + pose.Chest) / 3.0;
            Vec2 tailTarget = pose.Tail.Length > 0 ? pose.Tail[0] : pose.Hips + (pose.Hips - pose.Chest);
            double hipsAngle = AimScreen(pose.Chest, tailTarget);
            DrawElement(graphics, pose.HipsElement, hipsPosition, hipsAngle, hipsWidth, 1.0,
                0.5, 0.5, pose.VisualHipsColor, SelectTorsoSide(pose));
        }

        private void DrawAtlasLegs(ISpriteCanvas graphics, SlugcatPose pose)
        {
            string legsName;
            if (pose.BodyMode == BodyModeIndex.Stand)
                legsName = "LegsA" + PositiveModulo(pose.AnimationFrame, 7);
            else if (pose.BodyMode == BodyModeIndex.Crawl)
                legsName = "LegsACrawling" + PositiveModulo(pose.AnimationFrame / 2, 6);
            else if (pose.BodyMode == BodyModeIndex.WallClimb)
                legsName = "LegsAWall";
            else
                legsName = "LegsAAir0";
            double legsAngle = AimScreen(pose.LegsDirection, Vec2.Zero);
            double legsScaleX = pose.BodyMode == BodyModeIndex.Stand || pose.BodyMode == BodyModeIndex.Crawl
                ? pose.Facing
                : 1.0;
            DrawElement(graphics, legsName, pose.Legs, legsAngle, legsScaleX, 1.0,
                0.5, 0.25, pose.VisualLegsColor,
                legsScaleX < 0.0 ? DmsSpriteSide.Left : DmsSpriteSide.Right);
        }

        private void DrawAtlasArm(ISpriteCanvas graphics, SlugcatPose pose, int index, Color bodyColor)
        {
            Vec2 hand = pose.Hands[index];
            Vec2 shoulder = ComputeArmShoulder(pose, index);
            pose.ArmShoulders[index] = shoulder;
            if (!pose.ArmVisible[index]) return;
            int frame = MathUtil.Clamp((int)Math.Round(Vec2.Distance(hand, shoulder) / 2.0), 0, 12);
            double angle = ComputeArmRotation(pose, index);
            double scaleY = ComputeArmScaleY(pose, index);
            DrawElement(graphics, "PlayerArm" + frame, hand, angle, 1.0, scaleY,
                0.9, 0.5, bodyColor, index == 0 ? DmsSpriteSide.Left : DmsSpriteSide.Right);
        }

        private void DrawExtraGraphics(ISpriteCanvas graphics, SlugcatPose pose,
            ExtraGraphicsLayer layer)
        {
            if (pose.ExtraParts == null) return;
            for (int i = 0; i < pose.ExtraParts.Length; i++)
            {
                ExtraGraphicsPartPose part = pose.ExtraParts[i];
                if (part == null || !part.Visible || part.Layer != layer) continue;
                DrawElement(graphics, part.Element, part.SpritePosition, part.Rotation,
                    part.ScaleX, part.ScaleY, part.AnchorX, part.AnchorY, part.Tint,
                    part.ScaleX < 0.0 ? DmsSpriteSide.Left : DmsSpriteSide.Right);
            }
        }

        private void DrawAtlasHeadPart(ISpriteCanvas graphics, SlugcatPose pose, Color bodyColor, bool faceOnly)
        {
            OriginalFaceState state = ResolveOriginalFaceState(pose);

            if (!faceOnly)
            {
                DrawElement(graphics, state.HeadElement, state.HeadPosition,
                    state.HeadRotation, state.HeadScaleX,
                    1.0, 0.5, 0.5, bodyColor,
                    state.HeadScaleX < 0.0 ? DmsSpriteSide.Left : DmsSpriteSide.Right);
                return;
            }

            pose.HeadElement = state.HeadElement;
            pose.HeadSpritePosition = state.HeadPosition;
            pose.HeadRotation = state.HeadRotation;
            pose.HeadScaleX = state.HeadScaleX;
            pose.SelectedFaceElement = state.FaceElement;
            pose.FacePosition = state.FacePosition;
            pose.FaceRotation = state.FaceRotation;
            pose.FaceScaleX = state.FaceScaleX;
            pose.FaceSelectionReason = state.Reason;
                DrawElement(graphics, state.FaceElement, state.FacePosition,
                    state.FaceRotation, state.FaceScaleX, 1.0, 0.5, 0.5, pose.VisualEyeColor,
                    state.FaceScaleX < 0.0 ? DmsSpriteSide.Left : DmsSpriteSide.Right);
        }

        private void DrawHead(ISpriteCanvas graphics, SlugcatPose pose, bool useAtlas, Color bodyColor)
        {
            double angle = SelectHeadAngle(pose);
            if (useAtlas)
            {
                OriginalFaceState state = ResolveOriginalFaceState(pose);
                DrawElement(graphics, state.HeadElement, state.HeadPosition,
                    state.HeadRotation, state.HeadScaleX,
                    1.0, 0.5, 0.5, bodyColor);
                DrawElement(graphics, state.FaceElement, state.FacePosition,
                    state.FaceRotation, state.FaceScaleX,
                    1.0, 0.5, 0.5, pose.VisualEyeColor);
                return;
            }

            Vec2 right = pose.BodyRight;
            Vec2 up = pose.BodyUp;
            PointF[] leftEar =
            {
                (pose.Head - right * 7.0 + up * 2.0).ToPointF(),
                (pose.Head - right * 10.5 + up * 13.5).ToPointF(),
                (pose.Head - right * 1.8 + up * 8.0).ToPointF()
            };
            PointF[] rightEar =
            {
                (pose.Head + right * 7.0 + up * 2.0).ToPointF(),
                (pose.Head + right * 10.5 + up * 13.5).ToPointF(),
                (pose.Head + right * 1.8 + up * 8.0).ToPointF()
            };
            graphics.FillPolygon(OutlineColor, leftEar);
            graphics.FillPolygon(OutlineColor, rightEar);
            FillCircle(graphics, pose.Head, 11.8, OutlineColor);
            graphics.FillPolygon(bodyColor, leftEar);
            graphics.FillPolygon(bodyColor, rightEar);
            FillCircle(graphics, pose.Head, 8.9, bodyColor);

            Vec2 eyeCenter = pose.Head + pose.LookDirection * 1.8 + up * 0.5;
            FillCircle(graphics, eyeCenter - right * 3.2, 1.15, pose.VisualEyeColor);
            FillCircle(graphics, eyeCenter + right * 3.2, 1.15, pose.VisualEyeColor);
        }

        public static int SelectFaceFrame(SlugcatPose pose)
        {
            if (pose.Animation == AnimationIndex.Sleep) return 1;
            if (pose.BodyMode == BodyModeIndex.Crawl ||
                (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0)) return 4;

            Vec2 lookOffset = pose.LookDirection * 3.0;
            Vec2 faceAxis = pose.Head - pose.Hips;
            faceAxis.X *= 1.0 - MathUtil.Clamp(lookOffset.Length / 3.0, 0.0, 1.0);
            faceAxis = faceAxis.Normalized;
            return MathUtil.Clamp(
                (int)Math.Round(Math.Abs(AimScreen(Vec2.Zero, faceAxis) / 22.5)), 0, 8);
        }

        public static double SelectHeadAngle(SlugcatPose pose)
        {
            if (pose.Animation == AnimationIndex.Sleep) return 45.0 * pose.Facing;
            Vec2 bodyMiddle = (pose.Chest + pose.Hips) * 0.5;
            return AimScreen(bodyMiddle, pose.Head);
        }

        public static double SelectFaceScaleX(SlugcatPose pose)
        {
            double headAngle = SelectHeadAngle(pose);
            double headFacing = headAngle < 0.0 ? -1.0 : 1.0;
            if (pose.BodyMode == BodyModeIndex.Crawl)
            {
                double bodyDirectionX = pose.Chest.X - pose.Hips.X;
                return Math.Abs(bodyDirectionX) > 0.5
                    ? (bodyDirectionX < 0.0 ? -1.0 : 1.0)
                    : (pose.Facing < 0 ? -1.0 : 1.0);
            }
            if (pose.Animation == AnimationIndex.Sleep)
                return BodyAxisSign(pose);
            if (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0)
                return headFacing;
            Vec2 look = pose.LookDirection * 3.0;
            return Math.Abs(look.X) < 0.1 ? headFacing : (look.X < 0.0 ? -1.0 : 1.0);
        }

        public static OriginalFaceState ResolveOriginalFaceState(SlugcatPose pose)
        {
            if (pose == null) throw new ArgumentNullException("pose");
            OriginalFaceState result = new OriginalFaceState();
            double rawHeadAngle = AimScreen((pose.Chest + pose.Hips) * 0.5, pose.Head);
            int headFrame = MathUtil.Clamp(
                (int)Math.Round(Math.Abs(rawHeadAngle / 360.0 * 34.0)), 0, 17);
            double headScaleX = rawHeadAngle < 0.0 ? -1.0 : 1.0;
            Vec2 headPosition = pose.Head;
            Vec2 faceLook = pose.LookDirection * 3.0;
            int faceFrame;
            double faceRotation = 0.0;
            double faceScaleX;
            string faceElement;
            string reason;

            if (!pose.Conscious)
            {
                faceLook = Vec2.Zero;
                headFrame = 0;
                faceElement = pose.Dead ? "FaceDead" : "FaceStunned";
                faceRotation = rawHeadAngle;
                faceScaleX = headScaleX;
                reason = pose.Dead ? "Dead" : "Stunned";
            }
            else if (pose.Animation == AnimationIndex.Sleep)
            {
                double bodyAxis = BodyAxisSign(pose);
                headFrame = 4;
                rawHeadAngle = 45.0 * bodyAxis;
                headScaleX = rawHeadAngle < 0.0 ? -1.0 : 1.0;
                headPosition += new Vec2(bodyAxis * 2.0, -1.0);
                faceFrame = 1;
                faceElement = FaceFamily(pose) + faceFrame;
                faceScaleX = bodyAxis;
                faceLook = new Vec2(-4.0 * bodyAxis, 2.0);
                reason = "Sleep";
            }
            else if (pose.BodyMode == BodyModeIndex.ZeroG)
            {
                headFrame = 0;
                faceElement = FaceFamily(pose) + "0";
                faceScaleX = SelectDefaultFaceScaleX(pose, rawHeadAngle);
                faceRotation = rawHeadAngle;
                reason = "ZeroG";
            }
            else if (pose.BodyMode == BodyModeIndex.Crawl ||
                     (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0))
            {
                bool crawl = pose.BodyMode == BodyModeIndex.Crawl;
                headFrame = crawl ? 7 : 6;
                faceFrame = 4;
                faceElement = FaceFamily(pose) + faceFrame;
                faceLook.X = 0.0;
                faceScaleX = crawl ? BodyAxisSign(pose) : headScaleX;
                reason = crawl ? "Crawl" : "StandMovement";
            }
            else
            {
                faceFrame = SelectFaceFrame(pose);
                faceElement = FaceFamily(pose) + faceFrame;
                faceScaleX = SelectDefaultFaceScaleX(pose, rawHeadAngle);
                if (pose.IsAirborne)
                    reason = pose.IsRising ? "AirborneRising" : "AirborneFalling";
                else if (pose.BodyMode == BodyModeIndex.WallClimb)
                    reason = "WallClimb";
                else if (pose.BodyMode == BodyModeIndex.ClimbingOnBeam)
                    reason = "Beam";
                else if (pose.Animation == AnimationIndex.LedgeCrawl)
                    reason = "Ledge";
                else
                    reason = "Original";
            }

            if (pose.MouseAttentionActive) reason += "+MouseAttention";
            SlugcatGraphicsProfile profile = ResolvePoseProfile(pose);
            result.HeadElement = profile.HeadFamily + headFrame;
            result.HeadPosition = headPosition;
            result.HeadRotation = rawHeadAngle;
            result.HeadScaleX = headScaleX * pose.VisualHeadScale;
            result.FaceElement = faceElement;
            result.FacePosition = headPosition + faceLook + new Vec2(0.0, 2.0);
            result.FaceRotation = faceRotation;
            result.FaceScaleX = faceScaleX;
            result.Reason = reason;
            return result;
        }

        private static double SelectDefaultFaceScaleX(SlugcatPose pose, double headAngle)
        {
            Vec2 look = pose.LookDirection * 3.0;
            if (Math.Abs(look.X) < 0.1) return headAngle < 0.0 ? -1.0 : 1.0;
            return look.X < 0.0 ? -1.0 : 1.0;
        }

        private static string FaceFamily(SlugcatPose pose)
        {
            return ResolvePoseProfile(pose).ResolveFaceFamily(
                pose.Blink, SelectFaceScaleX(pose));
        }

        private static SlugcatGraphicsProfile ResolvePoseProfile(SlugcatPose pose)
        {
            if (pose.CurrentSkin != SlugcatSkin.Default)
                return SlugcatVisualProfiles.Get(pose.CurrentSkin);
            return SlugcatGraphicsProfiles.Get(pose.SelectedSlugcat);
        }

        private static double BodyAxisSign(SlugcatPose pose)
        {
            double bodyDirectionX = pose.Chest.X - pose.Hips.X;
            if (Math.Abs(bodyDirectionX) > 0.5)
                return bodyDirectionX < 0.0 ? -1.0 : 1.0;
            return pose.Facing < 0 ? -1.0 : 1.0;
        }

        public static Vec2 ComputeArmShoulder(SlugcatPose pose, int index)
        {
            double bodyAngle = AimScreen(pose.Hips, pose.Chest);
            double shoulderSpread = 4.5 / (pose.ArmRetractCounters[index] + 1.0);
            shoulderSpread *= Math.Abs(Math.Cos(bodyAngle / 360.0 * Math.PI * 2.0));
            shoulderSpread *= pose.ArmShoulderScale;
            Vec2 shoulderOffset = new Vec2((-1.0 + 2.0 * index) * shoulderSpread, 3.5);
            return pose.Chest + RotateScreen(shoulderOffset, bodyAngle);
        }

        // PlayerGraphics.DrawSprites recomputes this directly from the
        // interpolated hand and shoulder every draw. There is intentionally no
        // retained angle, wrap interpolation, clamp or stabilizer.
        public static double ComputeArmRotation(SlugcatPose pose, int index)
        {
            return AimScreen(pose.Hands[index], ComputeArmShoulder(pose, index)) + 90.0;
        }

        public static double ComputeArmScaleY(SlugcatPose pose, int index)
        {
            if (pose.BodyMode == BodyModeIndex.Crawl)
                return pose.Chest.X < pose.Hips.X ? -1.0 : 1.0;
            if (pose.BodyMode == BodyModeIndex.WallClimb)
                return pose.Facing == -1 ? -1.0 : 1.0;
            // Custom.DistanceToLine is evaluated in Futile's y-up space.
            // Reflecting it into this renderer's y-down space reverses the
            // signed distance. The arm, hand target and spear keep one shared
            // coordinate conversion instead of independently flipping sprites.
            return SignedDistanceToLine(pose.Hands[index], pose.Chest, pose.Hips) < 0.0
                ? 1.0
                : -1.0;
        }

        private void DrawElement(ISpriteCanvas graphics, string name, Vec2 position, double angle, double scaleX, double scaleY, double anchorX, double anchorY, Color tint)
        {
            DrawElement(graphics, name, position, angle, scaleX, scaleY, anchorX,
                anchorY, tint, DmsSpriteSide.None);
        }

        private void DrawElement(ISpriteCanvas graphics, string name, Vec2 position,
            double angle, double scaleX, double scaleY, double anchorX, double anchorY,
            Color tint, DmsSpriteSide side)
        {
            // Futile accepts zero-scale sprites (for example the outside row of
            // Spearmaster's tinyStar speckles); GDI+ rejects a singular matrix.
            if (Math.Abs(scaleX) < 0.000001 || Math.Abs(scaleY) < 0.000001) return;
            AtlasSprite sprite = null;
            DmsSkinDefinition selectedPartSkin = null;
            if (activePose != null)
            {
                string generic = DmsSpriteGroups.ToGenericElement(name,
                    activePose.OriginalSlugcatId);
                selectedPartSkin = GetDmsPart(DmsSpriteGroups.PartForElement(generic));
            }
            bool dmsApplied = selectedPartSkin != null &&
                selectedPartSkin.TryGetSprite(name, activePose.OriginalSlugcatId, side, out sprite);
            if (!dmsApplied && !atlas.TryGet(name, out sprite)) return;
            if (dmsApplied) tint = selectedPartSkin.ResolveTint(name,
                activePose.OriginalSlugcatId, tint);
            AtlasElement element = sprite.Element;
            graphics.Save();
            try
            {
                graphics.TranslateTransform((float)position.X, (float)position.Y);
                graphics.RotateTransform((float)angle);
                graphics.ScaleTransform((float)scaleX, (float)scaleY);
                RectangleF destination = element.GetLocalRectangle(anchorX, anchorY);
                RectangleF source = new RectangleF(element.Frame.X, element.Frame.Y, element.Frame.Width, element.Frame.Height);
                destinationPoints[0] = new PointF(destination.Left, destination.Top);
                destinationPoints[1] = new PointF(destination.Right, destination.Top);
                destinationPoints[2] = new PointF(destination.Left, destination.Bottom);
                graphics.DrawImage(sprite.Atlas.Image, destinationPoints, source,
                    tint, false);

                if (activePose != null && activeRenderSpace != null)
                {
                    activePose.SpritePlacements.Add(new SpritePlacement
                    {
                        Name = name,
                        PhysicsSource = position,
                        InterpolatedPosition = position,
                        Anchor = new Vec2(anchorX, anchorY),
                        LocalRectangle = destination,
                        OverlayPosition = activeRenderSpace.WorldToOverlay(activePose.ToRenderedWorld(position)),
                        FinalScreenPosition = activePose.ToRenderedWorld(position)
                    });
                }
            }
            finally
            {
                graphics.Restore();
            }
        }

        public void RenderFoods(System.Drawing.Graphics graphics,
            DesktopFoodManager foodManager, RenderSpace renderSpace,
            double characterRenderScale, double interpolation, bool heldLayer)
        {
            gdiCanvas.Begin(graphics);
            try
            {
                RenderFoodsCore(gdiCanvas, foodManager, renderSpace,
                    characterRenderScale, interpolation, heldLayer);
            }
            finally
            {
                gdiCanvas.End();
            }
        }

        internal void RenderFoodsGpu(GpuSpriteCanvas canvas,
            DesktopFoodManager foodManager, RenderSpace renderSpace,
            double characterRenderScale, double interpolation, bool heldLayer)
        {
            if (canvas == null) throw new ArgumentNullException("canvas");
            RenderFoodsCore(canvas, foodManager, renderSpace,
                characterRenderScale, interpolation, heldLayer);
        }

        private void RenderFoodsCore(ISpriteCanvas graphics,
            DesktopFoodManager foodManager, RenderSpace renderSpace,
            double characterRenderScale, double interpolation, bool heldLayer)
        {
            if (foodManager == null || foodManager.Foods.Count == 0) return;
            IList<DesktopFood> foods = foodManager.Foods;
            bool hasFoodInLayer = false;
            for (int i = 0; i < foods.Count; i++)
            {
                DesktopFood candidate = foods[i];
                if (!candidate.IsActive) continue;
                bool held = candidate.State == DesktopFoodState.Held ||
                    candidate.State == DesktopFoodState.Biting ||
                    candidate.State == DesktopFoodState.Dragged;
                if (held != heldLayer) continue;
                hasFoodInLayer = true;
                break;
            }
            // Rendering is called once behind and once in front of the
            // Slugcat. Most frames have food in only one layer, so avoid a
            // Matrix allocation and graphics state change for the empty pass.
            if (!hasFoodInLayer) return;
            graphics.Save();
            try
            {
                graphics.SetTransform((float)characterRenderScale,
                    0.0f, 0.0f, (float)characterRenderScale,
                    (float)-renderSpace.WorldOrigin.X,
                    (float)-renderSpace.WorldOrigin.Y);

                for (int i = 0; i < foods.Count; i++)
                {
                    DesktopFood food = foods[i];
                    if (!food.IsActive) continue;
                    bool held = food.State == DesktopFoodState.Held ||
                        food.State == DesktopFoodState.Biting ||
                        food.State == DesktopFoodState.Dragged;
                    if (held != heldLayer) continue;

                    Vec2 center = food.Chunk.RenderPosition(interpolation);
                    Vec2 direction = MathUtil.SlerpDirection(food.LastRotation,
                        food.Rotation, interpolation);
                    double angle = AimScreen(Vec2.Zero, direction);
                    if (food.Kind == DesktopFoodKind.EggBugEgg)
                    {
                        DrawEggBugEgg(graphics, food, center, direction, angle,
                            interpolation);
                        continue;
                    }
                    AtlasSprite ignored;
                    bool hasFront = atlas != null &&
                        atlas.TryGet(food.FrontElement, out ignored);
                    bool hasBack = atlas != null &&
                        atlas.TryGet(food.BackElement, out ignored);
                    if (hasFront)
                        DrawElement(graphics, food.FrontElement, center, angle,
                            1.0, 1.0, 0.5, 0.5,
                            FoodRenderPalette.DangleFruit.BaseColor);
                    else
                        FillCachedCircle(graphics, center, 8.0,
                            FoodRenderPalette.DangleFruit.BaseColor);
                    if (hasBack)
                        DrawElement(graphics, food.BackElement, center, angle,
                            1.0, 1.0, 0.5, 0.5,
                            FoodRenderPalette.DangleFruit.PrimaryColor);
                    else
                        FillCachedCircle(graphics, center, 6.5,
                            FoodRenderPalette.DangleFruit.PrimaryColor);
                }
            }
            finally
            {
                graphics.Restore();
            }
        }

        private void DrawEggBugEgg(ISpriteCanvas graphics, DesktopFood food,
            Vec2 center, Vec2 direction, double angle, double interpolation)
        {
            const double swellFactor = 1.15;
            center -= direction * (3.0 * swellFactor);
            double scaleX = 0.7 * swellFactor;
            double scaleY = 0.75 * swellFactor;
            AtlasSprite ignored;
            bool hasShell = atlas != null &&
                atlas.TryGet(food.FrontElement, out ignored);
            bool hasColor = atlas != null &&
                atlas.TryGet(food.BackElement, out ignored);
            bool hasEye = atlas != null &&
                atlas.TryGet(food.DetailElement, out ignored);
            FoodLayerPalette palette = FoodRenderPalette.EggBugEgg(food.VisualHue);

            if (food.HasVisibleEggTail)
                DrawEggBugTail(graphics, food, center, direction, swellFactor,
                    interpolation, palette.BaseColor);

            if (hasShell)
                DrawElement(graphics, food.FrontElement, center, angle,
                    scaleX, scaleY, 0.5, 0.3, palette.BaseColor);
            else
                FillCachedCircle(graphics, center, 5.4, palette.BaseColor);
            if (hasColor)
                DrawElement(graphics, food.BackElement, center, angle,
                    scaleX, scaleY, 0.5, 0.3, palette.PrimaryColor);
            else
                FillCachedCircle(graphics, center, 4.1, palette.PrimaryColor);
            if (hasEye)
                DrawElement(graphics, food.DetailElement, center, angle,
                    0.45 * swellFactor, 0.45 * swellFactor, 0.5,
                    food.SpriteFrame == 0 ? 0.7 : 0.4, palette.DetailColor);
            else
                FillCachedCircle(graphics, center - direction * 2.0, 1.8,
                    palette.DetailColor);
        }

        private void DrawEggBugTail(ISpriteCanvas graphics,
            DesktopFood food, Vec2 center, Vec2 direction, double swellFactor,
            double interpolation, Color color)
        {
            if (direction.LengthSquared < 0.000001) direction = Vec2.Down;
            else direction = direction.Normalized;
            eggTailCenters[0] = center + direction * (5.0 * swellFactor);
            for (int i = 0; i < DesktopFood.EggBugEggTailSegmentCount; i++)
                eggTailCenters[i + 1] = food.EggTailPosition(i, interpolation);

            int nodeCount = eggTailCenters.Length;
            for (int node = 0; node < nodeCount; node++)
            {
                Vec2 tangent;
                if (node == 0)
                    tangent = eggTailCenters[1] - eggTailCenters[0];
                else if (node == nodeCount - 1)
                    tangent = eggTailCenters[node] - eggTailCenters[node - 1];
                else
                    tangent = eggTailCenters[node + 1] -
                        eggTailCenters[node - 1];
                if (tangent.LengthSquared < 0.000001) tangent = direction;
                else tangent = tangent.Normalized;

                double progress = node == 0 ? 0.0 : (node - 1) /
                    (double)(DesktopFood.EggBugEggTailSegmentCount - 1);
                double width = MathUtil.Lerp(1.0, 0.5,
                    Math.Pow(progress, 0.25));
                Vec2 perpendicular = tangent.Perpendicular * width;
                eggTailOutline[node] =
                    (eggTailCenters[node] - perpendicular).ToPointF();
                eggTailOutline[eggTailOutline.Length - 1 - node] =
                    (eggTailCenters[node] + perpendicular).ToPointF();
            }
            // One continuous silhouette avoids the antialiased seams produced
            // by filling five independent segment quads.
            graphics.FillPolygon(color, eggTailOutline);
        }

        private void FillCachedCircle(ISpriteCanvas graphics,
            Vec2 center, double radius, Color color)
        {
            graphics.FillEllipse(color,
                (float)(center.X - radius), (float)(center.Y - radius),
                (float)(radius * 2.0), (float)(radius * 2.0));
        }

        private static DmsSpriteSide SelectTorsoSide(SlugcatPose pose)
        {
            if (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0)
                return pose.InputX < 0 ? DmsSpriteSide.Left : DmsSpriteSide.Right;
            double axis = pose.Chest.X - pose.Hips.X;
            if (Math.Abs(axis) > 0.5) return axis < 0.0 ? DmsSpriteSide.Left : DmsSpriteSide.Right;
            return pose.Facing < 0.0 ? DmsSpriteSide.Left : DmsSpriteSide.Right;
        }

        private ImageAttributes GetTintAttributes(Color tint)
        {
            ImageAttributes attributes;
            if (tintAttributes.TryGetValue(tint.ToArgb(), out attributes)) return attributes;

            if (tintAttributes.Count >= ResourceCacheLimit)
            {
                foreach (KeyValuePair<int, ImageAttributes> item in tintAttributes)
                    item.Value.Dispose();
                tintAttributes.Clear();
            }

            attributes = CreateTintAttributes(tint);
            tintAttributes[tint.ToArgb()] = attributes;
            return attributes;
        }

        private static ImageAttributes CreateTintAttributes(Color tint)
        {
            float red = tint.R / 255.0f;
            float green = tint.G / 255.0f;
            float blue = tint.B / 255.0f;
            ColorMatrix matrix = new ColorMatrix(new float[][]
            {
                new float[] { red, 0, 0, 0, 0 },
                new float[] { 0, green, 0, 0, 0 },
                new float[] { 0, 0, blue, 0, 0 },
                new float[] { 0, 0, 0, tint.A / 255.0f, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            return attributes;
        }

        private static Color Shade(Color color)
        {
            return Color.FromArgb(color.A,
                MathUtil.Clamp((int)Math.Round(color.R * 0.9), 0, 255),
                MathUtil.Clamp((int)Math.Round(color.G * 0.93), 0, 255),
                MathUtil.Clamp((int)Math.Round(color.B * 0.96), 0, 255));
        }

        private static double AimScreen(Vec2 from, Vec2 to)
        {
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI + 90.0;
            while (angle > 180.0) angle -= 360.0;
            while (angle < -180.0) angle += 360.0;
            return angle;
        }

        private static Vec2 RotateScreen(Vec2 value, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new Vec2(value.X * cosine - value.Y * sine,
                value.X * sine + value.Y * cosine);
        }

        private static double SignedDistanceToLine(Vec2 point, Vec2 lineA, Vec2 lineB)
        {
            Vec2 axis = lineB - lineA;
            Vec2 relative = point - lineA;
            return axis.X * relative.Y - axis.Y * relative.X;
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static void DrawDebugWorld(System.Drawing.Graphics graphics, SlugcatPose pose, DesktopCollisionWorld world, Slugcat slugcat, DesktopPetAI ai)
        {
            Vec2 renderedChest = pose.ToRenderedWorld(pose.Chest);
            using (Pen surfacePen = new Pen(Color.FromArgb(190, 63, 220, 130), 1.0f))
            using (Pen connectionPen = new Pen(Color.FromArgb(220, 255, 205, 60), 1.4f))
            using (Pen targetPen = new Pen(Color.FromArgb(220, 80, 185, 255), 1.0f))
            using (Pen rawAttentionPen = new Pen(Color.FromArgb(230, 255, 115, 210), 1.0f))
            {
                IList<DesktopSurface> surfaces = world.Surfaces;
                for (int i = 0; i < surfaces.Count; i++)
                {
                    DesktopSurface surface = surfaces[i];
                    Rectangle bounds = surface.Bounds;
                    if (bounds.Right < renderedChest.X - 616.0 || bounds.Left > renderedChest.X + 616.0 ||
                        bounds.Bottom < renderedChest.Y - 418.0 || bounds.Top > renderedChest.Y + 418.0)
                    {
                        continue;
                    }
                    if (surface.IsHorizontal)
                    {
                        graphics.DrawLine(surfacePen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
                    }
                    else
                    {
                        int wallX = (surface.Kind == DesktopSurfaceKind.WindowRightWall ||
                            surface.Kind == DesktopSurfaceKind.MonitorRightBoundary)
                            ? bounds.Right
                            : bounds.Left;
                        graphics.DrawLine(surfacePen, wallX, bounds.Top, wallX, bounds.Bottom);
                    }
                }

                Vec2 renderedHipsBody = pose.ToRenderedWorld(pose.Hips);
                Vec2 renderedHead = pose.ToRenderedWorld(pose.Head);
                Vec2 renderedSmoothedAttention = pose.ToRenderedWorld(ai.Attention.Smoothed);
                Vec2 renderedAttention = pose.ToRenderedWorld(ai.Attention.Target);
                graphics.DrawLine(connectionPen, renderedChest.ToPointF(), renderedHipsBody.ToPointF());
                graphics.DrawLine(targetPen, renderedHead.ToPointF(), renderedSmoothedAttention.ToPointF());
                DrawCross(graphics, targetPen, renderedSmoothedAttention, 4.0);
                graphics.DrawLine(rawAttentionPen, renderedSmoothedAttention.ToPointF(), renderedAttention.ToPointF());
                DrawCross(graphics, rawAttentionPen, renderedAttention, 5.0);
                for (int i = 0; i < 2; i++)
                {
                    Vec2 shoulder = pose.ToRenderedWorld(pose.ArmShoulders[i]);
                    Vec2 hand = pose.ToRenderedWorld(pose.Hands[i]);
                    Vec2 target = pose.ToRenderedWorld(pose.HandTargets[i]);
                    Vec2 connection = pose.ToRenderedWorld(pose.ArmConnections[i]);
                    graphics.DrawLine(connectionPen, shoulder.ToPointF(), hand.ToPointF());
                    graphics.DrawLine(targetPen, hand.ToPointF(), target.ToPointF());
                    DrawCross(graphics, targetPen, target, 3.0);
                    DrawCross(graphics, connectionPen, connection, 2.5);
                }
                Vec2 renderedHips = pose.ToRenderedWorld(pose.Hips);
                Vec2 renderedLegs = pose.ToRenderedWorld(pose.Legs);
                Vec2 renderedLegTarget = pose.ToRenderedWorld(pose.FootTargets[0]);
                graphics.DrawLine(connectionPen, renderedHips.ToPointF(), renderedLegs.ToPointF());
                graphics.DrawLine(targetPen, renderedLegs.ToPointF(), renderedLegTarget.ToPointF());
                DrawCross(graphics, targetPen, renderedLegTarget, 3.0);
                for (int i = 0; i < pose.ExtraParts.Length; i++)
                {
                    ExtraGraphicsPartPose part = pose.ExtraParts[i];
                    if (part == null || !part.Visible ||
                        part.ExtensionName != "AxolotlGills") continue;
                    Vec2 connection = pose.ToRenderedWorld(part.ConnectionPosition);
                    Vec2 control = pose.ToRenderedWorld(part.CurrentPosition);
                    Vec2 target = pose.ToRenderedWorld(part.TargetPosition);
                    graphics.DrawLine(connectionPen, connection.ToPointF(), control.ToPointF());
                    graphics.DrawLine(targetPen, control.ToPointF(), target.ToPointF());
                    DrawCross(graphics, connectionPen, connection, 2.5);
                    DrawCross(graphics, targetPen, target, 2.5);
                }
            }

            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
            {
                BodyChunk chunk = slugcat.BodyChunks[i];
                Vec2 center = pose.ToRenderedWorld(chunk.Position);
                Vec2 velocityEnd = pose.ToRenderedWorld(chunk.Position + chunk.Velocity * 3.0);
                double radius = DesktopWorldTransform.ToDesktopLength(chunk.Radius);
                using (Pen pen = new Pen(Color.FromArgb(230, 255, 90, 90), 1.0f))
                {
                    graphics.DrawEllipse(pen, (float)(center.X - radius), (float)(center.Y - radius),
                        (float)(radius * 2.0), (float)(radius * 2.0));
                    graphics.DrawLine(pen, center.ToPointF(), velocityEnd.ToPointF());
                }
            }

            using (Pen controlPen = new Pen(Color.FromArgb(230, 255, 145, 55), 1.0f))
            using (Pen interpolatedPen = new Pen(Color.FromArgb(230, 235, 90, 255), 1.0f))
            using (Pen tangentPen = new Pen(Color.FromArgb(220, 255, 220, 80), 1.0f))
            using (Pen perpendicularPen = new Pen(Color.FromArgb(220, 80, 225, 255), 1.0f))
            using (Pen wirePen = new Pen(Color.FromArgb(125, 255, 255, 255), 0.8f))
            using (Pen leftEdgePen = new Pen(Color.FromArgb(235, 70, 155, 255), 1.4f))
            using (Pen rightEdgePen = new Pen(Color.FromArgb(235, 80, 255, 145), 1.4f))
            {
                for (int i = 0; i < pose.Tail.Length; i++)
                {
                    Vec2 control = pose.ToRenderedWorld(pose.TailCurrent[i]);
                    Vec2 center = pose.ToRenderedWorld(pose.Tail[i]);
                    double radius = DesktopWorldTransform.ToDesktopLength(pose.TailRadii[i]);
                    DrawCross(graphics, controlPen, control, 3.0);
                    graphics.DrawEllipse(interpolatedPen,
                        (float)(center.X - radius), (float)(center.Y - radius),
                        (float)(radius * 2.0), (float)(radius * 2.0));

                    Vec2 section = pose.ToRenderedWorld(pose.TailCrossSectionCenters[i]);
                    Vec2 tangentEnd = pose.ToRenderedWorld(
                        pose.TailCrossSectionCenters[i] + pose.TailTangents[i] * 8.0);
                    double sectionRadius = i == 0 ? pose.TailRootRadius : pose.TailRadii[i - 1];
                    Vec2 perpendicularA = pose.ToRenderedWorld(
                        pose.TailCrossSectionCenters[i] -
                        pose.TailPerpendiculars[i] * sectionRadius);
                    Vec2 perpendicularB = pose.ToRenderedWorld(
                        pose.TailCrossSectionCenters[i] +
                        pose.TailPerpendiculars[i] * sectionRadius);
                    graphics.DrawLine(tangentPen, section.ToPointF(), tangentEnd.ToPointF());
                    graphics.DrawLine(perpendicularPen,
                        perpendicularA.ToPointF(), perpendicularB.ToPointF());
                }

                if (pose.TailMeshVertices != null &&
                    pose.TailMeshVertices.Length == OriginalTailMeshVertexCount)
                {
                    PointF[] triangle = new PointF[3];
                    for (int i = 0; i < TailTriangles.GetLength(0); i++)
                    {
                        for (int j = 0; j < 3; j++)
                            triangle[j] = pose.ToRenderedWorld(
                                pose.TailMeshVertices[TailTriangles[i, j]]).ToPointF();
                        graphics.DrawPolygon(wirePen, triangle);
                    }

                    PointF[] left = new PointF[TailLeftEdge.Length];
                    PointF[] right = new PointF[TailRightEdge.Length];
                    for (int i = 0; i < TailLeftEdge.Length; i++)
                        left[i] = pose.ToRenderedWorld(
                            pose.TailMeshVertices[TailLeftEdge[i]]).ToPointF();
                    for (int i = 0; i < TailRightEdge.Length; i++)
                        right[i] = pose.ToRenderedWorld(
                            pose.TailMeshVertices[TailRightEdge[i]]).ToPointF();
                    graphics.DrawLines(leftEdgePen, left);
                    graphics.DrawLines(rightEdgePen, right);
                    DrawCross(graphics, leftEdgePen,
                        pose.ToRenderedWorld(pose.TailRoot), 4.5);
                    DrawCross(graphics, rightEdgePen,
                        pose.ToRenderedWorld(pose.TailTip), 4.5);
                }
            }
        }

        private static Pen CreateRoundPen(Color color, float width)
        {
            Pen pen = new Pen(color, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private void DrawAbilityObjects(ISpriteCanvas graphics,
            Slugcat slugcat, SlugcatPose pose, double interpolation)
        {
            SaintAbilityController saint = slugcat.AbilityController as SaintAbilityController;
            bool drawsTongue = saint != null && saint.Mode != SaintTongueMode.Retracted;
            if (!drawsTongue && slugcat.Spears.Count == 0 &&
                slugcat.AbilityEffects.Count == 0)
            {
                return;
            }
            if (drawsTongue)
            {
                Vec2[] currentRope = saint.RopeForRender;
                Vec2[] previousRope = saint.LastRopeForRender;
                Vec2 previous = Vec2.Lerp(previousRope[0], currentRope[0], interpolation);
                if (currentRope.Length > 1)
                    previous += (previous - Vec2.Lerp(previousRope[1],
                        currentRope[1], interpolation)).Normalized;
                double stretch = saint.RopeStretchFactor;
                for (int segment = 1; segment < currentRope.Length; segment++)
                {
                    double fraction = segment / (double)(currentRope.Length - 1);
                    Vec2 next = segment < currentRope.Length - 2
                        ? Vec2.Lerp(previousRope[segment], currentRope[segment], interpolation)
                        : pose.FacePosition;
                    Vec2 perpendicular = (previous - next).Normalized.Perpendicular;
                    double width = 0.2 + 1.6 * MathUtil.Lerp(1.0, stretch,
                        Math.Pow(Math.Sin(fraction * Math.PI), 0.7));
                    Vec2 a = previous - perpendicular * width;
                    Vec2 b = previous + perpendicular * width;
                    Vec2 c = next - perpendicular * width;
                    Vec2 d = next + perpendicular * width;
                    double paletteFraction = Math.Max(0.0,
                        Math.Sin(fraction * Math.PI));
                    Color originalTongue = HslToRgb(
                        MathUtil.Lerp(0.95, 1.0, paletteFraction), 1.0,
                        MathUtil.Lerp(0.75, 0.9, Math.Pow(paletteFraction, 0.15)));
                    Color tongueColor = LerpColor(Color.FromArgb(45, 45, 50),
                        originalTongue, 0.7);
                    abilityQuad[0] = a.ToPointF();
                    abilityQuad[1] = b.ToPointF();
                    abilityQuad[2] = d.ToPointF();
                    abilityQuad[3] = c.ToPointF();
                    graphics.FillPolygon(tongueColor, abilityQuad);
                    previous = next;
                }
            }

            DrawSpears(graphics, slugcat, pose, interpolation, true);

            for (int i = 0; i < slugcat.AbilityEffects.Count; i++)
            {
                AbilityEffect effect = slugcat.AbilityEffects[i];
                Vec2 position = Vec2.Lerp(effect.LastPosition, effect.Position, interpolation);
                double life = MathUtil.Lerp(effect.LastLife, effect.Life, interpolation);
                if (effect.Kind == AbilityEffectKind.ShockWave)
                {
                    continue;
                }
                else if (effect.Kind == AbilityEffectKind.ExplosionLight)
                {
                    // The 160-unit flash is wider than the character surface.
                    // Submit it with smoke to the independently-sized GPU layer.
                    continue;
                }
                else if (effect.Kind == AbilityEffectKind.ExplosionSpikes)
                {
                    continue;
                }
                else if (effect.Kind == AbilityEffectKind.SootMark)
                {
                    FillCircle(graphics, position, effect.Radius,
                        Color.FromArgb(MathUtil.Clamp((int)(105 * life), 0, 105), 18, 18, 18));
                }
                else if (effect.Kind == AbilityEffectKind.Spark ||
                    effect.Kind == AbilityEffectKind.WaterDrip)
                {
                    Color trailColor = effect.Kind == AbilityEffectKind.Spark
                        // Spark.DrawSprites leaves the FSprite alpha at its
                        // default one. Its tail shortens only in the final
                        // tenth of life; it does not fade every frame.
                        ? Color.White
                        : Color.FromArgb(MathUtil.Clamp((int)(210 * life), 0, 255), 220, 225, 235);
                    Vec2 trail = Vec2.Lerp(effect.PreviousPreviousPosition,
                        effect.PreviousPreviousPreviousPosition, interpolation);
                    if (Vec2.Distance(position, trail) < 9.0)
                        trail = position - effect.Velocity.Normalized * 9.0;
                    trail = Vec2.Lerp(position, trail,
                        MathUtil.InverseLerp(0.0, 0.1, life));
                    Vec2 axis = position - trail;
                    if (axis.LengthSquared < 0.000001) axis = Vec2.Down;
                    Vec2 perpendicular = axis.Normalized.Perpendicular * effect.Radius;
                    abilityTriangle[0] = (position + perpendicular).ToPointF();
                    abilityTriangle[1] = (position - perpendicular).ToPointF();
                    abilityTriangle[2] = trail.ToPointF();
                    graphics.FillPolygon(trailColor, abilityTriangle);
                }
                else if (effect.Kind == AbilityEffectKind.Smoke ||
                    effect.Kind == AbilityEffectKind.FlashingSmoke)
                {
                    // Submitted after the CPU sprite surface through the
                    // Direct3D effect path. Do not draw a GDI fallback here;
                    // that would restore the expensive GPU/CPU round trip.
                    continue;
                }
                else if (effect.Kind == AbilityEffectKind.Explosion)
                {
                    continue;
                }
                else
                {
                    double progress = MathUtil.Clamp01(1.0 - life);
                    FillCircle(graphics, position, Math.Max(0.5, effect.Radius * progress),
                        Color.FromArgb(MathUtil.Clamp((int)(160 * life), 0, 255), 255, 255, 255));
                }
            }
        }

        public void CollectGpuSmokeEffects(Slugcat slugcat, SlugcatPose pose,
            RenderSpace renderSpace, DirectCompositionHost.GpuSmokeEffect[] target,
            ref int count)
        {
            if (target == null) throw new ArgumentNullException("target");
            double interpolation = pose.TimeStacker;
            double renderScale = pose.CharacterRenderScale;
            for (int i = 0; i < slugcat.AbilityEffects.Count && count < target.Length; i++)
            {
                AbilityEffect effect = slugcat.AbilityEffects[i];
                double life = MathUtil.Lerp(effect.LastLife, effect.Life, interpolation);
                Vec2 position = Vec2.Lerp(effect.LastPosition, effect.Position,
                    interpolation);
                Vec2 center = position * renderScale - renderSpace.WorldOrigin;
                if (effect.Kind == AbilityEffectKind.ExplosionLight)
                {
                    double rootLife = Math.Sqrt(Math.Max(0.0, life));
                    float size = (float)(rootLife * effect.Radius * 2.0 * renderScale);
                    float lightAlpha = (float)MathUtil.Clamp01(rootLife * effect.Intensity);
                    DirectCompositionHost.GpuSmokeEffect light =
                        new DirectCompositionHost.GpuSmokeEffect();
                    light.CenterX = (float)center.X;
                    light.CenterY = (float)center.Y;
                    light.BackSize = size;
                    light.FrontSize = size;
                    light.BackAlpha = (float)MathUtil.Clamp01(life * effect.Intensity * 0.5);
                    light.FrontRed = light.FrontGreen = light.FrontBlue = 1.0f;
                    light.FrontAlpha = 1.0f - (1.0f - lightAlpha) * (1.0f - lightAlpha);
                    light.Seed = -1.0f;
                    target[count++] = light;
                    continue;
                }
                if (effect.Kind == AbilityEffectKind.ShockWave)
                {
                    double progress = MathUtil.Clamp01(life);
                    DirectCompositionHost.GpuSmokeEffect wave =
                        new DirectCompositionHost.GpuSmokeEffect();
                    wave.CenterX = (float)center.X;
                    wave.CenterY = (float)center.Y;
                    wave.BackSize = (float)(Math.Sqrt(progress) * effect.Radius *
                        2.0 * renderScale);
                    wave.BackRed = (float)Math.Pow(progress, 0.1);
                    wave.BackGreen = (float)MathUtil.Clamp01(effect.Intensity);
                    wave.BackBlue = (float)progress;
                    wave.BackAlpha = 1.0f;
                    wave.Seed = -3.0f;
                    target[count++] = wave;
                    continue;
                }
                if (effect.Kind == AbilityEffectKind.Explosion)
                {
                    double progress = MathUtil.Clamp01(1.0 - life);
                    DirectCompositionHost.GpuSmokeEffect explosion =
                        new DirectCompositionHost.GpuSmokeEffect();
                    explosion.CenterX = (float)center.X;
                    explosion.CenterY = (float)center.Y;
                    explosion.BackSize = (float)(effect.Radius * progress *
                        2.0 * renderScale);
                    explosion.BackRed = explosion.BackGreen = explosion.BackBlue = 1.0f;
                    explosion.BackAlpha = (float)MathUtil.Clamp01(160.0 / 255.0 * life);
                    explosion.Seed = -4.0f;
                    target[count++] = explosion;
                    continue;
                }
                if (effect.Kind == AbilityEffectKind.ExplosionSpikes)
                {
                    double progress = MathUtil.Clamp01(1.0 - life);
                    double radius = effect.Radius * Math.Sin(progress * Math.PI * 0.5);
                    DirectCompositionHost.GpuSmokeEffect spikes =
                        new DirectCompositionHost.GpuSmokeEffect();
                    spikes.CenterX = (float)center.X;
                    spikes.CenterY = (float)center.Y;
                    spikes.BackSize = (float)(Math.Max(30.0, radius) *
                        2.0 * renderScale);
                    spikes.BackRed = spikes.BackGreen = spikes.BackBlue = 1.0f;
                    spikes.BackAlpha = (float)MathUtil.Clamp01(190.0 / 255.0 * life);
                    spikes.Seed = -5.0f;
                    target[count++] = spikes;
                    continue;
                }
                if (effect.Kind != AbilityEffectKind.Smoke &&
                    effect.Kind != AbilityEffectKind.FlashingSmoke) continue;

                double scale = life > 0.5
                    ? MathUtil.Lerp(1.0, 0.5, MathUtil.InverseLerp(0.5, 1.0, life))
                    : Math.Sin(Math.Max(0.0, life) * Math.PI);
                double alpha = Math.Pow(Math.Max(0.0, life), 1.8);
                double baseScale = 11.0 * effect.Radius * Math.Max(0.0, scale);
                if (baseScale <= 0.0001 || alpha <= 0.0001) continue;

                Color paletteBlack = Color.FromArgb(28, 31, 34);
                Color paletteFog = Color.FromArgb(92, 98, 105);
                Color colorA = LerpColor(paletteBlack, paletteFog, 0.1);
                Color colorB = LerpColor(paletteBlack, paletteFog, 0.4);
                Color back = effect.Kind == AbilityEffectKind.FlashingSmoke
                    ? Color.White : LerpColor(colorB, colorA,
                        0.2 + 0.8 * Math.Sqrt(Math.Max(0.0, life)));
                Color front = effect.Kind == AbilityEffectKind.FlashingSmoke
                    ? Color.White : LerpColor(colorB, colorA, life);
                DirectCompositionHost.GpuSmokeEffect command =
                    new DirectCompositionHost.GpuSmokeEffect();
                command.CenterX = (float)center.X;
                command.CenterY = (float)center.Y;
                command.Rotation = (float)MathUtil.Lerp(effect.LastRotation,
                    effect.Rotation, interpolation);
                command.BackSize = (float)(baseScale * 1.1 * 16.0 * renderScale);
                command.FrontSize = (float)(baseScale * 0.9 * 16.0 * renderScale);
                command.BackRed = back.R / 255.0f;
                command.BackGreen = back.G / 255.0f;
                command.BackBlue = back.B / 255.0f;
                command.BackAlpha = (float)(alpha * 0.8);
                command.FrontRed = front.R / 255.0f;
                command.FrontGreen = front.G / 255.0f;
                command.FrontBlue = front.B / 255.0f;
                command.FrontAlpha = (float)(alpha * 0.6);
                command.Seed = (float)(effect.Radius * 0.173 +
                    (effect.Lifetime % 97) * 0.113);
                target[count++] = command;
            }
        }

        public RectangleF CalculateGpuEffectBounds(Slugcat slugcat, SlugcatPose pose)
        {
            double interpolation = pose.TimeStacker;
            double renderScale = pose.CharacterRenderScale;
            bool hasBounds = false;
            double left = 0.0, top = 0.0, right = 0.0, bottom = 0.0;
            for (int i = 0; i < slugcat.AbilityEffects.Count; i++)
            {
                AbilityEffect effect = slugcat.AbilityEffects[i];
                double life = MathUtil.Lerp(effect.LastLife, effect.Life, interpolation);
                double size;
                if (effect.Kind == AbilityEffectKind.ExplosionLight)
                {
                    size = Math.Sqrt(Math.Max(0.0, life)) * effect.Radius *
                        2.0 * renderScale;
                }
                else if (effect.Kind == AbilityEffectKind.ShockWave)
                {
                    size = Math.Sqrt(MathUtil.Clamp01(life)) * effect.Radius *
                        2.0 * renderScale;
                }
                else if (effect.Kind == AbilityEffectKind.Explosion)
                {
                    size = effect.Radius * MathUtil.Clamp01(1.0 - life) *
                        2.0 * renderScale;
                }
                else if (effect.Kind == AbilityEffectKind.ExplosionSpikes)
                {
                    double progress = MathUtil.Clamp01(1.0 - life);
                    double radius = effect.Radius * Math.Sin(progress * Math.PI * 0.5);
                    size = Math.Max(30.0, radius) * 2.0 * renderScale;
                }
                else if (effect.Kind == AbilityEffectKind.Smoke ||
                    effect.Kind == AbilityEffectKind.FlashingSmoke)
                {
                    double scale = life > 0.5
                        ? MathUtil.Lerp(1.0, 0.5,
                            MathUtil.InverseLerp(0.5, 1.0, life))
                        : Math.Sin(Math.Max(0.0, life) * Math.PI);
                    double baseScale = 11.0 * effect.Radius * Math.Max(0.0, scale);
                    size = baseScale * 1.1 * 16.0 * renderScale;
                }
                else continue;
                if (size <= 0.0001) continue;
                Vec2 position = Vec2.Lerp(effect.LastPosition, effect.Position,
                    interpolation) * renderScale;
                double half = size * 0.5 + 2.0;
                if (!hasBounds)
                {
                    left = position.X - half; top = position.Y - half;
                    right = position.X + half; bottom = position.Y + half;
                    hasBounds = true;
                }
                else
                {
                    left = Math.Min(left, position.X - half);
                    top = Math.Min(top, position.Y - half);
                    right = Math.Max(right, position.X + half);
                    bottom = Math.Max(bottom, position.Y + half);
                }
            }
            return hasBounds ? RectangleF.FromLTRB((float)left, (float)top,
                (float)right, (float)bottom) : RectangleF.Empty;
        }

        private void DrawSpears(ISpriteCanvas graphics, Slugcat slugcat,
            SlugcatPose pose, double interpolation, bool inFront)
        {
            for (int i = 0; i < slugcat.Spears.Count; i++)
            {
                DesktopSpear spear = slugcat.Spears[i];
                if (spear.InFrontOfPlayer != inFront) continue;
                double spearOpacity = spear.Opacity;
                if (spearOpacity <= 0.0) continue;
                Vec2 center = spear.Chunk.RenderPosition(interpolation);
                Vec2 direction = MathUtil.SlerpDirection(
                    spear.LastRotation, spear.Rotation, interpolation);
                if (spear.HasUmbilical)
                {
                    Vec2[] current = spear.Umbilical;
                    Vec2[] previousFrame = spear.LastUmbilical;
                    double[] lives = spear.UmbilicalLife;
                    for (int segment = 1; segment < current.Length; segment++)
                    {
                        // Spear.Umbilical fades one short mesh section at a
                        // time after NeedleDisconnect; do not collapse the
                        // entire tether on the impact frame.
                        double life = Math.Min(lives[segment - 1], lives[segment]);
                        double opacity = MathUtil.InverseLerp(0.0, 0.3, life) *
                            spearOpacity;
                        if (opacity <= 0.0) continue;
                        Color color = ResolveOriginalUmbilicalColor(segment,
                            current.Length, life, lives[segment - 1]);
                        color = Color.FromArgb((int)Math.Round(255.0 * opacity), color);
                        Vec2 previous = Vec2.Lerp(previousFrame[segment - 1],
                            current[segment - 1], interpolation);
                        Vec2 next = Vec2.Lerp(previousFrame[segment],
                            current[segment], interpolation);
                        graphics.DrawLine(color, (float)(0.65 * opacity),
                            previous.ToPointF(), next.ToPointF());
                    }
                }

                Color needleColor = spear.NeedleHasConnection
                    ? Color.White
                    : LerpColor(OutlineColor, Color.White, spear.NeedleFadeFraction);
                needleColor = Color.FromArgb((int)Math.Round(
                    needleColor.A * spearOpacity), needleColor);
                string element = "BioSpear" + (spear.NeedleType + 1);
                AtlasSprite atlasSpear;
                if (atlas != null && atlas.TryGet(element, out atlasSpear))
                {
                    double anchorY = spear.Mode == DesktopSpearMode.Thrown ||
                        spear.Mode == DesktopSpearMode.StuckInCreature ? 0.85 : 0.5;
                    DrawElement(graphics, element, center,
                        AimScreen(Vec2.Zero, direction), 1.0, 1.0, 0.5,
                        anchorY, needleColor);
                }
                else
                {
                    graphics.DrawLine(needleColor, 2.0f,
                        (center - direction * 13.0).ToPointF(),
                        (center + direction * 13.0).ToPointF());
                }
            }
        }

        private static void FillCircle(ISpriteCanvas graphics, Vec2 center, double radius, Color color)
        {
            graphics.FillEllipse(color, (float)(center.X - radius),
                (float)(center.Y - radius), (float)(radius * 2.0),
                (float)(radius * 2.0));
        }

        private enum EffectShaderMask
        {
            FlatLight,
            LightSource,
            ShockWave
        }

        private static Bitmap CreateEffectShaderMask(EffectShaderMask kind)
        {
            const int size = 64;
            Bitmap mask = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double nx = (x + 0.5 - size * 0.5) / (size * 0.5);
                    double ny = (y + 0.5 - size * 0.5) / (size * 0.5);
                    double radius = Math.Sqrt(nx * nx + ny * ny);
                    double alpha;
                    if (kind == EffectShaderMask.LightSource)
                        alpha = Math.Pow(Math.Max(0.0, 1.0 - radius), 2.0);
                    else if (kind == EffectShaderMask.FlatLight)
                        alpha = Math.Pow(Math.Max(0.0, 1.0 - radius), 0.65);
                    else if (kind == EffectShaderMask.ShockWave)
                        alpha = Math.Exp(-Math.Pow((radius - 0.76) / 0.055, 2.0));
                    else alpha = 0.0;
                    int a = MathUtil.Clamp((int)Math.Round(alpha * 255.0), 0, 255);
                    mask.SetPixel(x, y, Color.FromArgb(a, 255, 255, 255));
                }
            }
            return mask;
        }

        private void DrawEffectShaderSprite(System.Drawing.Graphics graphics,
            Bitmap mask, Vec2 center, double rotation, double size, Color tint)
        {
            if (mask == null || size <= 0.0001 || tint.A <= 0) return;
            DrawEffectSprite(graphics, mask, center, rotation, size,
                GetEffectTintAttributes(tint));
        }

        private void DrawEffectSprite(System.Drawing.Graphics graphics, Bitmap bitmap,
            Vec2 center, double rotation, double size, ImageAttributes attributes)
        {
            // DrawImage accepts a parallelogram. Supplying the already-rotated
            // world-space points avoids Save/Translate/Rotate/Restore for each
            // smoke mask and light sprite, which was the hot path during rapid
            // Artificer jumps without changing its three original sprites.
            double radians = rotation * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double half = size * 0.5;
            Vec2 topLeft = center + new Vec2(-half * cosine + half * sine,
                -half * sine - half * cosine);
            effectDestinationPoints[0] = topLeft.ToPointF();
            effectDestinationPoints[1] = (topLeft + new Vec2(size * cosine,
                size * sine)).ToPointF();
            effectDestinationPoints[2] = (topLeft + new Vec2(-size * sine,
                size * cosine)).ToPointF();
            graphics.DrawImage(bitmap, effectDestinationPoints,
                new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel,
                attributes, null, 0);
        }

        private ImageAttributes GetEffectTintAttributes(Color tint)
        {
            // The original shader receives continuous values. GDI+ needs a
            // native ImageAttributes object per colour matrix, so retain a
            // visually finer 5-bit approximation while bounding the cache.
            Color quantized = Color.FromArgb(QuantizeEffectChannel(tint.A),
                QuantizeEffectChannel(tint.R), QuantizeEffectChannel(tint.G),
                QuantizeEffectChannel(tint.B));
            int key = quantized.ToArgb();
            ImageAttributes attributes;
            if (effectTintAttributes.TryGetValue(key, out attributes)) return attributes;
            if (effectTintAttributes.Count >= 512)
            {
                foreach (KeyValuePair<int, ImageAttributes> item in effectTintAttributes)
                    item.Value.Dispose();
                effectTintAttributes.Clear();
            }
            attributes = CreateTintAttributes(quantized);
            effectTintAttributes[key] = attributes;
            return attributes;
        }

        private static int QuantizeEffectChannel(int value)
        {
            return MathUtil.Clamp((int)Math.Round(value / 8.0) * 8, 0, 255);
        }

        public static Color ResolveOriginalUmbilicalColor(int segment,
            int segmentCount, double life, double previousLife)
        {
            if (segmentCount < 2) throw new ArgumentOutOfRangeException("segmentCount");
            double fraction = MathUtil.InverseLerp(0.0, segmentCount - 1.0, segment);
            // Spear.Umbilical.DrawSprites: Color.Lerp(fogColor,
            // Color.Lerp(red, threadCol, .1 + .9 * Pow(f, .25 + life)),
            // Min(life, previousLife)). There is no water-shininess term on
            // the desktop, so its source factor remains zero.
            Color threadGradient = LerpColor(Color.FromArgb(255, 255, 0, 0),
                OriginalUmbilicalThread, 0.1 + 0.9 * Math.Pow(fraction,
                    0.25 + Math.Max(0.0, life)));
            return LerpColor(OriginalUmbilicalFog, threadGradient,
                Math.Min(life, previousLife));
        }

        private static Color LerpColor(Color from, Color to, double amount)
        {
            amount = MathUtil.Clamp01(amount);
            return Color.FromArgb(
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.A, to.A, amount)), 0, 255),
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.R, to.R, amount)), 0, 255),
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.G, to.G, amount)), 0, 255),
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.B, to.B, amount)), 0, 255));
        }

        private static Color HslToRgb(double hue, double saturation, double lightness)
        {
            hue -= Math.Floor(hue);
            saturation = MathUtil.Clamp01(saturation);
            lightness = MathUtil.Clamp01(lightness);
            double chroma = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
            double sector = hue * 6.0;
            double secondary = chroma * (1.0 - Math.Abs(sector % 2.0 - 1.0));
            double red = 0.0;
            double green = 0.0;
            double blue = 0.0;
            if (sector < 1.0) { red = chroma; green = secondary; }
            else if (sector < 2.0) { red = secondary; green = chroma; }
            else if (sector < 3.0) { green = chroma; blue = secondary; }
            else if (sector < 4.0) { green = secondary; blue = chroma; }
            else if (sector < 5.0) { red = secondary; blue = chroma; }
            else { red = chroma; blue = secondary; }
            double match = lightness - chroma * 0.5;
            return Color.FromArgb(255,
                MathUtil.Clamp((int)Math.Round((red + match) * 255.0), 0, 255),
                MathUtil.Clamp((int)Math.Round((green + match) * 255.0), 0, 255),
                MathUtil.Clamp((int)Math.Round((blue + match) * 255.0), 0, 255));
        }

        private static void DrawCross(System.Drawing.Graphics graphics, Pen pen, Vec2 point, double radius)
        {
            graphics.DrawLine(pen, (float)(point.X - radius), (float)point.Y, (float)(point.X + radius), (float)point.Y);
            graphics.DrawLine(pen, (float)point.X, (float)(point.Y - radius), (float)point.X, (float)(point.Y + radius));
        }

        public void Dispose()
        {
            tailRasterGraphics.Dispose();
            tailRaster.Dispose();
            flatLightShaderMask.Dispose();
            lightSourceShaderMask.Dispose();
            shockWaveShaderMask.Dispose();
            debugFont.Dispose();
            foreach (KeyValuePair<int, ImageAttributes> item in tintAttributes)
            {
                item.Value.Dispose();
            }
            tintAttributes.Clear();
            foreach (KeyValuePair<int, ImageAttributes> item in effectTintAttributes)
            {
                item.Value.Dispose();
            }
            effectTintAttributes.Clear();
            foreach (KeyValuePair<int, SolidBrush> item in bodyBrushes)
            {
                item.Value.Dispose();
            }
            bodyBrushes.Clear();
            if (atlas != null) atlas.Dispose();
        }
    }
}
