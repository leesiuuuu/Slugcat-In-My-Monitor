using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Core
{
    public sealed class GameLoop : IDisposable
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly FixedTimeStep fixedTimeStep = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
        private readonly MouseTracker mouse = new MouseTracker();
        private readonly MouseAttentionState mouseAttention = new MouseAttentionState();
        private readonly bool managesWorldRefresh;
        private double lastTime;
        private double surfaceRefreshAccumulator;
        private long simulationTick;
        private readonly ParityDiagnostics parityDiagnostics = new ParityDiagnostics();
        private readonly Stopwatch renderMetricClock = Stopwatch.StartNew();
        private int renderFramesInSample;
        private int simulationStepsLastFrame;
        private double renderFramesPerSecond;
        private double monitorRefreshRate;
        private readonly RainWorldAtlasSet atlas;
        private bool disposed;
        private int offscreenTicks;
        private Vec2 lastVisibleCenter;
        private bool hasVisibleCenter;
        private readonly string baseAssetStatus;
        private readonly WorkshopLog workshopLog;
        private WorkshopCatalog workshopCatalog;
        private DmsSkinCatalog dmsSkins;
        private readonly Dictionary<string, string> dmsPartSelections =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatId selectedSlugcat)
            : this(overlayHandle, installation, selectedSlugcat, 0)
        {
        }

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatId selectedSlugcat, int spawnIndex)
            : this(overlayHandle, installation, selectedSlugcat, spawnIndex, null)
        {
        }

        internal GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatId selectedSlugcat, int spawnIndex, DesktopCollisionWorld sharedWorld)
        {
            Installation = installation;
            managesWorldRefresh = sharedWorld == null;
            World = sharedWorld ?? new DesktopCollisionWorld(new WindowEnumerator());
            if (managesWorldRefresh) World.Refresh(overlayHandle);
            Point cursor = System.Windows.Forms.Cursor.Position;
            MonitorInfo monitor = MonitorManager.FindNearest(cursor);
            double spawnMargin = DesktopWorldTransform.ToDesktopLength(70.0);
            double spawnX = MathUtil.Clamp(cursor.X, monitor.WorkArea.Left + spawnMargin,
                monitor.WorkArea.Right - spawnMargin);
            if (spawnIndex > 0)
            {
                int step = (spawnIndex + 1) / 2;
                int direction = spawnIndex % 2 == 1 ? 1 : -1;
                spawnX = MathUtil.Clamp(spawnX + direction * step *
                    DesktopWorldTransform.ToDesktopLength(48.0),
                    monitor.WorkArea.Left + spawnMargin,
                    monitor.WorkArea.Right - spawnMargin);
            }
            Vec2 spawn = DesktopWorldTransform.ToSimulation(new Vec2(spawnX,
                monitor.WorkArea.Bottom - DesktopWorldTransform.ToDesktopLength(
                    SimulationConstants.HipsChunkRadius + 2.0)));
            Slugcat = new Slugcat(spawn, selectedSlugcat);
            lastVisibleCenter = Slugcat.Center;
            hasVisibleCenter = true;
            // SlugNPCAI owns an AbstractCreature personality per NPC.  A
            // process-wide timestamp alone gives simultaneous desktop pets
            // the same random stream, which makes them select the same
            // action on the same tick.  Keep spawnIndex in the seed so every
            // pet receives an independent personality and decision stream.
            int aiSeed = unchecked(Environment.TickCount * 397 ^
                (spawnIndex + 1) * 7919);
            AI = new DesktopPetAI(aiSeed, spawnIndex);
            Foods = new DesktopFoodManager(unchecked(aiSeed ^ 0x45A91));
            AI.Attention.SetTarget(AttentionKind.RandomPoint,
                spawn + new Vec2(Slugcat.State.Facing * 60.0, -20.0));
            RainWorldAssetLoader assetLoader = new RainWorldAssetLoader(installation);
            atlas = assetLoader.TryLoadPlayerAtlas();
            AssetStatus = assetLoader.Status;
            SlugcatGraphicsProfile requested = Slugcat.SelectedSlugcat.Graphics;
            string missing;
            if (!requested.IsAvailable(atlas, out missing))
            {
                AssetStatus += UiLocalization.Text(
                    " 선택한 슬러그캣의 일부 자산이 없어 절차형 외형을 사용합니다: " + missing + ".",
                    " Selected Slugcat uses procedural fallback for missing " + missing + ".");
            }
            Graphics = new SlugcatGraphics(Slugcat, requested, atlas);
            baseAssetStatus = AssetStatus;
            mouse.Sample(SimulationConstants.LogicStepSeconds);
            Renderer = new SpriteRenderer(atlas);
#if DEBUG
            workshopLog = new WorkshopLog(true);
#else
            workshopLog = new WorkshopLog(false);
#endif
            workshopCatalog = new WorkshopCatalog(installation, workshopLog);
            ReloadWorkshopIntegrations(null);
        }

        public readonly DesktopCollisionWorld World;
        public readonly Slugcat Slugcat;
        public readonly DesktopPetAI AI;
        public readonly DesktopFoodManager Foods;
        public readonly SlugcatGraphics Graphics;
        public readonly SpriteRenderer Renderer;
        public readonly RainWorldInstallation Installation;
        public string AssetStatus { get; private set; }
        public bool DebugEnabled { get; set; }
        public bool Paused { get; set; }
        public SlugcatProfile SelectedSlugcat { get { return Slugcat.SelectedSlugcat; } }
        public double Interpolation { get { return fixedTimeStep.Alpha; } }
        public long SimulationTick { get { return simulationTick; } }
        public double RenderFramesPerSecond { get { return renderFramesPerSecond; } }
        public double MonitorRefreshRate { get { return monitorRefreshRate; } }
        public MouseAttentionState MouseAttention { get { return mouseAttention; } }
        public SlugcatAppearance Appearance { get { return Slugcat.Appearance; } }
        public SlugcatSkin Skin { get { return Graphics.VisualProfile.Skin; } }
        public int OffscreenRecoveryCount { get; private set; }

        public bool TryGetAtlasSprite(string name, bool original, out AtlasSprite sprite)
        {
            sprite = null;
            if (atlas == null) return false;
            return original ? atlas.TryGetBase(name, out sprite) : atlas.TryGet(name, out sprite);
        }

        public bool SetPartAtlas(string part, string imagePath, string metadataPath, out string reason)
        {
            reason = null;
            if (atlas == null)
            {
                reason = UiLocalization.Text("Rain World 원본 atlas를 사용할 수 없습니다.",
                    "The original Rain World atlas is unavailable.");
                return false;
            }
            try
            {
                RainWorldAtlas replacement = RainWorldAtlasLoader.Load(imagePath, metadataPath);
                atlas.SetPartOverride(part, replacement);
                Renderer.InvalidateAtlasAvailability();
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        public void ClearPartAtlas(string part)
        {
            if (atlas != null)
            {
                atlas.ClearPartOverride(part);
                Renderer.InvalidateAtlasAvailability();
            }
        }

        public Color GetPartColor(string part) { return Graphics.GetPartColor(part); }
        public void SetPartColor(string part, Color color) { Graphics.SetPartColor(part, color); }
        public void ClearPartColors() { Graphics.ClearPartColors(); }
        public WorkshopCatalog WorkshopCatalog { get { return workshopCatalog; } }
        public IList<DmsSkinDefinition> DmsSkins
        {
            get { return dmsSkins == null ? new DmsSkinDefinition[0] : dmsSkins.Skins; }
        }

        public bool TryGetDmsPartPreview(string part, out AtlasSprite sprite)
        {
            sprite = null;
            string element = DmsSpriteGroups.PreviewElement(part);
            if (string.IsNullOrEmpty(element)) return false;
            DmsSkinDefinition skin = Renderer.GetDmsPart(part);
            if (skin != null && skin.TryGetSprite(element, CurrentSlugcatId(),
                DmsSpriteSide.None, out sprite)) return true;
            return atlas != null && atlas.TryGet(element, out sprite);
        }
        public DmsSkinDefinition ActiveDmsSkin { get { return Renderer.ActiveDmsSkin; } }

        public void RecordRenderFrame(double displayRefreshRate)
        {
            monitorRefreshRate = displayRefreshRate;
            renderFramesInSample++;
            double seconds = renderMetricClock.Elapsed.TotalSeconds;
            if (seconds < 0.5) return;
            renderFramesPerSecond = renderFramesInSample / seconds;
            renderFramesInSample = 0;
            renderMetricClock.Restart();
        }

        public void Advance(IntPtr overlayHandle)
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = lastTime <= 0.0 ? SimulationConstants.LogicStepSeconds : now - lastTime;
            lastTime = now;
            mouse.Sample(elapsed);
            Foods.MoveDraggedFood(mouse.Position);
            if (Paused)
            {
                mouse.ConsumeClick();
                fixedTimeStep.Reset();
                return;
            }

            if (managesWorldRefresh)
            {
                if (World.TryApplyPendingRefresh()) ApplyMovingSurfaceDelta();
                surfaceRefreshAccumulator += elapsed;
                if (surfaceRefreshAccumulator >= SimulationConstants.WindowRefreshSeconds)
                {
                    surfaceRefreshAccumulator %= SimulationConstants.WindowRefreshSeconds;
                    World.RequestRefresh(overlayHandle);
                }
            }

            fixedTimeStep.AddElapsed(elapsed);
            int steps = 0;
            while (steps < 3 && fixedTimeStep.ConsumeStep())
            {
                Foods.StepPhysics(World);
                if (!Slugcat.State.Conscious || Slugcat.State.Dead ||
                    Slugcat.State.StunCounter > 0)
                {
                    mouse.ConsumeClick();
                    mouseAttention.Suppress(now, mouse.Position, Graphics.Head.Position);
                }
                else
                {
                    mouseAttention.Update(now, mouse.Position, mouse.ConsumeClick(), Graphics.Head.Position);
                }
                VirtualInput input = Slugcat.IsGrabbed
                    ? VirtualInput.Neutral
                    : AI.Step(Slugcat, World, mouse, mouseAttention);
                VirtualInput foodInput;
                if (!Slugcat.IsGrabbed && Foods.TryProduceInput(Slugcat, Graphics,
                    AI.Attention, out foodInput)) input = foodInput;
                Slugcat.Step(input, World, mouse.Position, mouse.Velocity);
                RecoverFromDesktopEscape();
                if (!Slugcat.State.Conscious || Slugcat.State.Dead ||
                    Slugcat.State.StunCounter > 0)
                    mouseAttention.Suppress(now, mouse.Position, Graphics.Head.Position);
                if (DebugEnabled)
                    parityDiagnostics.ObserveSurfaceState(Slugcat, World, input, simulationTick);
                Graphics.Step(AI.Attention, AI.OriginalAttentionTarget,
                    AI.MouseAttentionActive && Slugcat.State.Conscious &&
                        !Slugcat.State.Dead && Slugcat.State.StunCounter < 1,
                    World);
                Foods.StepInteraction(Slugcat, Graphics);
                simulationTick++;
                steps++;
            }
            // MainLoopProcess.RawUpdate zeroes myTimeStacker after the third
            // catch-up Update, preventing a stalled desktop from spiralling.
            if (steps == 3) fixedTimeStep.Reset();
            simulationStepsLastFrame = steps;
        }

        public void ApplyMovingSurfaceDelta()
        {
            if (Paused) return;
            Vec2 surfaceDelta = Slugcat.ApplyMovingSurfaceDelta(World);
            Graphics.ApplyMovingSurfaceDelta(surfaceDelta);
            Foods.ApplyMovingSurfaceDelta(World);
        }

        public bool FeedDangleFruit()
        {
            return Foods.TrySpawnDangleFruit(Slugcat, World);
        }

        public bool FeedEggBugEgg()
        {
            return Foods.TrySpawnEggBugEgg(Slugcat, World);
        }

        public void ClearFoods()
        {
            Foods.Clear();
        }

        public SlugcatPose BuildPose()
        {
            SlugcatPose pose = Graphics.BuildPose(Interpolation, AI.Attention,
                simulationTick, DebugEnabled);
            pose.LogicTicksPerSecond = SimulationConstants.LogicTicksPerSecond;
            pose.LogicStepSeconds = fixedTimeStep.StepSeconds;
            pose.AccumulatorSeconds = fixedTimeStep.AccumulatorSeconds;
            pose.SimulationTimeSeconds = simulationTick * SimulationConstants.LogicStepSeconds;
            pose.SimulationStepsLastFrame = simulationStepsLastFrame;
            pose.RenderFramesPerSecond = renderFramesPerSecond;
            pose.MonitorRefreshRate = monitorRefreshRate;
            pose.MousePosition = mouseAttention.MousePosition;
            pose.MouseDistanceToHead = mouseAttention.DistanceToHead;
            pose.MouseAttentionRadius = mouseAttention.Radius;
            pose.LastRelevantMouseClickTime = mouseAttention.LastRelevantClickTime;
            pose.TimeSinceRelevantMouseClick = mouseAttention.TimeSinceRelevantClick;
            pose.MouseAttentionTimeout = mouseAttention.TimeoutSeconds;
            pose.MouseAttentionActive = mouseAttention.IsActive;
            MonitorInfo currentMonitor = World.FindMonitor(Slugcat.Center);
            pose.CurrentMonitorName = currentMonitor.Name;
            pose.CurrentMonitorId = currentMonitor.TerrainId;
            pose.CurrentMonitorBounds = currentMonitor.Bounds;
            pose.CurrentMonitorWorkArea = currentMonitor.WorkArea;
            pose.CurrentTaskbarBounds = currentMonitor.TaskbarBounds;
            pose.CurrentTaskbarEdge = currentMonitor.TaskbarEdge;
            pose.CurrentMonitorFloorY = DesktopWorldTransform.ToSimulationLength(
                currentMonitor.FloorY);

            BodyChunk surfaceChunk = Slugcat.BodyChunks[1].SupportingSurfaceId != 0
                ? Slugcat.BodyChunks[1]
                : Slugcat.BodyChunks[0];
            if (surfaceChunk.SupportingSurfaceId != 0)
            {
                pose.CurrentSurfaceId = surfaceChunk.SupportingSurfaceId;
                pose.CurrentSurfaceKind = surfaceChunk.SupportingSurfaceKind;
            }
            else
            {
                surfaceChunk = Slugcat.BodyChunks[0].WallSurfaceId != 0
                    ? Slugcat.BodyChunks[0]
                    : Slugcat.BodyChunks[1];
                pose.CurrentSurfaceId = surfaceChunk.WallSurfaceId;
                pose.CurrentSurfaceKind = surfaceChunk.WallSurfaceKind;
            }
            DesktopSurface currentSurface;
            if (pose.CurrentSurfaceId != 0 && World.TryGetSurface(
                pose.CurrentSurfaceId, pose.CurrentSurfaceKind, out currentSurface))
            {
                pose.CurrentSurfaceLeft = currentSurface.Left;
                pose.CurrentSurfaceRight = currentSurface.Right;
                pose.CurrentSurfaceTop = currentSurface.Top;
            }
            else
            {
                pose.CurrentSurfaceId = 0;
                pose.CurrentSurfaceKind = DesktopSurfaceKind.ScreenEdge;
                pose.CurrentSurfaceLeft = 0.0;
                pose.CurrentSurfaceRight = 0.0;
                pose.CurrentSurfaceTop = 0.0;
            }
            if (DebugEnabled) parityDiagnostics.Observe(pose);
            return pose;
        }

        public bool HitTest(Vec2 screenPoint)
        {
            Vec2 simulationPoint = DesktopWorldTransform.ToSimulation(screenPoint);
            return Foods.HitTest(simulationPoint) ||
                Slugcat.HitTest(simulationPoint) ||
                Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0;
        }

        public bool BeginGrab(Vec2 screenPoint)
        {
            Vec2 simulationPoint = DesktopWorldTransform.ToSimulation(screenPoint);
            if (Foods.TryBeginDrag(simulationPoint)) return true;
            if (Slugcat.Grab(simulationPoint)) return true;
            if (Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0)
            {
                return Slugcat.Grab(Slugcat.BodyChunks[0].Position);
            }
            return false;
        }

        public void EndGrab()
        {
            if (Foods.EndDrag(Vec2.ClampMagnitude(mouse.Velocity /
                SimulationConstants.LogicTicksPerSecond, 25.0))) return;
            Slugcat.Release(mouse.Velocity);
        }

        private void RecoverFromDesktopEscape()
        {
            if (Slugcat.IsGrabbed)
            {
                offscreenTicks = 0;
                return;
            }

            IList<MonitorInfo> monitors = World.CurrentSnapshot.Monitors;
            if (DesktopRecovery.IsNearAnyMonitor(Slugcat.Center, monitors))
            {
                lastVisibleCenter = Slugcat.Center;
                hasVisibleCenter = true;
                offscreenTicks = 0;
                return;
            }
            if (DesktopRecovery.IsAboveMonitorCeiling(Slugcat.Center, monitors))
            {
                offscreenTicks = 0;
                return;
            }

            offscreenTicks++;
            bool hardEscape = DesktopRecovery.IsFarOutsideVirtualDesktop(
                Slugcat.Center, World.VirtualBounds);
            if (!hardEscape && offscreenTicks < DesktopRecovery.OffscreenGraceTicks) return;

            Vec2 preferred = hasVisibleCenter ? lastVisibleCenter : Slugcat.Center;
            Vec2 safeHips = DesktopRecovery.FindSafeHipsPosition(preferred, monitors,
                SimulationConstants.HipsChunkRadius);
            Vec2 delta = safeHips - Slugcat.BodyChunks[1].Position;
            Slugcat.Reposition(safeHips);
            Graphics.ApplyMovingSurfaceDelta(delta);
            AI.Attention.SetTarget(AttentionKind.RandomPoint,
                Slugcat.Center + new Vec2(Slugcat.State.Facing * 60.0, -20.0));
            lastVisibleCenter = Slugcat.Center;
            hasVisibleCenter = true;
            offscreenTicks = 0;
            OffscreenRecoveryCount++;
        }

        public void SetSelectedSlugcat(SlugcatId id)
        {
            SlugcatProfile next = SlugcatProfiles.Get(id);
            // No frame can observe mixed physics/graphics: switch the model,
            // clear incompatible ability state, then rebuild graphics before
            // the next fixed update or render.
            Slugcat.SetSelectedSlugcat(id);
            Graphics.SetGraphicsProfile(next.Graphics, atlas);
        }

        public void SetVariant(SlugcatVariant variant)
        {
            SetSelectedSlugcat(SlugcatProfiles.Get(variant).Id);
        }

        public bool CanUseSkin(SlugcatSkin skin, out string reason)
        {
            string missing;
            bool available = SlugcatVisualProfiles.Get(skin).IsAvailable(atlas, out missing);
            reason = available ? null : UiLocalization.Text(
                "로컬 Downpour 자산이 없습니다: ", "Missing local Downpour asset: ") + missing;
            return available;
        }

        public bool SetSkin(SlugcatSkin skin)
        {
            string reason;
            if (!CanUseSkin(skin, out reason)) return false;
            switch (skin)
            {
                case SlugcatSkin.Artificer: SetSelectedSlugcat(SlugcatId.Artificer); break;
                case SlugcatSkin.Spearmaster: SetSelectedSlugcat(SlugcatId.SpearMaster); break;
                case SlugcatSkin.Rivulet: SetSelectedSlugcat(SlugcatId.Rivulet); break;
                case SlugcatSkin.Saint: SetSelectedSlugcat(SlugcatId.Saint); break;
                default:
                    switch (Slugcat.Appearance.Variant)
                    {
                        case SlugcatVariant.Monk: SetSelectedSlugcat(SlugcatId.Yellow); break;
                        case SlugcatVariant.Hunter: SetSelectedSlugcat(SlugcatId.Red); break;
                        case SlugcatVariant.Gourmand: SetSelectedSlugcat(SlugcatId.Gourmand); break;
                        default: SetSelectedSlugcat(SlugcatId.White); break;
                    }
                    break;
            }
            return true;
        }

        public string GetDmsPartSelection(string part)
        {
            string id;
            return !string.IsNullOrWhiteSpace(part) &&
                dmsPartSelections.TryGetValue(part, out id) ? id : null;
        }

        public bool SetDmsPart(string part, string id, out string reason)
        {
            if (string.IsNullOrWhiteSpace(part) ||
                !DmsSpriteGroups.Required.ContainsKey(part))
            {
                reason = UiLocalization.Text("알 수 없는 DMS 파츠: ", "Unknown DMS part: ") +
                    (part ?? "<null>");
                return false;
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                dmsPartSelections.Remove(part);
                Renderer.SetDmsPart(part, null);
                reason = null;
                return true;
            }
            DmsSkinDefinition skin = dmsSkins == null ? null : dmsSkins.Find(id);
            if (skin == null)
            {
                reason = UiLocalization.Text("DMS 스프라이트 시트가 더 이상 설치되어 있지 않습니다: ",
                    "DMS spritesheet is no longer installed: ") + id;
                return false;
            }
            if (!skin.IsModActive)
            {
                reason = UiLocalization.Text(
                    "원본 모드가 설치되어 있지만 Rain World Remix에서 비활성화되어 있습니다: ",
                    "The source mod is installed but disabled in Rain World Remix: ") + skin.ModName;
                return false;
            }
            if (!skin.HasPart(part))
            {
                reason = UiLocalization.Text(skin.Name + "에 완전한 " + part + " 스프라이트 그룹이 없습니다.",
                    skin.Name + " does not provide a complete " + part + " sprite group.");
                return false;
            }
            dmsPartSelections[part] = skin.Id;
            Renderer.SetDmsPart(part, skin);
            reason = null;
            return true;
        }

        public void ClearDmsParts()
        {
            dmsPartSelections.Clear();
            Renderer.ClearDmsParts();
        }

        // Command-line compatibility: it atomically replaces every selection,
        // rather than layering a whole skin over retained editor overrides.
        public bool SetDmsSkin(string id, out string reason)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                ClearDmsParts();
                reason = null;
                return true;
            }
            DmsSkinDefinition skin = dmsSkins == null ? null : dmsSkins.Find(id);
            if (skin == null)
            {
                reason = UiLocalization.Text("DMS 스프라이트 시트가 더 이상 설치되어 있지 않습니다: ",
                    "DMS spritesheet is no longer installed: ") + id;
                return false;
            }
            if (!skin.IsModActive)
            {
                reason = UiLocalization.Text(
                    "원본 모드가 설치되어 있지만 Rain World Remix에서 비활성화되어 있습니다: ",
                    "The source mod is installed but disabled in Rain World Remix: ") + skin.ModName;
                return false;
            }
            ClearDmsParts();
            foreach (string part in skin.AvailableParts)
            {
                dmsPartSelections[part] = skin.Id;
                Renderer.SetDmsPart(part, skin);
            }
            reason = null;
            return true;
        }

        public void RefreshWorkshopIntegration()
        {
            workshopCatalog.Refresh();
            ReloadWorkshopIntegrations(null);
        }

        private void ReloadWorkshopIntegrations(string selectedDmsId)
        {
            Renderer.ClearDmsParts();
            if (dmsSkins != null) dmsSkins.Dispose();
            dmsSkins = new DmsSkinCatalog(workshopCatalog, workshopLog);
            // Re-resolve each explicit part against the new catalog. Missing,
            // disabled, or now-incomplete sheets become Vanilla; no stale atlas
            // reference survives disposal of the old catalog.
            string[] selectedParts = new string[dmsPartSelections.Count];
            dmsPartSelections.Keys.CopyTo(selectedParts, 0);
            for (int i = 0; i < selectedParts.Length; i++)
            {
                string selectedId = dmsPartSelections[selectedParts[i]];
                string ignored;
                if (!SetDmsPart(selectedParts[i], selectedId, out ignored))
                    dmsPartSelections.Remove(selectedParts[i]);
            }
            if (!string.IsNullOrWhiteSpace(selectedDmsId))
            {
                string ignored;
                SetDmsSkin(selectedDmsId, out ignored);
            }
            AssetStatus = baseAssetStatus + UiLocalization.Text(
                " Workshop: 모드 " + workshopCatalog.Mods.Count + "개, DMS 시트 " +
                    DmsSkins.Count + "개.",
                " Workshop: " + workshopCatalog.Mods.Count + " mods, " +
                    DmsSkins.Count + " DMS sheets.");
        }

        private string CurrentSlugcatId()
        {
            return Graphics.VisualProfile.ResolveOriginalSlugcatId(Slugcat.Appearance);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (dmsSkins != null) dmsSkins.Dispose();
            if (workshopCatalog != null) workshopCatalog.Dispose();
            Renderer.Dispose();
        }
    }
}
