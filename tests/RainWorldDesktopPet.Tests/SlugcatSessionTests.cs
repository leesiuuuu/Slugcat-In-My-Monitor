using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.UI;

namespace RainWorldDesktopPet.Tests
{
    internal static class SlugcatSessionTests
    {
        internal static void OverlayRestart()
        {
            Exception failure = null;
            Thread thread = new Thread(delegate()
            {
                try
                {
                    WithStore(delegate(string path, SlugcatSessionStore store)
                    {
                        RainWorldInstallation installation = new RainWorldInstallation(Path.GetDirectoryName(path));
                        Directory.CreateDirectory(Path.GetDirectoryName(installation.MoreSlugcatsModInfoPath));
                        File.WriteAllText(installation.MoreSlugcatsModInfoPath, "{\"id\":\"moreslugcats\"}");
                        using (LayeredOverlayWindow first = new LayeredOverlayWindow(
                            installation, false, SlugcatId.White, null, false, store))
                        {
                            IntPtr handle = first.Handle;
                            first.SettingsSetSlugcat(SlugcatId.Rivulet);
                            first.SettingsSetSlugcatSize(SlugcatSize.Small);
                            first.SettingsSetSlugpupAppearance(true);
                            first.SettingsAddSlugcat();
                            first.SettingsSetSlugcat(SlugcatId.Saint);
                            first.SettingsSetSlugcatSize(SlugcatSize.Normal);
                            first.SettingsAddSlugcat();
                            first.SettingsRemoveSelectedSlugcat();
                            first.SettingsSelectSlugcat(0);
                            string warning;
                            Check(store.Load(out warning).Pets.Count == 2,
                                "Add/delete hooks did not save before shutdown.");
                        }
                        using (LayeredOverlayWindow second = new LayeredOverlayWindow(
                            installation, false, SlugcatId.White, null, false, new SlugcatSessionStore(path)))
                        {
                            IntPtr handle = second.Handle;
                            Check(second.SettingsSlugcatNames.Length == 2 && second.SettingsSelectedSlugcatIndex == 0,
                                "Overlay restart lost count or selected index.");
                            Check(second.SettingsSlugcatId == SlugcatId.Rivulet && second.SettingsSlugcatSize == SlugcatSize.Small,
                                "First restored pet differs.");
                            Check(second.SettingsIsSlugpupAppearance(), "Slugpup appearance was not restored.");
                            second.SettingsSelectSlugcat(1);
                            Check(second.SettingsSlugcatId == SlugcatId.Saint && second.SettingsSlugcatSize == SlugcatSize.Normal,
                                "Second restored pet differs.");
                        }
                        using (LayeredOverlayWindow explicitLaunch = new LayeredOverlayWindow(
                            installation, false, SlugcatId.Red, null, true, new SlugcatSessionStore(path)))
                        {
                            IntPtr handle = explicitLaunch.Handle;
                            Check(explicitLaunch.SettingsSlugcatNames.Length == 2 && explicitLaunch.SettingsSlugcatId == SlugcatId.Red,
                                "Explicit launch must override only the selected pet, keeping the roster.");
                        }
                        string future = "{\"Version\":2,\"Pets\":[]}";
                        File.WriteAllText(path, future);
                        using (LayeredOverlayWindow olderApp = new LayeredOverlayWindow(
                            installation, false, SlugcatId.White, null, false, new SlugcatSessionStore(path)))
                        {
                            IntPtr handle = olderApp.Handle;
                            olderApp.SettingsAddSlugcat();
                            olderApp.SettingsSetSlugcatSize(SlugcatSize.Small);
                        }
                        Check(File.ReadAllText(path) == future,
                            "Startup, mutations or shutdown overwrote a newer session format.");
                    });
                }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new InvalidOperationException("Overlay restart integration failed", failure);
        }

        internal static void RoundTrip()
        {
            WithStore(delegate(string path, SlugcatSessionStore store)
            {
                string warning;
                Check(store.Load(out warning) == null, "First launch must have no saved roster.");
                SlugcatSession session = Session(8);
                session.SelectedIndex = 5;
                session.Pets[2].Character = "Rivulet";
                session.Pets[2].Size = "Small";
                session.Pets[2].DmsParts["HEAD"] = "homeobox.raincoatriv";
                session.Pets[2].CustomColors["BODY"] = "80ABCDEF";
                store.Save(session);
                SlugcatSession restored = new SlugcatSessionStore(path).Load(out warning);
                Check(restored.Pets.Count == 8 && restored.SelectedIndex == 5, "Roster order/selection lost.");
                Check(restored.Pets[2].Character == "Rivulet" && restored.Pets[2].Size == "Small", "Identity/size lost.");
                Check(restored.Pets[2].DmsParts["HEAD"] == "homeobox.raincoatriv", "DMS ID lost.");
                Check(restored.Pets[2].CustomColors["BODY"] == "80ABCDEF" &&
                    !restored.Pets[2].CustomColors.ContainsKey("HEAD"), "Explicit tint state lost.");
            });
        }

        internal static void DeletionAndSelection()
        {
            WithStore(delegate(string path, SlugcatSessionStore store)
            {
                SlugcatSession session = Session(3);
                store.Save(session);
                session.Pets.RemoveAt(1);
                session.SelectedIndex = 100;
                store.Save(session);
                string warning;
                SlugcatSession restored = new SlugcatSessionStore(path).Load(out warning);
                Check(restored.Pets.Count == 2 && restored.SelectedIndex == 1, "Deleted pet returned or selection invalid.");
                Check(File.Exists(path + ".bak"), "Previous roster backup missing.");
                string backup = File.ReadAllText(path + ".bak");
                store.Save(restored);
                Check(File.ReadAllText(path + ".bak") == backup, "Unchanged state replaced the useful backup.");
            });
        }

        internal static void CorruptionRecovery()
        {
            WithStore(delegate(string path, SlugcatSessionStore store)
            {
                store.Save(Session(2));
                store.Save(Session(3));
                File.WriteAllText(path, "{interrupted");
                string warning;
                Check(store.Load(out warning).Pets.Count == 2 && warning != null, "Backup was not recovered.");
                File.Delete(path + ".bak");
                Expect<InvalidDataException>(delegate { store.Load(out warning); });
            });
        }

        internal static void FutureVersionAndLimits()
        {
            WithStore(delegate(string path, SlugcatSessionStore store)
            {
                store.Save(Session(1));
                string original = File.ReadAllText(path);
                Expect<InvalidDataException>(delegate { store.Save(Session(9)); });
                Expect<InvalidDataException>(delegate { store.Save(Session(0)); });
                Check(File.ReadAllText(path) == original, "Invalid write changed the previous session.");
                store.Save(Session(2)); // A valid older backup must not conceal a newer format.
                File.WriteAllText(path, "{\"Version\":2,\"Pets\":[]}");
                string future = File.ReadAllText(path);
                string warning;
                Expect<NotSupportedException>(delegate { store.Load(out warning); });
                Check(File.ReadAllText(path) == future, "Newer format was overwritten.");
            });
        }

        internal static void WriteFailurePreservesFile()
        {
            WithStore(delegate(string path, SlugcatSessionStore store)
            {
                store.Save(Session(2));
                string original = File.ReadAllText(path);
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Expect<IOException>(delegate { store.Save(Session(3)); });
                Check(File.ReadAllText(path) == original, "Failed replacement damaged the previous file.");
                Check(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0, "Temporary file leaked.");
                store.Save(Session(3));
                string warning;
                Check(store.Load(out warning).Pets.Count == 3, "Save did not recover after the lock was released.");
            });
        }

        internal static void RestoreAppearanceAndFallbacks()
        {
            WithStore(delegate(string path, SlugcatSessionStore store)
            {
                // Empty local installation exercises the real GameLoop's safe asset fallback.
                RainWorldInstallation installation = new RainWorldInstallation(Path.GetDirectoryName(path));
                using (GameLoop source = new GameLoop(IntPtr.Zero, installation, SlugcatId.Rivulet, 0, null, null))
                {
                    source.SetSize(SlugcatSize.Small);
                    source.SetPartColor("BODY", Color.FromArgb(128, 171, 205, 239));
                    SlugcatSession session = Session(1);
                    session.Pets[0] = SlugcatSessionPet.Capture(source);
                    store.Save(session);
                }
                string warning;
                SlugcatSessionPet pet = new SlugcatSessionStore(path).Load(out warning).Pets[0];
                pet.DmsParts["HEAD"] = "missing.skin";
                List<string> warnings = new List<string>();
                using (GameLoop target = new GameLoop(IntPtr.Zero, installation, SlugcatId.White, 0, null, null))
                {
                    pet.Apply(target, false, warnings.Add);
                    Check(target.SelectedSlugcat.Id == SlugcatId.Rivulet && target.Size == SlugcatSize.Small,
                        "Actual model identity/size did not restore.");
                    Check(target.HasCustomPartColor("BODY") && target.GetPartColor("BODY").ToArgb() ==
                        Color.FromArgb(255, 171, 205, 239).ToArgb(), "Custom color did not restore.");
                    Check(!target.HasCustomPartColor("HEAD") && target.GetDmsPartSelection("HEAD") == null &&
                        warnings.Count == 1, "Missing DMS should use vanilla without tinting unaffected parts.");
                    pet.Character = "Inv";
                    pet.Size = "999";
                    pet.Apply(target, false, null);
                    Check(target.SelectedSlugcat.Id == SlugcatId.White && target.Size == SlugcatSize.Large,
                        "Locked Inv or invalid size bypassed fallback.");
                    pet.Apply(target, true, null);
                    Check(target.SelectedSlugcat.Id == SlugcatId.Inv, "Unlocked Inv did not restore.");
                    pet.Character = "unknown";
                    pet.Apply(target, true, null);
                    Check(target.SelectedSlugcat.Id == SlugcatId.White, "Unknown identity did not fall back.");
                    pet.PupAppearance = true;
                    pet.Apply(target, true, null);
                    Check(!target.Slugcat.PupAppearance, "Missing expansion must disable the saved Slugpup option.");
                    Directory.CreateDirectory(Path.GetDirectoryName(installation.MoreSlugcatsModInfoPath));
                    File.WriteAllText(installation.MoreSlugcatsModInfoPath, "{\"id\":\"moreslugcats\"}");
                    pet.Apply(target, true, null);
                    Check(target.Slugcat.PupAppearance, "Available Slugpup option was not restored.");
                }
            });
        }

        private static SlugcatSession Session(int count)
        {
            SlugcatSession session = new SlugcatSession
            { Version = 1, Pets = new List<SlugcatSessionPet>() };
            for (int i = 0; i < count; i++) session.Pets.Add(new SlugcatSessionPet
            {
                Character = "White", Size = "Large", DmsParts = new Dictionary<string, string>(),
                CustomColors = new Dictionary<string, string>()
            });
            return session;
        }

        private static void WithStore(Action<string, SlugcatSessionStore> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "SIMM-session-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { string path = Path.Combine(root, "session.json"); test(path, new SlugcatSessionStore(path)); }
            finally { Directory.Delete(root, true); }
        }

        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
