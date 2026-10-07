using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Core
{
    public sealed class SlugcatSession
    {
        public int Version { get; set; }
        public int SelectedIndex { get; set; }
        public List<SlugcatSessionPet> Pets { get; set; }
    }

    public sealed class SlugcatSessionPet
    {
        public string Character { get; set; }
        public string Size { get; set; }
        public Dictionary<string, string> DmsParts { get; set; }
        // Only explicit colors are stored; authored DMS sprites stay untinted.
        public Dictionary<string, string> CustomColors { get; set; }

        internal SlugcatId ResolveCharacter(bool invUnlocked)
        {
            SlugcatId id;
            return SlugcatProfiles.TryParse(Character, out id) &&
                (id != SlugcatId.Inv || invUnlocked) ? id : SlugcatId.White;
        }

        internal static SlugcatSessionPet Capture(GameLoop loop)
        {
            SlugcatSessionPet pet = new SlugcatSessionPet
            {
                Character = loop.SelectedSlugcat.Id.ToString(), Size = loop.Size.ToString(),
                DmsParts = new Dictionary<string, string>(),
                CustomColors = new Dictionary<string, string>()
            };
            foreach (string part in DmsSpriteGroups.SelectableParts)
            {
                string id = loop.GetDmsPartSelection(part);
                if (!string.IsNullOrEmpty(id)) pet.DmsParts[part] = id;
                if (loop.HasCustomPartColor(part))
                    pet.CustomColors[part] = loop.GetPartColor(part).ToArgb().ToString("X8");
            }
            return pet;
        }

        internal void Apply(GameLoop loop, bool invUnlocked, Action<string> warn)
        {
            loop.SetSelectedSlugcat(ResolveCharacter(invUnlocked));
            SlugcatSize size;
            loop.SetSize(Enum.TryParse(Size, out size) && Enum.IsDefined(typeof(SlugcatSize), size)
                ? size : SlugcatSize.Large);
            loop.ClearDmsParts();
            loop.ClearPartColors();
            foreach (string part in DmsSpriteGroups.SelectableParts)
            {
                string value;
                if (DmsParts != null && DmsParts.TryGetValue(part, out value))
                {
                    try
                    {
                        string reason;
                        if (!loop.SetDmsPart(part, value, out reason) && warn != null) warn(reason);
                    }
                    catch (Exception exception)
                    {
                        // A damaged skin must not prevent the other parts/colors from restoring.
                        if (warn != null) warn(exception.Message);
                    }
                }
                uint argb;
                if (CustomColors != null && CustomColors.TryGetValue(part, out value) &&
                    uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb))
                    loop.SetPartColor(part, Color.FromArgb(unchecked((int)argb)));
            }
        }
    }

    internal sealed class SlugcatSessionStore
    {
        internal const int MaximumPets = 8;
        internal const int CurrentVersion = 1;
        private readonly string path;

        internal SlugcatSessionStore() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "slugcat-session.json")) { }

        internal SlugcatSessionStore(string path) { this.path = Path.GetFullPath(path); }

        internal SlugcatSession Load(out string warning)
        {
            warning = null;
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            try { return Read(path); }
            catch (NotSupportedException) { throw; } // Never overwrite a newer format.
            catch (Exception original)
            {
                try
                {
                    SlugcatSession recovered = Read(path + ".bak");
                    warning = "Recovered Slugcat session from backup: " + original.Message;
                    return recovered;
                }
                catch (NotSupportedException) { throw; }
                catch (Exception)
                {
                    throw new InvalidDataException("Unable to read the saved Slugcat session.", original);
                }
            }
        }

        private static SlugcatSession Read(string filename)
        {
            if (new FileInfo(filename).Length > 1024 * 1024)
                throw new InvalidDataException("Slugcat session is too large.");
            SlugcatSession session = new JavaScriptSerializer().Deserialize<SlugcatSession>(
                File.ReadAllText(filename, Encoding.UTF8));
            Validate(session);
            return session;
        }

        private static void Validate(SlugcatSession session)
        {
            if (session == null) throw new InvalidDataException("Empty Slugcat session.");
            if (session.Version > CurrentVersion)
                throw new NotSupportedException("The Slugcat session was saved by a newer app version.");
            if (session.Version != CurrentVersion || session.Pets == null ||
                session.Pets.Count == 0 || session.Pets.Count > MaximumPets)
                throw new InvalidDataException("Invalid Slugcat session version or pet count.");
            foreach (SlugcatSessionPet pet in session.Pets)
            {
                if (pet == null) throw new InvalidDataException("Empty Slugcat entry.");
                // Unknown identities and sizes use safe defaults when applied.
            }
            session.SelectedIndex = Math.Max(0, Math.Min(session.SelectedIndex, session.Pets.Count - 1));
        }

        internal void Save(SlugcatSession session)
        {
            Validate(session);
            string json = new JavaScriptSerializer().Serialize(session);
            // Selection refreshes and normal exit often produce identical state.
            // Keep the last different save as the backup rather than replacing it.
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == json) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
