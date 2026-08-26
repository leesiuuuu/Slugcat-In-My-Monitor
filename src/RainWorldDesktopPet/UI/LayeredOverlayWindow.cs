using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Workshop;
using Timer = System.Windows.Forms.Timer;

namespace RainWorldDesktopPet.UI
{
    internal enum OverlayRenderLayer
    {
        GroundFood,
        Slugcat,
        HeldFood
    }

    public sealed class LayeredOverlayWindow : Form
    {
        private const int MaximumSlugcats = 8;
        private const int MaximumFoods = 12;
        private const int DefaultRenderFramesPerSecond = 60;
        private const int DefaultRenderIntervalMilliseconds =
            (1000 + DefaultRenderFramesPerSecond - 1) / DefaultRenderFramesPerSecond;
        private const int MinimumOverlaySize = 384;
        private const int OverlaySizeQuantum = 128;
        private const int OverlayPadding = 24;
        private const int WmEnsureTopMost = 0x8001;
        private const int WmHookMouseInput = 0x8002;
        private readonly RainWorldInstallation installation;
        private readonly SlugcatId startSlugcat;
        private readonly Timer renderTimer;
        private readonly NotifyIcon trayIcon;
        private readonly Icon applicationIcon;
        private readonly ToolStripMenuItem slugcatMenu;
        private readonly ToolStripMenuItem refreshWorkshopItem;
        private readonly ToolStripMenuItem debugItem;
        private readonly ToolStripMenuItem retryRenderItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem activeSlugcatsMenu;
        private readonly ToolStripMenuItem spawnItem;
        private readonly ToolStripMenuItem removeItem;
        private readonly ToolStripMenuItem skinEditorItem;
        private readonly ToolStripMenuItem foodMenu;
        private readonly ToolStripMenuItem feedDangleFruitItem;
        private readonly ToolStripMenuItem feedEggBugEggItem;
        private readonly ToolStripMenuItem fullnessStatusItem;
        private readonly ToolStripMenuItem clearFoodsItem;
        private readonly List<GameLoop> gameLoops = new List<GameLoop>();
        private readonly SlugcatPose[] poseBuffer = new SlugcatPose[MaximumSlugcats];
        private readonly long[] mouseHitSnapshotTicks = new long[MaximumSlugcats];
        private readonly DirectCompositionHost.GpuSmokeEffect[] smokeEffectBuffer =
            new DirectCompositionHost.GpuSmokeEffect[256];
        private readonly List<Rectangle> surfaceBoundsBuffer =
            new List<Rectangle>(MaximumSlugcats);
        private readonly CompositionBatchPlanner compositionBatchPlanner =
            new CompositionBatchPlanner();
        private readonly Dictionary<string, double> displayRefreshRates =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly DesktopCollisionWorld collisionWorld =
            new DesktopCollisionWorld(new WindowEnumerator());
        private readonly Stopwatch surfaceRefreshClock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<HookMouseInput> hookMouseInputs =
            new ConcurrentQueue<HookMouseInput>();
        private DirectCompositionHost compositionHost;
        private readonly string startDmsSkinId;
        private GameLoop gameLoop;
        private GameLoop grabbedGameLoop;
        private readonly NativeMethods.WinEventProc foregroundEventCallback;
        private LowLevelMouseInputHook mouseHook;
        private volatile MouseHookHitSnapshot mouseHitSnapshot =
            MouseHookHitSnapshot.Empty;
        private IntPtr mouseInputWindowHandle;
        private int hookOwnsLeftButton;
        private IntPtr foregroundEventHook;
        private SettingsWindow settingsWindow;
        private SkinEditorWindow skinEditor;
        private Rectangle virtualDesktopBounds;
        private bool mouseCaptured;
        private bool leftButtonDown;
        private int renderErrorCount;
        private bool renderingEnabled;
        private bool renderingFrame;
        private double displayRefreshRate;

        private sealed class HookMouseInput
        {
            internal HookMouseInput(bool pressed, GameLoop target, Vec2 point)
            {
                Pressed = pressed;
                Target = target;
                Point = point;
            }

