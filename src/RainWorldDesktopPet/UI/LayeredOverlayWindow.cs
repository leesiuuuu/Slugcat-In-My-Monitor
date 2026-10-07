using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using RainWorldDesktopPet.Audio;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Workshop;
using RainWorldDesktopPet.AI;
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
        private const int MaximumSlugcats = SlugcatSessionStore.MaximumPets;
        private const int MaximumFoods = 12;
        private const int DefaultRenderFramesPerSecond = 60;
        private const int DefaultRenderIntervalMilliseconds =
            (1000 + DefaultRenderFramesPerSecond - 1) / DefaultRenderFramesPerSecond;
        private const double MinimumPlausibleRefreshRate = 20.0;
        private const double MaximumPlausibleRefreshRate = 1000.0;
        private const int MinimumOverlaySize = 384;
        private const int OverlaySizeQuantum = 128;
        private const int OverlayPadding = 24;
        private const double RefreshRateCacheLifetimeSeconds = 30.0;
        private const double DuplicatePowerResumeWindowSeconds = 5.0;
        private const int WmEnsureTopMost = 0x8001;
        private const int WmHookMouseInput = 0x8002;
        private readonly RainWorldInstallation installation;
        private readonly SlugcatId startSlugcat;
        private readonly SlugcatSessionStore sessionStore;
        private readonly bool overrideSavedCharacter;
        private bool sessionReady;
        private bool sessionSaveEnabled = true;
        private bool sessionSaveErrorShown;
        private readonly InvUnlockSettings invUnlockSettings;
        private readonly Timer renderTimer;
        private readonly NotifyIcon trayIcon;
        private readonly Icon applicationIcon;
        private readonly ToolStripMenuItem slugcatMenu;
        private readonly ToolStripMenuItem refreshWorkshopItem;
        private readonly ToolStripMenuItem debugItem;
        private readonly ToolStripMenuItem retryRenderItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem muteItem;
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
        private readonly RainWorldAudioEngine audioEngine;
        private readonly Stopwatch surfaceRefreshClock = Stopwatch.StartNew();
        private readonly Stopwatch refreshRateCacheClock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<HookMouseInput> hookMouseInputs =
            new ConcurrentQueue<HookMouseInput>();
        private readonly RadialCommandMenu commandMenu = new RadialCommandMenu();
        private readonly WindowLocationChangeTracker windowLocationChanges =
            new WindowLocationChangeTracker();
        private readonly IntPtr[] liveWindowHandleBuffer =
            new IntPtr[WindowLocationChangeTracker.Capacity];
        private DirectCompositionHost compositionHost;
        private readonly string startDmsSkinId;
        private GameLoop gameLoop;
        private GameLoop grabbedGameLoop;
        private readonly NativeMethods.WinEventProc foregroundEventCallback;
        private readonly NativeMethods.WinEventProc locationEventCallback;
        private LowLevelMouseInputHook mouseHook;
        private SecretWordInputHook secretWordHook;
        private volatile MouseHookHitSnapshot mouseHitSnapshot =
            MouseHookHitSnapshot.Empty;
        private volatile RadialCommandHitSnapshot commandMenuHitSnapshot =
            RadialCommandHitSnapshot.Empty;
        private IntPtr mouseInputWindowHandle;
        private int hookOwnsLeftButton;
        private int hookOwnsRightButton;
        private IntPtr foregroundEventHook;
        private IntPtr locationEventHook;
        private IntPtr suspendResumeNotification;
        private SettingsWindow settingsWindow;
        private SkinEditorWindow skinEditor;
        private Rectangle virtualDesktopBounds;
        private bool mouseCaptured;
        private bool leftButtonDown;
        private int renderErrorCount;
        private bool renderingEnabled;
        private bool renderingFrame;
        private bool invUnlocked;
        private bool powerSuspended;
        private bool resumeRenderingAfterPowerResume;
        private long lastPowerResumeTimestamp = long.MinValue;
        private double displayRefreshRate;

        private enum HookMouseInputAction
        {
            BeginGrab,
            EndGrab,
            OpenCommandMenu,
            SelectCommand,
            CloseCommandMenu
        }

        private sealed class HookMouseInput
        {
            internal HookMouseInput(HookMouseInputAction action, GameLoop target,
                Vec2 point, DesktopPetCommand command)
            {
                Action = action;
                Target = target;
                Point = point;
                Command = command;
            }

            internal readonly HookMouseInputAction Action;
            internal readonly GameLoop Target;
            internal readonly Vec2 Point;
            internal readonly DesktopPetCommand Command;
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat)
            : this(installation, startDebug, startSlugcat, null)
        {
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat, string startDmsSkinId)
            : this(installation, startDebug, startSlugcat, startDmsSkinId, false)
        {
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat, string startDmsSkinId, bool overrideSavedCharacter)
            : this(installation, startDebug, startSlugcat, startDmsSkinId, overrideSavedCharacter,
                new SlugcatSessionStore())
        {
        }

        internal LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat, string startDmsSkinId, bool overrideSavedCharacter,
            SlugcatSessionStore sessionStore)
        {
            this.sessionStore = sessionStore;
            this.overrideSavedCharacter = overrideSavedCharacter;
            this.installation = installation;
            invUnlockSettings = new InvUnlockSettings();
            invUnlocked = invUnlockSettings.IsUnlocked;
            // A command-line/preset identity must not bypass the same hidden
            // selection gate used by the visible controls.
            this.startSlugcat = startSlugcat == SlugcatId.Inv && !invUnlocked
                ? SlugcatId.White : startSlugcat;
            this.startDmsSkinId = startDmsSkinId;
            foregroundEventCallback = ForegroundEventCallback;
            locationEventCallback = LocationEventCallback;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            virtualDesktopBounds = MonitorManager.GetVirtualBounds();
            audioEngine = new RainWorldAudioEngine(installation, virtualDesktopBounds);
            audioEngine.SetMasterVolume(AudioVolumeSettings.Current);
            audioEngine.SetMuted(AudioMuteSettings.Current);
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
            muteItem = new ToolStripMenuItem(T("사운드 음소거", "Mute Audio"));
            muteItem.CheckOnClick = true;
            muteItem.Checked = audioEngine.Muted;
            muteItem.CheckedChanged += delegate
            {
                audioEngine.SetMuted(muteItem.Checked);
                AudioMuteSettings.Set(muteItem.Checked);
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
            RebuildSlugcatSelectionMenu();
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
            menu.Items.Add(muteItem);
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
            InstallSecretWordHook();
            InstallForegroundEventHook();
            InstallLocationEventHook();
            EnsureOverlayTopMost();
            compositionHost = new DirectCompositionHost(Handle, virtualDesktopBounds);
            RegisterForSuspendResumeNotifications();
            collisionWorld.Refresh(Handle);
            surfaceRefreshClock.Restart();
            RestoreSession();
            if (overrideSavedCharacter) gameLoop.SetSelectedSlugcat(startSlugcat);
            if (!string.IsNullOrWhiteSpace(startDmsSkinId))
            {
                string reason;
                if (!gameLoop.SetDmsSkin(startDmsSkinId, out reason))
                    trayIcon.ShowBalloonTip(5000, T("DMS 스킨을 사용할 수 없음", "DMS Skin Unavailable"), reason, ToolTipIcon.Warning);
            }
            sessionReady = true;
            RefreshSlugcatSelectionMenu();
            RefreshActiveSlugcatsMenu();
            SaveSession();
        }

        private void RestoreSession()
        {
            SlugcatSession session = null;
            List<string> warnings = new List<string>();
            try
            {
                string warning;
                session = sessionStore.Load(out warning);
                if (warning != null) warnings.Add(warning);
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                warnings.Add(exception.Message);
                if (exception is NotSupportedException) sessionSaveEnabled = false;
            }
            if (session != null)
            {
                foreach (SlugcatSessionPet pet in session.Pets)
                {
                    AddSlugcat(pet.ResolveCharacter(invUnlocked));
                    try
                    {
                        pet.Apply(gameLoop, invUnlocked, warnings.Add);
                        SlugpupSettingsBridge.SynchronizeRestoredAppearance(gameLoop);
                    }
                    catch (Exception exception)
                    {
                        Program.LogException(exception);
                        warnings.Add(exception.Message);
                    }
                }
                SelectSlugcat(gameLoops[session.SelectedIndex]);
            }
            else AddSlugcat(startSlugcat);
            if (warnings.Count > 0)
            {
                foreach (string warning in warnings)
                    Program.LogException(new InvalidOperationException(warning));
                trayIcon.ShowBalloonTip(5000, T("슬러그캣 복원 알림", "Slugcat Restore Notice"),
                    sessionSaveEnabled
                        ? T("저장 데이터 또는 스킨 복원 중 문제가 발생했습니다. 자세한 내용은 errors.log를 확인하세요.",
                            "Saved data or skins had restoration issues. See errors.log for details.")
                        : T("더 최신 형식의 저장 파일을 보호하기 위해 자동 저장을 중단했습니다.",
                            "Automatic saving is disabled to protect a newer session format."),
                    ToolTipIcon.Warning);
            }
        }

        private void SaveSession()
        {
            if (!sessionReady || !sessionSaveEnabled || gameLoops.Count == 0) return;
            try
            {
                SlugcatSession session = new SlugcatSession
                {
                    Version = SlugcatSessionStore.CurrentVersion,
                    SelectedIndex = gameLoops.IndexOf(gameLoop),
                    Pets = new List<SlugcatSessionPet>()
                };
                foreach (GameLoop loop in gameLoops) session.Pets.Add(SlugcatSessionPet.Capture(loop));
                sessionStore.Save(session);
                sessionSaveErrorShown = false;
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                if (!sessionSaveErrorShown)
                    trayIcon.ShowBalloonTip(5000, T("슬러그캣 저장 실패", "Unable to Save Slugcats"),
                        exception.Message, ToolTipIcon.Warning);
                sessionSaveErrorShown = true;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            SaveSession();
            sessionReady = false;
            renderingEnabled = false;
            renderTimer.Stop();
            UnregisterForSuspendResumeNotifications();
            UninstallLocationEventHook();
            UninstallForegroundEventHook();
            UninstallSecretWordHook();
            UninstallMouseHook();
            ReleaseGrabInput();
            if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Close();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.Close();
            CloseCommandSelection(true);
            for (int i = 0; i < gameLoops.Count; i++) gameLoops[i].Dispose();
            gameLoops.Clear();
            gameLoop = null;
            commandMenu.Dispose();
            AudioMuteSettings.Set(audioEngine.Muted);
            audioEngine.Dispose();
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
                UpdateCommandMenu();
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
                                    debug ? loop.AssetStatus + "\n" + loop.AudioStatus :
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
                                    poseBuffer[loopIndex],
                                    layer == OverlayRenderLayer.HeldFood);
                            else
                                loop.Renderer.RenderFoodsGpu(gpuCanvas,
                                    loop.Foods, renderSpace,
                                    poseBuffer[loopIndex],
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
                    RectangleF visibleEffectBounds;
                    if (!effectContentBounds.IsEmpty && TryClipVisibleContent(
                            effectContentBounds, virtualDesktopBounds,
                            out visibleEffectBounds))
                    {
                        Rectangle effectBounds = compositionHost.PrepareEffectBounds(
                            batchIndex, visibleEffectBounds);
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
                int activeSurfaceCount = batches.Count;
                if (commandMenu.IsVisible)
                {
                    Rectangle commandBounds = Rectangle.Intersect(
                        commandMenu.GetRenderBounds(), virtualDesktopBounds);
                    if (commandBounds.Width > 0 && commandBounds.Height > 0)
                    {
                        DirectCompositionHost.CompositionSurface commandSurface =
                            compositionHost.PrepareSurface(activeSurfaceCount,
                                commandBounds);
                        System.Drawing.Graphics commandGraphics =
                            commandSurface.Graphics;
                        commandGraphics.CompositingMode =
                            System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        commandGraphics.Clear(Color.Transparent);
                        commandGraphics.CompositingMode =
                            System.Drawing.Drawing2D.CompositingMode.SourceOver;
                        commandMenu.Render(commandGraphics, commandSurface.Bounds);
                        compositionHost.Present(activeSurfaceCount);
                        activeSurfaceCount++;
                    }
                }
                compositionHost.Commit(activeSurfaceCount);
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
            collisionWorld.TryApplyPendingRefresh();

            ApplyPendingLiveWindowTranslations();
            if (surfaceRefreshClock.Elapsed.TotalSeconds <
                SimulationConstants.WindowRefreshSeconds) return;

            collisionWorld.RequestRefresh(Handle);
            surfaceRefreshClock.Restart();
        }

        private void ApplyPendingLiveWindowTranslations()
        {
            // WinEvent is edge-triggered and allocation-free. Drain it once
            // per rendered frame so 120/144/240 Hz displays can publish the
            // newest collision-box location without polling when no HWND moved.
            int count = windowLocationChanges.Drain(liveWindowHandleBuffer);
            if (count == 0) return;

            collisionWorld.BeginLiveWindowTranslationBatch();
            bool needsFullRefresh = false;
            for (int i = 0; i < count; i++)
            {
                LiveWindowTranslationResult result =
                    collisionWorld.ApplyLiveWindowTranslation(
                        liveWindowHandleBuffer[i]);
                liveWindowHandleBuffer[i] = IntPtr.Zero;
                if (result == LiveWindowTranslationResult.RequiresFullRefresh)
                    needsFullRefresh = true;
            }
            if (needsFullRefresh) collisionWorld.RequestRefresh(Handle);
        }

        private void UpdateRenderCadence(SlugcatPose[] poses, int poseCount)
        {
            if (refreshRateCacheClock.Elapsed.TotalSeconds >=
                RefreshRateCacheLifetimeSeconds)
            {
                displayRefreshRates.Clear();
                refreshRateCacheClock.Restart();
            }
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
            refreshRate = NormalizeRefreshRate(refreshRate);
            displayRefreshRate = refreshRate;
            int interval = Math.Max(1, (int)Math.Round(1000.0 / refreshRate));
            if (renderTimer.Interval != interval) renderTimer.Interval = interval;
        }

        private void RegisterForSuspendResumeNotifications()
        {
            try
            {
                suspendResumeNotification =
                    NativeMethods.RegisterSuspendResumeNotification(Handle,
                        NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 7 still broadcasts conventional suspend messages to
                // top-level windows even though explicit registration is absent.
                suspendResumeNotification = IntPtr.Zero;
            }
        }

        private void UnregisterForSuspendResumeNotifications()
        {
            if (suspendResumeNotification == IntPtr.Zero) return;
            NativeMethods.UnregisterSuspendResumeNotification(
                suspendResumeNotification);
            suspendResumeNotification = IntPtr.Zero;
        }

        private void SuspendForPowerTransition()
        {
            powerSuspended = true;
            resumeRenderingAfterPowerResume = renderingEnabled &&
                renderTimer.Enabled;
            lastPowerResumeTimestamp = long.MinValue;
            renderTimer.Stop();
            windowLocationChanges.Clear();
            ReleaseGrabInput();
        }

        private void ResumeFromPowerTransition()
        {
            long now = Stopwatch.GetTimestamp();
            if (!ShouldProcessPowerResume(lastPowerResumeTimestamp, now,
                    Stopwatch.Frequency))
                return;
            lastPowerResumeTimestamp = now;

            bool shouldRestart = powerSuspended
                ? resumeRenderingAfterPowerResume
                : renderingEnabled && !retryRenderItem.Enabled;
            powerSuspended = false;
            resumeRenderingAfterPowerResume = false;
            renderTimer.Stop();
            try
            {
                ReleaseGrabInput();
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].ResetFrameTiming();
                surfaceRefreshClock.Restart();
                displayRefreshRates.Clear();
                refreshRateCacheClock.Restart();
                // Do not cache a potentially transitional display query. Start
                // at a safe cadence; the next rendered pose selects its monitor.
                ApplyRenderCadence(DefaultRenderFramesPerSecond);
                ConfigureVirtualDesktop();
                if (compositionHost != null) compositionHost.ResetSurfaces();
                collisionWorld.RequestRefresh(Handle);
                EnsureOverlayTopMost();
                if (shouldRestart)
                {
                    renderErrorCount = 0;
                    retryRenderItem.Enabled = false;
                    renderingEnabled = true;
                    renderTimer.Start();
                }
                RefreshSettingsWindow();
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                renderingEnabled = false;
                renderTimer.Stop();
                retryRenderItem.Enabled = true;
                RefreshSettingsWindow();
            }
        }

        internal static bool IsSuspendPowerEvent(int powerEvent)
        {
            return powerEvent == NativeMethods.PBT_APMSUSPEND;
        }

        internal static bool IsResumePowerEvent(int powerEvent)
        {
            return powerEvent == NativeMethods.PBT_APMRESUMEAUTOMATIC ||
                powerEvent == NativeMethods.PBT_APMRESUMESUSPEND ||
                powerEvent == NativeMethods.PBT_APMRESUMECRITICAL;
        }

        internal static double NormalizeRefreshRate(double refreshRate)
        {
            return double.IsNaN(refreshRate) || double.IsInfinity(refreshRate) ||
                refreshRate < MinimumPlausibleRefreshRate ||
                refreshRate > MaximumPlausibleRefreshRate
                ? DefaultRenderFramesPerSecond
                : refreshRate;
        }

        internal static bool ShouldProcessPowerResume(long previousTimestamp,
            long currentTimestamp, long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException("frequency");
            if (previousTimestamp == long.MinValue) return true;
            long elapsedTicks = currentTimestamp - previousTimestamp;
            return elapsedTicks < 0 || elapsedTicks / (double)frequency >=
                DuplicatePowerResumeWindowSeconds;
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
                Vec2 spearPosition = spear.Chunk.RenderPosition(pose.TimeStacker);
                Vec2 center = spear.Mode == DesktopSpearMode.Held
                    ? pose.ToRenderedWorld(spearPosition)
                    : pose.ToRenderedStaticWorld(spearPosition);
                RectangleF spearBounds = new RectangleF((float)(center.X - 28.0),
                    (float)(center.Y - 28.0), 56.0f, 56.0f);
                content = RectangleF.Union(content, spearBounds);
                if (!spear.HasUmbilical) continue;
                Vec2[] points = spear.Umbilical;
                for (int point = 0; point < points.Length; point++)
                {
                    Vec2 rendered = pose.ToRenderedStaticWorld(Vec2.Lerp(
                        spear.LastUmbilical[point], points[point], pose.TimeStacker));
                    content = RectangleF.Union(content, new RectangleF(
                        (float)(rendered.X - 2.0), (float)(rendered.Y - 2.0),
                        4.0f, 4.0f));
                }
            }
            for (int i = 0; i < loop.Foods.Foods.Count; i++)
            {
                DesktopFood food = loop.Foods.Foods[i];
                if (!food.IsActive || SpriteRenderer.IsFoodAttachedToSlugcat(food))
                    continue;
                Vec2 center = SpriteRenderer.ResolveFoodRenderPosition(pose,
                    food.Chunk.RenderPosition(pose.TimeStacker), false);
                double reach = food.VisualReach *
                    SpriteRenderer.ResolveFoodRenderScale(pose, false);
                content = RectangleF.Union(content, new RectangleF(
                    (float)(center.X - reach), (float)(center.Y - reach),
                    (float)(reach * 2.0), (float)(reach * 2.0)));
            }
            DesktopFood heldFood = loop.Foods.HeldFoodForRender;
            if (heldFood != null)
            {
                Vec2 center = SpriteRenderer.ResolveFoodRenderPosition(pose,
                    heldFood.Chunk.RenderPosition(pose.TimeStacker), true);
                double reach = heldFood.VisualReach *
                    SpriteRenderer.ResolveFoodRenderScale(pose, true);
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
                    Vec2 currentPoint = pose.ToRenderedStaticWorld(currentRope[point]);
                    Vec2 previousPoint = pose.ToRenderedStaticWorld(previousRope[point]);
                    content = RectangleF.Union(content, new RectangleF(
                        (float)(currentPoint.X - 8.0), (float)(currentPoint.Y - 8.0),
                        16.0f, 16.0f));
                    content = RectangleF.Union(content, new RectangleF(
                        (float)(previousPoint.X - 8.0), (float)(previousPoint.Y - 8.0),
                        16.0f, 16.0f));
                }
            }

            // DirectComposition surfaces only need to cover pixels that can be
            // seen on the virtual desktop.  A far-thrown Spearmaster needle or
            // Saint tongue used to expand this rectangle without a limit.  That
            // allocated very large GPU surfaces, caused severe compositor stalls,
            // and eventually made BeginDraw/PresentGpu fail with E_INVALIDARG.
            RectangleF visibleContent;
            if (TryClipVisibleContent(content, virtualDesktopBounds,
                    out visibleContent))
            {
                content = visibleContent;
            }
            else
            {
                content = new RectangleF(
                    virtualDesktopBounds.Left + virtualDesktopBounds.Width * 0.5f,
                    virtualDesktopBounds.Top + virtualDesktopBounds.Height * 0.5f,
                    1.0f, 1.0f);
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

        internal static bool TryClipVisibleContent(RectangleF content,
            Rectangle desktop, out RectangleF visible)
        {
            visible = RectangleF.Empty;
            if (desktop.Width <= 0 || desktop.Height <= 0 ||
                float.IsNaN(content.Left) || float.IsNaN(content.Top) ||
                float.IsNaN(content.Right) || float.IsNaN(content.Bottom) ||
                float.IsInfinity(content.Left) || float.IsInfinity(content.Top) ||
                float.IsInfinity(content.Right) || float.IsInfinity(content.Bottom))
                return false;

            float left = Math.Max(content.Left, desktop.Left);
            float top = Math.Max(content.Top, desktop.Top);
            float right = Math.Min(content.Right, desktop.Right);
            float bottom = Math.Min(content.Bottom, desktop.Bottom);
            if (right <= left || bottom <= top) return false;
            visible = RectangleF.FromLTRB(left, top, right, bottom);
            return true;
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

        private void InstallSecretWordHook()
        {
            if (secretWordHook != null) return;
            SecretWordInputHook installed = new SecretWordInputHook(QueueInvToggle);
            try
            {
                installed.Start();
                secretWordHook = installed;
            }
            catch (Exception exception)
            {
                installed.Dispose();
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000,
                    T("Inv 시크릿 입력을 감지할 수 없음", "Inv Secret Input Unavailable"),
                    exception.Message, ToolTipIcon.Warning);
            }
        }

        private void UninstallSecretWordHook()
        {
            SecretWordInputHook installed = secretWordHook;
            secretWordHook = null;
            if (installed != null) installed.Dispose();
        }

        private void QueueInvToggle()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(ToggleInv)); }
            catch (InvalidOperationException) { }
        }

        private void ToggleInv()
        {
            if (IsDisposed) return;
            bool enable = !invUnlocked;
            string reason;
            bool persisted = enable
                ? invUnlockSettings.TryUnlock(out reason)
                : invUnlockSettings.TryLock(out reason);
            if (!persisted)
            {
                trayIcon.ShowBalloonTip(5000,
                    enable
                        ? T("Inv 해제 저장 실패", "Unable to Save Inv Unlock")
                        : T("Inv 비활성화 저장 실패", "Unable to Save Inv Deactivation"),
                    reason ?? T("Inv 상태를 저장하지 못했습니다.",
                        "The Inv state could not be saved."), ToolTipIcon.Error);
                return;
            }

            invUnlocked = enable;
            if (!enable)
            {
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    if (gameLoops[i].SelectedSlugcat.Id == SlugcatId.Inv)
                        gameLoops[i].SetSelectedSlugcat(SlugcatId.White);
                }
            }
            RebuildSlugcatSelectionMenu();
            if (skinEditor != null && !skinEditor.IsDisposed)
                skinEditor.SetInvUnlocked(enable);
            RefreshActiveSlugcatsMenu();
            SaveSession();
            trayIcon.ShowBalloonTip(5000,
                enable
                    ? T("시크릿 캐릭터 해제", "Secret Character Unlocked")
                    : T("시크릿 캐릭터 비활성화", "Secret Character Deactivated"),
                enable
                    ? T("Inv(인브)가 캐릭터 선택란에 추가되었습니다.",
                        "Inv is now available in the character selector.")
                    : T("Inv(인브)가 캐릭터 선택란에서 숨겨졌습니다.",
                        "Inv is now hidden from the character selector."),
                ToolTipIcon.Info);
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

        private void InstallLocationEventHook()
        {
            if (locationEventHook != IntPtr.Zero) return;
            locationEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, locationEventCallback, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT |
                NativeMethods.WINEVENT_SKIPOWNPROCESS);
            if (locationEventHook == IntPtr.Zero)
                Program.LogException(new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to monitor live window movement."));
        }

        private void UninstallLocationEventHook()
        {
            IntPtr hook = locationEventHook;
            locationEventHook = IntPtr.Zero;
            if (hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(hook);
            windowLocationChanges.Clear();
        }

        private void LocationEventCallback(IntPtr hook, uint eventType, IntPtr handle,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (eventType != NativeMethods.EVENT_OBJECT_LOCATIONCHANGE ||
                objectId != NativeMethods.OBJID_WINDOW ||
                childId != NativeMethods.CHILDID_SELF) return;
            windowLocationChanges.Record(handle);
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
                int owner = Interlocked.Exchange(ref hookOwnsLeftButton, 0);
                if (owner == 0)
                    return false;
                if (owner == 1)
                    QueueHookMouseInput(new HookMouseInput(
                        HookMouseInputAction.EndGrab, null,
                        new Vec2(nativePoint.X, nativePoint.Y),
                        DesktopPetCommand.Move));
                return true;
            }

            if (mouseMessage == NativeMethods.WM_RBUTTONUP)
                return Interlocked.Exchange(ref hookOwnsRightButton, 0) != 0;

            MouseHookHitSnapshot snapshot = mouseHitSnapshot;
            Vec2 point = new Vec2(nativePoint.X, nativePoint.Y);

            if (mouseMessage == NativeMethods.WM_RBUTTONDOWN ||
                mouseMessage == NativeMethods.WM_RBUTTONDBLCLK)
            {
                GameLoop rightHit = snapshot.HitTest(point) as GameLoop;
                if (rightHit == null)
                {
                    if (commandMenuHitSnapshot.Target != null)
                        QueueHookMouseInput(new HookMouseInput(
                            HookMouseInputAction.CloseCommandMenu, null, point,
                            DesktopPetCommand.Move));
                    return false;
                }
                if (Interlocked.CompareExchange(ref hookOwnsRightButton, 1, 0) != 0)
                    return true;
                QueueHookMouseInput(new HookMouseInput(
                    HookMouseInputAction.OpenCommandMenu, rightHit, point,
                    DesktopPetCommand.Move));
                return true;
            }

            RadialCommandHitSnapshot commandSnapshot = commandMenuHitSnapshot;
            if (commandSnapshot.Target != null)
            {
                DesktopPetCommand command;
                if (commandSnapshot.TryHit(point, out command))
                {
                    if (Interlocked.CompareExchange(ref hookOwnsLeftButton, 2, 0) != 0)
                        return true;
                    QueueHookMouseInput(new HookMouseInput(
                        HookMouseInputAction.SelectCommand,
                        commandSnapshot.Target, point, command));
                    return true;
                }

                QueueHookMouseInput(new HookMouseInput(
                    HookMouseInputAction.CloseCommandMenu, null, point,
                    DesktopPetCommand.Move));
                if (commandSnapshot.Contains(point))
                {
                    Interlocked.CompareExchange(ref hookOwnsLeftButton, 2, 0);
                    return true;
                }
                return false;
            }

            GameLoop hit = snapshot.HitTest(point) as GameLoop;
            if (hit == null) return false;
            if (Interlocked.CompareExchange(ref hookOwnsLeftButton, 1, 0) != 0)
                return true;
            QueueHookMouseInput(new HookMouseInput(
                HookMouseInputAction.BeginGrab, hit, point,
                DesktopPetCommand.Move));
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
                if (input.Action == HookMouseInputAction.EndGrab)
                {
                    ReleaseGrabInput();
                    leftButtonDown = false;
                    continue;
                }

                if (input.Action == HookMouseInputAction.CloseCommandMenu)
                {
                    CloseCommandSelection(false);
                    continue;
                }

                if (input.Action == HookMouseInputAction.OpenCommandMenu)
                {
                    if (!gameLoops.Contains(input.Target)) continue;
                    ReleaseGrabInput();
                    SelectSlugcat(input.Target);
                    Vec2 center = input.Target.ToRenderedScreen(
                        input.Target.Slugcat.Center);
                    MonitorInfo monitor = MonitorManager.FindNearest(new Point(
                        (int)Math.Round(center.X), (int)Math.Round(center.Y)));
                    CloseCommandSelection(true);
                    input.Target.SetCommandSelectionPending(true);
                    commandMenu.Open(input.Target, center, monitor.WorkArea);
                    commandMenuHitSnapshot = RadialCommandHitSnapshot.Empty;
                    continue;
                }

                if (input.Action == HookMouseInputAction.SelectCommand)
                {
                    if (gameLoops.Contains(input.Target))
                        input.Target.SetCommand(input.Command);
                    CloseCommandSelection(false);
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
            if (message.Msg == NativeMethods.WM_POWERBROADCAST)
            {
                int powerEvent = message.WParam.ToInt32();
                if (IsSuspendPowerEvent(powerEvent))
                    SuspendForPowerTransition();
                else if (IsResumePowerEvent(powerEvent))
                    ResumeFromPowerTransition();
                else
                {
                    base.WndProc(ref message);
                    return;
                }
                message.Result = new IntPtr(1);
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
                    refreshRateCacheClock.Restart();
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

        internal static Vec2 ResolveWorldFoodHitCenter(Vec2 simulationPosition)
        {
            return DesktopWorldTransform.ToDesktop(simulationPosition);
        }

        internal static double ResolveWorldFoodHitRadius(double simulationRadius)
        {
            return DesktopWorldTransform.ToDesktopLength(simulationRadius);
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
                        ResolveWorldFoodHitCenter(food.Chunk.Position),
                        ResolveWorldFoodHitRadius(food.VisualReach + 5.0));
                }
                for (int chunkIndex = 0;
                    chunkIndex < loop.Slugcat.BodyChunks.Length; chunkIndex++)
                {
                    BodyChunk chunk = loop.Slugcat.BodyChunks[chunkIndex];
                    circles[circleCount++] = new MouseHookHitCircle(
                        loop.ToRenderedScreen(chunk.Position),
                        loop.ToRenderedScreenLength(chunk.Radius + 14.0));
                }
                circles[circleCount++] = new MouseHookHitCircle(
                    loop.ToRenderedScreen(loop.Graphics.Head.Position),
                    loop.ToRenderedScreenLength(17.0));
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
                gameLoops.Count, collisionWorld, audioEngine);
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
            if (ReferenceEquals(commandMenu.Target, removed))
                CloseCommandSelection(true);
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
            SaveSession();
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

        private void RebuildSlugcatSelectionMenu()
        {
            SlugcatId selected = gameLoop == null
                ? startSlugcat : gameLoop.SelectedSlugcat.Id;
            slugcatMenu.DropDownItems.Clear();
            IList<SlugcatProfile> selectable = SlugcatProfiles.Selectable(invUnlocked);
            for (int i = 0; i < selectable.Count; i++)
            {
                SlugcatProfile profile = selectable[i];
                slugcatMenu.DropDownItems.Add(CreateSlugcatItem(
                    SlugcatProfiles.SelectionLabel(profile.Id), profile.Id, selected));
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
                    SaveSession();
                }, invUnlocked);
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
            SlugcatId selectedId = (SlugcatId)selected.Tag;
            if (selectedId == SlugcatId.Inv && !invUnlocked) return;
            for (int i = 0; i < slugcatMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = slugcatMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = ReferenceEquals(item, selected);
            }
            if (gameLoop != null) gameLoop.SetSelectedSlugcat(selectedId);
            RefreshActiveSlugcatsMenu();
            SaveSession();
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
        internal bool SettingsAudioMuted
        {
            get { return muteItem.Checked; }
            set { muteItem.Checked = value; }
        }
        internal int SettingsAudioVolumePercent
        {
            get { return (int)Math.Round(audioEngine.MasterVolume * 100.0); }
            set
            {
                double volume = AudioVolumeSettings.Clamp(value / 100.0);
                audioEngine.SetMasterVolume(volume);
            }
        }

        private void UpdateCommandMenu()
        {
            GameLoop target = commandMenu.Target;
            if (target == null)
            {
                commandMenuHitSnapshot = RadialCommandHitSnapshot.Empty;
                return;
            }
            if (!gameLoops.Contains(target))
            {
                CloseCommandSelection(true);
                commandMenuHitSnapshot = RadialCommandHitSnapshot.Empty;
                return;
            }

            Vec2 center = target.ToRenderedScreen(target.Slugcat.Center);
            MonitorInfo monitor = MonitorManager.FindNearest(new Point(
                (int)Math.Round(center.X), (int)Math.Round(center.Y)));
            Point pointer = Cursor.Position;
            commandMenu.Update(center, monitor.WorkArea,
                new Vec2(pointer.X, pointer.Y));
            commandMenuHitSnapshot = commandMenu.CreateHitSnapshot();
        }

        private void CloseCommandSelection(bool immediately)
        {
            GameLoop target = commandMenu.Target;
            if (target != null) target.SetCommandSelectionPending(false);
            if (immediately) commandMenu.CloseImmediately();
            else commandMenu.Close();
        }
        internal void SettingsPersistAudioVolume()
        { AudioVolumeSettings.Set(audioEngine.MasterVolume); }
        internal string SettingsAudioStatus { get { return audioEngine.Status; } }
        internal SlugcatId SettingsSlugcatId
        { get { return gameLoop == null ? startSlugcat : gameLoop.SelectedSlugcat.Id; } }
        internal bool SettingsInvUnlocked { get { return invUnlocked; } }
        internal SlugcatSize SettingsSlugcatSize
        { get { return gameLoop == null ? SlugcatSize.Large : gameLoop.Size; } }
        internal void SettingsSelectSlugcat(int index)
        {
            if (index >= 0 && index < gameLoops.Count) SelectSlugcat(gameLoops[index]);
        }

        internal void SettingsAddSlugcat() { SpawnSlugcat(null, EventArgs.Empty); }
        internal void SettingsSelectNextSlugcat() { SelectNextSlugcat(null, EventArgs.Empty); }
        internal void SettingsRemoveSelectedSlugcat() { RemoveSelectedSlugcat(null, EventArgs.Empty); }
        internal void SettingsSetSlugcat(SlugcatId id)
        {
            if (gameLoop == null || id == SlugcatId.Inv && !invUnlocked) return;
            gameLoop.SetSelectedSlugcat(id);
            RefreshSlugcatSelectionMenu();
            RefreshActiveSlugcatsMenu();
            SaveSession();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        internal void SettingsSetSlugcatSize(SlugcatSize size)
        {
            if (gameLoop == null) return;
            gameLoop.SetSize(size);
            PublishMouseHitSnapshot();
            SaveSession();
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
