using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.UI;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--preview")
            {
                try
                {
                    RenderPreview(args[1], args.Length >= 3 ? ReadVariant(args[2]) : SlugcatVariant.Survivor,
                        args.Length >= 4 ? args[3] : "walk",
                        args.Length >= 5 ? ReadSkin(args[4]) : SlugcatSkin.Default,
                        args.Length >= 6 ? args[5] : null);
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.WriteLine("Preview failed: " + exception.GetType().FullName);
                    Console.WriteLine(exception.Message);
                    return 1;
                }
            }
            if (args.Length >= 2 && args[0] == "--food-preview")
            {
                try
                {
                    RenderFoodPreview(args[1]);
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.WriteLine("Food preview failed: " +
                        exception.GetType().FullName);
                    Console.WriteLine(exception.Message);
                    return 1;
                }
            }

            Run("FixedTimeStep uses 40 Hz independently of render rate", FixedStepUsesFortyHertz);
            Run("Desktop world transform scales original X/Y travel uniformly", DesktopWorldTransformScalesTravelUniformly);
            Run("Original horizontal acceleration and friction match input order", OriginalHorizontalInputParity);
            Run("Crawl reversal uses Player's 0.75 dynamicRunSpeed branch",
                CrawlReverseUsesOriginalDynamicRunSpeed);
            Run("Normal Flip and belly-reversal Flip keep distinct angular forces",
                FlipAngularForceUsesOriginalEntryKind);
            Run("Backflip entry keeps Player.Jump launch values and state transition",
                BackflipEntryMatchesOriginalJumpBranch);
            Run("Down input changes standing intent before physical Crawl", OriginalPostureTransitionUsesPhysics);
            Run("Both BodyChunks resolve against one immutable tick snapshot", BodyChunksShareFrozenSnapshot);
            Run("60/144/240 Hz rendering preserves identical physics and animation", RefreshRatesPreservePhysicsAndAnimation);
            Run("Original free-fall curve remains per-tick at 40 Hz", OriginalFreeFallCurve);
            Run("Swept free-fall collision lands on the desktop floor", FreeFallLandsOnDesktopFloor);
            Run("BodyChunkConnection projects to its target distance", ConnectionProjectsDistance);
            Run("Desktop floor collision prevents tunneling", DesktopFloorCollision);
            Run("Blue Fruit preserves the original three-bite edible contract",
                DangleFruitPreservesOriginalEdibleContract);
            Run("Eggbug Egg preserves its two-bite layered edible contract",
                EggBugEggPreservesOriginalEdibleContract);
            Run("Food visual bounds include fruit body and Eggbug tail",
                FoodVisualBoundsIncludeProceduralParts);
            Run("Blue Fruit and Eggbug Egg settle on flat desktop floors",
                FoodItemsSettleOnFlatFloor);
            Run("Food airborne and grounded rotation follows the local item DLLs",
                FoodRotationMatchesOriginalItemRules);
            Run("Eggbug Egg tail follows its original five-segment animation",
                EggBugEggTailMatchesOriginalProceduralAnimation);
            Run("Blue Fruit and Eggbug Egg can be repositioned with the mouse",
                FoodItemsSupportMouseDragging);
            Run("Food manager clear resets every interaction flag",
                FoodClearResetsInteractionState);
            Run("Food fallback remains visible without a local atlas",
                FoodFallbackRendersWithoutAtlas);
            Run("Renderer color-resource caches remain bounded",
                RendererColorResourceCachesRemainBounded);
            Run("Food palettes preserve Blue Fruit layers and normal Eggbug hue",
                FoodPalettesMatchOriginalColorRules);
            Run("Food interaction seeks, reserves, and consumes through VirtualInput",
                FoodInteractionUsesVirtualInputAndConsumes);
            Run("Food bite animation matches PlayerGraphics BiteFly cadence",
                FoodBiteAnimationMatchesOriginalCadence);
            Run("Spearmaster holds food for one to three seconds then tosses it",
                SpearmasterTossesFoodWithoutEating);
            Run("Crawl eating starts at the planted hand and moves toward the mouth",
                CrawlFoodMovesFromHandToMouth);
            Run("Food offers use a farther randomized drop distance",
                FoodSpawnUsesFarRandomizedDrop);
            Run("Fullness prevents five consecutive guaranteed meals",
                FullnessPreventsGuaranteedEating);
            Run("Each monitor contributes floor, taskbar, and exposed boundaries", MonitorTerrainTopologyIsExplicit);
            Run("Window-edge falls land on the first lower window", WindowEdgeFallLandsOnLowerWindow);
            Run("Window-edge falls with empty space land on monitor terrain", EmptyAreaFallLandsOnMonitorFloor);
            Run("Negative and staggered monitors keep continuous terrain identity", MultiMonitorTopologyUsesVirtualCoordinates);
            Run("Offscreen throws recover to a visible monitor floor", OffscreenThrowRecoveryTarget);
            Run("Connection penetration cannot become an infinite desktop fall", LongFloorContactSurvivesConnectionPenetration);
            Run("Monitor floor corners survive post-connection penetration", MonitorCornerSurvivesConnectionPenetration);
            Run("Swept high-speed travel cannot tunnel through a small window", FastHorizontalSmallWindowDoesNotTunnel);
            Run("Dragging passes through window walls", DraggingPassesThroughWindowWalls);
            Run("Slugcat dragging blocks desktop pointer interactions",
                SlugcatDraggingBlocksDesktopInteractions);
            Run("Mouse hook hit snapshots preserve click-through and topmost order",
                MouseHookHitSnapshotsPreserveInputRules);
            Run("AI produces VirtualInput without moving physics directly", AiDoesNotMoveCreature);
            Run("Futile atlas metadata parses frame geometry", AtlasMetadataParses);
            Run("DMS part atlas overrides and restores original sprites", DmsPartAtlasOverrideRestoresBase);
            Run("DMS sprites beside the executable are discovered", DmsSpritesBesideExecutableAreDiscovered);
            Run("Customize colors reach each rendered sprite part", PartColorsReachRenderedPose);
            Run("Rain World locator validates an explicit installation", LocatorValidatesExplicitPath);
            Run("Required autonomous behavior states are present", RequiredBehaviorsExist);
            Run("Jump and DropDown utility states are reachable", UtilityActionsAreReachable);
            Run("Exploration intent makes free jumps reachable", ExplorationJumpIsReachable);
            Run("Obstacle contact makes an original jump attempt reachable", ObstacleJumpIsReachable);
            Run("Mouse locomotion requires explicit click attention", MouseLocomotionRequiresAttention);
            Run("Wall contact reaches gravity-driven WallClimb through VirtualInput", WallContactReachesClimbMovement);
            Run("WallClimb hands use alternating wall targets", WallClimbHandsTargetTheWall);
            Run("Sleep curl pulls both hands to the original target", SleepCurlHandsShareOriginalTarget);
            Run("Moving window walls carry a climbing Slugcat", MovingWindowWallCarriesClimber);
            Run("Moving windows carry both chunks for fast motion in every direction", MovingWindowCarriesConnectedBody);
            Run("Desktop window enumeration publishes snapshots asynchronously",
                DesktopRefreshIsAsynchronous);
            Run("Transient HWND enumeration misses retain then expire surfaces", TransientWindowMissesUseGracePeriod);
            Run("Stale limb grips release when their HWND surface disappears", StaleLimbGripReleases);
            Run("Occluded windows do not create hidden surfaces", OccludedWindowsAreClipped);
            Run("Monitor-ceiling window tops cannot hide the Slugcat", MonitorCeilingWindowTopIsRejected);
            Run("PlayerGraphics face frame uses the body-head axis", OriginalFaceFrameSelection);
            Run("Original face resolver matches movement and airborne states", OriginalFaceResolverMatchesDllStates);
            Run("Original slugcat variants match local DLL constants", OriginalVariantValues);
            Run("PlayerGraphics tail uses the original four-segment layout", OriginalTailLayout);
            Run("All render paths expose one continuous original tail mesh", OriginalTailMeshIsContinuous);
            Run("Tail mesh topology stays continuous through movement and stun", TailMeshStaysContinuousAcrossStates);
            Run("Sit and sleep flow through VirtualInput into movement", RestPosturesUseVirtualInput);
            Run("Movement wakes a curled rest posture before locomotion", MovementWakesRestPosture);
            Run("Original Stand forces keep the upper body upright", StandForcesKeepUpperBodyUpright);
            Run("Idle and rest poses do not cycle walking frames", IdleAndRestFramesStayStill);
            Run("Crawl idle has zero facing-dependent drift for 30 seconds", CrawlIdleHasNoFacingDrift);
            Run("Crawl and turns keep arm rotation continuous for 30 seconds", CrawlTurnsKeepArmRotationContinuous);
            Run("Jump launch is not overwritten by Stand forces", JumpLaunchClearsGroundedForces);
            Run("Normal jump uses original air control, boost, and animation state", OriginalAirSequence);
            Run("Air-control cases A-D match the DLL velocity recurrence", OriginalAirControlCases);
            Run("Opposite airborne input preserves momentum for the original ticks", OppositeAirInputPreservesMomentum);
            Run("Hunter air speed is not multiplied by ground run speed", HunterAirSpeedUsesOriginalLimit);
            Run("TerrainImpact preserves pre-impact component velocity", TerrainImpactPreservesPreImpactVelocity);
            Run("Terrain first-contact follows contact direction, not surface identity", TerrainFirstContactUsesDirection);
            Run("Original floor severity produces normal, stun, and capped safety states", OriginalFloorImpactThresholds);
            Run("Original wall impact threshold stuns without floor-only death", OriginalWallImpactStuns);
            Run("Gourmand uses the DLL's 40/80 impact thresholds", GourmandImpactThresholds);
            Run("Extreme terrain impacts recover within the three-second cap", ExtremeImpactIsNonLethalAndRecovers);
            Run("Repeated terrain impacts cannot reset the recovery deadline", RepeatedImpactsCannotExtendStunForever);
            Run("Stun blocks movement but keeps BodyChunk physics active", StunKeepsPhysicsAndBlocksMovement);
            Run("Stunned graphics retract limbs and select FaceStunned", StunnedGraphicsUseOriginalState);
            Run("Stun suppresses click attention and recovers from current physics", StunSuppressesMouseAndRecoversNaturally);
            Run("DropDown requests window-surface pass-through", DropDownRequestsSurfacePassThrough);
            Run("GenericBodyPart uses original ConnectToPoint equation", OriginalConnectToPointEquation);
            Run("Head follows start and stop without duplicate render lag", HeadStartStopKeepsOriginalConnection);
            Run("All graphics parts share one timeStacker", SharedGraphicsInterpolation);
            Run("Futile trim and anchor restore sprite-local coordinates", FutileTrimAnchorCoordinates);
            Run("Negative virtual-desktop coordinates convert once", NegativeVirtualDesktopCoordinates);
            Run("240 Hz rendering preserves the 40 Hz simulation count", TwoFortyHertzRenderCadence);
            Run("Ten-second idle/walk/turn/jump graphics stay connected", LongGraphicsScenarioStaysConnected);
            Run("Five-minute varied-window soak preserves sprite integrity", FiveMinuteVariedWindowSpriteIntegrity);
            Run("Graphics bounds include procedural extremities", GraphicsBoundsIncludeExtremities);
            Run("Overlapping Slugcats share one bounded composition upload",
                OverlappingSlugcatsShareCompositionUpload);
            Run("Render order keeps held food above Slugcat 1 through 8",
                HeldFoodAndSlugcatRenderOrder);
            Run("Composition surfaces grow without resize oscillation",
                CompositionSurfacesOnlyGrow);
            Run("GPU smoke command ABI matches the native renderer",
                GpuSmokeCommandAbiMatchesNativeRenderer);
            Run("GPU sprite command ABI matches the native renderer",
                GpuSpriteCommandAbiMatchesNativeRenderer);
            Run("GPU sprite surface renders through Direct2D",
                GpuSpriteSurfaceRendersThroughDirect2D);
            Run("Artificer smoke emits direct GPU effect commands",
                ArtificerSmokeEmitsGpuEffectCommands);
            Run("Artificer flash expands the independent GPU effect bounds",
                ArtificerFlashExpandsGpuEffectBounds);
            Run("Artificer self-destruct effects use the large GPU bounds",
                ArtificerSelfDestructUsesGpuEffectBounds);
            Run("Unused Stand and Walk hands retract like SlugcatHand", UnusedHandsRetract);
            Run("Crawl hands use original velocity-relative targets", CrawlHandsUseOriginalTargets);
            Run("Entering Crawl clears both raised standing-hand targets",
                CrawlEntryClearsRaisedHandTargets);
            Run("SlugcatHand connection constraint prevents arm separation", ArmConstraintPreventsSeparation);
            Run("Crawl face follows persistent body facing, not attention", CrawlFaceUsesBodyFacing);
            Run("Arm shoulders rotate from the interpolated body axis", ArmShouldersFollowBodyAxis);
            Run("CharacterRenderScale uniformly enlarges visual coordinates", UniformCharacterRenderScale);
            Run("Expanded arm/leg/face debug overlay renders without mutation", ExpandedDebugOverlayRenders);
            Run("Mouse attention requires near clicks and refreshes its timeout", MouseAttentionClickCases);
            Run("Downpour visual profiles match local DLL constants", DownpourVisualProfilesMatchDllConstants);
            Run("Runtime skin switching preserves Player physics", RuntimeSkinSwitchPreservesPhysics);
            Run("Rivulet gills use six procedural parts and shared interpolation", RivuletGillsUseOriginalProceduralLayout);
            Run("Spearmaster uses its original tail profile and speckle mapping", SpearmasterTailProfileAndSpeckles);
            Run("PlayerGraphics arm reflection matches y-up signed distance",
                ArmScaleReflectionMatchesFutileCoordinates);
            Run("Skin face and head families follow PlayerGraphics branches", SkinFaceFamiliesMatchPlayerGraphics);
            Run("Every visual profile remains valid through movement and stun states", AllVisualProfilesRemainStableAcrossStates);
            AbilityParityReplayTests.Register(Run);

            RainWorldInstallation localInstallation = new RainWorldLocator().Locate(null);
            if (localInstallation == null)
                Console.WriteLine("SKIP  Local embedded original atlas (Rain World installation not found)");
            else
            {
                Run("Local embedded original atlas loads without DMS", delegate { EmbeddedOriginalAtlasLoads(localInstallation); });
                Run("Local food atlas renders deep blue, cyan, and warm egg layers",
                    delegate { FoodAtlasRendersOriginalPalette(localInstallation); });
                Run("Installed Workshop mods parse without loading their DLLs",
                    delegate { LocalWorkshopIntegrationsParse(localInstallation); });
            }

            Console.WriteLine(failures == 0
                ? "All RainWorldDesktopPet tests passed."
                : failures + " RainWorldDesktopPet test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void RenderPreview(string outputPath, SlugcatVariant variant, string scenario,
            SlugcatSkin skin, string dmsSkinId)
        {
            RainWorldInstallation installation = new RainWorldLocator().Locate(null);
            if (installation == null) throw new InvalidOperationException("Rain World installation was not found.");
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 spawn = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom)) - new Vec2(0.0, 9.0);
            Slugcat slugcat = new Slugcat(spawn, variant);
            DesktopPetAI ai = new DesktopPetAI(22);
            double attentionX = scenario == "crawl-right" ? -120.0 : 120.0;
            ai.Attention.SetTarget(AttentionKind.Mouse, slugcat.Center + new Vec2(attentionX, -55.0));
            RainWorldAssetLoader loader = new RainWorldAssetLoader(installation);
            RainWorldAtlasSet set = loader.TryLoadPlayerAtlas();
            string missing;
            SlugcatVisualProfile profile = SlugcatVisualProfiles.Get(skin);
            if (!profile.IsAvailable(set, out missing))
                throw new InvalidOperationException(profile.DisplayName + " unavailable: " + missing);
            SlugcatGraphics proceduralGraphics = new SlugcatGraphics(slugcat, profile, set);
            for (int i = 0; i < 90; i++)
            {
                VirtualInput input;
                if (scenario == "idle") input = VirtualInput.Neutral;
                else if (scenario == "crawl-right") input = new VirtualInput(1, 1, false, false);
                else if (scenario == "crawl-left") input = new VirtualInput(-1, 1, false, false);
                else if (scenario == "jump") input = new VirtualInput(1, 0, i >= 35 && i < 44, false);
                else if (scenario == "stunned")
                {
                    if (i == 55) slugcat.Stun(60);
                    input = i < 45 ? new VirtualInput(1, 0, false, false) : VirtualInput.Neutral;
                }
                else input = i > 20 && i < 68 ? new VirtualInput(1, 0, i == 45, false) : VirtualInput.Neutral;
                slugcat.Step(input, world, ai.Attention.Target, Vec2.Zero);
                ai.Attention.Step();
                proceduralGraphics.Step(ai.Attention, world);
            }

            WorkshopCatalog workshop = null;
            DmsSkinCatalog dms = null;
            using (SpriteRenderer renderer = new SpriteRenderer(set))
            using (Bitmap bitmap = new Bitmap(560, 420, PixelFormat.Format32bppPArgb))
            using (System.Drawing.Graphics drawing = System.Drawing.Graphics.FromImage(bitmap))
            {
                if (!string.IsNullOrWhiteSpace(dmsSkinId))
                {
                    WorkshopLog log = new WorkshopLog(Path.Combine(Path.GetTempPath(),
                        "SlugcatInMyMonitor-tests", "preview.log"), false);
                    workshop = new WorkshopCatalog(installation, log);
                    dms = new DmsSkinCatalog(workshop, log);
                    DmsSkinDefinition dmsSkin = dms.Find(dmsSkinId);
                    if (dmsSkin == null) throw new InvalidOperationException(
                        "DMS spritesheet unavailable: " + dmsSkinId);
                    renderer.SetDmsSkin(dmsSkin);
                }
                drawing.Clear(Color.Transparent);
                SlugcatPose pose = proceduralGraphics.BuildPose(1.0, ai.Attention);
                Vec2 origin = pose.ToRenderedWorld((pose.Chest + pose.Hips) * 0.5) -
                    new Vec2(280.0, 220.0);
                renderer.Render(drawing, pose, origin, false, world, slugcat, ai, loader.Status, slugcat.Appearance);
                string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
            if (dms != null) dms.Dispose();
            if (workshop != null) workshop.Dispose();
            Console.WriteLine("Preview written to " + Path.GetFullPath(outputPath));
            Console.WriteLine(loader.Status);
            Console.WriteLine("Scenario " + scenario);
            Console.WriteLine("Skin " + skin);
            Console.WriteLine("DMS " + (dmsSkinId ?? "none"));
        }

        private static void LocalWorkshopIntegrationsParse(RainWorldInstallation installation)
        {
            string logPath = Path.Combine(Path.GetTempPath(), "SlugcatInMyMonitor-tests",
                "workshop.log");
            WorkshopLog log = new WorkshopLog(logPath, false);
            using (WorkshopCatalog workshop = new WorkshopCatalog(installation, log))
            {
                if (workshop.FindById("dressmyslugcat") != null)
                {
                    using (DmsSkinCatalog dms = new DmsSkinCatalog(workshop, log))
                    {
                        True(dms.Skins.Count > 0, "installed DMS must expose at least one valid spritesheet");
                        string[] officialParts = { "HEAD", "FACE", "BODY", "ARMS", "HIPS",
                            "LEGS", "TAIL", "FACESCAR", "GILLS", "TAILSPECKLES",
                            "ASCENSION", "PIXEL" };
                        Equal(officialParts.Length, DmsSpriteGroups.SelectableParts.Length,
                            "Skin Editor exposes every official DMS part");
                        for (int partIndex = 0; partIndex < officialParts.Length; partIndex++)
                            True(string.Equals(officialParts[partIndex],
                                DmsSpriteGroups.SelectableParts[partIndex],
                                StringComparison.OrdinalIgnoreCase),
                                "DMS part order " + officialParts[partIndex]);
                        foreach (DmsSkinDefinition skin in dms.Skins)
                            True(skin.AvailableParts.Any(), skin.Id + " must contain a complete DMS sprite group");

                        DmsSkinDefinition headSkin = dms.Skins.FirstOrDefault(skin =>
                            skin.IsModActive && skin.HasPart("HEAD"));
                        DmsSkinDefinition bodySkin = dms.Skins.FirstOrDefault(skin =>
                            skin.IsModActive && skin.HasPart("BODY") && skin != headSkin);
                        if (headSkin != null)
                        {
                            using (SpriteRenderer renderer = new SpriteRenderer(null))
                            {
                                renderer.SetDmsPart("HEAD", headSkin);
                                True(renderer.GetDmsPart("HEAD") == headSkin,
                                    "HEAD has one explicit source");
                                if (bodySkin != null)
                                {
                                    renderer.SetDmsPart("BODY", bodySkin);
                                    renderer.SetDmsPart("HEAD", null);
                                    True(renderer.GetDmsPart("HEAD") == null,
                                        "Vanilla clears the previous HEAD reference");
                                    True(renderer.GetDmsPart("BODY") == bodySkin,
                                        "an intentional BODY selection remains independent");
                                }
                                renderer.SetDmsSkin(headSkin);
                                True(renderer.GetDmsPart("BODY") ==
                                    (headSkin.HasPart("BODY") ? headSkin : null),
                                    "atomic whole-set compatibility leaves no previous BODY skin");
                            }
                        }

                        AtlasSprite sprite;
                        DmsSkinDefinition saintRaincoat = dms.Find("homeobox.raincoatsaint");
                        if (saintRaincoat != null)
                            True(saintRaincoat.TryGetSprite("HeadB0", "Saint", DmsSpriteSide.None,
                                out sprite), "Saint HeadB sprites must map to the generic DMS HeadA family");

                        DmsSkinDefinition template = dms.Find("dressmyslugcat.template");
                        if (template != null)
                            True(template.TryGetSprite("FaceC0", "Artificer", DmsSpriteSide.None,
                                out sprite), "Artificer FaceC sprites must map to the generic FaceA family");

                        DmsSkinDefinition bow = dms.Find("InanimateSwagsanity.Bow");
                        if (bow != null)
                        {
                            True(bow.TryGetSprite("HeadA0", "White", DmsSpriteSide.None, out sprite),
                                "valid parts from a partly broken installed atlas set must remain available");
                            True(!bow.TryGetSprite("HipsA", "White", DmsSpriteSide.None, out sprite),
                                "an invalid atlas pair must fall back instead of exposing corrupt sprites");
                        }

                        DmsSkinDefinition rivulet = dms.Find("VNNYS.RIVL.REDRWN");
                        if (rivulet != null)
                        {
                            True(rivulet.TryGetSprite("LizardScaleA3", "Rivulet", DmsSpriteSide.None,
                                out sprite), "valid Rivulet gills from an installed DMS skin must load");
                            True(!rivulet.TryGetSprite("TailTexture", "Rivulet", DmsSpriteSide.None,
                                out sprite), "a corrupt optional tail atlas must preserve the base tail");
                        }
                    }
                }

                List<RainWorldMod> removedDms = workshop.Mods.Where(mod =>
                    string.Equals(mod.Id, "dressmyslugcat", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (RainWorldMod mod in removedDms) workshop.Mods.Remove(mod);
                using (DmsSkinCatalog absentDms = new DmsSkinCatalog(workshop, log))
                {
                    True(!absentDms.IsFrameworkInstalled && absentDms.Skins.Count == 0,
                        "missing Dress My Slugcat must leave the base appearance available");
                }
                foreach (RainWorldMod mod in removedDms) workshop.Mods.Add(mod);
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine("FAIL  " + name);
                Console.WriteLine("      " + exception.Message);
            }
        }

        private static void RenderFoodPreview(string outputPath)
        {
            RainWorldInstallation installation = new RainWorldLocator().Locate(null);
            if (installation == null) throw new InvalidOperationException(
                "Rain World installation was not found.");
            using (Bitmap bitmap = CreateFoodPreview(installation))
                bitmap.Save(outputPath, ImageFormat.Png);
        }

        private static Bitmap CreateFoodPreview(RainWorldInstallation installation)
        {
            RainWorldAssetLoader loader = new RainWorldAssetLoader(installation);
            RainWorldAtlasSet set = loader.TryLoadPlayerAtlas();
            if (set == null) throw new InvalidOperationException(
                "Rain World food atlas was not loaded.");
            Bitmap bitmap = new Bitmap(640, 220, PixelFormat.Format32bppPArgb);
            try
            {
                DesktopFoodManager manager = new DesktopFoodManager(81723);
                manager.TryAddDangleFruit(new Vec2(35.0, 55.0));
                for (int i = 0; i < 4; i++)
                    manager.TryAddEggBugEgg(new Vec2(75.0 + i * 40.0, 55.0));
                using (SpriteRenderer renderer = new SpriteRenderer(set))
                using (System.Drawing.Graphics drawing =
                    System.Drawing.Graphics.FromImage(bitmap))
                {
                    drawing.Clear(Color.Transparent);
                    renderer.RenderFoods(drawing, manager,
                        new RenderSpace(new Rectangle(0, 0, bitmap.Width,
                            bitmap.Height)), 2.8, 1.0, false);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            finally
            {
                set.Dispose();
            }
        }

        private static void DangleFruitPreservesOriginalEdibleContract()
        {
            DesktopFood fruit = new DesktopFood(DesktopFoodKind.DangleFruit,
                new Vec2(100.0, 80.0));
            Equal(3, fruit.BitesRemaining, "DangleFruit starts with three bites");
            Equal(1, fruit.FoodPoints, "DangleFruit grants one food point");
            Near(8.0, fruit.Chunk.Radius, 0.000001,
                "DangleFruit keeps its original radius");
            Near(0.2, fruit.Chunk.Mass, 0.000001,
                "DangleFruit keeps its original mass");
            True(fruit.FrontElement == "DangleFruit0A",
                "the untouched fruit uses atlas frame zero");

            True(fruit.Claim(), "a free fruit can be reserved");
            True(fruit.PickUp(new Vec2(102.0, 77.0)),
                "a reserved fruit can be picked up");
            True(fruit.BeginBiting(), "a held fruit can enter the bite sequence");
            True(fruit.Bite(), "the first bite succeeds");
            Equal(2, fruit.BitesRemaining, "the first bite leaves two bites");
            True(fruit.FrontElement == "DangleFruit1A",
                "the first bite advances the original atlas frame");
            True(fruit.Bite(), "the second bite succeeds");
            True(fruit.FrontElement == "DangleFruit2A",
                "the second bite advances the original atlas frame");
            True(fruit.Bite(), "the final bite succeeds");
            True(fruit.State == DesktopFoodState.Consumed,
                "the final bite consumes the item");
            True(!fruit.Bite(), "a consumed item cannot be bitten again");
        }

        private static void FoodInteractionUsesVirtualInputAndConsumes()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 100.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            DesktopFoodManager manager = new DesktopFoodManager();
            AttentionSystem attention = new AttentionSystem();
            VirtualInput input;

            True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(80.0, 0.0)),
                "a fruit can be added to an empty manager");
            True(manager.TryProduceInput(slugcat, graphics, attention, out input),
                "an available fruit overrides autonomous input");
            Equal(1, input.X, "the food controller walks toward the fruit");
            True(manager.Target.State == DesktopFoodState.Claimed,
                "the selected fruit is reserved by its owning Slugcat");
            True(attention.Kind == AttentionKind.Food,
                "food becomes the visible attention target");

            manager.Clear();
            slugcat.State.Grounded = true;
            True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(8.0, 0.0)),
                "a reachable fruit can be added");
            True(manager.TryProduceInput(slugcat, graphics, attention, out input),
                "the reachable fruit owns the input tick");
            True(manager.Target.State == DesktopFoodState.Held,
                "the Slugcat picks up a fruit inside reach");

            for (int tick = 0; tick < 80; tick++)
                manager.StepInteraction(slugcat, graphics);
            Equal(3, manager.TotalBites, "the interaction performs all three bites");
            Equal(1, manager.FoodPointsEaten,
                "the completed fruit grants its one original food point");
            True(manager.InteractionState == FoodInteractionState.None,
                "the controller releases the consumed target");
        }

        private static void EggBugEggPreservesOriginalEdibleContract()
        {
            DesktopFood egg = new DesktopFood(DesktopFoodKind.EggBugEgg,
                new Vec2(100.0, 80.0), 0.2);
            Equal(2, egg.BitesRemaining, "EggBugEgg starts with two bites");
            Equal(1, egg.FoodPoints, "EggBugEgg grants one food point");
            Near(4.6, egg.Chunk.Radius, 0.000001,
                "EggBugEgg applies the original default swell radius");
            Near(0.2, egg.Chunk.Mass, 0.000001,
                "EggBugEgg keeps its original mass");
            True(egg.FrontElement == "DangleFruit0A" &&
                egg.BackElement == "EggBugEggColor" &&
                egg.DetailElement == "JetFishEyeA",
                "the intact egg uses the original three sprite layers");
            True(egg.Claim() && egg.PickUp(egg.Chunk.Position) && egg.BeginBiting(),
                "the egg enters its bite sequence");
            True(egg.Bite(), "the first egg bite succeeds");
            True(egg.FrontElement == "DangleFruit1A" &&
                egg.BackElement == "EggBugEggColorEaten",
                "the first bite switches to the original eaten layers");
            True(egg.Bite() && egg.State == DesktopFoodState.Consumed,
                "the second bite consumes the egg");
        }

        private static void FoodBiteAnimationMatchesOriginalCadence()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 100.0));
            slugcat.State.Grounded = true;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            DesktopFoodManager manager = new DesktopFoodManager(4401);
            AttentionSystem attention = new AttentionSystem();
            VirtualInput input;

            True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(8.0, 0.0)),
                "a reachable fruit can be offered for animation replay");
            True(manager.TryProduceInput(slugcat, graphics, attention, out input) &&
                manager.Target.State == DesktopFoodState.Held,
                "the replay starts with the original held grasp");

            graphics.Step(attention, world);
            manager.StepInteraction(slugcat, graphics);
            Equal(0, manager.TotalBites,
                "pickup gets one separate frame before BiteEdibleObject");
            True(graphics.Arms[0].Mode == LimbMode.HuntRelativePosition,
                "Player.FreeHand chooses grasp zero for the edible");
            Near(-20.0, graphics.Arms[0].RelativeHuntPosition.X, 0.000001,
                "held edible starts at SlugcatHand's eatCounter 40 x target");
            Near(12.0, graphics.Arms[0].RelativeHuntPosition.Y, 0.000001,
                "held edible starts below its raised bite position");

            for (int tick = 0; tick < 20; tick++)
            {
                graphics.Step(attention, world);
                manager.StepInteraction(slugcat, graphics);
                Equal(0, manager.TotalBites,
                    "the hand raises before the first bite");
            }
            Near(-16.8, graphics.Arms[0].RelativeHuntPosition.X, 0.000001,
                "eatCounter 20 reaches the original raised hand x target");
            Near(4.4, graphics.Arms[0].RelativeHuntPosition.Y, 0.000001,
                "eatCounter 20 reaches the original raised hand y target");

            slugcat.State.Facing = -slugcat.State.Facing;
            for (int tick = 20; tick < 40; tick++)
            {
                graphics.Step(attention, world);
                manager.StepInteraction(slugcat, graphics);
                Equal(0, manager.TotalBites,
                    "the initial eatCounter completes without an early bite");
            }
            Near(0.0, Vec2.Distance(manager.Target.Chunk.Position,
                graphics.Arms[0].End.Position), 0.000001,
                "the edible follows the same grasp while it is being raised");

            graphics.Step(attention, world);
            manager.StepInteraction(slugcat, graphics);
            Equal(1, manager.TotalBites,
                "the first bite follows the complete 40-tick raise");
            Near(0.0, Vec2.Distance(manager.Target.Chunk.Position,
                slugcat.BodyChunks[0].Position), 0.000001,
                "BitByPlayer snaps the edible to mainBodyChunk for one frame");
            True(graphics.BuildPose(1.0, attention).Blink,
                "BiteFly closes the original face on the bite frame");

            for (int tick = 0; tick < 14; tick++)
            {
                graphics.Step(attention, world);
                manager.StepInteraction(slugcat, graphics);
                Equal(1, manager.TotalBites,
                    "no extra mouth-phase bite occurs before tick fifteen");
            }
            Near(0.0, Vec2.Distance(manager.Target.Chunk.Position,
                graphics.Arms[0].End.Position), 0.000001,
                "between bites the edible follows its stable grasp hand");

            graphics.Step(attention, world);
            manager.StepInteraction(slugcat, graphics);
            Equal(2, manager.TotalBites,
                "Player.GrabUpdate repeats BiteEdibleObject after fifteen ticks");
            Near(0.0, Vec2.Distance(manager.Target.Chunk.Position,
                slugcat.BodyChunks[0].Position), 0.000001,
                "the second bite is another single-frame body snap");
        }

        private static void SpearmasterTossesFoodWithoutEating()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 100.0),
                SlugcatId.SpearMaster);
            slugcat.State.Grounded = true;
            slugcat.State.Facing = 1;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            DesktopFoodManager manager = new DesktopFoodManager(8127);
            AttentionSystem attention = new AttentionSystem();
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            VirtualInput input;

            True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(8.0, 0.0)),
                "a reachable fruit can be offered to Spearmaster");
            True(manager.TryProduceInput(slugcat, graphics, attention, out input) &&
                manager.Target != null &&
                manager.Target.State == DesktopFoodState.Held,
                "Spearmaster still picks up the edible");
            DesktopFood fruit = manager.Target;
            int tossTick = -1;
            for (int tick = 1; tick <= 121; tick++)
            {
                graphics.Step(attention, world);
                manager.StepInteraction(slugcat, graphics);
                Equal(0, manager.TotalBites,
                    "Spearmaster never enters BiteEdibleObject");
                if (manager.Target == null)
                {
                    tossTick = tick;
                    break;
                }
            }

            True(tossTick >= (int)SimulationConstants.LogicTicksPerSecond &&
                tossTick <= (int)(SimulationConstants.LogicTicksPerSecond * 3.0),
                "the toss delay stays inside the requested one-to-three-second range");
            Equal(fruit.InitialBites, fruit.BitesRemaining,
                "the tossed fruit loses no bites");
            Equal(0, manager.FoodPointsEaten,
                "Spearmaster gains no food points");
            True(fruit.State == DesktopFoodState.Free &&
                manager.LastEvent.EndsWith("TossUneaten", StringComparison.Ordinal),
                "the held edible returns to the world as a tossed item");
            Near(Math.Sin(Math.PI / 3.0) * 12.5, fruit.Chunk.Velocity.X,
                0.000001, "Player.TossObject horizontal velocity");
            Near(-Math.Cos(Math.PI / 3.0) * 12.5, fruit.Chunk.Velocity.Y,
                0.000001, "Player.TossObject upward velocity in screen coordinates");
            True(!manager.TryProduceInput(slugcat, graphics, attention, out input) &&
                manager.Target == null,
                "Spearmaster does not immediately pick up the same tossed item again");
        }

        private static void CrawlFoodMovesFromHandToMouth()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 100.0));
            slugcat.BodyChunks[0].Position = new Vec2(108.0, 100.0);
            slugcat.BodyChunks[1].Position = new Vec2(91.0, 100.0);
            slugcat.State.Grounded = true;
            slugcat.State.Facing = 1;
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.None;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            DesktopFoodManager manager = new DesktopFoodManager(3419);
            AttentionSystem attention = new AttentionSystem();
            VirtualInput input;

            graphics.Step(attention, world);
            True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(5.0, 0.0)) &&
                manager.TryProduceInput(slugcat, graphics, attention, out input),
                "the crawling Slugcat picks up a reachable fruit");
            graphics.Step(attention, world);
            Vec2 plantedHand = graphics.Arms[0].End.Position;
            manager.StepInteraction(slugcat, graphics);
            True(graphics.Arms[0].Mode == LimbMode.HuntAbsolutePosition,
                "crawl eating keeps an absolute low hand target");
            Near(0.0, Vec2.Distance(plantedHand,
                graphics.Arms[0].AbsoluteHuntPosition), 0.000001,
                "the first eating frame preserves the planted hand position");

            for (int tick = 0; tick < 20; tick++)
            {
                graphics.Step(attention, world);
                manager.StepInteraction(slugcat, graphics);
            }
            SlugcatPose pose = graphics.BuildPose(1.0, attention);
            Near(0.0, Vec2.Distance(pose.Chest,
                graphics.Arms[0].AbsoluteHuntPosition), 0.000001,
                "the original 40-to-20 eating phase reaches the mouth/chest target");
            Equal(0, manager.TotalBites,
                "the hand reaches the mouth before the first bite");
        }

        private static void FoodVisualBoundsIncludeProceduralParts()
        {
            DesktopFood fruit = new DesktopFood(DesktopFoodKind.DangleFruit,
                Vec2.Zero);
            DesktopFood egg = new DesktopFood(DesktopFoodKind.EggBugEgg,
                Vec2.Zero, 0.0);
            Near(DesktopFood.DangleFruitVisualReach, fruit.VisualReach, 0.000001,
                "Blue Fruit exposes its complete atlas reach");
            True(fruit.VisualReach > fruit.Chunk.Radius,
                "fruit bounds include pixels beyond its collision radius");
            Near(DesktopFood.EggBugEggVisualReach, egg.VisualReach, 0.000001,
                "Eggbug Egg exposes its procedural tail reach");
            True(egg.VisualReach >= 22.0 &&
                egg.VisualReach > egg.Chunk.Radius * 4.0,
                "egg bounds cannot clip the approximately 22-unit tail");

            bool rejectedUnknownKind = false;
            try
            {
                new DesktopFood((DesktopFoodKind)999, Vec2.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectedUnknownKind = true;
            }
            True(rejectedUnknownKind,
                "unknown food kinds fail explicitly instead of becoming fruit");
        }

        private static void FoodItemsSettleOnFlatFloor()
        {
            MonitorInfo monitor = new MonitorInfo("FOOD-FLOOR",
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1920, 1040), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            DesktopFoodKind[] kinds =
                { DesktopFoodKind.DangleFruit, DesktopFoodKind.EggBugEgg };

            for (int item = 0; item < kinds.Length; item++)
            {
                DesktopFood food = new DesktopFood(kinds[item],
                    new Vec2(300.0 + item * 100.0, floor -
                        (kinds[item] == DesktopFoodKind.DangleFruit
                            ? DesktopFood.DangleFruitRadius
                            : DesktopFood.EggBugEggRadius)));
                double startX = food.Chunk.Position.X;
                food.SetCreationVelocity(new Vec2(0.75, 0.0));

                for (int tick = 0; tick < 40; tick++)
                    food.StepPhysics(world);

                True(food.Chunk.ContactFloor,
                    kinds[item] + " remains supported by the desktop floor");
                True(Math.Abs(food.Chunk.Velocity.X) < 0.001,
                    kinds[item] + " reaches the original visually still speed");
                Near(floor - food.Chunk.Radius, food.Chunk.Position.Y, 0.000001,
                    kinds[item] + " rests at its collision radius above the floor");
                True(food.Chunk.Position.X - startX < 4.0,
                    kinds[item] + " does not slide across the continuous floor");
            }
        }

        private static void FoodRotationMatchesOriginalItemRules()
        {
            MonitorInfo airMonitor = new MonitorInfo("FOOD-AIR-ROTATION",
                new Rectangle(0, 0, 1920, 4000),
                new Rectangle(0, 0, 1920, 4000), true);
            DesktopCollisionWorld airWorld = CreateSyntheticWorld(
                new[] { airMonitor }, new DesktopWindowSnapshot[0]);
            DesktopFoodKind[] kinds =
                { DesktopFoodKind.DangleFruit, DesktopFoodKind.EggBugEgg };

            for (int item = 0; item < kinds.Length; item++)
            {
                DesktopFood food = new DesktopFood(kinds[item],
                    new Vec2(200.0 + item * 100.0, 100.0), 0.13, Vec2.Up);
                food.SetCreationVelocity(new Vec2(8.0, 3.0));
                for (int tick = 0; tick < 8; tick++) food.StepPhysics(airWorld);
                Near(0.0, Vec2.Distance(Vec2.Up, food.Rotation), 0.000001,
                    kinds[item] + " does not point its sprite along airborne velocity");
            }

            MonitorInfo floorMonitor = new MonitorInfo("FOOD-GROUND-ROTATION",
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1920, 1040), true);
            DesktopCollisionWorld floorWorld = CreateSyntheticWorld(
                new[] { floorMonitor }, new DesktopWindowSnapshot[0]);
            double floor = DesktopWorldTransform.ToSimulationLength(floorMonitor.FloorY);
            for (int item = 0; item < kinds.Length; item++)
            {
                double radius = kinds[item] == DesktopFoodKind.DangleFruit
                    ? DesktopFood.DangleFruitRadius : DesktopFood.EggBugEggRadius;
                DesktopFood food = new DesktopFood(kinds[item],
                    new Vec2(300.0 + item * 100.0, floor - radius),
                    0.13, Vec2.Up);
                food.SetCreationVelocity(new Vec2(2.0, 0.0));
                food.StepPhysics(floorWorld);

                double collisionVelocity = 2.0 * 0.999;
                double turn = kinds[item] == DesktopFoodKind.DangleFruit
                    ? 0.1 * collisionVelocity
                    : 0.8 * (0.12 * collisionVelocity);
                Vec2 expectedRotation = (Vec2.Up +
                    Vec2.Up.Perpendicular * turn).Normalized;
                Near(0.0, Vec2.Distance(expectedRotation, food.Rotation),
                    0.000001, kinds[item] + " uses its original ground rotation equation");
                Near(collisionVelocity * 0.8, food.Chunk.Velocity.X, 0.000001,
                    kinds[item] + " applies its original object-level floor damping");
            }

            DesktopFood held = new DesktopFood(DesktopFoodKind.DangleFruit,
                new Vec2(100.0, 100.0), 0.13, Vec2.Right);
            True(held.Claim() && held.PickUp(held.Chunk.Position),
                "fruit can enter the held-orientation replay");
            held.HoldAt(new Vec2(120.0, 100.0), new Vec2(100.0, 100.0));
            Near(0.0, Vec2.Distance(Vec2.Up, held.Rotation), 0.000001,
                "held food uses the original item-to-grabber perpendicular orientation");

            DesktopFood leftRollingEgg = new DesktopFood(
                DesktopFoodKind.EggBugEgg,
                new Vec2(500.0, floor - DesktopFood.EggBugEggRadius),
                0.13, Vec2.Up);
            leftRollingEgg.SetCreationVelocity(new Vec2(-2.0, 0.0));
            leftRollingEgg.StepPhysics(floorWorld);
            double leftCollisionVelocity = -2.0 * 0.999;
            double leftTurn = 0.8 * (0.12 * leftCollisionVelocity);
            Vec2 expectedLeftRotation = (Vec2.Up +
                Vec2.Up.Perpendicular * leftTurn).Normalized;
            Near(0.0, Vec2.Distance(expectedLeftRotation,
                leftRollingEgg.Rotation), 0.000001,
                "EggBugEgg applies signed ground rotation while moving left");
            True(leftRollingEgg.Rotation.X < 0.0,
                "left and right floor motion rotate EggBugEgg in opposite directions");
        }

        private static void EggBugEggTailMatchesOriginalProceduralAnimation()
        {
            MonitorInfo monitor = new MonitorInfo("EGG-TAIL",
                new Rectangle(0, 0, 1920, 4000),
                new Rectangle(0, 0, 1920, 4000), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            Vec2 start = new Vec2(200.0, 100.0);
            DesktopFood egg = new DesktopFood(DesktopFoodKind.EggBugEgg,
                start, 0.2, Vec2.Right);
            Equal(5, DesktopFood.EggBugEggTailSegmentCount,
                "EggBugEgg uses the DLL's five procedural segments");
            True(egg.HasVisibleEggTail,
                "the intact egg exposes its procedural tail mesh");

            Vec2[] expectedPositions =
                new Vec2[DesktopFood.EggBugEggTailSegmentCount];
            Vec2[] expectedLastPositions =
                new Vec2[DesktopFood.EggBugEggTailSegmentCount];
            Vec2[] expectedVelocities =
                new Vec2[DesktopFood.EggBugEggTailSegmentCount];
            for (int i = 0; i < expectedPositions.Length; i++)
            {
                expectedPositions[i] = start + Vec2.Right * i;
                expectedLastPositions[i] = expectedPositions[i];
                Near(0.0, Vec2.Distance(expectedPositions[i],
                    egg.EggTailPosition(i, 1.0)), 0.000001,
                    "tail reset position " + i);
            }

            egg.StepPhysics(world);
            StepExpectedEggTail(expectedPositions, expectedLastPositions,
                expectedVelocities, egg.Chunk.Position, egg.Rotation);
            for (int i = 0; i < expectedPositions.Length; i++)
            {
                Near(0.0, Vec2.Distance(expectedLastPositions[i],
                    egg.EggTailPosition(i, 0.0)), 0.000001,
                    "tail previous position follows DLL segment " + i);
                Near(0.0, Vec2.Distance(expectedPositions[i],
                    egg.EggTailPosition(i, 1.0)), 0.000001,
                    "tail current position follows DLL segment " + i);
                Near(0.0, Vec2.Distance(expectedVelocities[i],
                    egg.EggTailVelocity(i)), 0.000001,
                    "tail velocity follows DLL segment " + i);
            }

            True(egg.Claim() && egg.PickUp(egg.Chunk.Position) &&
                egg.BeginBiting() && egg.Bite(),
                "egg can replay its first bite for tail visibility");
            True(!egg.HasVisibleEggTail,
                "the original tail mesh disappears after the first bite");
        }

        private static void StepExpectedEggTail(Vec2[] positions,
            Vec2[] lastPositions, Vec2[] velocities, Vec2 bodyPosition,
            Vec2 rotation)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                double value = i / (double)(positions.Length - 1);
                lastPositions[i] = positions[i];
                positions[i] += velocities[i];
                velocities[i] *= 0.995;
                velocities[i].Y += 0.9 *
                    MathUtil.InverseLerp(0.5, 1.0, value);
                velocities[i] += rotation * (5.0 *
                    MathUtil.InverseLerp(0.5, 0.0, value));
                if (i > 1)
                {
                    Vec2 separation = MathUtil.Direction(
                        positions[i - 2], positions[i]);
                    velocities[i] += separation;
                    velocities[i - 2] -= separation;
                }
                ConnectExpectedEggTail(positions, velocities, i,
                    bodyPosition, rotation);
            }
            for (int i = positions.Length - 1; i >= 0; i--)
                ConnectExpectedEggTail(positions, velocities, i,
                    bodyPosition, rotation);
            for (int i = 0; i < positions.Length; i++)
                ConnectExpectedEggTail(positions, velocities, i,
                    bodyPosition, rotation);
        }

        private static void ConnectExpectedEggTail(Vec2[] positions,
            Vec2[] velocities, int index, Vec2 bodyPosition, Vec2 rotation)
        {
            if (index == 0)
            {
                Vec2 target = bodyPosition + rotation * (7.0 * 1.15);
                Vec2 direction = MathUtil.Direction(positions[index], target);
                double distance = Vec2.Distance(positions[index], target);
                Vec2 correction = direction * (2.0 - distance);
                positions[index] -= correction;
                velocities[index] -= correction;
                return;
            }
            Vec2 towardPrevious = MathUtil.Direction(positions[index],
                positions[index - 1]);
            double distanceToPrevious = Vec2.Distance(positions[index],
                positions[index - 1]);
            Vec2 sharedCorrection = towardPrevious *
                ((2.0 - distanceToPrevious) * 0.5);
            positions[index] -= sharedCorrection;
            velocities[index] -= sharedCorrection;
            positions[index - 1] += sharedCorrection;
            velocities[index - 1] += sharedCorrection;
        }

        private static void FoodItemsSupportMouseDragging()
        {
            MonitorInfo monitor = new MonitorInfo("FOOD-DRAG",
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1920, 1040), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            DesktopFoodKind[] kinds =
                { DesktopFoodKind.DangleFruit, DesktopFoodKind.EggBugEgg };

            for (int item = 0; item < kinds.Length; item++)
            {
                DesktopFoodManager manager = new DesktopFoodManager(7100 + item);
                Vec2 start = new Vec2(120.0 + item * 80.0, 140.0);
                bool added = kinds[item] == DesktopFoodKind.DangleFruit
                    ? manager.TryAddDangleFruit(start)
                    : manager.TryAddEggBugEgg(start);
                True(added, kinds[item] + " can be prepared for pointer dragging");
                DesktopFood food = manager.Foods[0];
                DesktopFoodState expectedReleaseState;
                if (item == 0)
                {
                    True(food.Claim(), "fruit can preserve its accepted state while dragged");
                    expectedReleaseState = DesktopFoodState.Claimed;
                }
                else
                {
                    True(food.Ignore(), "egg can preserve its ignored state while dragged");
                    expectedReleaseState = DesktopFoodState.Ignored;
                }

                Vec2 press = start + new Vec2(3.0, 2.0);
                True(manager.HitTest(press),
                    kinds[item] + " visual bounds respond to the pointer");
                True(manager.TryBeginDrag(press),
                    kinds[item] + " starts a mouse drag");
                True(manager.IsDragging &&
                    food.State == DesktopFoodState.Dragged && !food.IsPhysical,
                    kinds[item] + " becomes kinematic while held by the pointer");

                Vec2 pointer = new Vec2(420.0, 260.0 + item * 40.0);
                manager.MoveDraggedFood(pointer);
                Vec2 expectedPosition = pointer + (start - press);
                for (int tick = 0; tick < 5; tick++) manager.StepPhysics(world);
                Near(0.0, Vec2.Distance(expectedPosition, food.Chunk.Position),
                    0.000001, kinds[item] + " follows the cursor without gravity drift");

                Vec2 releaseVelocity = new Vec2(4.0, -2.0);
                True(manager.EndDrag(releaseVelocity),
                    kinds[item] + " completes its mouse drag");
                True(!manager.IsDragging && food.State == expectedReleaseState,
                    kinds[item] + " restores its pre-drag appetite state");
                Near(0.0, Vec2.Distance(releaseVelocity, food.Chunk.Velocity),
                    0.000001, kinds[item] + " inherits the pointer release velocity");
            }
        }

        private static void FoodClearResetsInteractionState()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 100.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            DesktopFoodManager manager = new DesktopFoodManager(1193);
            AttentionSystem attention = new AttentionSystem();
            VirtualInput input;
            slugcat.State.Grounded = true;
            True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(8.0, 0.0)),
                "a fruit can be prepared for the clear-state test");
            True(manager.TryProduceInput(slugcat, graphics, attention, out input),
                "the fruit enters an interaction state");
            manager.Clear();
            Equal(0, manager.Foods.Count, "clear removes every food");
            True(manager.Target == null,
                "clear removes the reserved target");
            True(manager.InteractionState == FoodInteractionState.None,
                "clear resets the interaction state");
            True(!manager.LastSpawnAccepted,
                "clear cannot expose a stale accepted-spawn result");
        }

        private static void FoodFallbackRendersWithoutAtlas()
        {
            using (Bitmap bitmap = new Bitmap(260, 130,
                PixelFormat.Format32bppPArgb))
            using (SpriteRenderer renderer = new SpriteRenderer(null))
            using (System.Drawing.Graphics drawing =
                System.Drawing.Graphics.FromImage(bitmap))
            {
                DesktopFoodManager manager = new DesktopFoodManager(3187);
                manager.TryAddDangleFruit(new Vec2(45.0, 45.0));
                manager.TryAddEggBugEgg(new Vec2(90.0, 45.0));
                drawing.Clear(Color.Transparent);
                renderer.RenderFoods(drawing, manager,
                    new RenderSpace(new Rectangle(0, 0, bitmap.Width,
                        bitmap.Height)), 2.0, 1.0, false);
                int visiblePixels = 0;
                int fruitPixels = 0;
                int eggPixels = 0;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (bitmap.GetPixel(x, y).A == 0) continue;
                        visiblePixels++;
                        if (x < 135) fruitPixels++;
                        else eggPixels++;
                    }
                }
                True(visiblePixels > 200 && fruitPixels > 100 && eggPixels > 50,
                    "both foods keep complete procedural fallbacks");
            }
        }

        private static void RendererColorResourceCachesRemainBounded()
        {
            using (SpriteRenderer renderer = new SpriteRenderer(null))
            {
                MethodInfo tintMethod = typeof(SpriteRenderer).GetMethod(
                    "GetTintAttributes", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo brushMethod = typeof(SpriteRenderer).GetMethod(
                    "GetBodyBrush", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo tintField = typeof(SpriteRenderer).GetField(
                    "tintAttributes", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo brushField = typeof(SpriteRenderer).GetField(
                    "bodyBrushes", BindingFlags.Instance | BindingFlags.NonPublic);
                True(tintMethod != null && brushMethod != null && tintField != null &&
                    brushField != null, "renderer cache members remain testable");
                for (int i = 0; i < 1100; i++)
                {
                    Color color = Color.FromArgb(255, i & 255, (i >> 8) & 255,
                        (i * 17) & 255);
                    tintMethod.Invoke(renderer, new object[] { color });
                    brushMethod.Invoke(renderer, new object[] { color });
                }
                int tintCount = (int)tintField.GetValue(renderer).GetType().
                    GetProperty("Count").GetValue(tintField.GetValue(renderer), null);
                int brushCount = (int)brushField.GetValue(renderer).GetType().
                    GetProperty("Count").GetValue(brushField.GetValue(renderer), null);
                True(tintCount <= 1024 && brushCount <= 1024,
                    "long-running hue variation cannot grow GDI caches without limit");
            }
        }

        private static void FoodPalettesMatchOriginalColorRules()
        {
            FoodLayerPalette fruit = FoodRenderPalette.DangleFruit;
            True(fruit.BaseColor.R < 40 && fruit.BaseColor.G < 40 &&
                fruit.BaseColor.B < 50,
                "DangleFruit A uses the desktop RoomPalette black equivalent");
            True(fruit.PrimaryColor.B >= 150 &&
                fruit.PrimaryColor.B > fruit.PrimaryColor.R * 8 &&
                fruit.PrimaryColor.B > fruit.PrimaryColor.G * 8,
                "DangleFruit B stays a deep saturated blue");
            True(!(fruit.PrimaryColor.R == 120 && fruit.PrimaryColor.G == 170 &&
                fruit.PrimaryColor.B == 255),
                "the former pale sky-blue tint is not retained");

            Random random = new Random(39117);
            bool sawNegative = false;
            bool sawPositive = false;
            for (int i = 0; i < 4096; i++)
            {
                double hue = FoodRenderPalette.CreateNormalEggHue(random);
                True(hue >= FoodRenderPalette.NormalEggHueMinimum - 0.000001 &&
                    hue <= FoodRenderPalette.NormalEggHueMaximum + 0.000001,
                    "normal Eggbug hue stays in the original -0.15..0.10 interval");
                sawNegative |= hue < 0.0;
                sawPositive |= hue > 0.0;
            }
            True(sawNegative && sawPositive,
                "the constrained hue distribution still varies between eggs");

            FoodLayerPalette egg = FoodRenderPalette.EggBugEgg(-0.025);
            True(egg.PrimaryColor.G > 220 && egg.PrimaryColor.B > 170 &&
                egg.PrimaryColor.R < 40,
                "a representative normal egg keeps its bright cyan liquid");
            True(egg.DetailColor.R > egg.DetailColor.G * 4 &&
                egg.DetailColor.R > egg.DetailColor.B * 3,
                "a representative normal egg keeps its warm red-pink detail");
        }

        private static void FoodAtlasRendersOriginalPalette(
            RainWorldInstallation installation)
        {
            using (Bitmap bitmap = CreateFoodPreview(installation))
            {
                int deepBluePixels = 0;
                int cyanPixels = 0;
                int warmPixels = 0;
                int paleFruitPixels = 0;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color color = bitmap.GetPixel(x, y);
                        if (color.A == 0) continue;
                        if (color.B > 100 && color.B > color.R * 5 &&
                            color.B > color.G * 5) deepBluePixels++;
                        if (color.G > 140 && color.B > 100 &&
                            color.R < 60) cyanPixels++;
                        if (color.R > 80 && color.R > color.G * 3 &&
                            color.R > color.B * 2) warmPixels++;
                        if (x < 150 && color.R > 70 && color.G > 100 &&
                            color.B > 180) paleFruitPixels++;
                    }
                }
                True(deepBluePixels > 20,
                    "the real DangleFruit atlas produces a deep blue layer");
                True(cyanPixels > 20 && warmPixels > 5,
                    "the real EggBugEgg atlas produces cyan and warm layers");
                Equal(0, paleFruitPixels,
                    "the fruit region contains no former sky-blue tint");
            }
        }

        private static void FoodSpawnUsesFarRandomizedDrop()
        {
            MonitorInfo monitor = new MonitorInfo("FOOD-MONITOR",
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1920, 1040), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            Slugcat slugcat = new Slugcat(DesktopWorldTransform.ToSimulation(
                new Vec2(960.0, 1000.0)));
            DesktopFoodManager manager = new DesktopFoodManager(4419);
            True(manager.TrySpawnEggBugEgg(slugcat, world),
                "an egg can spawn on monitor terrain");
            DesktopFood food = manager.Foods[0];
            double desktopDistance = Math.Abs(
                DesktopWorldTransform.ToDesktop(food.Chunk.Position).X -
                DesktopWorldTransform.ToDesktop(slugcat.Center).X);
            True(desktopDistance >= 139.0 && desktopDistance <= 361.0,
                "spawn distance stays in the 140-360 desktop pixel range: " +
                desktopDistance.ToString("0.###"));
            double floorY = monitor.FloorY;
            double foodY = DesktopWorldTransform.ToDesktop(food.Chunk.Position).Y;
            True(foodY < floorY - 40.0,
                "food starts above the floor so it visibly drops");
        }

        private static void FullnessPreventsGuaranteedEating()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 100.0));
            slugcat.State.Grounded = true;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            DesktopFoodManager manager = new DesktopFoodManager(9182);
            AttentionSystem attention = new AttentionSystem();
            int accepted = 0;
            int ignored = 0;

            for (int offer = 0; offer < 5; offer++)
            {
                manager.Clear();
                True(manager.TryAddDangleFruit(slugcat.Center + new Vec2(8.0, 0.0)),
                    "offer " + offer + " can be placed");
                VirtualInput input;
                if (!manager.TryProduceInput(slugcat, graphics, attention, out input))
                {
                    ignored++;
                    True(manager.Foods[0].State == DesktopFoodState.Ignored,
                        "a refused food remains visible and physical");
                    continue;
                }

                accepted++;
                for (int tick = 0; tick < 80; tick++)
                    manager.StepInteraction(slugcat, graphics);
            }

            True(accepted > 0 && accepted < 5 && ignored > 0,
                "five rapid offers include both eating and refusal; accepted=" + accepted);
            True(manager.Fullness <= DesktopFoodManager.MaximumFullness,
                "fullness never exceeds its capacity");
            double beforeDigestion = manager.Fullness;
            for (int tick = 0; tick < DesktopFoodManager.DigestionTicksPerFoodPoint; tick++)
                manager.StepMetabolism();
            Near(Math.Max(0.0, beforeDigestion - 1.0), manager.Fullness, 0.001,
                "one food point digests over the configured interval");
        }

        private static void FixedStepUsesFortyHertz()
        {
            FixedTimeStep step = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
            step.AddElapsed(0.1);
            int count = 0;
            while (step.ConsumeStep()) count++;
            Equal(4, count, "0.1 seconds must contain four 40 Hz ticks");
        }

        private static void DesktopWorldTransformScalesTravelUniformly()
        {
            BodyChunk chunk = new BodyChunk(0, Vec2.Zero, 9.0, 0.35);
            chunk.Velocity = new Vec2(1.0, 1.0);
            chunk.BeginTick();
            chunk.Integrate(0.0, 1.0);
            Near(1.0, chunk.Position.X, 0.000001, "internal X integration remains original");
            Near(1.0, chunk.Position.Y, 0.000001, "internal Y integration remains original");
            Near(1.0, chunk.Velocity.X, 0.000001, "X velocity constant is not multiplied");
            Near(1.0, chunk.Velocity.Y, 0.000001, "Y velocity constant is not multiplied");
            Vec2 desktop = DesktopWorldTransform.ToDesktop(chunk.Position);
            Near(2.20, desktop.X, 0.000001, "screen X travel scale");
            Near(2.20, desktop.Y, 0.000001, "screen Y travel scale");
            Near(2.20, SimulationConstants.DesktopWorldScale, 0.000001,
                "configured desktop world scale");
        }

        private static void OriginalHorizontalInputParity()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.BodyChunks[0].ContactFloor = true;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(new VirtualInput(1, 0, false, false), world);
            Near(1.2, slugcat.BodyChunks[0].Velocity.X, 0.000001,
                "Player MovementUpdate upper acceleration");
            Near(1.2, slugcat.BodyChunks[1].Velocity.X, 0.000001,
                "Player MovementUpdate lower acceleration");

            slugcat.BodyChunks[0].ContactFloor = true;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(VirtualInput.Neutral, world);
            double expectedFriction = 1.2 * (1.0 - Math.Pow(0.5, 1.5));
            Near(expectedFriction, slugcat.BodyChunks[0].Velocity.X, 0.000001,
                "grounded surfaceFriction^1.5 upper");
            Near(expectedFriction, slugcat.BodyChunks[1].Velocity.X, 0.000001,
                "grounded surfaceFriction^1.5 lower");

            Slugcat crawler = new Slugcat(new Vec2(300.0, 300.0));
            crawler.BodyChunks[0].ContactFloor = true;
            crawler.BodyChunks[1].ContactFloor = true;
            crawler.Movement.ApplyInput(new VirtualInput(1, 1, false, false), world);
            Near(1.2, crawler.BodyChunks[0].Velocity.X, 0.000001,
                "first down tick remains physical Stand upper");
            Near(1.2, crawler.BodyChunks[1].Velocity.X, 0.000001,
                "first down tick remains physical Stand lower");
            Equal((int)BodyModeIndex.Stand, (int)crawler.State.BodyMode,
                "down input does not select Crawl directly");
        }

        private static void CrawlReverseUsesOriginalDynamicRunSpeed()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(new DesktopWindowSnapshot[0]);
            Slugcat crawler = new Slugcat(new Vec2(300.0, 300.0), SlugcatId.White);
            crawler.BodyChunks[0].Position = new Vec2(310.0, 300.0);
            crawler.BodyChunks[1].Position = new Vec2(300.0, 300.0);
            crawler.State.BodyMode = BodyModeIndex.Crawl;
            crawler.State.Standing = false;
            for (int tick = 0; tick < 2; tick++)
            {
                crawler.BodyChunks[0].ContactFloor = true;
                crawler.BodyChunks[1].ContactFloor = true;
                crawler.Movement.ApplyInput(new VirtualInput(-1, 0, false, false), world);
            }
            Near(-1.875, crawler.BodyChunks[0].Velocity.X, 0.000001,
                "Crawl reverse upper cap is 2.5 * .75");
            Near(-1.875, crawler.BodyChunks[1].Velocity.X, 0.000001,
                "Crawl reverse lower cap is 2.5 * .75");
            True(crawler.State.Animation != AnimationIndex.CrawlTurn,
                "dynamicRunSpeed reduction precedes the six-tick CrawlTurn gate");
        }

        private static void FlipAngularForceUsesOriginalEntryKind()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(new DesktopWindowSnapshot[0]);
            Slugcat normal = CreateFlipForForceTest(false);
            Slugcat reversedSlide = CreateFlipForForceTest(true);
            normal.Movement.ApplyInput(VirtualInput.Neutral, world);
            reversedSlide.Movement.ApplyInput(VirtualInput.Neutral, world);
            Near(-0.38, normal.BodyChunks[0].Velocity.X, 0.000001,
                "ordinary backflip upper angular force");
            Near(0.38, normal.BodyChunks[1].Velocity.X, 0.000001,
                "ordinary backflip lower angular force");
            Near(-0.95, reversedSlide.BodyChunks[0].Velocity.X, 0.000001,
                "belly reversal keeps the original 2.5 multiplier");
            Near(0.95, reversedSlide.BodyChunks[1].Velocity.X, 0.000001,
                "belly reversal lower angular force");
        }

        private static Slugcat CreateFlipForForceTest(bool fromSlide)
        {
            Slugcat slugcat = new Slugcat(new Vec2(0.0, 17.0), SlugcatId.White);
            slugcat.BodyChunks[0].Position = Vec2.Zero;
            slugcat.BodyChunks[1].Position = new Vec2(0.0, 17.0);
            slugcat.BodyChunks[0].Velocity = Vec2.Zero;
            slugcat.BodyChunks[1].Velocity = Vec2.Zero;
            slugcat.State.Animation = AnimationIndex.Flip;
            slugcat.State.BodyMode = BodyModeIndex.Default;
            slugcat.State.SlideDirection = 1;
            slugcat.State.FlipFromSlide = fromSlide;
            slugcat.State.AerobicLevel = 0.0;
            return slugcat;
        }

        private static void BackflipEntryMatchesOriginalJumpBranch()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(new DesktopWindowSnapshot[0]);
            Slugcat slugcat = new Slugcat(new Vec2(0.0, 17.0), SlugcatId.White);
            slugcat.BodyChunks[0].Position = Vec2.Zero;
            slugcat.BodyChunks[1].Position = new Vec2(0.0, 17.0);
            slugcat.BodyChunks[0].ContactFloor = true;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.State.BodyMode = BodyModeIndex.Stand;
            slugcat.State.Standing = true;
            slugcat.State.SlideDirection = 1;
            slugcat.State.SlideCounter = 3;
            VirtualInput jump = new VirtualInput(-1, 0, true, false);
            jump.ResolveEdges(VirtualInput.Neutral);
            slugcat.Movement.ApplyInput(jump, world);
            True(slugcat.State.Animation == AnimationIndex.Flip,
                "standing skid jump enters Flip");
            True(!slugcat.State.FlipFromSlide,
                "ordinary backflip is distinct from belly-slide reversal");
            Near(-9.0, slugcat.BodyChunks[0].Velocity.Y, 0.000001,
                "backflip upper vertical assignment");
            Near(-7.0, slugcat.BodyChunks[1].Velocity.Y, 0.000001,
                "backflip lower vertical assignment");
            Near(5.0, slugcat.Movement.JumpBoost, 0.000001,
                "backflip stores five jumpBoost ticks");
            True(!slugcat.State.Grounded &&
                slugcat.State.BodyMode == BodyModeIndex.Default,
                "backflip clears grounded Stand state in the launch tick");
        }

        private static void DesktopRefreshIsAsynchronous()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            long before = world.CurrentSnapshot.Version;
            Stopwatch stopwatch = Stopwatch.StartNew();
            world.RequestRefresh(IntPtr.Zero);
            stopwatch.Stop();
            True(stopwatch.ElapsedMilliseconds < 100,
                "RequestRefresh must only enqueue Win32/DWM work");
            Stopwatch deadline = Stopwatch.StartNew();
            bool applied = false;
            while (deadline.ElapsedMilliseconds < 3000 && !applied)
            {
                applied = world.TryApplyPendingRefresh();
                if (!applied) System.Threading.Thread.Sleep(10);
            }
            True(applied, "background desktop snapshot completed");
            True(world.CurrentSnapshot.Version > before,
                "completed snapshot is applied on the caller tick");
        }

        private static void ArmScaleReflectionMatchesFutileCoordinates()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.BodyMode = BodyModeIndex.Default;
            pose.Chest = new Vec2(100.0, 80.0);
            pose.Hips = new Vec2(100.0, 120.0);
            pose.Hands[0] = new Vec2(65.0, 85.0);
            pose.Hands[1] = new Vec2(135.0, 115.0);
            Near(-1.0, SpriteRenderer.ComputeArmScaleY(pose, 0), 0.000001,
                "screen-left hand becomes positive signed distance before y reflection");
            Near(1.0, SpriteRenderer.ComputeArmScaleY(pose, 1), 0.000001,
                "screen-right hand becomes negative signed distance before y reflection");

            pose.Chest = new Vec2(80.0, 100.0);
            pose.Hips = new Vec2(120.0, 100.0);
            pose.Hands[0] = new Vec2(90.0, 65.0);
            pose.Hands[1] = new Vec2(110.0, 135.0);
            Near(1.0, SpriteRenderer.ComputeArmScaleY(pose, 0), 0.000001,
                "upward spear-hand pose keeps the Futile reflection sign");
            Near(-1.0, SpriteRenderer.ComputeArmScaleY(pose, 1), 0.000001,
                "downward spear-hand pose keeps the Futile reflection sign");
        }

        private static void OriginalPostureTransitionUsesPhysics()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.State.BodyMode = BodyModeIndex.Stand;
            for (int tick = 0; tick < 5; tick++)
            {
                slugcat.BodyChunks[0].ContactFloor = false;
                slugcat.BodyChunks[1].ContactFloor = true;
                slugcat.Movement.ApplyInput(new VirtualInput(0, 1, false, false), world);
                if (tick == 0)
                {
                    True(!slugcat.State.Standing, "down edge clears Player.standing intent");
                    Equal((int)BodyModeIndex.Stand, (int)slugcat.State.BodyMode,
                        "first down frame keeps the physical Stand mode");
                    True(slugcat.State.Animation != AnimationIndex.DownOnFours,
                        "first down frame is not the final crawl transition");
                }
            }
            Equal((int)AnimationIndex.DownOnFours, (int)slugcat.State.Animation,
                "contact counters enter DownOnFours after the original gate");

            slugcat.BodyChunks[0].Position = slugcat.BodyChunks[1].Position;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(new VirtualInput(0, 1, false, false), world);
            Equal((int)BodyModeIndex.Crawl, (int)slugcat.State.BodyMode,
                "BodyChunk geometry establishes Crawl");

            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(new VirtualInput(0, -1, false, false), world);
            True(slugcat.State.Standing, "up edge restores Player.standing intent");
            Equal((int)BodyModeIndex.Crawl, (int)slugcat.State.BodyMode,
                "StandUp begins while the body is still physically low");
            Equal((int)AnimationIndex.StandUp, (int)slugcat.State.Animation,
                "low body enters StandUp instead of swapping to standing sprite");
        }

        private static void BodyChunksShareFrozenSnapshot()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>());
            long snapshotVersion = world.CurrentSnapshot.Version;
            Slugcat slugcat = new Slugcat(new Vec2(200.0, 200.0));
            slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            Equal((int)snapshotVersion, (int)slugcat.BodyChunks[0].CollisionSnapshotVersion,
                "upper chunk snapshot version");
            Equal((int)snapshotVersion, (int)slugcat.BodyChunks[1].CollisionSnapshotVersion,
                "lower chunk snapshot version");
        }

        private sealed class CadenceResult
        {
            public Vec2 Chest;
            public Vec2 Hips;
            public Vec2 ChestVelocity;
            public Vec2 Head;
            public int AnimationFrame;
            public int Updates;
        }

        private static void RefreshRatesPreservePhysicsAndAnimation()
        {
            CadenceResult sixty = SimulateRenderCadence(60);
            CadenceResult oneFortyFour = SimulateRenderCadence(144);
            CadenceResult twoForty = SimulateRenderCadence(240);
            Equal(80, sixty.Updates, "60 Hz update count");
            Equal(80, oneFortyFour.Updates, "144 Hz update count");
            Equal(80, twoForty.Updates, "240 Hz update count");
            Near(0.0, Vec2.Distance(sixty.Chest, oneFortyFour.Chest), 0.000001, "60/144 chest");
            Near(0.0, Vec2.Distance(sixty.Chest, twoForty.Chest), 0.000001, "60/240 chest");
            Near(0.0, Vec2.Distance(sixty.Hips, twoForty.Hips), 0.000001, "60/240 hips");
            Near(0.0, Vec2.Distance(sixty.ChestVelocity, twoForty.ChestVelocity), 0.000001,
                "60/240 velocity");
            Near(0.0, Vec2.Distance(sixty.Head, twoForty.Head), 0.000001,
                "PlayerGraphics fixed-tick state");
            Equal(sixty.AnimationFrame, oneFortyFour.AnimationFrame, "60/144 animation frame");
            Equal(sixty.AnimationFrame, twoForty.AnimationFrame, "60/240 animation frame");
        }

        private static CadenceResult SimulateRenderCadence(int renderRate)
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Slugcat slugcat = new Slugcat(new Vec2(work.Left + work.Width * 0.5,
                work.Bottom - SimulationConstants.HipsChunkRadius - 1.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(80.0, -40.0));
            FixedTimeStep fixedStep = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
            int updates = 0;
            for (int frame = 0; frame < renderRate * 2; frame++)
            {
                fixedStep.AddElapsed(1.0 / renderRate);
                while (fixedStep.ConsumeStep())
                {
                    slugcat.Step(new VirtualInput(1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
                    attention.Step();
                    graphics.Step(attention, world);
                    updates++;
                }
                graphics.BuildPose(fixedStep.Alpha, attention, updates);
            }
            CadenceResult result = new CadenceResult();
            result.Chest = slugcat.BodyChunks[0].Position;
            result.Hips = slugcat.BodyChunks[1].Position;
            result.ChestVelocity = slugcat.BodyChunks[0].Velocity;
            result.Head = graphics.Head.Position;
            result.AnimationFrame = slugcat.State.AnimationFrame;
            result.Updates = updates;
            return result;
        }

        private static void OriginalFreeFallCurve()
        {
            BodyChunk chunk = new BodyChunk(0, Vec2.Zero, 9.0, 0.35);
            int[] ticks = { 1, 2, 5, 10, 20, 40, 80 };
            double[] positions = { 0.8991, 2.6964009000000004, 13.4685314811063,
                49.30244478803089, 187.62055986639854, 727.7679760958325,
                2837.845908953463 };
            double[] velocities = { 0.8991, 1.7973009000000002, 4.486517986505399,
                8.950648203415385, 17.81219163176537, 35.27150352743158,
                69.15931340445094 };
            int sample = 0;
            for (int tick = 1; tick <= 80; tick++)
            {
                chunk.BeginTick();
                chunk.Integrate(SimulationConstants.GravityPerTick, SimulationConstants.AirFriction);
                if (tick != ticks[sample]) continue;
                Near(positions[sample], chunk.Position.Y, 0.00000001, "free-fall y tick " + tick);
                Near(velocities[sample], chunk.Velocity.Y, 0.00000001, "free-fall vy tick " + tick);
                sample++;
                if (sample == ticks.Length) break;
            }
        }

        private static void FreeFallLandsOnDesktopFloor()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            BodyChunk chunk = new BodyChunk(0,
                floorPoint - new Vec2(0.0, DesktopWorldTransform.ToSimulationLength(420.0)),
                9.0, 0.35);
            bool landed = false;
            for (int tick = 0; tick < 80; tick++)
            {
                chunk.BeginTick();
                chunk.Integrate(SimulationConstants.GravityPerTick, SimulationConstants.AirFriction);
                world.Resolve(chunk);
                if (chunk.ContactFloor)
                {
                    landed = true;
                    break;
                }
            }
            True(landed, "free-fall must cross and land within 80 original ticks");
            Near(floorPoint.Y - chunk.Radius, chunk.Position.Y, 0.000001, "landing surface y");
            Near(0.0, chunk.Velocity.Y, 0.000001, "original low-bounce landing stop");
        }

        private static void ConnectionProjectsDistance()
        {
            BodyChunk first = new BodyChunk(0, new Vec2(0.0, 0.0), 9.0, 0.7);
            BodyChunk second = new BodyChunk(1, new Vec2(100.0, 0.0), 8.0, 0.3);
            BodyChunkConnection connection = new BodyChunkConnection(first, second, 17.0,
                BodyChunkConnectionType.Normal, 1.0, 0.5);
            connection.Solve();
            Near(17.0, Vec2.Distance(first.Position, second.Position), 0.0001, "connection distance");
            Near(41.5, first.Velocity.X, 0.0001, "first chunk connection velocity correction");
            Near(-41.5, second.Velocity.X, 0.0001, "second chunk connection velocity correction");
        }

        private static void DesktopFloorCollision()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            BodyChunk chunk = new BodyChunk(0, floorPoint - new Vec2(0.0, 8.0), 9.0, 1.0);
            chunk.LastPosition = floorPoint - new Vec2(0.0, 13.0);
            chunk.Velocity = new Vec2(0.0, 5.0);
            world.Resolve(chunk);
            True(chunk.ContactFloor, "chunk should contact the work-area floor");
            Near(floorPoint.Y - chunk.Radius, chunk.Position.Y, 0.01, "resolved floor height");
        }

        private static void LongFloorContactSurvivesConnectionPenetration()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                SimulationConstants.HipsChunkRadius + 1.0));

            int direction = 1;
            for (int tick = 0; tick < 1600; tick++)
            {
                if (slugcat.Center.X > floorPoint.X + 180.0) direction = -1;
                else if (slugcat.Center.X < floorPoint.X - 180.0) direction = 1;
                bool jump = tick % 240 >= 30 && tick % 240 < 38;
                int down = tick % 360 >= 180 && tick % 360 < 260 ? 1 : 0;
                slugcat.Step(new VirtualInput(direction, down, jump, false),
                    world, Vec2.Zero, Vec2.Zero);
                for (int chunkIndex = 0; chunkIndex < slugcat.BodyChunks.Length; chunkIndex++)
                {
                    BodyChunk chunk = slugcat.BodyChunks[chunkIndex];
                    True(chunk.Position.Y < floorPoint.Y + SimulationConstants.BodyConnectionDistance,
                        "chunk " + chunkIndex + " escaped below work-area floor at tick " + tick +
                        " pos=" + chunk.Position + " floor=" + floorPoint);
                }
            }

            True(slugcat.BodyChunks[0].CollisionSnapshotVersion == world.CurrentSnapshot.Version &&
                 slugcat.BodyChunks[1].CollisionSnapshotVersion == world.CurrentSnapshot.Version,
                 "long run must retain one current terrain snapshot for both chunks");
        }

        private static void MonitorCornerSurvivesConnectionPenetration()
        {
            MonitorInfo monitor = new MonitorInfo("CORNER-MONITOR",
                new Rectangle(0, 0, 1200, 900), new Rectangle(0, 0, 1200, 850), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            double wall = DesktopWorldTransform.ToSimulationLength(monitor.Bounds.Left);
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            Slugcat slugcat = new Slugcat(new Vec2(
                wall + SimulationConstants.HipsChunkRadius,
                floor - SimulationConstants.HipsChunkRadius));

            for (int tick = 0; tick < 800; tick++)
            {
                slugcat.Step(new VirtualInput(-1, -1, false, false),
                    world, Vec2.Zero, Vec2.Zero);
                for (int chunkIndex = 0; chunkIndex < slugcat.BodyChunks.Length; chunkIndex++)
                {
                    BodyChunk chunk = slugcat.BodyChunks[chunkIndex];
                    True(chunk.Position.X - chunk.Radius >= wall - 0.000001,
                        "chunk " + chunkIndex + " crossed the exposed left boundary at tick " + tick);
                    True(chunk.Position.Y + chunk.Radius <= floor + 0.000001,
                        "chunk " + chunkIndex + " crossed the monitor floor at tick " + tick);
                }
            }
        }

        private static void FastHorizontalSmallWindowDoesNotTunnel()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int left = work.Left + work.Width / 2;
            int top = work.Top + 260;
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>
            {
                new DesktopWindowSnapshot
                {
                    Handle = new IntPtr(91001),
                    Bounds = Rectangle.FromLTRB(left, top, left + 20, top + 120),
                    Title = "small swept platform",
                    ClassName = "test"
                }
            });
            Vec2 topLeft = DesktopWorldTransform.ToSimulation(new Vec2(left, top));
            Vec2 topRight = DesktopWorldTransform.ToSimulation(new Vec2(left + 20, top));
            BodyChunk chunk = new BodyChunk(0, topLeft - new Vec2(30.0, 19.0), 9.0, 0.35);
            chunk.Velocity = new Vec2(66.0, 20.0);
            chunk.BeginTick();
            chunk.Integrate(0.0, 1.0);
            True(chunk.Position.X > topRight.X, "final X must have crossed the complete small window");
            world.Resolve(chunk);
            True(chunk.ContactFloor, "impact-time X must detect the narrow window top");
            Near(topLeft.Y - chunk.Radius, chunk.Position.Y, 0.000001, "small-window landing y");
            Equal(91001, (int)chunk.SupportingSurfaceId, "small-window supporting HWND");
        }

        private static void DraggingPassesThroughWindowWalls()
        {
            MonitorInfo monitor = new MonitorInfo("DRAG-MONITOR",
                new Rectangle(0, 0, 1200, 900), new Rectangle(0, 0, 1200, 860), true);
            DesktopWindowSnapshot obstacle = Window(7301, new Rectangle(500, 100, 200, 650));
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new[] { obstacle });
            Vec2 start = DesktopWorldTransform.ToSimulation(new Vec2(450.0, 300.0));
            Vec2 target = DesktopWorldTransform.ToSimulation(new Vec2(780.0, 300.0));
            Slugcat slugcat = new Slugcat(start);

            True(slugcat.Grab(slugcat.BodyChunks[0].Position), "head chunk should be grabbed");
            for (int tick = 0; tick < 12; tick++)
                slugcat.Step(VirtualInput.Neutral, world, target, Vec2.Zero);

            double rightWall = DesktopWorldTransform.ToSimulation(new Vec2(700.0, 300.0)).X;
            True(slugcat.BodyChunks[0].Position.X > rightWall + slugcat.BodyChunks[0].Radius,
                "grabbed chunk should cross the other window's right wall");
            True(slugcat.BodyChunks[0].WallSurfaceId == 0,
                "dragging should not retain a window wall contact");
        }

        private static void SlugcatDraggingBlocksDesktopInteractions()
        {
            int overlayStyle = LayeredOverlayWindow.BuildOverlayExtendedStyle(
                0);
            True((overlayStyle & NativeMethods.WS_EX_TRANSPARENT) != 0,
                "the full-desktop overlay must remain click-through to other applications");
            True((overlayStyle & NativeMethods.WS_EX_TOPMOST) != 0,
                "the overlay must be created in the topmost window band");
            True((overlayStyle & NativeMethods.WS_EX_NOACTIVATE) != 0,
                "restoring topmost state must not require activating the overlay");
            True(LayeredOverlayWindow.ShouldSuppressLeftButton(
                    NativeMethods.WM_LBUTTONDOWN, false, true),
                "the initial press on a Slugcat must be suppressed");
            True(LayeredOverlayWindow.ShouldSuppressLeftButton(
                    NativeMethods.WM_LBUTTONUP, true, false),
                "the release completing a Slugcat drag must be suppressed");
            True(!LayeredOverlayWindow.ShouldSuppressLeftButton(
                    NativeMethods.WM_LBUTTONUP, false, true),
                "an unrelated release must continue to the underlying application");
            True(!LayeredOverlayWindow.ShouldSuppressLeftButton(
                    NativeMethods.WM_LBUTTONDOWN, false, false),
                "a click outside every Slugcat must reach the underlying application");
        }

        private static void MouseHookHitSnapshotsPreserveInputRules()
        {
            object lowerSlugcat = new object();
            object upperSlugcat = new object();
            MouseHookHitCircle[] circles =
            {
                new MouseHookHitCircle(new Vec2(100.0, 100.0), 30.0),
                new MouseHookHitCircle(new Vec2(40.0, 40.0), 10.0),
                new MouseHookHitCircle(new Vec2(110.0, 100.0), 20.0)
            };
            MouseHookHitSnapshot snapshot = new MouseHookHitSnapshot(new[]
            {
                new MouseHookHitTarget(lowerSlugcat, 0, 2),
                new MouseHookHitTarget(upperSlugcat, 2, 1)
            }, circles);

            True(ReferenceEquals(upperSlugcat,
                    snapshot.HitTest(new Vec2(105.0, 100.0))),
                "the last rendered Slugcat should own an overlapping click");
            True(ReferenceEquals(lowerSlugcat,
                    snapshot.HitTest(new Vec2(40.0, 50.0))),
                "hit circles should include their exact boundary");
            True(snapshot.HitTest(new Vec2(400.0, 400.0)) == null,
                "a click outside immutable pet bounds must remain click-through");
        }

        private static void AiDoesNotMoveCreature()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            DesktopPetAI ai = new DesktopPetAI(1234);
            MouseTracker mouse = new MouseTracker();
            mouse.Sample(0.025);
            Vec2 before = slugcat.Center;
            VirtualInput input = ai.Step(slugcat, world, mouse);
            Vec2 after = slugcat.Center;
            Near(0.0, Vec2.Distance(before, after), 0.000001, "AI must not set position");
            True(input.X >= -1 && input.X <= 1, "virtual horizontal input range");
        }

        private static void AtlasMetadataParses()
        {
            string root = Path.Combine(Path.GetTempPath(), "slugcat-atlas-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string png = Path.Combine(root, "player.png");
            string txt = Path.Combine(root, "player.txt");
            try
            {
                using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb)) bitmap.Save(png, ImageFormat.Png);
                File.WriteAllText(txt,
                    "{\"frames\":{\"BodyA.png\":{\"frame\":{\"x\":1,\"y\":2,\"w\":10,\"h\":12}," +
                    "\"rotated\":false,\"trimmed\":true,\"spriteSourceSize\":{\"x\":3,\"y\":4,\"w\":10,\"h\":12}," +
                    "\"sourceSize\":{\"w\":18,\"h\":20}}}}");
                using (RainWorldAtlas atlas = RainWorldAtlasLoader.Load(png, txt))
                {
                    AtlasElement element;
                    True(atlas.TryGet("BodyA", out element), "extensionless lookup");
                    Equal(10, element.Frame.Width, "frame width");
                    Equal(20, element.SourceSize.Height, "source height");
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void OffscreenThrowRecoveryTarget()
        {
            MonitorInfo monitor = new MonitorInfo("RECOVERY",
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1920, 1040), true);
            IList<MonitorInfo> monitors = new List<MonitorInfo> { monitor };
            Vec2 visible = DesktopWorldTransform.ToSimulation(new Vec2(960.0, 500.0));
            Vec2 escaped = DesktopWorldTransform.ToSimulation(new Vec2(2600.0, -500.0));
            True(DesktopRecovery.IsNearAnyMonitor(visible, monitors), "visible point detection");
            True(!DesktopRecovery.IsNearAnyMonitor(escaped, monitors), "escaped point detection");
            True(DesktopRecovery.IsFarOutsideVirtualDesktop(escaped, monitor.Bounds),
                "hard escape detection");

            Vec2 aboveCeiling = DesktopWorldTransform.ToSimulation(new Vec2(960.0, -500.0));
            Vec2 aboveButOutsideColumn = DesktopWorldTransform.ToSimulation(
                new Vec2(2300.0, -500.0));
            True(DesktopRecovery.IsAboveMonitorCeiling(aboveCeiling, monitors),
                "ceiling throw remains a physical excursion");
            True(!DesktopRecovery.IsAboveMonitorCeiling(aboveButOutsideColumn, monitors),
                "side escape above the ceiling still recovers");

            Vec2 safe = DesktopRecovery.FindSafeHipsPosition(escaped, monitors,
                SimulationConstants.HipsChunkRadius);
            Vec2 safeDesktop = DesktopWorldTransform.ToDesktop(safe);
            True(safeDesktop.X >= monitor.WorkArea.Left && safeDesktop.X < monitor.WorkArea.Right,
                "recovery x inside work area");
            True(safeDesktop.Y < monitor.FloorY && safeDesktop.Y > monitor.WorkArea.Top,
                "recovery hips above desktop floor");

            Slugcat slugcat = new Slugcat(Vec2.Zero);
            slugcat.BodyChunks[0].Velocity = new Vec2(20.0, -15.0);
            slugcat.BodyChunks[1].Velocity = new Vec2(20.0, -15.0);
            slugcat.Reposition(safe);
            Near(0.0, slugcat.BodyChunks[0].Velocity.Length, 0.000001,
                "recovered chest velocity");
            Near(0.0, slugcat.BodyChunks[1].Velocity.Length, 0.000001,
                "recovered hips velocity");
            Near(SimulationConstants.BodyConnectionDistance,
                Vec2.Distance(slugcat.BodyChunks[0].Position, slugcat.BodyChunks[1].Position),
                0.000001, "recovered body connection");
        }

        private static void DmsPartAtlasOverrideRestoresBase()
        {
            string root = Path.Combine(Path.GetTempPath(), "slugcat-dms-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string json = "{\"frames\":{\"BodyA.png\":{\"frame\":{\"x\":0,\"y\":0,\"w\":8,\"h\":8}," +
                "\"rotated\":false,\"spriteSourceSize\":{\"x\":0,\"y\":0,\"w\":8,\"h\":8}," +
                "\"sourceSize\":{\"w\":8,\"h\":8}}}}";
            string basePng = Path.Combine(root, "base.png");
            string baseTxt = Path.Combine(root, "base.txt");
            string dmsPng = Path.Combine(root, "body.png");
            string dmsTxt = Path.Combine(root, "body.txt");
            try
            {
                using (Bitmap bitmap = new Bitmap(8, 8)) bitmap.Save(basePng, ImageFormat.Png);
                using (Bitmap bitmap = new Bitmap(8, 8)) bitmap.Save(dmsPng, ImageFormat.Png);
                File.WriteAllText(baseTxt, json);
                File.WriteAllText(dmsTxt, json);
                using (RainWorldAtlasSet set = new RainWorldAtlasSet())
                {
                    set.Add(RainWorldAtlasLoader.Load(basePng, baseTxt));
                    set.SetPartOverride("Body", RainWorldAtlasLoader.Load(dmsPng, dmsTxt));
                    AtlasSprite sprite;
                    True(set.TryGet("BodyA", out sprite) && sprite.Atlas.ImagePath == dmsPng,
                        "DMS body must override the base atlas");
                    set.ClearPartOverride("Body");
                    True(set.TryGet("BodyA", out sprite) && sprite.Atlas.ImagePath == basePng,
                        "Default must restore the original atlas sprite");
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void DmsSpritesBesideExecutableAreDiscovered()
        {
            string root = Path.Combine(Path.GetTempPath(), "slugcat-dms-root-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string json = "{\"frames\":{\"HeadA0.png\":{\"frame\":{\"x\":0,\"y\":0,\"w\":8,\"h\":8}," +
                "\"rotated\":false,\"spriteSourceSize\":{\"x\":0,\"y\":0,\"w\":8,\"h\":8}," +
                "\"sourceSize\":{\"w\":8,\"h\":8}}}}";
            try
            {
                using (Bitmap bitmap = new Bitmap(8, 8))
                    bitmap.Save(Path.Combine(root, "head.png"), ImageFormat.Png);
                File.WriteAllText(Path.Combine(root, "head.txt"), json);
                File.WriteAllText(Path.Combine(root, "metadata.json"),
                    "{\"id\":\"portable\",\"name\":\"Portable skin\"}");

                using (DmsSpriteCatalog catalog = new DmsSpriteCatalog(null, root))
                {
                    Equal(1, catalog.Sets.Count,
                        "a sprite set placed directly beside the executable");
                    True(catalog.Sets[0].Id == "portable" &&
                         catalog.Sets[0].DirectoryPath == root,
                        "the executable-root sprite set keeps its metadata and path");
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void PartColorsReachRenderedPose()
        {
            Slugcat slugcat = new Slugcat(new Vec2(200.0, 200.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            graphics.SetPartColor("Head", Color.CornflowerBlue);
            graphics.SetPartColor("Face", Color.OrangeRed);
            graphics.SetPartColor("Tail", Color.MediumPurple);
            SlugcatPose pose = graphics.BuildPose(0.0, new AttentionSystem());
            True(pose.VisualHeadColor.ToArgb() == Color.CornflowerBlue.ToArgb(), "head tint");
            True(pose.VisualEyeColor.ToArgb() == Color.OrangeRed.ToArgb(), "face tint");
            True(pose.VisualTailColor.ToArgb() == Color.MediumPurple.ToArgb(), "tail tint");
        }

        private static void LocatorValidatesExplicitPath()
        {
            string root = Path.Combine(Path.GetTempPath(), "slugcat-locator-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "RainWorld_Data", "Managed"));
                Directory.CreateDirectory(Path.Combine(root, "RainWorld_Data", "StreamingAssets"));
                using (File.Create(Path.Combine(root, "RainWorld.exe"))) { }
                using (File.Create(Path.Combine(root, "RainWorld_Data", "Managed", "Assembly-CSharp.dll"))) { }
                RainWorldLocator locator = new RainWorldLocator(Path.Combine(root, "test-settings", "rain-world-path.txt"));
                True(locator.IsValid(root), "fake layout should validate");
                RainWorldInstallation installation = locator.Locate(root);
                True(installation != null && string.Equals(installation.RootPath, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
                    "explicit path should win");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void RequiredBehaviorsExist()
        {
            string[] names =
            {
                "Idle", "Walk", "Explore", "Sit", "Sleep", "LookAround", "FollowMouse", "AvoidMouse",
                "Jump", "ClimbWindow", "DropDown", "BalanceNearEdge", "ObserveWindow"
            };
            HashSet<string> actual = new HashSet<string>(Enum.GetNames(typeof(DesktopBehavior)), StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++) True(actual.Contains(names[i]), "missing behavior " + names[i]);
        }

        private static void UtilityActionsAreReachable()
        {
            UtilityContext jump = new UtilityContext
            {
                Grounded = true,
                Curiosity = 1.0,
                JumpReady = true,
                TransitionAvailable = true,
                EdgeDistance = 200.0
            };
            double worstJump = UtilityEvaluator.Score(DesktopBehavior.Jump, jump, -0.06);
            double stickyExplore = UtilityEvaluator.Score(DesktopBehavior.Explore, jump, 0.08) + 0.07;
            True(worstJump > stickyExplore,
                "ready Jump must beat a hysteretic Explore score even at adverse random variation");

            UtilityContext drop = new UtilityContext
            {
                Grounded = true,
                OnWindow = true,
                Curiosity = 0.9,
                DropReady = true,
                EdgeDistance = 10.0
            };
            double worstDrop = UtilityEvaluator.Score(DesktopBehavior.DropDown, drop, -0.06);
            double stickyBalance = UtilityEvaluator.Score(DesktopBehavior.BalanceNearEdge, drop, 0.08) + 0.07;
            True(worstDrop > stickyBalance,
                "ready DropDown must beat a hysteretic BalanceNearEdge score");

            jump.JumpReady = false;
            drop.DropReady = false;
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.Jump, jump, 0.0), 0.000001,
                "Jump cooldown gate");
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.DropDown, drop, 0.0), 0.000001,
                "DropDown cooldown gate");
        }

        private static void MouseLocomotionRequiresAttention()
        {
            UtilityContext context = new UtilityContext
            {
                Grounded = true,
                MouseDistance = 180.0,
                Curiosity = 0.8,
                PersonalityAggression = 0.8,
                PersonalityNervous = 0.8
            };
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.FollowMouse, context, 0.0),
                0.000001, "passive cursor proximity cannot select FollowMouse");

            context.MouseDistance = 50.0;
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.AvoidMouse, context, 0.0),
                0.000001, "passive cursor proximity cannot select AvoidMouse");

            context.MouseAttentionActive = true;
            context.MouseDistance = 180.0;
            True(UtilityEvaluator.Score(DesktopBehavior.FollowMouse, context, 0.0) > 0.0,
                "near clicked mouse can select FollowMouse");
            context.MouseDistance = 50.0;
            True(UtilityEvaluator.Score(DesktopBehavior.AvoidMouse, context, 0.0) > 0.0,
                "near clicked mouse can select AvoidMouse");
        }

        private static void ObstacleJumpIsReachable()
        {
            UtilityContext obstacle = new UtilityContext
            {
                Grounded = true,
                Curiosity = 1.0,
                JumpReady = true,
                ObstacleAhead = true,
                ObstacleDirection = 1,
                TransitionAvailable = false
            };
            True(UtilityEvaluator.Score(DesktopBehavior.Jump, obstacle, 0.0) > 0.0,
                "a blocking wall enables a Player.Jump attempt without a platform route");
            obstacle.JumpReady = false;
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.Jump, obstacle, 0.0),
                0.000001, "obstacle jump still respects the original jump cooldown");
        }

        private static void ExplorationJumpIsReachable()
        {
            UtilityContext exploration = new UtilityContext
            {
                Grounded = true,
                Curiosity = 0.9,
                JumpReady = true,
                ExplorationJumpAvailable = true,
                EdgeDistance = 120.0
            };
            True(UtilityEvaluator.Score(DesktopBehavior.Jump, exploration, 0.0) > 0.0,
                "an interior exploration intent can choose an original free jump");
            exploration.ExplorationJumpAvailable = false;
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.Jump, exploration, 0.0),
                0.000001, "free jump requires an exploration intent or route");
        }

        private static void WallContactReachesClimbMovement()
        {
            Point cursor = System.Windows.Forms.Cursor.Position;
            Slugcat slugcat = new Slugcat(new Vec2(cursor.X + 500.0, cursor.Y + 500.0));
            slugcat.BodyChunks[0].ContactRight = true;
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            MouseTracker mouse = new MouseTracker();
            mouse.Sample(SimulationConstants.LogicStepSeconds);
            DesktopPetAI ai = new DesktopPetAI(991);

            VirtualInput input = ai.Step(slugcat, world, mouse);
            True(ai.Behavior == DesktopBehavior.ClimbWindow, "wall-contact rising edge should select ClimbWindow");
            Equal(1, input.X, "climb input should press into a right-side wall");
            Equal(-1, input.Y, "climb input should press upward");

            slugcat.Movement.ApplyInput(input, world);
            True(slugcat.State.BodyMode == BodyModeIndex.WallClimb,
                "movement must interpret climb VirtualInput without direct AI movement");
            True(slugcat.BodyChunks[0].Velocity.Y >= 0.0 && slugcat.BodyChunks[1].Velocity.Y >= 0.0,
                "wall slide must not inject upward screen-space velocity");
        }

        private static void OriginalFaceFrameSelection()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Hips = Vec2.Zero;
            pose.Head = new Vec2(0.0, -12.0);
            pose.LookDirection = new Vec2(1.0, 0.0);
            pose.BodyMode = BodyModeIndex.Stand;
            pose.Animation = AnimationIndex.None;
            Equal(0, SpriteRenderer.SelectFaceFrame(pose), "upright face frame");

            pose.Head = new Vec2(12.0, 0.0);
            pose.LookDirection = Vec2.Zero;
            Equal(4, SpriteRenderer.SelectFaceFrame(pose), "horizontal face frame");

            pose.Animation = AnimationIndex.Sleep;
            Equal(1, SpriteRenderer.SelectFaceFrame(pose), "sleep curl face frame");
            pose.Facing = 1;
            Near(45.0, SpriteRenderer.SelectHeadAngle(pose), 0.000001, "right-facing sleep head angle");
            pose.Facing = -1;
            Near(-45.0, SpriteRenderer.SelectHeadAngle(pose), 0.000001, "left-facing sleep head angle");
        }

        private static void OriginalFaceResolverMatchesDllStates()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = new Vec2(0.0, 0.0);
            pose.Hips = new Vec2(0.0, 17.0);
            pose.Head = new Vec2(0.0, -10.0);
            pose.LookDirection = new Vec2(1.0, 0.0);
            pose.Facing = 1;
            pose.Conscious = true;

            pose.BodyMode = BodyModeIndex.Stand;
            pose.InputX = 1;
            OriginalFaceState state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.HeadElement == "HeadA6", "moving Stand head element");
            True(state.FaceElement == "FaceA4", "moving Stand face element");
            Near(1.0, state.FaceScaleX, 0.000001, "moving Stand head-facing scaleX");
            Near(pose.Head.X, state.FacePosition.X, 0.000001,
                "moving Stand zeroes only the DLL look offset x");

            pose.BodyMode = BodyModeIndex.Crawl;
            pose.InputX = -1;
            pose.Chest = new Vec2(-8.0, 0.0);
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.HeadElement == "HeadA7", "Crawl head element");
            True(state.FaceElement == "FaceA4", "Crawl face element");
            Near(-1.0, state.FaceScaleX, 0.000001, "Crawl uses body axis, not look x");

            pose.BodyMode = BodyModeIndex.Default;
            pose.Animation = AnimationIndex.None;
            pose.InputX = -1;
            pose.IsAirborne = true;
            pose.IsRising = true;
            pose.Chest = new Vec2(0.0, 0.0);
            pose.LookDirection = new Vec2(-1.0, 0.0);
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement == "FaceA0", "upright air face frame");
            Near(-1.0, state.FaceScaleX, 0.000001, "air face follows look x");
            True(state.Reason == "AirborneRising", "airborne face diagnostic reason");

            pose.IsRising = false;
            pose.IsFalling = true;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement == "FaceA0", "fall keeps the same original resolver");
            True(state.Reason == "AirborneFalling", "fall diagnostic reason");

            pose.IsAirborne = false;
            pose.Animation = AnimationIndex.Sleep;
            pose.Chest = new Vec2(8.0, 0.0);
            pose.Hips = Vec2.Zero;
            pose.Head = new Vec2(0.0, -10.0);
            pose.LookDirection = new Vec2(-1.0, 0.0);
            pose.Blink = true;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.HeadElement == "HeadA4" && state.FaceElement == "FaceB1",
                "full sleep elements");
            Near(-2.0, state.FacePosition.X - pose.Head.X, 0.000001,
                "full sleep face x from DrawSprites");
            Near(3.0, state.FacePosition.Y - pose.Head.Y, 0.000001,
                "full sleep face y from y-up conversion");

            pose.Animation = AnimationIndex.None;
            pose.Blink = false;
            pose.Conscious = false;
            pose.Dead = false;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement == "FaceStunned", "unconscious face element");
            pose.Dead = true;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement == "FaceDead", "dead face element");
        }

        private static void WallClimbHandsTargetTheWall()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.BodyMode = BodyModeIndex.WallClimb;
            slugcat.State.Facing = 1;
            slugcat.BodyChunks[0].ContactRight = true;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(50.0, -20.0));

            graphics.Step(attention, world);
            True(graphics.Arms[0].TargetPosition.X > slugcat.BodyChunks[0].Position.X,
                "both wall-climb hands should target the contacted wall side");
            True(graphics.Arms[1].TargetPosition.X > slugcat.BodyChunks[0].Position.X,
                "both wall-climb hands should target the contacted wall side");
            Near(slugcat.BodyChunks[0].Position.Y - 3.0, graphics.Arms[0].TargetPosition.Y, 0.000001,
                "upper wall hand offset");
            Near(slugcat.BodyChunks[0].Position.Y + 7.0, graphics.Arms[1].TargetPosition.Y, 0.000001,
                "lower wall hand offset");
        }

        private static void SleepCurlHandsShareOriginalTarget()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.Animation = AnimationIndex.Sleep;
            slugcat.State.Facing = -1;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center);

            graphics.Step(attention, world);

            Vec2 expected = slugcat.Center + new Vec2(-10.0, 20.0);
            Near(0.0, Vec2.Distance(expected, graphics.Arms[0].TargetPosition), 0.000001,
                "left sleep hand target");
            Near(0.0, Vec2.Distance(expected, graphics.Arms[1].TargetPosition), 0.000001,
                "right sleep hand target");
        }

        private static void MovingWindowWallCarriesClimber()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(4321),
                Bounds = Rectangle.FromLTRB(100, 100, 300, 300),
                Title = "test",
                ClassName = "test"
            });
            world.RefreshFromSnapshots(snapshots);

            snapshots[0].Bounds = Rectangle.FromLTRB(120, 130, 320, 330);
            world.RefreshFromSnapshots(snapshots);
            Vec2 wallDelta = world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowLeftWall);
            Near(20.0 / 2.2, wallDelta.X, 0.000001, "translated left-wall x delta");
            Near(30.0 / 2.2, wallDelta.Y, 0.000001, "translated left-wall y delta");

            Slugcat slugcat = new Slugcat(new Vec2(80.0, 180.0));
            slugcat.State.BodyMode = BodyModeIndex.WallClimb;
            slugcat.BodyChunks[0].WallSurfaceId = 4321;
            slugcat.BodyChunks[0].WallSurfaceKind = DesktopSurfaceKind.WindowLeftWall;
            Vec2 chestBefore = slugcat.BodyChunks[0].Position;
            Vec2 hipsBefore = slugcat.BodyChunks[1].Position;
            Vec2 applied = slugcat.ApplyMovingSurfaceDelta(world);
            Near(20.0 / 2.2, applied.X, 0.000001, "applied wall x delta");
            Near(30.0 / 2.2, applied.Y, 0.000001, "applied wall y delta");
            Near(0.0, Vec2.Distance(chestBefore + applied, slugcat.BodyChunks[0].Position), 0.000001,
                "climbing chest follows wall");
            Near(0.0, Vec2.Distance(hipsBefore + applied, slugcat.BodyChunks[1].Position), 0.000001,
                "climbing hips follows wall");

            snapshots[0].Bounds = Rectangle.FromLTRB(140, 130, 320, 330);
            world.RefreshFromSnapshots(snapshots);
            Near(0.0, world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowTop).X, 0.000001,
                "left-edge resize must not translate the top platform");
            Near(20.0 / 2.2, world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowLeftWall).X, 0.000001,
                "left-edge resize moves the left wall");
            Near(0.0, world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowRightWall).X, 0.000001,
                "left-edge resize leaves the right wall fixed");
        }

        private static void MovingWindowCarriesConnectedBody()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2[] movements =
            {
                new Vec2(80.0, 0.0), new Vec2(-80.0, 0.0),
                new Vec2(0.0, 60.0), new Vec2(0.0, -60.0),
                new Vec2(120.0, -70.0)
            };
            for (int movement = 0; movement < movements.Length; movement++)
            {
                Vec2 desktopDelta = movements[movement];
                Vec2 delta = DesktopWorldTransform.ToSimulationDelta(desktopDelta);
                int left = work.Left + work.Width / 3;
                int top = work.Top + Math.Max(180, work.Height / 3);
                DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
                List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>
                {
                    new DesktopWindowSnapshot
                    {
                        Handle = new IntPtr(92000 + movement),
                        Bounds = Rectangle.FromLTRB(left, top, left + 300, top + 180),
                        Title = "moving platform",
                        ClassName = "test"
                    }
                };
                world.RefreshFromSnapshots(snapshots);
                Slugcat slugcat = new Slugcat(new Vec2(left + 140.0,
                    top - SimulationConstants.HipsChunkRadius));
                long id = 92000 + movement;
                slugcat.BodyChunks[0].SupportingSurfaceId = id;
                slugcat.BodyChunks[1].SupportingSurfaceId = id;
                Vec2 chestBefore = slugcat.BodyChunks[0].Position;
                Vec2 hipsBefore = slugcat.BodyChunks[1].Position;
                double connectionBefore = Vec2.Distance(chestBefore, hipsBefore);
                snapshots[0].Bounds = Rectangle.FromLTRB(
                    left + (int)desktopDelta.X, top + (int)desktopDelta.Y,
                    left + 300 + (int)desktopDelta.X, top + 180 + (int)desktopDelta.Y);
                world.RefreshFromSnapshots(snapshots);
                Vec2 applied = slugcat.ApplyMovingSurfaceDelta(world);
                Near(0.0, Vec2.Distance(delta, applied), 0.000001, "platform delta " + movement);
                Near(0.0, Vec2.Distance(chestBefore + delta, slugcat.BodyChunks[0].Position),
                    0.000001, "chest carry " + movement);
                Near(0.0, Vec2.Distance(hipsBefore + delta, slugcat.BodyChunks[1].Position),
                    0.000001, "hips carry " + movement);
                Near(connectionBefore, Vec2.Distance(slugcat.BodyChunks[0].Position,
                    slugcat.BodyChunks[1].Position), 0.000001, "body integrity " + movement);
            }
        }

        private static void TransientWindowMissesUseGracePeriod()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            long id = 93001;
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> present = new List<DesktopWindowSnapshot>
            {
                new DesktopWindowSnapshot
                {
                    Handle = new IntPtr(id),
                    Bounds = Rectangle.FromLTRB(work.Left + 200, work.Top + 220,
                        work.Left + 500, work.Top + 440),
                    Title = "grace",
                    ClassName = "test"
                }
            };
            DesktopSurface surface;
            world.RefreshFromSnapshots(present);
            True(world.TryGetSurface(id, DesktopSurfaceKind.WindowTop, out surface), "initial HWND surface");
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), false, false);
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), false, false);
            True(world.TryGetSurface(id, DesktopSurfaceKind.WindowTop, out surface),
                "failed EnumWindows must preserve cached surface");
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), true, false);
            True(world.TryGetSurface(id, DesktopSurfaceKind.WindowTop, out surface), "first transient miss");
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), true, false);
            True(world.TryGetSurface(id, DesktopSurfaceKind.WindowTop, out surface), "second transient miss");
            world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), true, false);
            True(!world.TryGetSurface(id, DesktopSurfaceKind.WindowTop, out surface),
                "surface expires after grace period");
        }

        private static void StaleLimbGripReleases()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int left = work.Left + work.Width / 2;
            int top = work.Top + 260;
            long id = 94001;
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>
            {
                new DesktopWindowSnapshot
                {
                    Handle = new IntPtr(id),
                    Bounds = Rectangle.FromLTRB(left, top, left + 240, top + 160),
                    Title = "grip",
                    ClassName = "test"
                }
            };
            world.RefreshFromSnapshots(snapshots);
            Slugcat slugcat = new Slugcat(new Vec2(left + 20.0, top - 8.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            Limb arm = graphics.Arms[0];
            arm.Mode = LimbMode.HuntAbsolutePosition;
            arm.AbsoluteHuntPosition = new Vec2(left + 20.0, top);
            arm.GripSurfaceId = id;
            arm.GripSurfaceKind = DesktopSurfaceKind.WindowTop;
            for (int i = 0; i <= SimulationConstants.MissingWindowRefreshGrace; i++)
                world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), true, false);
            slugcat.State.BodyMode = BodyModeIndex.Stand;
            slugcat.State.Animation = AnimationIndex.None;
            arm.Step(slugcat, slugcat.BodyChunks[0].Position, slugcat.BodyChunks[1].Position,
                slugcat.BodyChunks[0].Velocity, world);
            True(arm.GripSurfaceId == 0, "removed HWND grip identity must clear");
            True(!arm.IsPlanted, "removed HWND cannot visually support the limb");
        }

        private static void MonitorCeilingWindowTopIsRejected()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int inset = Math.Max(20, Math.Min(100, work.Width / 4));
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(8765),
                Bounds = Rectangle.FromLTRB(work.Left + inset, work.Top,
                    Math.Min(work.Right - inset, work.Left + inset + 300),
                    Math.Min(work.Bottom, work.Top + 300)),
                Title = "top-snapped",
                ClassName = "test"
            });
            world.RefreshFromSnapshots(snapshots);

            int wallSegments = 0;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                True(surface.Id != 8765 || surface.Kind != DesktopSurfaceKind.WindowTop,
                    "a top-border window must not create an off-screen standing surface");
                if (surface.Id == 8765 &&
                    (surface.Kind == DesktopSurfaceKind.WindowLeftWall ||
                     surface.Kind == DesktopSurfaceKind.WindowRightWall))
                {
                    wallSegments++;
                    True(surface.Top >= work.Top + SimulationConstants.VisibleWindowTopClearance,
                        "a top-border wall must be clipped to the visible climbing band");
                }
            }
            True(wallSegments > 0, "the visible part of an inset window wall should remain climbable");
        }

        private static void OccludedWindowsAreClipped()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7001),
                Bounds = Rectangle.FromLTRB(0, 0, 500, 500),
                Title = "front",
                ClassName = "front"
            });
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7002),
                Bounds = Rectangle.FromLTRB(100, 100, 300, 300),
                Title = "covered",
                ClassName = "covered"
            });
            world.RefreshFromSnapshots(snapshots);
            IList<DesktopSurface> surfaces = world.Surfaces;
            for (int i = 0; i < surfaces.Count; i++)
                True(surfaces[i].Id != 7002, "fully occluded window surface should be removed");

            for (int miss = 0; miss <= SimulationConstants.MissingWindowRefreshGrace; miss++)
                world.RefreshFromSnapshots(new List<DesktopWindowSnapshot>(), true, false);
            snapshots.Clear();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7003),
                Bounds = Rectangle.FromLTRB(150, 50, 250, 150),
                Title = "partial front",
                ClassName = "partial front"
            });
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7004),
                Bounds = Rectangle.FromLTRB(100, 100, 300, 300),
                Title = "partial back",
                ClassName = "partial back"
            });
            world.RefreshFromSnapshots(snapshots);
            int visibleTopSegments = 0;
            surfaces = world.Surfaces;
            for (int i = 0; i < surfaces.Count; i++)
            {
                if (surfaces[i].Id == 7004 && surfaces[i].Kind == DesktopSurfaceKind.WindowTop)
                {
                    visibleTopSegments++;
                    True(surfaces[i].Right <= 150 || surfaces[i].Left >= 250,
                        "partially covered top segments must exclude the occluder interval");
                }
            }
            Equal(2, visibleTopSegments, "visible top segment count after clipping");
        }

        private static void OriginalVariantValues()
        {
            SlugcatAppearance survivor = SlugcatAppearance.For(SlugcatVariant.Survivor);
            SlugcatAppearance monk = SlugcatAppearance.For(SlugcatVariant.Monk);
            SlugcatAppearance hunter = SlugcatAppearance.For(SlugcatVariant.Hunter);
            SlugcatAppearance gourmand = SlugcatAppearance.For(SlugcatVariant.Gourmand);

            Equal(Color.FromArgb(255, 255, 255, 255).ToArgb(), survivor.BodyColor.ToArgb(), "Survivor color");
            Equal(Color.FromArgb(255, 255, 255, 115).ToArgb(), monk.BodyColor.ToArgb(), "Monk color");
            Equal(Color.FromArgb(255, 255, 115, 115).ToArgb(), hunter.BodyColor.ToArgb(), "Hunter color");
            Equal(Color.FromArgb(255, 240, 193, 151).ToArgb(), gourmand.BodyColor.ToArgb(), "Gourmand color");
            Near(1.2, hunter.RunSpeedFactor, 0.000001, "Hunter run-speed factor");
            Near(1.35, gourmand.BodyWeightFactor, 0.000001, "Gourmand body-weight factor");
            Near(1.4, gourmand.BodyWidthScale, 0.000001, "Gourmand body scaleX");
            Near(1.6, gourmand.HipsWidthScale, 0.000001, "Gourmand hips scaleX");

            Slugcat creature = new Slugcat(Vec2.Zero, SlugcatVariant.Gourmand);
            Near(0.35 * 1.35, creature.BodyChunks[0].Mass, 0.000001, "Gourmand main chunk mass");
            Near(0.35 * 1.35, creature.BodyChunks[1].Mass, 0.000001, "Gourmand hips chunk mass");
        }

        private static void OriginalTailLayout()
        {
            ProceduralTail tail = new ProceduralTail(Vec2.Zero);
            Equal(4, tail.Segments.Length, "tail segment count");
            double[] radii = { 6.0, 4.0, 2.5, 1.0 };
            double[] lengths = { 4.0, 7.0, 7.0, 7.0 };
            for (int i = 0; i < tail.Segments.Length; i++)
            {
                Near(radii[i], tail.Segments[i].Radius, 0.000001, "tail radius " + i);
                Near(lengths[i], tail.Segments[i].Length, 0.000001, "tail connection radius " + i);
            }
        }

        private static void OriginalTailMeshIsContinuous()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = new Vec2(0.0, 0.0);
            pose.Hips = new Vec2(0.0, 17.0);
            pose.Tail = new[]
            {
                new Vec2(-4.0, 19.0), new Vec2(-11.0, 21.0),
                new Vec2(-18.0, 23.0), new Vec2(-25.0, 25.0)
            };
            pose.TailRadii = new[] { 6.0, 4.0, 2.5, 1.0 };
            Vec2[] vertices = SpriteRenderer.BuildOriginalTailMeshVertices(pose);
            Equal(15, vertices.Length, "PlayerGraphics tail mesh vertex count");
            Equal(13, SpriteRenderer.OriginalTailMeshTriangleCount,
                "PlayerGraphics tail mesh triangle count");
            Near(0.0, Vec2.Distance(pose.Tail[3], vertices[14]), 0.000001,
                "point tail tip");
            for (int i = 0; i < vertices.Length; i++)
            {
                True(!double.IsNaN(vertices[i].X) && !double.IsNaN(vertices[i].Y),
                    "finite continuous tail vertex " + i);
            }
            True(Vec2.Distance(vertices[2], vertices[4]) < 10.0 &&
                 Vec2.Distance(vertices[6], vertices[8]) < 10.0 &&
                 Vec2.Distance(vertices[10], vertices[12]) < 10.0,
                "overlapping bridge vertices join every TailSegment pair");
            Vec2 expectedRoot = (pose.Hips * 3.0 + pose.Chest) / 4.0;
            Near(0.0, Vec2.Distance(expectedRoot, pose.TailRoot), 0.000001,
                "PlayerGraphics body-interpolated tail root");
            Near(0.0, Vec2.Distance(expectedRoot, (vertices[0] + vertices[1]) * 0.5),
                0.000001, "first cross-section is centered on the root");
            Near(12.0, Vec2.Distance(vertices[0], vertices[1]), 0.000001,
                "root cross-section keeps the original radius six");
            Near(12.0, Vec2.Distance(vertices[4], vertices[5]), 0.000001,
                "segment zero radius flows into the next strip section");
            Near(8.0, Vec2.Distance(vertices[8], vertices[9]), 0.000001,
                "segment one radius flows into the next strip section");
            Near(5.0, Vec2.Distance(vertices[12], vertices[13]), 0.000001,
                "segment two radius tapers before the point tip");
            for (int i = 0; i < pose.TailTangents.Length; i++)
            {
                Near(1.0, pose.TailTangents[i].Length, 0.000001,
                    "unit tail tangent " + i);
                Near(0.0, Vec2.Dot(pose.TailTangents[i], pose.TailPerpendiculars[i]),
                    0.000001, "tail perpendicular " + i);
            }
        }

        private static void TailMeshStaysContinuousAcrossStates()
        {
            MonitorInfo monitor = new MonitorInfo("TAIL-MONITOR",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            Slugcat slugcat = new Slugcat(new Vec2(260.0,
                floor - SimulationConstants.HipsChunkRadius - 0.5));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint,
                slugcat.Center + new Vec2(80.0, -35.0));

            for (int tick = 0; tick < 320; tick++)
            {
                if (tick == 235) PrepareFloorImpact(slugcat, world, 45.0);
                VirtualInput input;
                if (tick < 40) input = VirtualInput.Neutral;
                else if (tick < 90) input = new VirtualInput(1, 0, false, false);
                else if (tick < 130) input = new VirtualInput(-1, 0, false, false);
                else if (tick < 175) input = new VirtualInput(1, 1, false, false);
                else if (tick < 215) input = new VirtualInput(1, 0,
                    tick >= 178 && tick < 187, false);
                else input = VirtualInput.Neutral;

                slugcat.Step(input, world, Vec2.Zero, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
                SlugcatPose pose = graphics.BuildPose((tick % 7) / 7.0,
                    attention, tick);
                Vec2[] vertices = SpriteRenderer.BuildOriginalTailMeshVertices(pose);
                Near(0.0, Vec2.Distance(pose.TailRoot,
                    (vertices[0] + vertices[1]) * 0.5), 0.000001,
                    "tail root section at tick " + tick);
                Near(0.0, Vec2.Distance(pose.TailTip, vertices[14]), 0.000001,
                    "tail tip at tick " + tick);
                for (int i = 0; i < vertices.Length; i++)
                {
                    True(!double.IsNaN(vertices[i].X) &&
                         !double.IsInfinity(vertices[i].X) &&
                         !double.IsNaN(vertices[i].Y) &&
                         !double.IsInfinity(vertices[i].Y),
                        "finite subpixel mesh vertex " + i + " at tick " + tick);
                }
                for (int section = 0; section < 4; section++)
                {
                    Vec2 leftToRight = vertices[section * 4 + 1] -
                        vertices[section * 4];
                    True(Vec2.Dot(leftToRight,
                         pose.TailPerpendiculars[section]) > 0.0,
                        "tail left/right ordering at section " + section +
                        " tick " + tick);
                }
            }
            True(!slugcat.State.Dead,
                "hard landing and stunned tail scenario remains non-lethal");
        }

        private static void RestPosturesUseVirtualInput()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sleep), world);
            True(slugcat.State.Animation == AnimationIndex.Sleep, "sleep posture must be interpreted by movement");

            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sit), world);
            True(slugcat.State.Animation == AnimationIndex.Sit, "sit posture must be interpreted by movement");
        }

        private static void MovementWakesRestPosture()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));

            slugcat.BodyChunks[0].Position = new Vec2(292.0, 300.0);
            slugcat.BodyChunks[1].Position = new Vec2(300.0, 300.0);
            slugcat.BodyChunks[0].ContactFloor = true;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Standing = false;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sleep), world);
            Equal((int)AnimationIndex.Sleep, (int)slugcat.State.Animation,
                "precondition: curled sleep posture");

            slugcat.BodyChunks[0].ContactFloor = true;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(new VirtualInput(1, 0, false, false), world);
            True(slugcat.State.Animation != AnimationIndex.Sleep &&
                 slugcat.State.Animation != AnimationIndex.Sit,
                "horizontal locomotion must clear stationary rest animation");
            True(slugcat.State.Standing &&
                 slugcat.State.Animation == AnimationIndex.StandUp,
                "low sleeping body must enter StandUp before normal locomotion pose");

            Slugcat combined = new Slugcat(new Vec2(300.0, 300.0));
            combined.BodyChunks[0].Position = new Vec2(292.0, 300.0);
            combined.BodyChunks[1].Position = new Vec2(300.0, 300.0);
            combined.BodyChunks[0].ContactFloor = true;
            combined.BodyChunks[1].ContactFloor = true;
            combined.State.BodyMode = BodyModeIndex.Crawl;
            combined.State.Animation = AnimationIndex.Sleep;
            combined.State.Standing = false;
            combined.Movement.ApplyInput(
                new VirtualInput(1, 0, false, false, VirtualPosture.Sleep), world);
            True(combined.State.Animation != AnimationIndex.Sleep &&
                 combined.State.Animation != AnimationIndex.Sit,
                "stale rest posture cannot override simultaneous movement");
        }

        private static void StandForcesKeepUpperBodyUpright()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                SimulationConstants.HipsChunkRadius + 1.0));

            for (int i = 0; i < 40; i++)
            {
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            }

            True(slugcat.State.BodyMode == BodyModeIndex.Stand, "grounded neutral posture should be Stand");
            True(slugcat.BodyChunks[0].Position.Y < slugcat.BodyChunks[1].Position.Y - 10.0,
                "the main body chunk must remain above the hips");
            Near(SimulationConstants.BodyConnectionDistance,
                Vec2.Distance(slugcat.BodyChunks[0].Position, slugcat.BodyChunks[1].Position),
                0.01,
                "standing body connection distance");
        }

        private static void IdleAndRestFramesStayStill()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.State.AnimationFrame = 5;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(VirtualInput.Neutral, world);
            Equal(0, slugcat.State.AnimationFrame, "idle Stand frame");

            slugcat.State.AnimationFrame = 5;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sleep), world);
            Equal(0, slugcat.State.AnimationFrame, "sleep frame");
        }

        private static void CrawlIdleHasNoFacingDrift()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            for (int facingIndex = 0; facingIndex < 2; facingIndex++)
            {
                int facing = facingIndex == 0 ? -1 : 1;
                DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
                world.Refresh(IntPtr.Zero);
                Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                    work.Left + work.Width * 0.5, work.Bottom));
                Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                    SimulationConstants.HipsChunkRadius + 1.0));
                slugcat.State.Facing = facing;
                for (int settle = 0; settle < 20; settle++)
                    slugcat.Step(new VirtualInput(0, 1, false, false), world, Vec2.Zero, Vec2.Zero);
                double startX = slugcat.Center.X;
                for (int tick = 0; tick < 30 * (int)SimulationConstants.LogicTicksPerSecond; tick++)
                    slugcat.Step(new VirtualInput(0, 1, false, false), world, Vec2.Zero, Vec2.Zero);
                Near(startX, slugcat.Center.X, 0.000001, "30 second crawl drift facing " + facing);
                Near(0.0, slugcat.BodyChunks[0].Velocity.X, 0.000001,
                    "upper horizontal velocity facing " + facing);
                Near(0.0, slugcat.BodyChunks[1].Velocity.X, 0.000001,
                    "lower horizontal velocity facing " + facing);
            }
        }

        private static void JumpLaunchClearsGroundedForces()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                SimulationConstants.HipsChunkRadius + 1.0));

            for (int i = 0; i < 8; i++) slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            slugcat.Step(new VirtualInput(0, 0, true, false), world, Vec2.Zero, Vec2.Zero);

            True(slugcat.State.Animation == AnimationIndex.None,
                "ordinary Player.Jump keeps AnimationIndex.None");
            True(slugcat.State.BodyMode == BodyModeIndex.Default && !slugcat.State.Grounded,
                "launch tick must be airborne");
            True(slugcat.BodyChunks[0].Velocity.Y < 0.0 && slugcat.BodyChunks[1].Velocity.Y < 0.0,
                "both chunks must have upward screen-space velocity at launch");
        }

        private static void OriginalAirSequence()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                SimulationConstants.HipsChunkRadius + 1.0));
            for (int i = 0; i < 12; i++)
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);

            slugcat.Step(new VirtualInput(1, 0, true, false), world, Vec2.Zero, Vec2.Zero);
            Near(-4.0, slugcat.BodyChunks[0].Velocity.Y, 0.000001, "normal jump chest y");
            Near(-3.0, slugcat.BodyChunks[1].Velocity.Y, 0.000001, "normal jump hips y");
            Near(SimulationConstants.BodyConnectionDistance, slugcat.BodyConnection.Distance,
                0.000001, "air connection distance");
            True(slugcat.State.Animation == AnimationIndex.None, "normal jump animation");

            slugcat.Step(new VirtualInput(1, 0, true, false), world, Vec2.Zero, Vec2.Zero);
            Near(1.2, slugcat.Movement.LastAirMovementContribution[0].X,
                0.000001, "chest horizontal air input contribution");
            Near(1.2, slugcat.Movement.LastAirMovementContribution[1].X,
                0.000001, "hips horizontal air input contribution");
            Near(-2.25, slugcat.Movement.LastAirMovementContribution[0].Y,
                0.000001, "held jump boost after decrement");
            True(slugcat.State.BodyMode == BodyModeIndex.Default &&
                 slugcat.State.Animation == AnimationIndex.None,
                "ascending ordinary jump remains Default/None");

            bool sawFalling = false;
            bool landed = false;
            for (int tick = 0; tick < 180; tick++)
            {
                int x = tick < 8 ? -1 : 0;
                slugcat.Step(new VirtualInput(x, 0, false, false), world, Vec2.Zero, Vec2.Zero);
                double vertical = (slugcat.BodyChunks[0].Velocity.Y +
                    slugcat.BodyChunks[1].Velocity.Y) * 0.5;
                if (!slugcat.State.Grounded && vertical >= 0.0)
                {
                    sawFalling = true;
                    True(slugcat.State.Animation == AnimationIndex.None,
                        "ordinary fall remains AnimationIndex.None");
                }
                if (slugcat.State.Grounded)
                {
                    landed = true;
                    break;
                }
            }
            True(sawFalling, "jump sequence reaches the falling phase");
            True(landed, "jump sequence lands through BodyChunk collision");
        }

        private static void CrawlTurnsKeepArmRotationContinuous()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                SimulationConstants.HipsChunkRadius + 1.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(50.0, -20.0));
            double[] lastRotation = new double[2];
            bool[] lastVisible = new bool[2];
            double[] lastArmLength = new double[2];
            bool hasSample = false;
            double maximumRotationDelta = 0.0;
            double maximumArmLength = 0.0;
            string maximumRotationDetails = string.Empty;

            for (int settle = 0; settle < 80; settle++)
            {
                slugcat.Step(VirtualInput.Neutral, world, attention.Target, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
            }

            int ticks = 30 * (int)SimulationConstants.LogicTicksPerSecond;
            for (int tick = 0; tick < ticks; tick++)
            {
                int direction = (tick / 150) % 2 == 0 ? 1 : -1;
                bool crawlPhase = tick % 240 < 190;
                VirtualInput input = crawlPhase
                    ? new VirtualInput(direction, 1, false, false)
                    : VirtualInput.Neutral;
                slugcat.Step(input, world, attention.Target, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
                SlugcatPose pose = graphics.BuildPose(1.0, attention, tick);
                for (int arm = 0; arm < 2; arm++)
                {
                    maximumArmLength = Math.Max(maximumArmLength,
                        Vec2.Distance(pose.Hands[arm], pose.ArmConnections[arm]));
                    double renderedArmLength = Vec2.Distance(
                        pose.Hands[arm], pose.ArmShoulders[arm]);
                    if (hasSample && lastVisible[arm] && pose.ArmVisible[arm] &&
                        lastArmLength[arm] >= 6.0 && renderedArmLength >= 6.0)
                    {
                        double rotationDelta = Math.Abs(ShortestAngleDelta(
                            lastRotation[arm], pose.ArmRotations[arm]));
                        if (rotationDelta > maximumRotationDelta)
                        {
                            maximumRotationDelta = rotationDelta;
                            maximumRotationDetails = string.Format(
                                " tick={0} arm={1} body={2} animation={3} mode={4} length={5:0.###} hand={6} shoulder={7} target={8}",
                                tick, arm, pose.BodyMode, pose.Animation, pose.ArmModes[arm],
                                Vec2.Distance(pose.Hands[arm], pose.ArmShoulders[arm]),
                                pose.Hands[arm], pose.ArmShoulders[arm], pose.HandTargets[arm]);
                        }
                    }
                    lastRotation[arm] = pose.ArmRotations[arm];
                    lastVisible[arm] = pose.ArmVisible[arm];
                    lastArmLength[arm] = renderedArmLength;
                }
                hasSample = true;
            }

            True(maximumArmLength <= 20.01,
                "SlugcatHand must remain within the original 20-unit connection radius");
            True(maximumRotationDelta < 120.0,
                "Crawl arm direction must not spike by a partial/full revolution: " +
                maximumRotationDelta.ToString("0.###") + maximumRotationDetails);
        }

        private static double ShortestAngleDelta(double from, double to)
        {
            double delta = to - from;
            while (delta > 180.0) delta -= 360.0;
            while (delta < -180.0) delta += 360.0;
            return delta;
        }

        private static void DropDownRequestsSurfacePassThrough()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.BodyChunks[1].SupportingSurfaceId = 1234;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 1, false, false, VirtualPosture.None, true), world);

            Equal(1234, (int)slugcat.Movement.IgnoredSurfaceId, "ignored window surface id");
            True(!slugcat.State.Grounded && slugcat.State.BodyMode == BodyModeIndex.Default,
                "drop-through should leave the grounded state");
            True(slugcat.BodyChunks[0].Velocity.Y > 0.0 && slugcat.BodyChunks[1].Velocity.Y > 0.0,
                "drop-through should push both chunks downward");
        }

        private static void EmbeddedOriginalAtlasLoads(RainWorldInstallation installation)
        {
            RainWorldAssetLoader loader = new RainWorldAssetLoader(installation);
            using (RainWorldAtlasSet set = loader.TryLoadPlayerAtlas())
            {
                True(set != null, loader.Status);
                AtlasSprite body;
                True(set.TryGet("BodyA", out body), "BodyA from embedded rainWorld atlas");
                True(body.Atlas.ImagePath.EndsWith("#rainWorld", StringComparison.OrdinalIgnoreCase),
                    "BodyA must resolve to the original embedded atlas, not DMS");
                Equal(464, body.Atlas.Image.Width, "base atlas width");
                Equal(512, body.Atlas.Image.Height, "base atlas height");
                Equal(358, body.Element.Frame.X, "BodyA frame x");
                Equal(50, body.Element.Frame.Y, "BodyA frame y");
                Equal(14, body.Element.Frame.Width, "BodyA frame width");
                Equal(19, body.Element.Frame.Height, "BodyA frame height");
                for (int i = 0; i < 9; i++)
                {
                    AtlasSprite face;
                    True(set.TryGet("FaceA" + i, out face), "base normal face " + i);
                    True(set.TryGet("FaceB" + i, out face), "base blink face " + i);
                    True(set.TryGet("FaceC" + i, out face), "Artificer right face " + i);
                    True(set.TryGet("FaceD" + i, out face), "Artificer left face " + i);
                    True(set.TryGet("FaceE" + i, out face), "Sofanthiel face " + i);
                }
                AtlasSprite specialFace;
                True(set.TryGet("FaceDead", out specialFace), "dead face element");
                True(set.TryGet("FaceStunned", out specialFace), "stunned face element");
                for (int i = 1; i <= 3; i++)
                {
                    AtlasSprite bioSpear;
                    True(set.TryGet("BioSpear" + i, out bioSpear),
                        "embedded original BioSpear" + i);
                    True(bioSpear.Atlas.ImagePath.EndsWith("#rainWorld",
                        StringComparison.OrdinalIgnoreCase) ||
                        bioSpear.Atlas.ImagePath.EndsWith("#rainworldmsc",
                            StringComparison.OrdinalIgnoreCase),
                        "BioSpear must resolve from the installed original atlas");
                }
                for (int frame = 0; frame < 3; frame++)
                {
                    AtlasSprite fruitLayer;
                    True(set.TryGet("DangleFruit" + frame + "A", out fruitLayer),
                        "embedded original DangleFruit" + frame + "A");
                    True(set.TryGet("DangleFruit" + frame + "B", out fruitLayer),
                        "embedded original DangleFruit" + frame + "B");
                    True(fruitLayer.Atlas.ImagePath.EndsWith("#rainWorld",
                        StringComparison.OrdinalIgnoreCase),
                        "DangleFruit must resolve from the installed original atlas");
                }
                AtlasSprite eggLayer;
                True(set.TryGet("EggBugEggColor", out eggLayer),
                    "embedded original EggBugEggColor");
                True(set.TryGet("EggBugEggColorEaten", out eggLayer),
                    "embedded original EggBugEggColorEaten");
                True(set.TryGet("JetFishEyeA", out eggLayer),
                    "embedded original JetFishEyeA detail");
                True(eggLayer.Atlas.ImagePath.EndsWith("#rainWorld",
                    StringComparison.OrdinalIgnoreCase),
                    "EggBugEgg layers must resolve from the installed original atlas");
                for (int i = 0; i < SlugcatVisualProfiles.All.Count; i++)
                {
                    SlugcatVisualProfile profile = SlugcatVisualProfiles.All[i];
                    string missing;
                    True(profile.IsAvailable(set, out missing),
                        profile.DisplayName + " local atlas availability: " + missing);
                }
            }
        }

        private static void DownpourVisualProfilesMatchDllConstants()
        {
            Equal(5, SlugcatVisualProfiles.All.Count, "profile count");
            AssertProfile(SlugcatVisualProfiles.Artificer, "Artificer", 112, 35, 60, 255, 255, 255);
            AssertProfile(SlugcatVisualProfiles.Spearmaster, "Spear", 79, 46, 105, 255, 255, 255);
            AssertProfile(SlugcatVisualProfiles.Rivulet, "Rivulet", 145, 204, 240, 16, 16, 16);
            AssertProfile(SlugcatVisualProfiles.Saint, "Saint", 170, 241, 86, 16, 16, 16);
            True(SlugcatVisualProfiles.Default.UsesVariantBodyColor,
                "Default profile must preserve Survivor/Monk/Hunter/Gourmand colour selection");
            True(SlugcatVisualProfiles.Saint.HeadFamily == "HeadB",
                "Saint uses the MSC HeadB family");
            True(SlugcatVisualProfiles.Saint.ResolveFaceFamily(false, 1.0) == "FaceB",
                "SaintFaceCondition keeps the original Saint eyes closed");
            True(SlugcatVisualProfiles.Spearmaster.OriginalSlugcatId == "Spear",
                "Spearmaster's DLL identifier is Spear");
        }

        private static void AssertProfile(SlugcatVisualProfile profile, string id,
            int bodyR, int bodyG, int bodyB, int eyeR, int eyeG, int eyeB)
        {
            True(profile.OriginalSlugcatId == id, profile.DisplayName + " original id");
            Equal(bodyR, profile.BodyColor.R, profile.DisplayName + " body red");
            Equal(bodyG, profile.BodyColor.G, profile.DisplayName + " body green");
            Equal(bodyB, profile.BodyColor.B, profile.DisplayName + " body blue");
            Equal(eyeR, profile.EyeColor.R, profile.DisplayName + " eye red");
            Equal(eyeG, profile.EyeColor.G, profile.DisplayName + " eye green");
            Equal(eyeB, profile.EyeColor.B, profile.DisplayName + " eye blue");
        }

        private static void RuntimeSkinSwitchPreservesPhysics()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Gourmand, out world);
            slugcat.BodyChunks[0].Velocity = new Vec2(2.5, -1.25);
            slugcat.BodyChunks[1].Velocity = new Vec2(2.0, -0.75);
            slugcat.Stun(37);
            Vec2[] positions = { slugcat.BodyChunks[0].Position, slugcat.BodyChunks[1].Position };
            Vec2[] velocities = { slugcat.BodyChunks[0].Velocity, slugcat.BodyChunks[1].Velocity };
            double[] masses = { slugcat.BodyChunks[0].Mass, slugcat.BodyChunks[1].Mass };
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);

            graphics.SetVisualProfile(SlugcatVisualProfiles.Rivulet, null);
            True(graphics.VisualProfile.Skin == SlugcatSkin.Rivulet,
                "Default -> Rivulet changes graphics profile");
            Equal(12, graphics.Extensions[0].SpriteCount, "Rivulet extra allocation");
            graphics.SetVisualProfile(SlugcatVisualProfiles.Spearmaster, null);
            True(graphics.VisualProfile.Skin == SlugcatSkin.Spearmaster,
                "Rivulet -> Spearmaster removes Rivulet extension");
            Equal(19, graphics.Extensions[0].SpriteCount, "Spearmaster extra allocation");
            graphics.SetVisualProfile(SlugcatVisualProfiles.Default, null);
            Equal(0, graphics.Extensions.Length, "Default removes all skin-only graphics");

            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
            {
                Near(positions[i].X, slugcat.BodyChunks[i].Position.X, 0.000001, "skin switch position x " + i);
                Near(positions[i].Y, slugcat.BodyChunks[i].Position.Y, 0.000001, "skin switch position y " + i);
                Near(velocities[i].X, slugcat.BodyChunks[i].Velocity.X, 0.000001, "skin switch velocity x " + i);
                Near(velocities[i].Y, slugcat.BodyChunks[i].Velocity.Y, 0.000001, "skin switch velocity y " + i);
                Near(masses[i], slugcat.BodyChunks[i].Mass, 0.000001, "skin switch mass " + i);
            }
            True(slugcat.Appearance.Variant == SlugcatVariant.Gourmand,
                "visual profile does not replace SlugcatStats variant");
            Equal(37, slugcat.State.StunCounter, "skin switch preserves stun state");
        }

        private static void RivuletGillsUseOriginalProceduralLayout()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.Mouse, slugcat.Center + new Vec2(80.0, -40.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat,
                SlugcatVisualProfiles.Rivulet, null);
            for (int i = 0; i < 8; i++)
            {
                slugcat.Step(new VirtualInput(i < 4 ? 1 : -1, 0, i == 2, false),
                    world, attention.Target, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
            }

            RivuletGillsExtension gills = graphics.Extensions[0] as RivuletGillsExtension;
            True(gills != null, "Rivulet profile creates AxolotlGills");
            Equal(6, gills.ScaleObjects.Length, "three gills on each side");
            Near(15.0, gills.Lengths[0], 0.00001, "outer gill DLL length");
            Near(MathUtil.Lerp(2.5, 15.0, 1.310689 * 0.6056554),
                gills.Lengths[1], 0.00001, "middle gill DLL length");

            SlugcatPose pose25 = graphics.BuildPose(0.25, attention, 8);
            Equal(12, pose25.ExtraParts.Length, "six base and six colour overlay sprites");
            Vec2 firstLast = pose25.ExtraParts[0].LastPosition;
            Vec2 firstCurrent = pose25.ExtraParts[0].CurrentPosition;
            Vec2 render25 = pose25.ExtraParts[0].RenderPosition;
            Near(Vec2.Lerp(firstLast, firstCurrent, 0.25).X, render25.X, 0.000001,
                "gill x uses shared timeStacker");
            Near(Vec2.Lerp(firstLast, firstCurrent, 0.25).Y, render25.Y, 0.000001,
                "gill y uses shared timeStacker");
            True(pose25.ExtraParts[0].Element == "LizardScaleA3" &&
                 pose25.ExtraParts[6].Element == "LizardScaleB3",
                "original A/B gill element blocks");
            Equal(12, pose25.ExtraParts[0].OriginalSpriteIndex, "first gill index");
            Equal(23, pose25.ExtraParts[11].OriginalSpriteIndex, "last gill index");
            True(pose25.ExtraParts[0].Layer == ExtraGraphicsLayer.InFront,
                "AxolotlGills AddToContainer appends to Midground");
            Near(pose25.FacePosition.X - 5.0,
                pose25.ExtraParts[0].SpritePosition.X, 0.000001, "left draw root follows face sprite");

            SlugcatPose pose75 = graphics.BuildPose(0.75, attention, 8);
            Vec2 render75 = pose75.ExtraParts[0].RenderPosition;
            Near(Vec2.Lerp(firstLast, firstCurrent, 0.75).X, render75.X, 0.000001,
                "240 Hz intermediate gill x interpolation");
            Near(Vec2.Lerp(firstLast, firstCurrent, 0.75).Y, render75.Y, 0.000001,
                "240 Hz intermediate gill y interpolation");
        }

        private static void SpearmasterTailProfileAndSpeckles()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(60.0, -20.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat,
                SlugcatVisualProfiles.Spearmaster, null);
            graphics.Step(attention, world);
            SlugcatPose pose = graphics.BuildPose(0.5, attention, 1);
            double[] expectedRadii = { 8.0, 6.0, 4.0, 2.0 };
            double[] expectedLengths = { 4.0, 7.0, 7.0, 7.0 };
            for (int i = 0; i < 4; i++)
            {
                Near(expectedRadii[i], graphics.Tail.Segments[i].Radius, 0.000001,
                    "Spearmaster tail radius " + i);
                Near(expectedLengths[i], graphics.Tail.Segments[i].Length, 0.000001,
                    "Spearmaster tail length " + i);
            }
            Near(6.0, pose.TailRootRadius, 0.000001, "Spearmaster mesh root width");
            Near(0.76, pose.VisualBodyScale, 0.000001, "Spearmaster body scale");
            Near(0.76, pose.VisualHipsScale, 0.000001, "Spearmaster hips scale");
            Near(0.85, Math.Abs(pose.HeadScaleX), 0.000001, "Spearmaster head scale");
            Near(0.6, pose.ArmShoulderScale, 0.000001, "Spearmaster shoulder factor");
            Vec2[] mesh = SpriteRenderer.BuildOriginalTailMeshVertices(pose);
            Near(12.0, Vec2.Distance(mesh[0], mesh[1]), 0.000001,
                "Spearmaster continuous mesh root diameter");
            Equal(19, pose.ExtraParts.Length, "16 tail speckles plus three pearl sprites");
            for (int i = 0; i < 15; i++)
                True(pose.ExtraParts[i].Visible && pose.ExtraParts[i].Element == "tinyStar",
                    "visible tinyStar speckle " + i);
            True(!pose.ExtraParts[15].Visible && pose.ExtraParts[15].Element == "BioSpear1",
                "spearProg zero keeps generated spear hidden");
            for (int i = 16; i < 19; i++)
                True(!pose.ExtraParts[i].Visible,
                    "story-state CosmeticPearl remains inactive " + i);
        }

        private static void SkinFaceFamiliesMatchPlayerGraphics()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = new Vec2(0.0, 0.0);
            pose.Hips = new Vec2(0.0, 17.0);
            pose.Head = new Vec2(0.0, -8.0);
            pose.Facing = 1;
            pose.Conscious = true;
            pose.LookDirection = Vec2.Right;
            pose.CurrentSkin = SlugcatSkin.Artificer;
            OriginalFaceState state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement.StartsWith("FaceC", StringComparison.Ordinal),
                "Artificer positive eyeScale uses FaceC");
            pose.LookDirection = -Vec2.Right;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement.StartsWith("FaceD", StringComparison.Ordinal),
                "Artificer negative eyeScale uses FaceD");
            pose.Blink = true;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement.StartsWith("FaceB", StringComparison.Ordinal),
                "Artificer blink uses shared FaceB");
            pose.Conscious = false;
            pose.Dead = false;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.FaceElement == "FaceStunned",
                "Artificer unconscious face is shared only because the DLL shares it");

            pose.Conscious = true;
            pose.Blink = false;
            pose.CurrentSkin = SlugcatSkin.Saint;
            state = SpriteRenderer.ResolveOriginalFaceState(pose);
            True(state.HeadElement.StartsWith("HeadB", StringComparison.Ordinal),
                "Saint head uses HeadB in every movement state");
            True(state.FaceElement.StartsWith("FaceB", StringComparison.Ordinal),
                "Saint normal face uses the closed-eye FaceB family");
        }

        private static void AllVisualProfilesRemainStableAcrossStates()
        {
            for (int profileIndex = 0; profileIndex < SlugcatVisualProfiles.All.Count; profileIndex++)
            {
                SlugcatVisualProfile profile = SlugcatVisualProfiles.All[profileIndex];
                DesktopCollisionWorld world;
                Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
                AttentionSystem attention = new AttentionSystem();
                attention.SetTarget(AttentionKind.Mouse,
                    slugcat.Center + new Vec2(75.0, -35.0));
                SlugcatGraphics graphics = new SlugcatGraphics(slugcat, profile, null);
                for (int tick = 0; tick < 130; tick++)
                {
                    VirtualInput input;
                    if (tick < 10) input = VirtualInput.Neutral;                    // idle
                    else if (tick < 30) input = new VirtualInput(1, 0, false, false); // right
                    else if (tick < 50) input = new VirtualInput(-1, 0, false, false); // turn/left
                    else if (tick < 70) input = new VirtualInput(tick < 60 ? -1 : 1, 1, false, false); // crawl/turn
                    else if (tick < 90) input = new VirtualInput(1, 0, tick == 71, false); // jump/fall
                    else input = VirtualInput.Neutral;
                    if (tick == 92) slugcat.State.LandingCompression = 1.0;
                    if (tick == 100) slugcat.Stun(20);
                    slugcat.Step(input, world, attention.Target, Vec2.Zero);
                    attention.Step();
                    graphics.Step(attention, attention.Target, true, world);
                    double timeStacker = (tick % 5) * 0.25;
                    SlugcatPose pose = graphics.BuildPose(timeStacker, attention, tick);
                    True(pose.CurrentSkin == profile.Skin,
                        profile.DisplayName + " profile survives state tick " + tick);
                    True(!string.IsNullOrEmpty(pose.HeadElement) &&
                         !string.IsNullOrEmpty(pose.SelectedFaceElement),
                        profile.DisplayName + " head/face selection tick " + tick);
                    Equal(profile.ExtraSpriteCount, pose.ExtraParts.Length,
                        profile.DisplayName + " stable extra allocation tick " + tick);
                    True(IsFinite(pose.Head) && IsFinite(pose.Tail[3]),
                        profile.DisplayName + " finite interpolated base graphics tick " + tick);
                    for (int i = 0; i < pose.ExtraParts.Length; i++)
                    {
                        ExtraGraphicsPartPose part = pose.ExtraParts[i];
                        if (!part.Visible) continue;
                        True(IsFinite(part.RenderPosition) && IsFinite(part.SpritePosition) &&
                             !double.IsNaN(part.Rotation) && !double.IsInfinity(part.Rotation),
                            profile.DisplayName + " finite extra graphics tick " + tick + "/" + i);
                    }
                }
            }
        }

        private static bool IsFinite(Vec2 value)
        {
            return !double.IsNaN(value.X) && !double.IsInfinity(value.X) &&
                   !double.IsNaN(value.Y) && !double.IsInfinity(value.Y);
        }

        private static void OriginalConnectToPointEquation()
        {
            BodyPart part = new BodyPart(Vec2.Zero, 4.0, 0.8, 0.99);
            part.ConnectToPoint(new Vec2(10.0, 0.0), 3.0, false, 0.2,
                new Vec2(2.0, 0.0), 0.7, 0.1);
            Near(7.0, part.Position.X, 0.000001, "constraint position correction");
            Near(4.16, part.Velocity.X, 0.000001, "host-relative adapted velocity");
        }

        private static void HeadStartStopKeepsOriginalConnection()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Vec2 floorPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                work.Left + work.Width * 0.5, work.Bottom));
            Slugcat slugcat = new Slugcat(floorPoint - new Vec2(0.0,
                SimulationConstants.HipsChunkRadius + 1.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(70.0, -30.0));
            double maximumConnectionError = 0.0;
            Vec2 start = slugcat.Center;

            for (int tick = 0; tick < 100; tick++)
            {
                VirtualInput input = tick >= 20 && tick < 60
                    ? new VirtualInput(1, 0, false, false)
                    : VirtualInput.Neutral;
                slugcat.Step(input, world, attention.Target, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
                SlugcatPose pose = graphics.BuildPose(1.0, attention, tick);
                maximumConnectionError = Math.Max(maximumConnectionError,
                    Vec2.Distance(graphics.Head.Position, pose.HeadTarget));
                Near(0.0, Vec2.Distance(pose.Head, graphics.Head.Position), 0.000001,
                    "timeStacker=1 must expose the current head particle once");
            }

            True(Vec2.Distance(start, slugcat.Center) > 10.0,
                "start phase must materially move the player");
            True(maximumConnectionError <= 3.0001,
                "GenericBodyPart head must stay inside its original 3-unit connection radius");
        }

        private static void SharedGraphicsInterpolation()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(420.0, 360.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(80.0, -20.0));
            slugcat.BodyChunks[0].Position += new Vec2(12.0, -4.0);
            slugcat.BodyChunks[1].Position += new Vec2(8.0, 2.0);
            graphics.Step(attention, world);
            SlugcatPose pose = graphics.BuildPose(0.25, attention, 17);

            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.DrawLast[0], pose.DrawCurrent[0], 0.25), pose.Chest),
                0.000001, "upper draw interpolation");
            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.HeadLast, pose.HeadCurrent, 0.25), pose.Head),
                0.000001, "head interpolation");
            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.LegsLast, pose.LegsCurrent, 0.25), pose.Legs),
                0.000001, "legs interpolation");
            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.TailLast[0], pose.TailCurrent[0], 0.25), pose.Tail[0]),
                0.000001, "tail interpolation");
            Near(0.25, pose.TimeStacker, 0.000001, "reported timeStacker");
            Equal(17, (int)pose.SimulationTick, "reported simulation tick");
        }

        private static void FutileTrimAnchorCoordinates()
        {
            AtlasElement element = new AtlasElement
            {
                Frame = new Rectangle(1, 2, 10, 12),
                SpriteSource = new Rectangle(3, 4, 10, 12),
                SourceSize = new Size(18, 20)
            };
            RectangleF local = element.GetLocalRectangle(0.5, 0.7894737);
            Near(-6.0, local.X, 0.00001, "trimmed local x");
            Near(-0.210526, local.Y, 0.0001, "trimmed local y");
            Near(10.0, local.Width, 0.00001, "trimmed width");
        }

        private static void NegativeVirtualDesktopCoordinates()
        {
            RenderSpace space = new RenderSpace(Rectangle.FromLTRB(-1920, -200, 2560, 1440));
            Vec2 world = new Vec2(-1800.5, -50.25);
            Vec2 overlay = space.WorldToOverlay(world);
            Near(119.5, overlay.X, 0.000001, "negative screen world-to-overlay x");
            Near(149.75, overlay.Y, 0.000001, "negative screen world-to-overlay y");
            Near(0.0, Vec2.Distance(world, space.OverlayToWorld(overlay)), 0.000001,
                "coordinate conversion round trip");
        }

        private static void OverlappingSlugcatsShareCompositionUpload()
        {
            Rectangle[] nearby =
            {
                new Rectangle(0, 0, 384, 384),
                new Rectangle(48, 0, 384, 384),
                new Rectangle(96, 0, 384, 384),
                new Rectangle(144, 0, 384, 384)
            };
            CompositionBatchPlanner planner = new CompositionBatchPlanner();
            IList<CompositionBatch> combined = planner.Plan(nearby, 128);
            Equal(1, combined.Count, "nearby surface batch count");
            Equal(4, combined[0].SurfaceIndices.Count, "combined Slugcat count");
            long separateArea = nearby.Sum(delegate(Rectangle bounds)
            {
                return (long)bounds.Width * bounds.Height;
            });
            long combinedArea = (long)combined[0].Bounds.Width * combined[0].Bounds.Height;
            True(combinedArea < separateArea,
                "combined upload should contain fewer pixels than separate surfaces");

            Rectangle[] distant =
            {
                new Rectangle(0, 0, 384, 384),
                new Rectangle(1200, 0, 384, 384)
            };
            IList<CompositionBatch> separated = planner.Plan(distant, 128);
            Equal(2, separated.Count, "distant surface batch count");

            Rectangle[] barelyOverlapping =
            {
                new Rectangle(0, 0, 384, 384),
                new Rectangle(383, 0, 384, 384)
            };
            IList<CompositionBatch> requiredMerge = planner.Plan(
                barelyOverlapping, 128);
            Equal(1, requiredMerge.Count,
                "even a one-pixel overlap must share one Z-ordered surface");
        }

        private static void HeldFoodAndSlugcatRenderOrder()
        {
            IList<int> indices = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            for (int step = 0; step < indices.Count * 3; step++)
            {
                int loopIndex;
                OverlayRenderLayer layer;
                LayeredOverlayWindow.ResolveRenderStep(indices, step,
                    out loopIndex, out layer);
                int expectedLayer = step / indices.Count;
                int expectedLoop = indices.Count - 1 - step % indices.Count;
                Equal(expectedLayer, (int)layer,
                    "global render layer at step " + step);
                Equal(expectedLoop, loopIndex,
                    "back-to-front Slugcat order at step " + step);
            }

            int frontLoop;
            OverlayRenderLayer frontLayer;
            LayeredOverlayWindow.ResolveRenderStep(indices,
                indices.Count * 3 - 1, out frontLoop, out frontLayer);
            True(frontLayer == OverlayRenderLayer.HeldFood && frontLoop == 0,
                "Slugcat 1 held food is the final and frontmost sprite pass");
        }

        private static void CompositionSurfacesOnlyGrow()
        {
            Size initial = DirectCompositionHost.SelectReusableSurfaceSize(Size.Empty,
                new Size(384, 384));
            True(initial == new Size(384, 384), "initial surface size");
            Size grown = DirectCompositionHost.SelectReusableSurfaceSize(initial,
                new Size(512, 384));
            True(grown == new Size(512, 384), "grown surface size");
            Size retained = DirectCompositionHost.SelectReusableSurfaceSize(grown,
                new Size(384, 384));
            True(grown == retained, "smaller content should reuse the grown surface");
        }

        private static void GpuSmokeCommandAbiMatchesNativeRenderer()
        {
            Equal(14 * sizeof(float),
                Marshal.SizeOf(typeof(DirectCompositionHost.GpuSmokeEffect)),
                "GPU smoke command byte size");
        }

        private static void GpuSpriteCommandAbiMatchesNativeRenderer()
        {
            Equal(8, Marshal.SizeOf(typeof(GpuPoint)), "GPU point byte size");
            Equal(56, Marshal.SizeOf(typeof(GpuDrawCommand)),
                "GPU sprite command byte size");
        }

        private static void GpuSpriteSurfaceRendersThroughDirect2D()
        {
            using (System.Windows.Forms.Form form = new System.Windows.Forms.Form())
            using (DirectCompositionHost host = new DirectCompositionHost(
                form.Handle, new Rectangle(0, 0, 640, 480)))
            using (Bitmap texture = new Bitmap(4, 4,
                PixelFormat.Format32bppPArgb))
            {
                using (System.Drawing.Graphics drawing =
                    System.Drawing.Graphics.FromImage(texture))
                    drawing.Clear(Color.White);
                GpuSpriteCanvas canvas = host.PrepareGpuSurface(0,
                    new Rectangle(32, 24, 384, 384));
                canvas.SetTransform(2.0f, 0.0f, 0.0f, 2.0f, -40.0f, -20.0f);
                canvas.Save();
                canvas.TranslateTransform(10.0f, 15.0f);
                canvas.RotateTransform(32.0f);
                canvas.ScaleTransform(-1.0f, 0.75f);
                PointF transformFrom = new PointF(3.0f, 7.0f);
                PointF transformTo = new PointF(11.0f, -2.0f);
                canvas.DrawLine(Color.White, 2.0f, transformFrom, transformTo);
                using (System.Drawing.Drawing2D.Matrix expectedTransform =
                    new System.Drawing.Drawing2D.Matrix(2.0f, 0.0f, 0.0f,
                        2.0f, -40.0f, -20.0f))
                {
                    expectedTransform.Translate(10.0f, 15.0f);
                    expectedTransform.Rotate(32.0f);
                    expectedTransform.Scale(-1.0f, 0.75f);
                    PointF[] expected = { transformFrom, transformTo };
                    expectedTransform.TransformPoints(expected);
                    Near(expected[0].X, canvas.Points[0].X, 0.0001,
                        "GPU transform first X");
                    Near(expected[0].Y, canvas.Points[0].Y, 0.0001,
                        "GPU transform first Y");
                    Near(expected[1].X, canvas.Points[1].X, 0.0001,
                        "GPU transform second X");
                    Near(expected[1].Y, canvas.Points[1].Y, 0.0001,
                        "GPU transform second Y");
                }
                canvas.Restore();
                canvas.FillEllipse(Color.FromArgb(230, 80, 190, 255),
                    20.0f, 25.0f, 60.0f, 45.0f);
                PointF[] destination =
                {
                    new PointF(100.0f, 80.0f),
                    new PointF(132.0f, 80.0f),
                    new PointF(100.0f, 112.0f)
                };
                canvas.DrawImage(texture, destination,
                    new RectangleF(0.0f, 0.0f, 4.0f, 4.0f),
                    Color.FromArgb(255, 160, 220, 255), false);
                host.PresentGpu(canvas);
                host.Commit(1);
                True(canvas.CommandCount == 3,
                    "line, ellipse and texture should remain ordered GPU commands");

                DesktopCollisionWorld world = new DesktopCollisionWorld(
                    new WindowEnumerator());
                world.Refresh(IntPtr.Zero);
                Slugcat slugcat = new Slugcat(new Vec2(150.0, 130.0));
                DesktopPetAI ai = new DesktopPetAI(2718);
                SlugcatGraphics procedural = new SlugcatGraphics(slugcat);
                procedural.Step(ai.Attention, world);
                SlugcatPose pose = procedural.BuildPose(0.5, ai.Attention, 1);
                canvas = host.PrepareGpuSurface(0,
                    new Rectangle(0, 0, 384, 384));
                using (SpriteRenderer renderer = new SpriteRenderer(null))
                    renderer.RenderGpu(canvas, pose,
                        new RenderSpace(canvas.Bounds), world, slugcat, ai,
                        "gpu-test", slugcat.SelectedSlugcat);
                True(canvas.CommandCount > 10,
                    "a procedural Slugcat should emit a complete GPU draw list");
                host.PresentGpu(canvas);
                host.Commit(1);
            }
        }

        private static void ArtificerSmokeEmitsGpuEffectCommands()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 80.0));
            AbilityEffect smoke = new AbilityEffect(AbilityEffectKind.Smoke,
                new Vec2(100.0, 80.0), Vec2.Zero, 200, 1.0);
            smoke.LastLife = 0.75;
            smoke.Life = 0.75;
            slugcat.AddEffect(smoke);
            SlugcatPose pose = new SlugcatPose();
            pose.CharacterRenderScale = 2.0;
            pose.TimeStacker = 1.0;
            DirectCompositionHost.GpuSmokeEffect[] commands =
                new DirectCompositionHost.GpuSmokeEffect[4];
            int count = 0;
            using (SpriteRenderer renderer = new SpriteRenderer(null))
                renderer.CollectGpuSmokeEffects(slugcat, pose,
                    new RenderSpace(new Rectangle(50, 20, 400, 300)), commands,
                    ref count);
            Equal(1, count, "smoke command count");
            Near(150.0, commands[0].CenterX, 0.0001, "smoke local X");
            Near(140.0, commands[0].CenterY, 0.0001, "smoke local Y");
            True(commands[0].BackSize > commands[0].FrontSize,
                "back smoke quad should be larger than front smoke quad");
            True(commands[0].BackAlpha > commands[0].FrontAlpha,
                "back smoke layer should retain the original stronger alpha");
        }

        private static void ArtificerFlashExpandsGpuEffectBounds()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 80.0));
            slugcat.AddEffect(AbilityEffect.CreateExplosionLight(
                new Vec2(100.0, 80.0), 160.0, 1.0, 3));
            SlugcatPose pose = new SlugcatPose();
            pose.CharacterRenderScale = 2.2;
            pose.TimeStacker = 1.0;
            using (SpriteRenderer renderer = new SpriteRenderer(null))
            {
                RectangleF bounds = renderer.CalculateGpuEffectBounds(slugcat, pose);
                True(bounds.Width > 700.0f && bounds.Height > 700.0f,
                    "flash bounds should exceed the 384px character surface");
                DirectCompositionHost.GpuSmokeEffect[] commands =
                    new DirectCompositionHost.GpuSmokeEffect[2];
                int count = 0;
                renderer.CollectGpuSmokeEffects(slugcat, pose,
                    new RenderSpace(Rectangle.Ceiling(bounds)), commands, ref count);
                Equal(1, count, "flash GPU command count");
                True(commands[0].Seed < 0.0f,
                    "negative command seed selects the radial-light shader");
            }
        }

        private static void ArtificerSelfDestructUsesGpuEffectBounds()
        {
            Slugcat slugcat = new Slugcat(new Vec2(100.0, 80.0));
            AbilityEffect explosion = new AbilityEffect(AbilityEffectKind.Explosion,
                new Vec2(100.0, 80.0), Vec2.Zero, 7, 350.0);
            explosion.LastLife = explosion.Life = 0.5;
            slugcat.AddEffect(explosion);
            AbilityEffect spikes = new AbilityEffect(AbilityEffectKind.ExplosionSpikes,
                new Vec2(100.0, 80.0), Vec2.Zero, 7, 170.0);
            spikes.LastLife = spikes.Life = 0.5;
            slugcat.AddEffect(spikes);
            AbilityEffect wave = AbilityEffect.CreateShockWave(
                new Vec2(100.0, 80.0), 430.0, 0.045, 5);
            wave.LastLife = wave.Life = 0.5;
            slugcat.AddEffect(wave);
            SlugcatPose pose = new SlugcatPose();
            pose.CharacterRenderScale = 2.2;
            pose.TimeStacker = 1.0;
            using (SpriteRenderer renderer = new SpriteRenderer(null))
            {
                RectangleF bounds = renderer.CalculateGpuEffectBounds(slugcat, pose);
                True(bounds.Width > 1300.0f && bounds.Height > 1300.0f,
                    "self-destruct shockwave bounds");
                DirectCompositionHost.GpuSmokeEffect[] commands =
                    new DirectCompositionHost.GpuSmokeEffect[4];
                int count = 0;
                renderer.CollectGpuSmokeEffects(slugcat, pose,
                    new RenderSpace(Rectangle.Ceiling(bounds)), commands, ref count);
                Equal(3, count, "self-destruct GPU command count");
                True(commands[0].Seed == -4.0f && commands[1].Seed == -5.0f &&
                    commands[2].Seed == -3.0f, "self-destruct shader command kinds");
            }
        }

        private static void TwoFortyHertzRenderCadence()
        {
            FixedTimeStep step = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
            int updates = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                step.AddElapsed(1.0 / 240.0);
                while (step.ConsumeStep()) updates++;
            }
            Equal(40, updates, "one second at 240 render frames");
        }

        private static void LongGraphicsScenarioStaysConnected()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Slugcat slugcat = new Slugcat(new Vec2(work.Left + work.Width * 0.5,
                work.Bottom - SimulationConstants.HipsChunkRadius - 1.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(90.0, -35.0));
            for (int tick = 0; tick < 520; tick++)
            {
                int direction = tick < 120 ? 0 : (tick < 240 ? 1 : (tick < 360 ? -1 : 1));
                bool jump = tick >= 400 && tick < 410;
                slugcat.Step(new VirtualInput(direction, 0, jump, false), world, Vec2.Zero, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
                SlugcatPose pose = graphics.BuildPose((tick % 6) / 6.0, attention, tick);
                True(Vec2.Distance(pose.Head, pose.Chest) < 35.0, "head separation at tick " + tick);
                True(Vec2.Distance(pose.Tail[0], pose.Hips) < 25.0, "tail-root separation at tick " + tick);
                for (int i = 0; i < pose.Hands.Length; i++)
                    True(Vec2.Distance(pose.Hands[i], pose.Chest) < 42.0, "hand separation at tick " + tick);
            }
        }

        private static void FiveMinuteVariedWindowSpriteIntegrity()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(work.Left + work.Width * 0.5,
                work.Bottom - SimulationConstants.HipsChunkRadius - 1.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(60.0, -40.0));
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            for (int tick = 0; tick < 5 * 60 * (int)SimulationConstants.LogicTicksPerSecond; tick++)
            {
                if (tick % 10 == 0)
                {
                    snapshots.Clear();
                    if (tick % 70 != 0)
                    {
                        int phase = tick / 10;
                        int width = 120 + (phase % 9) * 23;
                        int height = 80 + (phase % 7) * 17;
                        int left = work.Left + 80 + (phase * 13) % Math.Max(1, work.Width - width - 160);
                        int top = work.Top + 120 + (phase * 7) % Math.Max(1, work.Height / 3);
                        snapshots.Add(new DesktopWindowSnapshot
                        {
                            Handle = new IntPtr(95001),
                            Bounds = Rectangle.FromLTRB(left, top, left + width, top + height),
                            Title = "five-minute varied window",
                            ClassName = "test"
                        });
                    }
                    world.RefreshFromSnapshots(snapshots, true, false);
                }
                slugcat.Step(new VirtualInput(0, 1, false, false), world, Vec2.Zero, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
                if (tick % 40 != 0) continue;
                SlugcatPose pose = graphics.BuildPose(0.5, attention, tick);
                True(Vec2.Distance(slugcat.BodyChunks[0].Position,
                    slugcat.BodyChunks[1].Position) < 18.1, "body connection at tick " + tick);
                True(Vec2.Distance(pose.Head, pose.Chest) < 35.0, "head integrity at tick " + tick);
                True(Vec2.Distance(pose.Tail[0], pose.Hips) < 25.0, "tail integrity at tick " + tick);
                for (int limb = 0; limb < 2; limb++)
                {
                    True(Vec2.Distance(pose.Hands[limb], pose.Chest) < 42.0,
                        "hand integrity at tick " + tick + " limb " + limb);
                    True(Vec2.Distance(pose.Feet[limb], pose.Hips) < 25.0,
                        "foot integrity at tick " + tick + " limb " + limb);
                }
            }
        }

        private static void GraphicsBoundsIncludeExtremities()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = new Vec2(0.0, 0.0);
            pose.Hips = new Vec2(0.0, 10.0);
            pose.Head = new Vec2(0.0, -10.0);
            pose.Legs = new Vec2(0.0, 20.0);
            pose.Hands[0] = new Vec2(-30.0, 0.0);
            pose.Hands[1] = new Vec2(30.0, 0.0);
            pose.Tail = new[] { new Vec2(0.0, 12.0), new Vec2(15.0, 18.0), new Vec2(30.0, 22.0) };
            pose.UpdateGraphicsBounds();
            True(pose.GraphicsBounds.Left < -30.0 && pose.GraphicsBounds.Right > 30.0,
                "hand and tail x extents");
            True(pose.GraphicsBounds.Top < -10.0 && pose.GraphicsBounds.Bottom > 22.0,
                "head and tail y extents");
        }

        private static void UnusedHandsRetract()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.BodyMode = BodyModeIndex.Stand;
            slugcat.State.Animation = AnimationIndex.None;
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(30.0, -20.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            for (int i = 0; i < 35; i++) graphics.Step(attention, world);
            True(graphics.Arms[0].Mode == LimbMode.Retracted, "left idle hand must retract");
            True(graphics.Arms[1].Mode == LimbMode.Retracted, "right idle hand must retract");
            Near(0.0, Vec2.Distance(slugcat.BodyChunks[0].Position, graphics.Arms[0].End.Position),
                0.000001, "retracted left hand position");
        }

        private static void CrawlHandsUseOriginalTargets()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.DownOnFours;
            slugcat.BodyChunks[0].Velocity = new Vec2(2.0, 0.0);
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            graphics.Step(attention, world);
            Vec2 connection = slugcat.BodyChunks[0].Position;
            Near(connection.X + 14.0, graphics.Arms[0].TargetPosition.X, 0.000001,
                "left DownOnFours target x");
            Near(connection.X + 26.0, graphics.Arms[1].TargetPosition.X, 0.000001,
                "right DownOnFours target x");
            Near(connection.Y, graphics.Arms[0].TargetPosition.Y, 0.000001,
                "crawl target y at horizontal velocity");
            True(graphics.Arms[0].Mode == LimbMode.HuntAbsolutePosition &&
                 graphics.Arms[1].Mode == LimbMode.HuntAbsolutePosition,
                "crawl arms use absolute hunt mode");
        }

        private static void CrawlEntryClearsRaisedHandTargets()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            Vec2 connection = slugcat.BodyChunks[0].Position;
            for (int hand = 0; hand < 2; hand++)
            {
                graphics.Arms[hand].Mode = LimbMode.HuntAbsolutePosition;
                graphics.Arms[hand].AbsoluteHuntPosition =
                    connection + new Vec2(hand == 0 ? -5.0 : 5.0, -18.0);
                graphics.Arms[hand].End.Position =
                    graphics.Arms[hand].AbsoluteHuntPosition;
            }

            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.None;
            for (int tick = 0; tick < 3; tick++) graphics.Step(attention, world);
            for (int hand = 0; hand < 2; hand++)
            {
                True(graphics.Arms[hand].TargetPosition.Y >= connection.Y - 0.000001,
                    "crawl entry replaces raised target for hand " + hand);
                True(graphics.Arms[hand].End.Position.Y >= connection.Y - 2.0,
                    "crawl hand lowers instead of preserving the raised pose for hand " + hand);
            }
        }

        private static void ArmConstraintPreventsSeparation()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(440.0, 380.0));
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.DownOnFours;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            for (int tick = 0; tick < 120; tick++)
            {
                slugcat.BodyChunks[0].Velocity = new Vec2(tick < 60 ? 8.0 : -8.0, tick % 9 == 0 ? 4.0 : 0.0);
                graphics.Step(attention, world);
                for (int hand = 0; hand < 2; hand++)
                {
                    True(Vec2.Distance(graphics.Arms[hand].End.Position,
                        slugcat.BodyChunks[0].Position) <= 20.0001,
                        "arm length at tick " + tick + " hand " + hand);
                }
            }
        }

        private static void CrawlFaceUsesBodyFacing()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.BodyMode = BodyModeIndex.Crawl;
            pose.Animation = AnimationIndex.DownOnFours;
            pose.Facing = 1;
            pose.Hips = new Vec2(0.0, 0.0);
            pose.Chest = new Vec2(8.0, 0.0);
            pose.Head = new Vec2(10.0, 0.0);
            pose.LookDirection = new Vec2(-1.0, 0.0);
            Near(1.0, SpriteRenderer.SelectFaceScaleX(pose), 0.000001,
                "right crawl body must ignore left attention for face flip");
            pose.Chest = new Vec2(-8.0, 0.0);
            pose.LookDirection = new Vec2(1.0, 0.0);
            Near(-1.0, SpriteRenderer.SelectFaceScaleX(pose), 0.000001,
                "left crawl body must ignore right attention for face flip");
            pose.Chest = new Vec2(0.1, 0.0);
            pose.Facing = -1;
            Near(-1.0, SpriteRenderer.SelectFaceScaleX(pose), 0.000001,
                "near-vertical crawl uses persistent facing hysteresis");
        }

        private static void ArmShouldersFollowBodyAxis()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = Vec2.Zero;
            pose.Hips = new Vec2(0.0, 17.0);
            pose.ArmRetractCounters[0] = 0;
            pose.ArmRetractCounters[1] = 0;
            Vec2 left = SpriteRenderer.ComputeArmShoulder(pose, 0);
            Vec2 right = SpriteRenderer.ComputeArmShoulder(pose, 1);
            Near(9.0, Vec2.Distance(left, right), 0.00001, "upright shoulder separation");
            Near(left.Y, right.Y, 0.00001, "upright shoulder axis");

            pose.Hips = new Vec2(-17.0, 0.0);
            left = SpriteRenderer.ComputeArmShoulder(pose, 0);
            right = SpriteRenderer.ComputeArmShoulder(pose, 1);
            Near(0.0, Vec2.Distance(left, right), 0.00001,
                "original cosine shoulder compression for horizontal torso");
        }

        private static void UniformCharacterRenderScale()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.CharacterOrigin = new Vec2(10.0, 10.0);
            pose.CharacterRenderScale = 2.20;
            Vec2 point = pose.ToRenderedWorld(new Vec2(20.0, 5.0));
            Near(44.0, point.X, 0.000001, "global scaled x");
            Near(11.0, point.Y, 0.000001, "global scaled y");
            Near(2.20, SimulationConstants.CharacterRenderScale, 0.000001,
                "configured desktop world scale");
        }

        private static void ExpandedDebugOverlayRenders()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(320.0, 260.0));
            DesktopPetAI ai = new DesktopPetAI(77);
            AttentionSystem attention = ai.Attention;
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(50.0, -20.0));
            SlugcatGraphics procedural = new SlugcatGraphics(slugcat);
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.DownOnFours;
            procedural.Step(attention, world);
            SlugcatPose pose = procedural.BuildPose(0.5, attention, 12);
            Vec2 before = slugcat.Center;
            using (SpriteRenderer renderer = new SpriteRenderer(null))
            using (Bitmap bitmap = new Bitmap(640, 480, PixelFormat.Format32bppPArgb))
            using (System.Drawing.Graphics drawing = System.Drawing.Graphics.FromImage(bitmap))
            {
                renderer.Render(drawing, pose, new RenderSpace(new Rectangle(0, 0, 640, 480)),
                    true, world, slugcat, ai, "debug-test", slugcat.Appearance);
            }
            True(pose.TailRenderMode == "OriginalTriangleMesh" &&
                 pose.TailMeshVertexCount == SpriteRenderer.OriginalTailMeshVertexCount,
                "procedural fallback also renders the one continuous tail mesh");
            Near(0.0, Vec2.Distance(pose.TailTip, pose.TailMeshVertices[14]),
                0.000001, "debug mesh diagnostics expose the point tip");
            Near(0.0, Vec2.Distance(before, slugcat.Center), 0.000001,
                "debug rendering must not mutate player physics");
        }

        private static void MouseAttentionClickCases()
        {
            MouseAttentionState state = new MouseAttentionState(90.0, 1.5);
            Vec2 head = new Vec2(100.0, 100.0);
            Vec2 near = new Vec2(140.0, 100.0);
            Vec2 far = new Vec2(300.0, 100.0);

            state.Update(0.0, near, false, head);
            True(state.IsMouseNear && !state.IsActive,
                "Case A: near hover without click stays inactive");
            state.Update(0.1, near, true, head);
            True(state.IsActive, "Case B: near click activates mouse attention");
            Near(0.1, state.LastRelevantClickTime, 0.000001, "relevant click time");

            state.Update(0.9, near, true, head);
            Near(0.9, state.LastRelevantClickTime, 0.000001,
                "Case E: repeated near click refreshes timeout");
            state.Update(2.0, near, false, head);
            True(state.IsActive, "refreshed timeout remains active");
            state.Update(2.41, near, false, head);
            True(!state.IsActive && !state.HasRecentRelevantClick,
                "Case C: timeout restores original attention");

            MouseAttentionState distant = new MouseAttentionState(90.0, 1.5);
            distant.Update(5.0, far, true, head);
            True(!distant.IsMouseNear && !distant.IsActive &&
                 !distant.HasRecentRelevantClick,
                "Case D: far click is irrelevant");
            state.Update(3.0, near, true, head);
            state.Update(3.1, far, false, head);
            True(!state.IsActive, "moving far immediately releases the override");
        }

        private static void MonitorTerrainTopologyIsExplicit()
        {
            MonitorInfo monitor = new MonitorInfo("MONITOR-A",
                new Rectangle(0, 0, 1200, 900), new Rectangle(0, 0, 1200, 850), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new DesktopWindowSnapshot[0]);
            bool floor = false;
            bool taskbar = false;
            bool left = false;
            bool right = false;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                if (surface.Id != monitor.TerrainId) continue;
                floor |= surface.Kind == DesktopSurfaceKind.MonitorFloor;
                taskbar |= surface.Kind == DesktopSurfaceKind.TaskbarTop;
                left |= surface.Kind == DesktopSurfaceKind.MonitorLeftBoundary;
                right |= surface.Kind == DesktopSurfaceKind.MonitorRightBoundary;
            }
            True(floor, "explicit monitor floor");
            True(taskbar, "bottom taskbar top surface");
            True(left && right, "exposed monitor boundaries");
            Equal(850, monitor.FloorY, "bottom taskbar defines natural floor");
            True(world.CurrentSnapshot.Monitors.Count == 1 &&
                 world.CurrentSnapshot.Monitors[0].TerrainId == monitor.TerrainId,
                "monitor identity is frozen into the collision snapshot");

            MonitorInfo sideTaskbar = new MonitorInfo("MONITOR-SIDE",
                new Rectangle(1200, 0, 1000, 900), new Rectangle(1250, 0, 950, 900), false);
            Equal(900, sideTaskbar.FloorY,
                "side taskbar must use the lower monitor boundary as floor");
        }

        private static void WindowEdgeFallLandsOnLowerWindow()
        {
            MonitorInfo monitor = new MonitorInfo("EDGE-MONITOR",
                new Rectangle(0, 0, 1200, 900), new Rectangle(0, 0, 1200, 850), true);
            DesktopWindowSnapshot upper = Window(1, new Rectangle(200, 200, 400, 100));
            DesktopWindowSnapshot lower = Window(2, new Rectangle(530, 500, 370, 120));
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new[] { upper, lower });
            double upperTop = DesktopWorldTransform.ToSimulationLength(upper.Bounds.Top);
            Slugcat slugcat = new Slugcat(new Vec2(
                DesktopWorldTransform.ToSimulationLength(575.0),
                upperTop - SimulationConstants.HipsChunkRadius));
            bool airborne = false;
            bool landedOnLower = false;
            for (int tick = 0; tick < 180; tick++)
            {
                slugcat.Step(new VirtualInput(1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
                airborne |= !slugcat.State.Grounded;
                if (slugcat.PrimarySupportingSurfaceId == lower.Handle.ToInt64() &&
                    slugcat.PrimarySupportingSurfaceKind == DesktopSurfaceKind.WindowTop)
                {
                    landedOnLower = true;
                    break;
                }
            }
            True(airborne, "the window edge must remain a real fall, not an invisible wall");
            True(landedOnLower, "swept collision must choose the first lower window");
        }

        private static void EmptyAreaFallLandsOnMonitorFloor()
        {
            MonitorInfo monitor = new MonitorInfo("EMPTY-MONITOR",
                new Rectangle(0, 0, 1200, 900), new Rectangle(0, 0, 1200, 850), true);
            DesktopWindowSnapshot upper = Window(3, new Rectangle(150, 180, 300, 100));
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { monitor }, new[] { upper });
            Slugcat slugcat = new Slugcat(new Vec2(
                DesktopWorldTransform.ToSimulationLength(430.0),
                DesktopWorldTransform.ToSimulationLength(upper.Bounds.Top) -
                    SimulationConstants.HipsChunkRadius));
            bool leftWindow = false;
            bool landed = false;
            for (int tick = 0; tick < 240; tick++)
            {
                slugcat.Step(new VirtualInput(1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
                leftWindow |= !slugcat.State.Grounded;
                if (slugcat.PrimarySupportingSurfaceId == monitor.TerrainId &&
                    (slugcat.PrimarySupportingSurfaceKind == DesktopSurfaceKind.TaskbarTop ||
                     slugcat.PrimarySupportingSurfaceKind == DesktopSurfaceKind.MonitorFloor))
                {
                    landed = true;
                    break;
                }
            }
            True(leftWindow, "slugcat leaves the application window normally");
            True(landed, "empty space must terminate at monitor terrain");
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            True(slugcat.BodyChunks[0].Position.Y < floor &&
                 slugcat.BodyChunks[1].Position.Y < floor,
                "both chunks remain above the desktop floor");
        }

        private static void MultiMonitorTopologyUsesVirtualCoordinates()
        {
            MonitorInfo left = new MonitorInfo("LEFT",
                new Rectangle(-1280, 100, 1280, 900),
                new Rectangle(-1280, 100, 1280, 860), false);
            MonitorInfo right = new MonitorInfo("RIGHT",
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1920, 1040), true);
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[] { left, right }, new DesktopWindowSnapshot[0]);
            MonitorInfo current = world.FindMonitor(DesktopWorldTransform.ToSimulation(
                new Vec2(-640.0, 500.0)));
            True(current.TerrainId == left.TerrainId,
                "negative virtual X belongs to the left monitor");

            double seamY = DesktopWorldTransform.ToSimulationLength(500.0);
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                if (surface.Kind != DesktopSurfaceKind.MonitorLeftBoundary &&
                    surface.Kind != DesktopSurfaceKind.MonitorRightBoundary) continue;
                if (Math.Abs(surface.WallX) < 0.000001 &&
                    seamY >= surface.Top && seamY <= surface.Bottom)
                    throw new InvalidOperationException("shared monitor seam became an invisible wall");
            }

            BodyChunk chunk = new BodyChunk(0,
                DesktopWorldTransform.ToSimulation(new Vec2(-640.0, 250.0)), 9.0, 0.35);
            for (int tick = 0; tick < 200 && !chunk.ContactFloor; tick++)
            {
                chunk.BeginTick();
                chunk.Integrate(SimulationConstants.GravityPerTick, SimulationConstants.AirFriction);
                world.Resolve(chunk);
            }
            True(chunk.ContactFloor && chunk.SupportingSurfaceId == left.TerrainId,
                "negative-X fall lands on the correct monitor identity");
            Near(DesktopWorldTransform.ToSimulationLength(left.FloorY) - chunk.Radius,
                chunk.Position.Y, 0.000001, "staggered monitor floor height");
        }

        private static void OriginalAirControlCases()
        {
            AssertAirCurve("A hold right", 3.6, new[] { 1, 1, 1, 1 });
            AssertAirCurve("B neutral", 3.6, new[] { 0, 0, 0, 0 });
            AssertAirCurve("C reverse left", 3.6, new[] { -1, -1, -1, -1, -1 });
            AssertAirCurve("D vertical fall alternating", 0.0, new[] { -1, 1, -1, 1 });
        }

        private static void OppositeAirInputPreservesMomentum()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            slugcat.BodyChunks[0].Velocity.X = 3.6;
            slugcat.BodyChunks[1].Velocity.X = 3.6;
            slugcat.Step(new VirtualInput(-1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
            True(slugcat.BodyChunks[0].Velocity.X > 0.0, "opposite tick 1 retains right momentum");
            slugcat.Step(new VirtualInput(-1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
            True(slugcat.BodyChunks[0].Velocity.X > 0.0, "opposite tick 2 retains right momentum");
            slugcat.Step(new VirtualInput(-1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
            True(slugcat.BodyChunks[0].Velocity.X < 0.0,
                "original recurrence reverses only on the third opposite-input tick");
        }

        private static void HunterAirSpeedUsesOriginalLimit()
        {
            DesktopCollisionWorld world;
            Slugcat hunter = CreateAirSlugcat(SlugcatVariant.Hunter, out world);
            for (int tick = 0; tick < 12; tick++)
                hunter.Step(new VirtualInput(1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
            Near(4.0, hunter.BodyChunks[0].Velocity.X, 0.000001,
                "Player UpdateBodyMode dynamicRunSpeed is 4 in ordinary air for Hunter too");
            True(hunter.BodyChunks[0].Velocity.X < 4.0 * hunter.Appearance.RunSpeedFactor,
                "ground runspeedFac must not leak into air control");
        }

        private static void TerrainImpactPreservesPreImpactVelocity()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateFloorImpact(40.0, SlugcatVariant.Survivor, out world);
            TerrainImpactData impact = slugcat.LastTerrainImpact;
            Near(40.0, impact.PreImpactVelocity.Y, 0.000001,
                "floor impact uses velocity before collision resolution");
            Near(40.0, impact.ImpactSpeed, 0.000001,
                "floor impact uses the vertical component only");
            Near(0.0, impact.PostImpactVelocity.Y, 0.000001,
                "original bounce stop threshold resolves the post-impact velocity");
            True(impact.ImpactDirection.Y < 0.0 && impact.CollisionNormal.Y < 0.0,
                "floor TerrainImpact direction and desktop normal");
            True(impact.FirstContact && impact.TerrainImpactTriggered,
                "first-contact impact is preserved by the adapter");
        }

        private static void OriginalFloorImpactThresholds()
        {
            DesktopCollisionWorld world;
            Slugcat low = CreateFloorImpact(12.0, SlugcatVariant.Survivor, out world);
            True(low.State.StunCounter == 0 && !low.State.Dead,
                "ordinary landing does not stun");

            Slugcat medium = CreateFloorImpact(40.0, SlugcatVariant.Survivor, out world);
            int expected = Slugcat.CalculateOriginalImpactStun(40.0, 35.0, 60.0);
            Equal(expected, medium.State.StunCounter, "DLL LerpMap impact stun");
            True(medium.State.IsStunned && !medium.State.Conscious && !medium.State.Dead,
                "hard landing enters Creature.Stunned");

            Slugcat high = CreateFloorImpact(61.0, SlugcatVariant.Survivor, out world);
            True(!high.State.Dead && !high.LastTerrainImpact.CausedDeath,
                "desktop safety layer blocks the original lethal result");
            True(high.LastTerrainImpact.WasOriginallyLethal &&
                 high.LastTerrainImpact.SafetyOverrideApplied,
                "original lethal severity remains visible to diagnostics");
            Equal(SimulationConstants.MaxImpactStunTicks, high.State.StunCounter,
                "original lethal result becomes maximum impact stun");
            True(high.LastTerrainImpact.DesktopResult == DesktopPetImpactResult.MaximumStun,
                "desktop impact result is MaximumStun");
        }

        private static void TerrainFirstContactUsesDirection()
        {
            DesktopCollisionWorld world = CreateSyntheticWorld(
                new[]
                {
                    new MonitorInfo("CONTACT-MONITOR", new Rectangle(0, 0, 1200, 1000),
                        new Rectangle(0, 0, 1200, 1000), true)
                },
                new[]
                {
                    Window(8101, new Rectangle(0, 600, 500, 300)),
                    Window(8102, new Rectangle(500, 600, 500, 300))
                });
            BodyChunk chunk = new BodyChunk(0,
                new Vec2(750.0 / SimulationConstants.DesktopWorldScale,
                    600.0 / SimulationConstants.DesktopWorldScale - 9.0),
                9.0, 0.35);
            chunk.ContactFloor = true;
            chunk.SupportingSurfaceId = 8101;
            chunk.SupportingSurfaceKind = DesktopSurfaceKind.WindowTop;
            chunk.BeginTick();
            chunk.Position = new Vec2(755.0 / SimulationConstants.DesktopWorldScale,
                600.0 / SimulationConstants.DesktopWorldScale - 8.5);
            chunk.Velocity = new Vec2(2.0, 40.0);
            world.Resolve(chunk);
            True(chunk.ContactFloor && chunk.SupportingSurfaceId == 8102,
                "chunk moved onto the second coplanar window terrain");
            True(chunk.TerrainImpactCount > 0 && !chunk.TerrainImpacts[0].FirstContact,
                "retained floor direction must not become first contact when HWND changes");
        }

        private static void OriginalWallImpactStuns()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateWallImpact(40.0, SlugcatVariant.Survivor, out world);
            TerrainImpactData impact = slugcat.LastTerrainImpact;
            True(impact.SurfaceKind == DesktopSurfaceKind.WindowLeftWall,
                "window side is a terrain adapter wall");
            Near(40.0, impact.PreImpactVelocity.X, 0.000001,
                "wall impact uses pre-resolution horizontal velocity");
            Near(0.0, impact.PostImpactVelocity.X, 0.000001,
                "wall rebound is stopped by the original bounce threshold");
            Equal(Slugcat.CalculateOriginalImpactStun(40.0, 35.0, 60.0),
                slugcat.State.StunCounter, "wall impact stun duration");
            True(!slugcat.State.Dead && impact.ImpactDirection.X > 0.0 &&
                 impact.CollisionNormal.X < 0.0,
                "wall impacts stun but do not use floor-only death");
        }

        private static void GourmandImpactThresholds()
        {
            DesktopCollisionWorld world;
            Slugcat normal = CreateFloorImpact(39.0, SlugcatVariant.Gourmand, out world);
            True(normal.State.StunCounter == 0 && !normal.State.Dead,
                "Gourmand remains below its 40 stun threshold");
            Slugcat stunned = CreateFloorImpact(45.0, SlugcatVariant.Gourmand, out world);
            Equal(Slugcat.CalculateOriginalImpactStun(45.0, 40.0, 80.0),
                stunned.State.StunCounter, "Gourmand 40/80 LerpMap stun");
            Slugcat dead = CreateFloorImpact(81.0, SlugcatVariant.Gourmand, out world);
            True(!dead.State.Dead && dead.LastTerrainImpact.WasOriginallyLethal,
                "Gourmand's original lethal threshold is detected but blocked");
            Equal(SimulationConstants.MaxImpactStunTicks, dead.State.StunCounter,
                "Gourmand lethal severity becomes capped maximum stun");
        }

        private static void ExtremeImpactIsNonLethalAndRecovers()
        {
            Near(3.0, SimulationConstants.MaxImpactStunDurationSeconds, 0.000001,
                "impact stun duration has one three-second source setting");
            Equal(120, SimulationConstants.MaxImpactStunTicks,
                "three seconds converts once to 120 ticks at 40 Hz");
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateFloorImpact(200.0, SlugcatVariant.Survivor, out world);
            Equal(140, slugcat.LastTerrainImpact.CalculatedStun,
                "original LerpMap severity reaches 140 for an extreme impact");
            Equal(SimulationConstants.MaxImpactStunTicks,
                slugcat.LastTerrainImpact.AppliedStun,
                "only the desktop safety cap truncates the original severity");
            True(!slugcat.State.Dead && slugcat.State.IsStunned,
                "extreme impact is non-lethal and initially stunned");

            for (int tick = 0; tick < SimulationConstants.MaxImpactStunTicks; tick++)
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            Equal(0, slugcat.State.StunCounter,
                "maximum impact stun ends after exactly three seconds");
            True(slugcat.State.Conscious && !slugcat.State.Dead,
                "extreme impact returns to a conscious Player state");
        }

        private static void RepeatedImpactsCannotExtendStunForever()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateFloorImpact(200.0, SlugcatVariant.Survivor, out world);
            for (int tick = 1; tick <= SimulationConstants.MaxImpactStunTicks; tick++)
            {
                if (tick % 8 == 0 && tick < SimulationConstants.MaxImpactStunTicks)
                    PrepareFloorImpact(slugcat, world, 200.0);
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
                True(slugcat.State.StunCounter <=
                     SimulationConstants.MaxImpactStunTicks - tick,
                    "terrain impact did not move the absolute recovery deadline at tick " + tick);
                True(!slugcat.State.Dead, "repeated terrain impact stayed non-lethal");
            }
            Equal(0, slugcat.State.StunCounter,
                "repeated bounces still reach the recovery tick");
            True(slugcat.State.Conscious,
                "repeated terrain impacts cannot create permanent unconsciousness");
        }

        private static void StunKeepsPhysicsAndBlocksMovement()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            slugcat.Stun(40);
            Vec2 before = slugcat.BodyChunks[0].Position;
            slugcat.Step(new VirtualInput(1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
            Equal(39, slugcat.State.StunCounter, "Creature.Update decrements stun by one tick");
            True(slugcat.BodyChunks[0].Position.Y > before.Y,
                "gravity and BodyChunk integration continue while stunned");
            Near(0.0, slugcat.BodyChunks[0].Velocity.X, 0.000001,
                "Player MovementUpdate is skipped despite AI horizontal input");
            True(slugcat.State.BodyMode == BodyModeIndex.Stunned &&
                 slugcat.State.Animation == AnimationIndex.None &&
                 !slugcat.State.Standing,
                "stunned Player body state matches the DLL");
        }

        private static void StunnedGraphicsUseOriginalState()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            slugcat.Stun(40);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.Mouse, slugcat.Center + new Vec2(100.0, -50.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            for (int tick = 0; tick < 35; tick++) graphics.Step(attention, world);
            SlugcatPose pose = graphics.BuildPose(1.0, attention, 1);
            True(!pose.Conscious && pose.IsStunned, "one Player stun state drives all graphics");
            True(pose.SelectedFaceElement == "FaceStunned",
                "original atlas FaceStunned is selected");
            True(pose.FaceSelectionReason == "Stunned", "stunned face selection branch");
            True(graphics.Arms[0].Mode == LimbMode.Retracted &&
                 graphics.Arms[1].Mode == LimbMode.Retracted,
                "SlugcatHand unused Stunned branch retracts both limbs");
            Near(0.0, pose.LookDirection.Length, 0.000001,
                "PlayerGraphics zeroes lookDirection while unconscious");
        }

        private static void StunSuppressesMouseAndRecoversNaturally()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            slugcat.Stun(12);
            MouseAttentionState click = new MouseAttentionState(90.0, 1.5);
            Vec2 mouse = slugcat.Center + new Vec2(20.0, 0.0);
            click.Update(0.0, mouse, true, slugcat.Center);
            True(click.IsActive, "precondition: near click activates attention");
            DesktopPetAI ai = new DesktopPetAI(123);
            MouseTracker tracker = new MouseTracker();
            ai.Step(slugcat, world, tracker, click);
            True(!ai.MouseAttentionActive,
                "stun state has priority over an active mouse click");
            click.Suppress(0.1, mouse, slugcat.Center);
            True(!click.IsActive && !click.HasRecentRelevantClick,
                "click latch is discarded during stun");

            for (int tick = 0; tick < 12; tick++)
                slugcat.Step(new VirtualInput(1, 0, false, false), world, Vec2.Zero, Vec2.Zero);
            Equal(0, slugcat.State.StunCounter, "stun counter reaches zero naturally");
            True(slugcat.State.BodyMode == BodyModeIndex.Default && !slugcat.State.Grounded,
                "recovery derives airborne mode from current contacts instead of forced Stand");
            True(slugcat.BodyChunks[0].Velocity.X > 0.0,
                "input resumes only on the naturally recovered tick");
        }

        private static DesktopCollisionWorld CreateSyntheticWorld(
            IList<MonitorInfo> monitors, IList<DesktopWindowSnapshot> windows)
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(windows, monitors);
            return world;
        }

        private static DesktopWindowSnapshot Window(long id, Rectangle bounds)
        {
            return new DesktopWindowSnapshot
            {
                Handle = new IntPtr(id),
                Bounds = bounds,
                Title = "Window " + id,
                ClassName = "TestWindow"
            };
        }

        private static Slugcat CreateAirSlugcat(SlugcatVariant variant,
            out DesktopCollisionWorld world)
        {
            MonitorInfo monitor = new MonitorInfo("AIR-MONITOR",
                new Rectangle(-10000, -10000, 20000, 30000),
                new Rectangle(-10000, -10000, 20000, 30000), true);
            world = CreateSyntheticWorld(new[] { monitor }, new DesktopWindowSnapshot[0]);
            Slugcat slugcat = new Slugcat(new Vec2(0.0, 17.0), variant);
            slugcat.BodyChunks[0].Position = Vec2.Zero;
            slugcat.BodyChunks[0].LastPosition = Vec2.Zero;
            slugcat.BodyChunks[1].Position = new Vec2(0.0, 17.0);
            slugcat.BodyChunks[1].LastPosition = new Vec2(0.0, 17.0);
            slugcat.State.Grounded = false;
            slugcat.State.BodyMode = BodyModeIndex.Default;
            slugcat.State.Animation = AnimationIndex.None;
            return slugcat;
        }

        private static void AssertAirCurve(string name, double initialVelocity,
            int[] inputs)
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateAirSlugcat(SlugcatVariant.Survivor, out world);
            slugcat.BodyChunks[0].Velocity.X = initialVelocity;
            slugcat.BodyChunks[1].Velocity.X = initialVelocity;
            double expectedVelocity = initialVelocity;
            double expectedPosition = 0.0;
            for (int tick = 0; tick < inputs.Length; tick++)
            {
                expectedVelocity *= SimulationConstants.AirFriction;
                expectedPosition += expectedVelocity;
                expectedVelocity = ExpectedOriginalAirInput(expectedVelocity, inputs[tick]);
                slugcat.Step(new VirtualInput(inputs[tick], 0, false, false),
                    world, Vec2.Zero, Vec2.Zero);
                Near(expectedVelocity, slugcat.BodyChunks[0].Velocity.X, 0.000001,
                    name + " velocity tick " + tick);
                Near(expectedPosition, slugcat.BodyChunks[0].Position.X, 0.000001,
                    name + " displacement tick " + tick);
                True(slugcat.State.BodyMode == BodyModeIndex.Default &&
                     slugcat.State.Animation == AnimationIndex.None,
                    name + " state tick " + tick);
            }
        }

        private static double ExpectedOriginalAirInput(double velocity, int direction)
        {
            const double speed = 4.0;
            double amount = 2.4 * SimulationConstants.SurfaceFriction;
            if (direction < 0)
            {
                if (velocity - amount < -speed) amount = speed + velocity;
                if (amount > 0.0) velocity -= amount;
            }
            else if (direction > 0)
            {
                if (velocity + amount > speed) amount = speed - velocity;
                if (amount > 0.0) velocity += amount;
            }
            return velocity;
        }

        private static Slugcat CreateFloorImpact(double targetImpactSpeed,
            SlugcatVariant variant, out DesktopCollisionWorld world)
        {
            MonitorInfo monitor = new MonitorInfo("IMPACT-MONITOR",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            world = CreateSyntheticWorld(new[] { monitor }, new DesktopWindowSnapshot[0]);
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            Slugcat slugcat = new Slugcat(new Vec2(200.0,
                floor - SimulationConstants.HipsChunkRadius - 0.5), variant);
            double initialY = targetImpactSpeed / SimulationConstants.AirFriction -
                SimulationConstants.GravityPerTick;
            slugcat.BodyChunks[0].Velocity = new Vec2(0.0, initialY);
            slugcat.BodyChunks[1].Velocity = new Vec2(0.0, initialY);
            slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            return slugcat;
        }

        private static void PrepareFloorImpact(Slugcat slugcat,
            DesktopCollisionWorld world, double targetImpactSpeed)
        {
            MonitorInfo monitor = world.FindMonitor(slugcat.Center);
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            BodyChunk hips = slugcat.BodyChunks[1];
            BodyChunk chest = slugcat.BodyChunks[0];
            hips.Position = new Vec2(200.0,
                floor - SimulationConstants.HipsChunkRadius - 0.5);
            chest.Position = hips.Position -
                new Vec2(0.0, SimulationConstants.BodyConnectionDistance);
            hips.LastPosition = hips.Position;
            chest.LastPosition = chest.Position;
            double initialY = targetImpactSpeed / SimulationConstants.AirFriction -
                SimulationConstants.GravityPerTick;
            hips.Velocity = new Vec2(0.0, initialY);
            chest.Velocity = new Vec2(0.0, initialY);
            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
            {
                slugcat.BodyChunks[i].ContactFloor = false;
                slugcat.BodyChunks[i].SupportingSurfaceId = 0;
            }
        }

        private static Slugcat CreateWallImpact(double targetImpactSpeed,
            SlugcatVariant variant, out DesktopCollisionWorld world)
        {
            MonitorInfo monitor = new MonitorInfo("WALL-MONITOR",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            DesktopWindowSnapshot window = Window(901,
                new Rectangle(400, 100, 200, 800));
            world = CreateSyntheticWorld(new[] { monitor }, new[] { window });
            double wall = DesktopWorldTransform.ToSimulationLength(window.Bounds.Left);
            Slugcat slugcat = new Slugcat(new Vec2(
                wall - SimulationConstants.HipsChunkRadius - 0.5, 300.0), variant);
            double initialX = targetImpactSpeed / SimulationConstants.AirFriction;
            slugcat.BodyChunks[0].Velocity = new Vec2(initialX,
                -SimulationConstants.GravityPerTick);
            slugcat.BodyChunks[1].Velocity = new Vec2(initialX,
                -SimulationConstants.GravityPerTick);
            slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            return slugcat;
        }

        private static SlugcatVariant ReadVariant(string value)
        {
            if (string.Equals(value, "yellow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "monk", StringComparison.OrdinalIgnoreCase)) return SlugcatVariant.Monk;
            if (string.Equals(value, "red", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "hunter", StringComparison.OrdinalIgnoreCase)) return SlugcatVariant.Hunter;
            if (string.Equals(value, "gourmand", StringComparison.OrdinalIgnoreCase)) return SlugcatVariant.Gourmand;
            return SlugcatVariant.Survivor;
        }

        private static SlugcatSkin ReadSkin(string value)
        {
            SlugcatSkin skin;
            return Enum.TryParse(value, true, out skin) ? skin : SlugcatSkin.Default;
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Equal(int expected, int actual, string message)
        {
            if (expected != actual) throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }

        private static void Near(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }
}