            internal readonly bool Pressed;
            internal readonly GameLoop Target;
            internal readonly Vec2 Point;
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat)
            : this(installation, startDebug, startSlugcat, null)
        {
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat, string startDmsSkinId)
        {
            this.installation = installation;
            this.startSlugcat = startSlugcat;
            this.startDmsSkinId = startDmsSkinId;
            foregroundEventCallback = ForegroundEventCallback;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            virtualDesktopBounds = MonitorManager.GetVirtualBounds();
            Bounds = virtualDesktopBounds;
            Text = "SlugcatInMyMonitor";
            applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (applicationIcon != null) Icon = applicationIcon;

            renderTimer = new Timer();
            // Start conservatively, then follow the refresh rate of the
            // monitor(s) occupied by active Slugcats after the first frame.
            renderTimer.Interval = DefaultRenderIntervalMilliseconds;
            renderTimer.Tick += RenderTimerTick;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem settingsItem = new ToolStripMenuItem(T("설정 열기", "Open Settings"));
            settingsItem.Click += OpenSettings;
            debugItem = new ToolStripMenuItem(T("디버그 오버레이", "Debug Overlay"));
            debugItem.CheckOnClick = true;
            debugItem.Checked = startDebug;
            debugItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].DebugEnabled = debugItem.Checked;
                if (compositionHost != null) compositionHost.ResetSurfaces();
                RefreshSettingsWindow();
            };
            pauseItem = new ToolStripMenuItem(T("모든 슬러그캣 일시 정지", "Pause All Slugcats"));
            pauseItem.CheckOnClick = true;
            pauseItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].Paused = pauseItem.Checked;
                RefreshSettingsWindow();
            };
            retryRenderItem = new ToolStripMenuItem(T("렌더링 재시도", "Retry Rendering"));
            retryRenderItem.Enabled = false;
            retryRenderItem.Click += RetryRendering;
            skinEditorItem = new ToolStripMenuItem(T("스킨 편집기 (실험적)", "Skin Editor (Experimental)"));
            skinEditorItem.Click += ToggleSkinEditor;
            ToolStripMenuItem exitItem = new ToolStripMenuItem(T("종료", "Exit"));
            exitItem.Click += delegate { Close(); };
            slugcatMenu = new ToolStripMenuItem(T("캐릭터와 능력", "Character and Ability"));
            for (int i = 0; i < SlugcatProfiles.All.Count; i++)
            {
                SlugcatProfile profile = SlugcatProfiles.All[i];
                slugcatMenu.DropDownItems.Add(CreateSlugcatItem(
                    SlugcatProfiles.SelectionLabel(profile.Id), profile.Id, startSlugcat));
            }
            refreshWorkshopItem = new ToolStripMenuItem(T("Workshop 모드 새로 고침", "Refresh Workshop Mods"));
            refreshWorkshopItem.Click += RefreshWorkshopItemClick;
            activeSlugcatsMenu = new ToolStripMenuItem(T("슬러그캣", "Slugcats"));
            spawnItem = new ToolStripMenuItem(T("슬러그캣 추가", "Add Slugcat"));
            spawnItem.Click += SpawnSlugcat;
            ToolStripMenuItem nextItem = new ToolStripMenuItem(T("다음 슬러그캣 선택", "Select Next Slugcat"));
            nextItem.Click += SelectNextSlugcat;
            removeItem = new ToolStripMenuItem(T("선택한 슬러그캣 삭제", "Remove Selected Slugcat"));
            removeItem.Click += RemoveSelectedSlugcat;
            activeSlugcatsMenu.DropDownItems.Add(spawnItem);
            activeSlugcatsMenu.DropDownItems.Add(nextItem);
            activeSlugcatsMenu.DropDownItems.Add(removeItem);
            activeSlugcatsMenu.DropDownItems.Add(new ToolStripSeparator());
            foodMenu = new ToolStripMenuItem(T("먹이 주기", "Feed"));
            feedDangleFruitItem = new ToolStripMenuItem(
                T("푸른 열매 주기", "Give Blue Fruit"));
            feedDangleFruitItem.Click += FeedDangleFruit;
            feedEggBugEggItem = new ToolStripMenuItem(
                T("알벌레 알 주기", "Give Eggbug Egg"));
            feedEggBugEggItem.Click += FeedEggBugEgg;
            fullnessStatusItem = new ToolStripMenuItem(
                T("슬러그캣 포만감", "Slugcat Fullness"));
            clearFoodsItem = new ToolStripMenuItem(
                T("먹이 치우기", "Clear Food"));
            clearFoodsItem.Click += ClearSelectedFoods;
            foodMenu.DropDownItems.Add(feedDangleFruitItem);
            foodMenu.DropDownItems.Add(feedEggBugEggItem);
            foodMenu.DropDownItems.Add(new ToolStripSeparator());
            foodMenu.DropDownItems.Add(fullnessStatusItem);
            foodMenu.DropDownItems.Add(clearFoodsItem);
            foodMenu.DropDownOpening += RefreshFoodMenu;
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(activeSlugcatsMenu);
            menu.Items.Add(foodMenu);
            menu.Items.Add(slugcatMenu);
            menu.Items.Add(skinEditorItem);
            menu.Items.Add(debugItem);
            menu.Items.Add(pauseItem);
            menu.Items.Add(refreshWorkshopItem);
            menu.Items.Add(retryRenderItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = applicationIcon ?? SystemIcons.Application;
            trayIcon.Text = "SlugcatInMyMonitor";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) OpenSettings(sender, EventArgs.Empty);
            };
            trayIcon.Visible = true;

            Shown += delegate
            {
                gameLoop.DebugEnabled = startDebug;
                displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
                ApplyRenderCadence(displayRefreshRate);
                renderingEnabled = true;
                renderTimer.Start();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle = BuildOverlayExtendedStyle(parameters.ExStyle);
                return parameters;
            }
        }

        internal static int BuildOverlayExtendedStyle(int inheritedStyle)
        {
            // The overlay covers the entire virtual desktop. Keeping it fully
            // transparent to input is required for buttons owned by other
            // processes; HTTRANSPARENT alone only reliably walks windows in
            // this UI thread.
            return inheritedStyle |
                   NativeMethods.WS_EX_TRANSPARENT |
                   NativeMethods.WS_EX_NOREDIRECTIONBITMAP |
                   NativeMethods.WS_EX_LAYERED |
                   NativeMethods.WS_EX_TOOLWINDOW |
                   NativeMethods.WS_EX_TOPMOST |
                   NativeMethods.WS_EX_NOACTIVATE;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ConfigureVirtualDesktop();
            InstallMouseHook();
            InstallForegroundEventHook();
            EnsureOverlayTopMost();
            compositionHost = new DirectCompositionHost(Handle, virtualDesktopBounds);
            collisionWorld.Refresh(Handle);
            surfaceRefreshClock.Restart();
            AddSlugcat(startSlugcat);
            if (!string.IsNullOrWhiteSpace(startDmsSkinId))
            {
                string reason;
                if (!gameLoop.SetDmsSkin(startDmsSkinId, out reason))
                    trayIcon.ShowBalloonTip(5000, T("DMS 스킨을 사용할 수 없음", "DMS Skin Unavailable"), reason, ToolTipIcon.Warning);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            renderingEnabled = false;
            renderTimer.Stop();
            UninstallForegroundEventHook();
            UninstallMouseHook();
            ReleaseGrabInput();
            if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Close();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.Close();
            for (int i = 0; i < gameLoops.Count; i++) gameLoops[i].Dispose();
            gameLoops.Clear();
            gameLoop = null;
            if (compositionHost != null) compositionHost.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (applicationIcon != null) applicationIcon.Dispose();
            base.OnHandleDestroyed(e);
        }

        private void RenderTimerTick(object sender, EventArgs e)
        {
            // A disabled renderer with an active timer is waiting for an
            // automatic presentation retry.
            if (!renderingEnabled) renderingEnabled = true;
            RenderFrame();
        }

        private void RenderFrame()
        {
            if (!renderingEnabled || renderingFrame) return;
            renderingFrame = true;
            try
            {
                PollDragInput();
                RefreshCollisionWorld();
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    gameLoops[i].Advance(Handle);
                    poseBuffer[i] = gameLoops[i].BuildPose();
                }
                bool mouseBoundsChanged = false;
                for (int i = 0; i < gameLoops.Count; i++)
                    if (mouseHitSnapshotTicks[i] != gameLoops[i].SimulationTick)
                        mouseBoundsChanged = true;
                if (mouseBoundsChanged) PublishMouseHitSnapshot();
                UpdateRenderCadence(poseBuffer, gameLoops.Count);
                surfaceBoundsBuffer.Clear();
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    bool debug = gameLoops[i].DebugEnabled &&
                        ReferenceEquals(gameLoops[i], gameLoop);
                    surfaceBoundsBuffer.Add(CalculateRenderBounds(gameLoops[i], poseBuffer[i], debug));
                }
                IList<CompositionBatch> batches = compositionBatchPlanner.Plan(
                    surfaceBoundsBuffer, OverlaySizeQuantum);
                compositionHost.BeginEffectFrame();
                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    CompositionBatch batch = batches[batchIndex];
                    bool batchUsesDebug = false;
                    for (int member = 0; member < batch.SurfaceIndices.Count; member++)
                    {
                        int memberIndex = batch.SurfaceIndices[member];
                        if (gameLoops[memberIndex].DebugEnabled &&
                            ReferenceEquals(gameLoops[memberIndex], gameLoop))
                        {
                            batchUsesDebug = true;
                            break;
                        }
                    }
                    DirectCompositionHost.CompositionSurface surface = null;
                    GpuSpriteCanvas gpuCanvas = null;
                    RenderSpace renderSpace;
                    if (batchUsesDebug)
                    {
                        surface = compositionHost.PrepareSurface(batchIndex, batch.Bounds);
                        System.Drawing.Graphics graphics = surface.Graphics;
                        graphics.CompositingMode =
                            System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.Clear(Color.Transparent);
                        graphics.CompositingMode =
                            System.Drawing.Drawing2D.CompositingMode.SourceOver;
                        renderSpace = new RenderSpace(surface.Bounds);
                    }
                    else
                    {
                        gpuCanvas = compositionHost.PrepareGpuSurface(batchIndex,
                            batch.Bounds);
                        renderSpace = new RenderSpace(gpuCanvas.Bounds);
                    }
                    int drawStepCount = batch.SurfaceIndices.Count * 3;
                    for (int drawStep = 0; drawStep < drawStepCount; drawStep++)
                    {
                        int loopIndex;
                        OverlayRenderLayer layer;
                        ResolveRenderStep(batch.SurfaceIndices, drawStep,
                            out loopIndex, out layer);
                        GameLoop loop = gameLoops[loopIndex];
                        bool debug = loop.DebugEnabled && ReferenceEquals(loop, gameLoop);
                        if (layer == OverlayRenderLayer.Slugcat)
                        {
                            if (batchUsesDebug)
                                loop.Renderer.Render(surface.Graphics,
                                    poseBuffer[loopIndex], renderSpace, debug,
                                    loop.World, loop.Slugcat, loop.AI,
                                    loop.AssetStatus, loop.SelectedSlugcat);
                            else
                                loop.Renderer.RenderGpu(gpuCanvas,
                                    poseBuffer[loopIndex], renderSpace,
                                    loop.World, loop.Slugcat, loop.AI,
                                    loop.AssetStatus, loop.SelectedSlugcat);
                        }
                        else
                        {
                            if (batchUsesDebug)
                                loop.Renderer.RenderFoods(surface.Graphics,
                                    loop.Foods, renderSpace,
                                    poseBuffer[loopIndex].CharacterRenderScale,
                                    poseBuffer[loopIndex].TimeStacker,
                                    layer == OverlayRenderLayer.HeldFood);
                            else
                                loop.Renderer.RenderFoodsGpu(gpuCanvas,
                                    loop.Foods, renderSpace,
                                    poseBuffer[loopIndex].CharacterRenderScale,
                                    poseBuffer[loopIndex].TimeStacker,
                                    layer == OverlayRenderLayer.HeldFood);
                        }
                    }
                    if (batchUsesDebug) compositionHost.Present(batchIndex);
                    else compositionHost.PresentGpu(gpuCanvas);

                    RectangleF effectContentBounds = RectangleF.Empty;
                    for (int member = 0; member < batch.SurfaceIndices.Count; member++)
                    {
                        int loopIndex = batch.SurfaceIndices[member];
                        GameLoop loop = gameLoops[loopIndex];
                        RectangleF memberBounds = loop.Renderer.CalculateGpuEffectBounds(
                            loop.Slugcat, poseBuffer[loopIndex]);
                        if (memberBounds.IsEmpty) continue;
                        effectContentBounds = effectContentBounds.IsEmpty ? memberBounds :
                            RectangleF.Union(effectContentBounds, memberBounds);
                    }
                    if (!effectContentBounds.IsEmpty)
                    {
                        Rectangle effectBounds = compositionHost.PrepareEffectBounds(
                            batchIndex, effectContentBounds);
                        RenderSpace effectRenderSpace = new RenderSpace(effectBounds);
                        int smokeEffectCount = 0;
                        for (int member = 0; member < batch.SurfaceIndices.Count; member++)
                        {
                            int loopIndex = batch.SurfaceIndices[member];
                            GameLoop loop = gameLoops[loopIndex];
                            loop.Renderer.CollectGpuSmokeEffects(loop.Slugcat,
                                poseBuffer[loopIndex], effectRenderSpace,
                                smokeEffectBuffer, ref smokeEffectCount);
                        }
                        compositionHost.PresentEffects(batchIndex, smokeEffectBuffer,
                            smokeEffectCount, effectBounds);
                    }
                }
                compositionHost.Commit(batches.Count);
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].RecordRenderFrame(displayRefreshRate);
                if (renderErrorCount != 0)
                {
                    renderErrorCount = 0;
                    retryRenderItem.Enabled = false;
                    RefreshSettingsWindow();
                }
            }
            catch (Exception exception)
            {
                // Simulation/atlas/GDI drawing failures are not assumed to be
                // transient. Keep the tray alive and let the user explicitly
                // retry, while recording this failure only once.
                Program.LogException(exception);
                renderingEnabled = false;
                renderTimer.Stop();
                retryRenderItem.Enabled = true;
                RefreshSettingsWindow();
                trayIcon.ShowBalloonTip(5000,
                    T("슬러그캣 렌더링 일시 정지", "Slugcat Rendering Paused"),
                    exception.Message + T(" 트레이 메뉴에서 렌더링 재시도를 선택하세요.",
                        " Use Retry Rendering from the tray menu."), ToolTipIcon.Error);
            }
            finally
            {
                renderingFrame = false;
            }
        }

        private void RetryRendering(object sender, EventArgs e)
        {
            try
            {
                RecreateCompositionHost();
                renderErrorCount = 0;
                retryRenderItem.Enabled = false;
                displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
                renderingEnabled = true;
                ApplyRenderCadence(displayRefreshRate);
                renderTimer.Start();
                RefreshSettingsWindow();
                RenderFrame();
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                retryRenderItem.Enabled = true;
                RefreshSettingsWindow();
                trayIcon.ShowBalloonTip(5000, T("슬러그캣 렌더링 재시도 실패", "Slugcat Rendering Retry Failed"),
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private void RecreateCompositionHost()
        {
            if (compositionHost != null) compositionHost.Dispose();
            compositionHost = null;
            compositionHost = new DirectCompositionHost(Handle, virtualDesktopBounds);
        }

        private void RefreshCollisionWorld()
        {
            if (collisionWorld.TryApplyPendingRefresh())
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].ApplyMovingSurfaceDelta();
            }
            if (surfaceRefreshClock.Elapsed.TotalSeconds <
                SimulationConstants.WindowRefreshSeconds) return;

            collisionWorld.RequestRefresh(Handle);
            surfaceRefreshClock.Restart();
        }

        private void UpdateRenderCadence(SlugcatPose[] poses, int poseCount)
        {
            double targetRefreshRate = 0.0;
            for (int i = 0; i < poseCount; i++)
            {
                string deviceName = poses[i].CurrentMonitorName;
                double refreshRate;
                if (!displayRefreshRates.TryGetValue(deviceName, out refreshRate))
                {
                    refreshRate = NativeMethods.GetDisplayRefreshRate(deviceName);
                    displayRefreshRates[deviceName] = refreshRate;
                }
                if (refreshRate > targetRefreshRate) targetRefreshRate = refreshRate;
            }

            if (targetRefreshRate <= 1.0)
                targetRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
            ApplyRenderCadence(targetRefreshRate);
        }

        private void ApplyRenderCadence(double refreshRate)
        {
            if (refreshRate <= 1.0) refreshRate = DefaultRenderFramesPerSecond;
            displayRefreshRate = refreshRate;
            int interval = Math.Max(1, (int)Math.Round(1000.0 / refreshRate));
            if (renderTimer.Interval != interval) renderTimer.Interval = interval;
        }

        private void ConfigureVirtualDesktop()
        {
            Rectangle virtualBounds = MonitorManager.GetVirtualBounds();
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                throw new InvalidOperationException("Windows reported an empty virtual desktop.");

            virtualDesktopBounds = virtualBounds;
            Bounds = virtualDesktopBounds;
            if (compositionHost != null) compositionHost.SetDesktopBounds(virtualDesktopBounds);
        }

        private Rectangle CalculateRenderBounds(GameLoop loop, SlugcatPose pose, bool debug)
        {
            if (debug) return virtualDesktopBounds;
            RectangleF content = pose.GraphicsBounds;
            // DirectComposition owns only the planned surface rectangle.
            // Include a Spearmaster needle and every live umbilical point,
            // otherwise a valid far throw is drawn outside that rectangle.
            double scale = pose.CharacterRenderScale;
            for (int i = 0; i < loop.Slugcat.Spears.Count; i++)
            {
                DesktopSpear spear = loop.Slugcat.Spears[i];
                Vec2 center = spear.Chunk.RenderPosition(pose.TimeStacker) * scale;
                RectangleF spearBounds = new RectangleF((float)(center.X - 28.0),
                    (float)(center.Y - 28.0), 56.0f, 56.0f);
                content = RectangleF.Union(content, spearBounds);
                if (!spear.HasUmbilical) continue;
                Vec2[] points = spear.Umbilical;
                for (int point = 0; point < points.Length; point++)
                {
                    Vec2 rendered = Vec2.Lerp(spear.LastUmbilical[point],
                        points[point], pose.TimeStacker) * scale;
                    content = RectangleF.Union(content, new RectangleF(
                        (float)(rendered.X - 2.0), (float)(rendered.Y - 2.0),
                        4.0f, 4.0f));
                }
            }
            for (int i = 0; i < loop.Foods.Foods.Count; i++)
            {
                DesktopFood food = loop.Foods.Foods[i];
                if (!food.IsActive) continue;
                Vec2 center = food.Chunk.RenderPosition(pose.TimeStacker) * scale;
                double reach = food.VisualReach * scale;
                content = RectangleF.Union(content, new RectangleF(
                    (float)(center.X - reach), (float)(center.Y - reach),
                    (float)(reach * 2.0), (float)(reach * 2.0)));
            }

            // Keep Saint's active tongue in the same dynamic composition-bounds
            // path as a far Spearmaster needle. The required surface follows every
            // current/interpolated rope point, so a distant attached tongue stays
            // visible without imposing a fixed render-distance cap. Once retracted,
            // it stops contributing to the required bounds and normal surface
            // reclamation can shrink the oversized allocation again.
            SaintAbilityController saint =
                loop.Slugcat.AbilityController as SaintAbilityController;
            if (saint != null && saint.Mode != SaintTongueMode.Retracted)
            {
                Vec2[] currentRope = saint.RopeForRender;
                Vec2[] previousRope = saint.LastRopeForRender;
                int ropePointCount = Math.Min(currentRope.Length, previousRope.Length);
                for (int point = 0; point < ropePointCount; point++)
                {
                    Vec2 currentPoint = currentRope[point] * scale;
                    Vec2 previousPoint = previousRope[point] * scale;
                    content = RectangleF.Union(content, new RectangleF(
                        (float)(currentPoint.X - 8.0), (float)(currentPoint.Y - 8.0),
                        16.0f, 16.0f));
                    content = RectangleF.Union(content, new RectangleF(
                        (float)(previousPoint.X - 8.0), (float)(previousPoint.Y - 8.0),
                        16.0f, 16.0f));
                }
            }

            int contentWidth = (int)Math.Ceiling(content.Width) + OverlayPadding * 2;
            int contentHeight = (int)Math.Ceiling(content.Height) + OverlayPadding * 2;
            int width = RoundOverlaySize(Math.Max(MinimumOverlaySize, contentWidth));
            int height = RoundOverlaySize(Math.Max(MinimumOverlaySize, contentHeight));
            int centerX = (int)Math.Round(content.Left + content.Width * 0.5f);
            int centerY = (int)Math.Round(content.Top + content.Height * 0.5f);
            return new Rectangle(centerX - width / 2, centerY - height / 2, width, height);
        }

        private static int RoundOverlaySize(int value)
        {
            return ((value + OverlaySizeQuantum - 1) / OverlaySizeQuantum) * OverlaySizeQuantum;
        }

        private void PollDragInput()
        {
            // A press consumed by WH_MOUSE_LL is intentionally absent from the
            // normal Windows button state. While the hook owns a pet-object drag,
            // only its matching WM_LBUTTONUP may end that drag.
            if (mouseCaptured) return;
            bool currentlyDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            leftButtonDown = currentlyDown;
        }

        private void InstallMouseHook()
        {
            if (mouseHook != null) return;
            Interlocked.Exchange(ref mouseInputWindowHandle, Handle);
            LowLevelMouseInputHook installed =
                new LowLevelMouseInputHook(HandleHookMouseButton);
            try
            {
                installed.Start();
                mouseHook = installed;
            }
            catch
            {
                Interlocked.Exchange(ref mouseInputWindowHandle, IntPtr.Zero);
                installed.Dispose();
                throw;
            }
        }

        private void UninstallMouseHook()
        {
            mouseHitSnapshot = MouseHookHitSnapshot.Empty;
            Interlocked.Exchange(ref hookOwnsLeftButton, 0);
            Interlocked.Exchange(ref mouseInputWindowHandle, IntPtr.Zero);
            LowLevelMouseInputHook installed = mouseHook;
            mouseHook = null;
            if (installed != null) installed.Dispose();
            HookMouseInput ignored;
            while (hookMouseInputs.TryDequeue(out ignored)) { }
        }

        private void InstallForegroundEventHook()
        {
            if (foregroundEventHook != IntPtr.Zero) return;
            foregroundEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, foregroundEventCallback, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT |
                NativeMethods.WINEVENT_SKIPOWNPROCESS);
            if (foregroundEventHook == IntPtr.Zero)
                Program.LogException(new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to monitor foreground window changes."));
        }

        private void UninstallForegroundEventHook()
        {
            IntPtr hook = foregroundEventHook;
            foregroundEventHook = IntPtr.Zero;
            if (hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(hook);
        }

        private void ForegroundEventCallback(IntPtr hook, uint eventType, IntPtr handle,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (handle == IntPtr.Zero || !IsHandleCreated || IsDisposed) return;
            NativeMethods.PostMessage(Handle, WmEnsureTopMost, IntPtr.Zero, IntPtr.Zero);
        }

        private void EnsureOverlayTopMost()
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (!NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER))
            {
                Program.LogException(new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to restore the overlay topmost position."));
            }
        }

        private bool HandleHookMouseButton(int mouseMessage, NativeMethods.Point nativePoint)
        {
            if (mouseMessage == NativeMethods.WM_LBUTTONUP)
            {
                if (Interlocked.Exchange(ref hookOwnsLeftButton, 0) == 0)
                    return false;
                QueueHookMouseInput(new HookMouseInput(false, null,
                    new Vec2(nativePoint.X, nativePoint.Y)));
                return true;
            }

            MouseHookHitSnapshot snapshot = mouseHitSnapshot;
            Vec2 point = new Vec2(nativePoint.X, nativePoint.Y);
            GameLoop hit = snapshot.HitTest(point) as GameLoop;
            if (hit == null) return false;
            if (Interlocked.CompareExchange(ref hookOwnsLeftButton, 1, 0) != 0)
                return true;
            QueueHookMouseInput(new HookMouseInput(true, hit, point));
            return true;
        }

        private void QueueHookMouseInput(HookMouseInput input)
        {
            hookMouseInputs.Enqueue(input);
            IntPtr window = Interlocked.CompareExchange(
                ref mouseInputWindowHandle, IntPtr.Zero, IntPtr.Zero);
            if (window != IntPtr.Zero)
                NativeMethods.PostMessage(window, WmHookMouseInput,
                    IntPtr.Zero, IntPtr.Zero);
        }

        private void DrainHookMouseInput()
        {
            HookMouseInput input;
            while (hookMouseInputs.TryDequeue(out input))
            {
                if (!input.Pressed)
                {
                    ReleaseGrabInput();
                    leftButtonDown = false;
                    continue;
                }

                if (!gameLoops.Contains(input.Target) ||
                    !BeginGrab(input.Target, input.Point))
                {
                    Interlocked.Exchange(ref hookOwnsLeftButton, 0);
                }
            }
        }

        private bool BeginGrab(GameLoop hit, Vec2 point)
        {
            SelectSlugcat(hit);
            if (!hit.BeginGrab(point)) return false;
            grabbedGameLoop = hit;
            mouseCaptured = true;
            leftButtonDown = true;
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHookMouseInput)
            {
                DrainHookMouseInput();
                return;
            }
            if (message.Msg == WmEnsureTopMost)
            {
                EnsureOverlayTopMost();
                return;
            }
            if (message.Msg == NativeMethods.WM_LBUTTONUP && mouseCaptured)
            {
                ReleaseGrabInput();
                leftButtonDown = false;
            }
            else if ((message.Msg == NativeMethods.WM_CAPTURECHANGED ||
                message.Msg == NativeMethods.WM_CANCELMODE) && mouseCaptured)
            {
                ReleaseGrabInput();
            }
            if (message.Msg == NativeMethods.WM_DISPLAYCHANGE || message.Msg == NativeMethods.WM_DPICHANGED)
            {
                ReleaseGrabInput();
                try
                {
                    ConfigureVirtualDesktop();
                    if (compositionHost != null) compositionHost.ResetSurfaces();
                    displayRefreshRates.Clear();
                    ApplyRenderCadence(NativeMethods.GetPrimaryDisplayRefreshRate());
                }
                catch (Exception exception)
                {
                    Program.LogException(exception);
                    renderingEnabled = false;
                    renderTimer.Stop();
                    retryRenderItem.Enabled = true;
                }
                return;
            }
            base.WndProc(ref message);
        }

        internal static bool ShouldSuppressLeftButton(int mouseMessage,
            bool draggingSlugcat, bool slugcatUnderPointer)
        {
            if (mouseMessage == NativeMethods.WM_LBUTTONUP) return draggingSlugcat;
            return (mouseMessage == NativeMethods.WM_LBUTTONDOWN ||
                mouseMessage == NativeMethods.WM_LBUTTONDBLCLK) && slugcatUnderPointer;
        }

        internal static void ResolveRenderStep(IList<int> surfaceIndices,
            int drawStep, out int loopIndex, out OverlayRenderLayer layer)
        {
            if (surfaceIndices == null) throw new ArgumentNullException("surfaceIndices");
            int count = surfaceIndices.Count;
            if (count < 1 || drawStep < 0 || drawStep >= count * 3)
                throw new ArgumentOutOfRangeException("drawStep");
            int layerIndex = drawStep / count;
            int backToFrontIndex = count - 1 - drawStep % count;
            loopIndex = surfaceIndices[backToFrontIndex];
            layer = (OverlayRenderLayer)layerIndex;
        }

        private void ReleaseGrabInput()
        {
            Interlocked.Exchange(ref hookOwnsLeftButton, 0);
            GameLoop grabbed = grabbedGameLoop;
            grabbedGameLoop = null;
            mouseCaptured = false;
            if (grabbed != null)
            {
                grabbed.EndGrab();
                PublishMouseHitSnapshot();
            }
        }

        private void PublishMouseHitSnapshot()
        {
            int maximumCircleCount = 0;
            for (int i = 0; i < gameLoops.Count; i++)
            {
                GameLoop loop = gameLoops[i];
                maximumCircleCount += loop.Slugcat.BodyChunks.Length + 1;
                for (int foodIndex = 0;
                    foodIndex < loop.Foods.Foods.Count; foodIndex++)
                {
                    DesktopFood food = loop.Foods.Foods[foodIndex];
                    if (food.IsActive && food.IsDraggable) maximumCircleCount++;
                }
            }
            MouseHookHitTarget[] targets = new MouseHookHitTarget[gameLoops.Count];
            MouseHookHitCircle[] circles =
                new MouseHookHitCircle[maximumCircleCount];
            int circleCount = 0;
            for (int i = 0; i < gameLoops.Count; i++)
            {
                GameLoop loop = gameLoops[i];
                int firstCircle = circleCount;
                for (int foodIndex = 0; foodIndex < loop.Foods.Foods.Count; foodIndex++)
                {
                    DesktopFood food = loop.Foods.Foods[foodIndex];
                    if (!food.IsActive || !food.IsDraggable) continue;
                    circles[circleCount++] = new MouseHookHitCircle(
                        DesktopWorldTransform.ToDesktop(food.Chunk.Position),
                        DesktopWorldTransform.ToDesktopLength(food.VisualReach + 5.0));
                }
                for (int chunkIndex = 0;
                    chunkIndex < loop.Slugcat.BodyChunks.Length; chunkIndex++)
                {
                    BodyChunk chunk = loop.Slugcat.BodyChunks[chunkIndex];
                    circles[circleCount++] = new MouseHookHitCircle(
                        DesktopWorldTransform.ToDesktop(chunk.Position),
                        DesktopWorldTransform.ToDesktopLength(chunk.Radius + 14.0));
                }
                circles[circleCount++] = new MouseHookHitCircle(
                    DesktopWorldTransform.ToDesktop(loop.Graphics.Head.Position),
                    DesktopWorldTransform.ToDesktopLength(17.0));
                // HitTest scans targets from the end. Publish Slugcat 1 at
                // the end so pointer priority matches its frontmost render order.
                int targetIndex = gameLoops.Count - 1 - i;
                targets[targetIndex] = new MouseHookHitTarget(loop, firstCircle,
                    circleCount - firstCircle);
                mouseHitSnapshotTicks[i] = loop.SimulationTick;
            }
            mouseHitSnapshot = new MouseHookHitSnapshot(targets, circles);
        }

        private void AddSlugcat(SlugcatId id)
        {
            if (gameLoops.Count >= MaximumSlugcats) return;
            GameLoop added = new GameLoop(Handle, installation, id,
                gameLoops.Count, collisionWorld);
            added.DebugEnabled = debugItem.Checked;
            added.Paused = pauseItem.Checked;
            gameLoops.Add(added);
            SelectSlugcat(added);
            PublishMouseHitSnapshot();
        }

        private void SpawnSlugcat(object sender, EventArgs e)
        {
            if (gameLoops.Count >= MaximumSlugcats)
            {
                trayIcon.ShowBalloonTip(3000, T("슬러그캣 수 제한", "Slugcat Limit"),
                    T("슬러그캣은 최대 " + MaximumSlugcats + "마리까지 실행할 수 있습니다.",
                        "Up to " + MaximumSlugcats + " Slugcats can be active."), ToolTipIcon.Info);
                return;
            }
            try
            {
                AddSlugcat(gameLoop == null ? startSlugcat : gameLoop.SelectedSlugcat.Id);
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(4000, T("슬러그캣 추가 실패", "Failed to Add Slugcat"),
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private void SelectNextSlugcat(object sender, EventArgs e)
        {
            if (gameLoops.Count < 2) return;
            int index = gameLoops.IndexOf(gameLoop);
            SelectSlugcat(gameLoops[(index + 1) % gameLoops.Count]);
        }

        private void RefreshFoodMenu(object sender, EventArgs e)
        {
            int activeFoods = CountActiveFoods();
            foodMenu.Text = T("먹이 주기", "Feed");
            feedDangleFruitItem.Enabled = gameLoop != null &&
                activeFoods < MaximumFoods &&
                gameLoop.Foods.Foods.Count < DesktopFoodManager.MaximumActiveFoods;
            feedEggBugEggItem.Enabled = feedDangleFruitItem.Enabled;

            // Food is shared, but hunger belongs to each Slugcat. Show every
            // active pet's name and fullness so the user can see who will seek
            // the next available food.
            fullnessStatusItem.DropDownItems.Clear();
            for (int i = 0; i < gameLoops.Count; i++)
            {
                GameLoop loop = gameLoops[i];
                ToolStripMenuItem statusItem = new ToolStripMenuItem(
                    T("슬러그캣 ", "Slugcat ") + (i + 1) + " · " +
                    SlugcatProfiles.SelectionLabel(loop.SelectedSlugcat.Id) + " · " +
                    T("포만감 ", "Fullness ") +
                    loop.Foods.Fullness.ToString("0.0") + "/" +
                    DesktopFoodManager.MaximumFullness.ToString("0.0"));
                statusItem.Enabled = false;
                fullnessStatusItem.DropDownItems.Add(statusItem);
            }
            fullnessStatusItem.Enabled = gameLoops.Count > 0;
            clearFoodsItem.Enabled = activeFoods > 0;
        }

        private void FeedDangleFruit(object sender, EventArgs e)
        {
            FeedFood(DesktopFoodKind.DangleFruit);
        }

        private void FeedEggBugEgg(object sender, EventArgs e)
        {
            FeedFood(DesktopFoodKind.EggBugEgg);
        }

        private void FeedFood(DesktopFoodKind kind)
        {
            if (gameLoop == null) return;
            bool spawned = CountActiveFoods() < MaximumFoods &&
                (kind == DesktopFoodKind.EggBugEgg
                    ? gameLoop.FeedEggBugEgg()
                    : gameLoop.FeedDangleFruit());
            if (!spawned)
            {
                trayIcon.ShowBalloonTip(2500,
                    T("먹이를 더 놓을 수 없습니다", "Food Limit Reached"),
                    T("화면에는 총 " + MaximumFoods + "개, 슬러그캣 한 마리에는 " +
                        DesktopFoodManager.MaximumActiveFoods + "개까지 놓을 수 있습니다.",
                        "The desktop supports " + MaximumFoods + " foods total and " +
                        DesktopFoodManager.MaximumActiveFoods + " per Slugcat."),
                    ToolTipIcon.Info);
                return;
            }
            if (!gameLoop.Foods.LastSpawnAccepted)
                trayIcon.ShowBalloonTip(1800,
                    T("지금은 먹고 싶지 않은가 봅니다", "Not Hungry Right Now"),
                    T("먹이는 그대로 남지만, 포만감과 기분에 따라 이번에는 먹지 않습니다.",
                        "The food remains, but fullness and appetite made this offer uninteresting."),
                    ToolTipIcon.None);
            PublishMouseHitSnapshot();
        }

        private void ClearSelectedFoods(object sender, EventArgs e)
        {
            if (gameLoop == null) return;
            gameLoop.ClearFoods();
            PublishMouseHitSnapshot();
        }

        private int CountActiveFoods()
        {
            int count = 0;
            for (int i = 0; i < gameLoops.Count; i++)
                count += gameLoops[i].Foods.Foods.Count;
            return count;
        }

        private void RemoveSelectedSlugcat(object sender, EventArgs e)
        {
            if (gameLoop == null || gameLoops.Count <= 1) return;
            GameLoop removed = gameLoop;
            int index = gameLoops.IndexOf(removed);
            if (ReferenceEquals(grabbedGameLoop, removed))
            {
                ReleaseGrabInput();
            }
            gameLoops.RemoveAt(index);
            removed.Dispose();
            PublishMouseHitSnapshot();
            if (compositionHost != null) compositionHost.ResetSurfaces();
            SelectSlugcat(gameLoops[Math.Min(index, gameLoops.Count - 1)]);
        }

        private void SelectSlugcat(GameLoop selected)
        {
            if (selected == null) return;
            if (!ReferenceEquals(gameLoop, selected) && skinEditor != null && !skinEditor.IsDisposed)
                skinEditor.Close();
            gameLoop = selected;
            RefreshSlugcatSelectionMenu();
            RefreshActiveSlugcatsMenu();
        }

        private void RefreshSlugcatSelectionMenu()
        {
            if (gameLoop == null) return;
            for (int i = 0; i < slugcatMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = slugcatMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = (SlugcatId)item.Tag == gameLoop.SelectedSlugcat.Id;
            }
        }

        private void RefreshActiveSlugcatsMenu()
        {
            while (activeSlugcatsMenu.DropDownItems.Count > 4)
                activeSlugcatsMenu.DropDownItems.RemoveAt(4);
            for (int i = 0; i < gameLoops.Count; i++)
            {
                GameLoop loop = gameLoops[i];
                ToolStripMenuItem item = new ToolStripMenuItem(
                    T("슬러그캣 ", "Slugcat ") + (i + 1) + " · " +
                    SlugcatProfiles.SelectionLabel(loop.SelectedSlugcat.Id));
                item.Tag = loop;
                item.Checked = ReferenceEquals(loop, gameLoop);
                item.Click += delegate(object itemSender, EventArgs args)
                {
                    ToolStripMenuItem clicked = itemSender as ToolStripMenuItem;
                    if (clicked != null) SelectSlugcat(clicked.Tag as GameLoop);
                };
                activeSlugcatsMenu.DropDownItems.Add(item);
            }
            activeSlugcatsMenu.Text = T("슬러그캣", "Slugcats") + " (" + gameLoops.Count + ")";
            spawnItem.Enabled = gameLoops.Count < MaximumSlugcats;
            removeItem.Enabled = gameLoops.Count > 1;
            trayIcon.Text = T("SlugcatInMyMonitor · 실행 중: " + gameLoops.Count + "마리",
                "SlugcatInMyMonitor · Active: " + gameLoops.Count);
            RefreshSettingsWindow();
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed)
            {
                settingsWindow.RefreshFromApp();
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(this);
            if (applicationIcon != null) settingsWindow.Icon = applicationIcon;
            settingsWindow.FormClosed += delegate { settingsWindow = null; };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void RefreshSettingsWindow()
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed)
                settingsWindow.RefreshFromApp();
        }

        private void ToggleSkinEditor(object sender, EventArgs e)
        {
            if (skinEditor != null && !skinEditor.IsDisposed && skinEditor.Visible)
            {
                skinEditor.Close();
                return;
            }
            try
            {
                skinEditor = new SkinEditorWindow(gameLoop, delegate
                {
                    RefreshSlugcatSelectionMenu();
                    RefreshActiveSlugcatsMenu();
                });
                if (applicationIcon != null) skinEditor.Icon = applicationIcon;
                skinEditor.FormClosed += delegate { skinEditor = null; };
                skinEditor.Show();
                skinEditor.Activate();
            }
            catch (Exception exception)
            {
                skinEditor = null;
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, T("스킨 편집기 실행 실패", "Skin Editor Failed"),
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private static Vec2 CurrentCursorPoint()
        {
            NativeMethods.Point point;
            return NativeMethods.GetCursorPos(out point) ? new Vec2(point.X, point.Y) : Vec2.Zero;
        }

        private ToolStripMenuItem CreateSlugcatItem(string label, SlugcatId id, SlugcatId selected)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = id;
            item.Checked = id == selected;
            item.Click += SlugcatItemClick;
            return item;
        }

        private void SlugcatItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem selected = sender as ToolStripMenuItem;
            if (selected == null) return;
            for (int i = 0; i < slugcatMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = slugcatMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = ReferenceEquals(item, selected);
            }
            if (gameLoop != null) gameLoop.SetSelectedSlugcat((SlugcatId)selected.Tag);
            RefreshActiveSlugcatsMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        internal string[] SettingsSlugcatNames
        {
            get
            {
                string[] names = new string[gameLoops.Count];
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    GameLoop loop = gameLoops[i];
                    names[i] = T("슬러그캣 ", "Slugcat ") + (i + 1) + " · " +
                        SlugcatProfiles.SelectionLabel(loop.SelectedSlugcat.Id);
                }
                return names;
            }
        }

        internal int SettingsSelectedSlugcatIndex
        { get { return gameLoop == null ? -1 : gameLoops.IndexOf(gameLoop); } }

        internal bool SettingsCanAddSlugcat { get { return gameLoops.Count < MaximumSlugcats; } }
        internal bool SettingsCanRemoveSlugcat { get { return gameLoops.Count > 1; } }
        internal bool SettingsCanSelectNextSlugcat { get { return gameLoops.Count > 1; } }
        internal bool SettingsCanRetryRendering { get { return retryRenderItem.Enabled; } }
        internal bool SettingsDebugEnabled
        {
            get { return debugItem.Checked; }
            set { debugItem.Checked = value; }
        }
        internal bool SettingsPaused
        {
            get { return pauseItem.Checked; }
            set { pauseItem.Checked = value; }
        }
        internal SlugcatId SettingsSlugcatId
        { get { return gameLoop == null ? startSlugcat : gameLoop.SelectedSlugcat.Id; } }
        internal void SettingsSelectSlugcat(int index)
        {
            if (index >= 0 && index < gameLoops.Count) SelectSlugcat(gameLoops[index]);
        }

        internal void SettingsAddSlugcat() { SpawnSlugcat(null, EventArgs.Empty); }
        internal void SettingsSelectNextSlugcat() { SelectNextSlugcat(null, EventArgs.Empty); }
        internal void SettingsRemoveSelectedSlugcat() { RemoveSelectedSlugcat(null, EventArgs.Empty); }
        internal void SettingsSetSlugcat(SlugcatId id)
        {
            if (gameLoop == null) return;
            gameLoop.SetSelectedSlugcat(id);
            RefreshSlugcatSelectionMenu();
            RefreshActiveSlugcatsMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        internal void SettingsSetLanguage(UiLanguage language)
        { UiLocalization.SetLanguage(language); }

        internal string SettingsRefreshWorkshop()
        {
            RefreshAllWorkshopIntegrations();
            return gameLoop == null
                ? T("선택한 슬러그캣이 없습니다.", "No Slugcat is selected.")
                : T("Dress My Slugcat 스프라이트 시트 " + gameLoop.DmsSkins.Count + "개를 찾았습니다.",
                    gameLoop.DmsSkins.Count + " Dress My Slugcat spritesheets found.");
        }

        internal void SettingsOpenAppearanceEditor()
        {
            if (skinEditor != null && !skinEditor.IsDisposed)
            {
                skinEditor.Activate();
                return;
            }
            ToggleSkinEditor(null, EventArgs.Empty);
        }

        internal void SettingsRetryRendering() { RetryRendering(null, EventArgs.Empty); }
        internal void SettingsExitApplication() { Close(); }

        private void RefreshWorkshopItemClick(object sender, EventArgs e)
        {
            if (gameLoop == null) return;
            try
            {
                string status = SettingsRefreshWorkshop();
                trayIcon.ShowBalloonTip(2500, T("Workshop 새로 고침 완료", "Workshop Refreshed"),
                    status, ToolTipIcon.Info);
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, T("Workshop 새로 고침 실패", "Workshop Refresh Failed"), exception.Message,
                    ToolTipIcon.Warning);
            }
        }

        private void RefreshAllWorkshopIntegrations()
        {
            for (int index = 0; index < gameLoops.Count; index++)
                gameLoops[index].RefreshWorkshopIntegration();
            RefreshSettingsWindow();
        }

        private static Vec2 ScreenPointFromLParam(IntPtr value)
        {
            long packed = value.ToInt64();
            int x = (short)(packed & 0xffff);
            int y = (short)((packed >> 16) & 0xffff);
            return new Vec2(x, y);
        }

        private static string T(string korean, string english)
        { return UiLocalization.Text(korean, english); }
    }
}
