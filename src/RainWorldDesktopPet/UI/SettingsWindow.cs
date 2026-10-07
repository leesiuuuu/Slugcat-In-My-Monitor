using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;

namespace RainWorldDesktopPet.UI
{
    internal sealed class SettingsWindow : Form
    {
        private readonly LayeredOverlayWindow app;
        private readonly ListBox slugcatList;
        private readonly Button addButton;
        private readonly Button nextButton;
        private readonly Button removeButton;
        private readonly ComboBox characterSelector;
        private readonly ComboBox sizeSelector;
        private readonly CheckBox pupAppearanceCheck;
        private readonly ComboBox languageSelector;
        private readonly CheckBox debugCheck;
        private readonly CheckBox pauseCheck;
        private readonly CheckBox audioMuteCheck;
        private readonly TrackBar audioVolumeTrack;
        private readonly Label audioVolumeValueLabel;
        private readonly Button retryButton;
        private readonly Label statusLabel;
        private bool updating;

        public SettingsWindow(LayeredOverlayWindow app)
        {
            if (app == null) throw new ArgumentNullException("app");
            this.app = app;

            Text = T("SlugcatInMyMonitor 설정", "SlugcatInMyMonitor Settings");
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(560, 500);
            ClientSize = new Size(640, 560);
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            GroupBox slugcatsGroup = new GroupBox { Text = T("슬러그캣", "Slugcats"), Dock = DockStyle.Fill };
            TableLayoutPanel slugcatsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 2
            };
            slugcatsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            slugcatsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            slugcatList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            slugcatList.SelectedIndexChanged += SlugcatSelectionChanged;
            slugcatsLayout.Controls.Add(slugcatList, 0, 0);
            FlowLayoutPanel slugcatActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0)
            };
            addButton = ActionButton(T("슬러그캣 추가", "Add Slugcat"), delegate { app.SettingsAddSlugcat(); RefreshFromApp(); });
            nextButton = ActionButton(T("다음 선택", "Select Next"), delegate { app.SettingsSelectNextSlugcat(); RefreshFromApp(); });
            removeButton = ActionButton(T("선택 항목 삭제", "Remove Selected"), delegate { app.SettingsRemoveSelectedSlugcat(); RefreshFromApp(); });
            slugcatActions.Controls.Add(addButton);
            slugcatActions.Controls.Add(nextButton);
            slugcatActions.Controls.Add(removeButton);
            slugcatsLayout.Controls.Add(slugcatActions, 0, 1);
            slugcatsGroup.Controls.Add(slugcatsLayout);
            root.Controls.Add(slugcatsGroup, 0, 0);

            GroupBox appearanceGroup = new GroupBox
            {
                Text = T("선택한 슬러그캣", "Selected Slugcat"),
                Dock = DockStyle.Fill
            };
            TableLayoutPanel appearanceLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 2,
                RowCount = 4
            };
            appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            appearanceLayout.Controls.Add(FieldLabel(T("캐릭터와 능력", "Character and Ability")), 0, 0);
            characterSelector = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            characterSelector.SelectedIndexChanged += CharacterChanged;
            appearanceLayout.Controls.Add(characterSelector, 1, 0);
            appearanceLayout.Controls.Add(FieldLabel(T("크기", "Size")), 0, 1);
            sizeSelector = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            sizeSelector.Items.Add(new SizeChoice(SlugcatSize.Small,
                T("작게", "Small")));
            sizeSelector.Items.Add(new SizeChoice(SlugcatSize.Normal,
                T("보통", "Normal")));
            sizeSelector.Items.Add(new SizeChoice(SlugcatSize.Large,
                T("크게", "Large")));
            sizeSelector.SelectedIndexChanged += SlugcatSizeChanged;
            appearanceLayout.Controls.Add(sizeSelector, 1, 1);
            appearanceLayout.Controls.Add(FieldLabel(T("외형", "Appearance")), 0, 2);
            pupAppearanceCheck = new CheckBox
            {
                Text = T("슬러그펍", "Slugpup"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            pupAppearanceCheck.CheckedChanged += SlugpupAppearanceChanged;
            appearanceLayout.Controls.Add(pupAppearanceCheck, 1, 2);
            FlowLayoutPanel appearanceActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true
            };
            Button editorButton = ActionButton(T("실험적 스킨 편집기 열기", "Open Experimental Skin Editor"), delegate
            {
                app.SettingsOpenAppearanceEditor();
            });
            appearanceActions.Controls.Add(editorButton);
            appearanceActions.Controls.Add(ActionButton(T("Workshop 새로 고침", "Refresh Workshop"), RefreshWorkshop));
            appearanceLayout.SetColumnSpan(appearanceActions, 2);
            appearanceLayout.Controls.Add(appearanceActions, 0, 3);
            appearanceGroup.Controls.Add(appearanceLayout);
            root.Controls.Add(appearanceGroup, 0, 1);

            GroupBox behaviorGroup = new GroupBox { Text = T("프로그램", "Application"), Dock = DockStyle.Fill };
            FlowLayoutPanel behaviorLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            debugCheck = new CheckBox { Text = T("디버그 오버레이", "Debug Overlay"), AutoSize = true, Margin = new Padding(3, 9, 18, 3) };
            debugCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsDebugEnabled = debugCheck.Checked;
            };
            pauseCheck = new CheckBox { Text = T("모든 슬러그캣 일시 정지", "Pause All Slugcats"), AutoSize = true, Margin = new Padding(3, 9, 18, 3) };
            pauseCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsPaused = pauseCheck.Checked;
            };
            audioMuteCheck = new CheckBox
            {
                Text = T("사운드 음소거", "Mute Audio"),
                AutoSize = true,
                Margin = new Padding(3, 9, 18, 3)
            };
            audioMuteCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsAudioMuted = audioMuteCheck.Checked;
            };
            Label audioVolumeLabel = new Label
            {
                Text = T("음량 (최대 200%)", "Volume (up to 200%)"),
                AutoSize = true,
                Margin = new Padding(3, 10, 0, 3)
            };
            audioVolumeTrack = new TrackBar
            {
                Minimum = 0,
                Maximum = 200,
                Value = 100,
                TickFrequency = 25,
                SmallChange = 5,
                LargeChange = 10,
                Width = 190,
                AutoSize = true,
                Margin = new Padding(3, 1, 0, 0)
            };
            audioVolumeValueLabel = new Label
            {
                Text = "100%",
                AutoSize = true,
                MinimumSize = new Size(42, 0),
                Margin = new Padding(3, 10, 12, 3)
            };
            audioVolumeTrack.ValueChanged += delegate
            {
                audioVolumeValueLabel.Text = audioVolumeTrack.Value + "%";
                if (!updating)
                    app.SettingsAudioVolumePercent = audioVolumeTrack.Value;
            };
            audioVolumeTrack.MouseUp += delegate
            { app.SettingsPersistAudioVolume(); };
            audioVolumeTrack.KeyUp += delegate
            { app.SettingsPersistAudioVolume(); };
            audioVolumeTrack.Leave += delegate
            { app.SettingsPersistAudioVolume(); };
            retryButton = ActionButton(T("렌더링 재시도", "Retry Rendering"), delegate { app.SettingsRetryRendering(); RefreshFromApp(); });
            behaviorLayout.Controls.Add(debugCheck);
            behaviorLayout.Controls.Add(pauseCheck);
            behaviorLayout.Controls.Add(audioMuteCheck);
            behaviorLayout.Controls.Add(audioVolumeLabel);
            behaviorLayout.Controls.Add(audioVolumeTrack);
            behaviorLayout.Controls.Add(audioVolumeValueLabel);
            behaviorLayout.Controls.Add(retryButton);
            behaviorLayout.Controls.Add(new Label
            {
                Text = T("언어", "Language"),
                AutoSize = true,
                Margin = new Padding(3, 10, 3, 3)
            });
            languageSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 105,
                Margin = new Padding(3, 6, 3, 3)
            };
            languageSelector.Items.Add(new LanguageChoice(UiLanguage.Korean, "한국어"));
            languageSelector.Items.Add(new LanguageChoice(UiLanguage.English, "English"));
            languageSelector.SelectedIndexChanged += LanguageChanged;
            behaviorLayout.Controls.Add(languageSelector);
            behaviorGroup.Controls.Add(behaviorLayout);
            root.Controls.Add(behaviorGroup, 0, 2);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(statusLabel, 0, 3);

            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };
            footer.Controls.Add(ActionButton(T("닫기", "Close"), delegate { Close(); }));
            footer.Controls.Add(ActionButton(T("프로그램 종료", "Exit Application"), delegate { app.SettingsExitApplication(); }));
            root.Controls.Add(footer, 0, 4);

            Activated += delegate { RefreshFromApp(); };
            RefreshFromApp();
        }

        internal void RefreshFromApp()
        {
            if (IsDisposed) return;
            updating = true;
            try
            {
                RefreshCharacterChoices();
                string[] names = app.SettingsSlugcatNames;
                slugcatList.BeginUpdate();
                try
                {
                    slugcatList.Items.Clear();
                    slugcatList.Items.AddRange(names);
                    int selected = app.SettingsSelectedSlugcatIndex;
                    slugcatList.SelectedIndex = selected >= 0 && selected < names.Length ? selected : -1;
                }
                finally { slugcatList.EndUpdate(); }

                addButton.Enabled = app.SettingsCanAddSlugcat;
                nextButton.Enabled = app.SettingsCanSelectNextSlugcat;
                removeButton.Enabled = app.SettingsCanRemoveSlugcat;
                retryButton.Enabled = app.SettingsCanRetryRendering;
                debugCheck.Checked = app.SettingsDebugEnabled;
                pauseCheck.Checked = app.SettingsPaused;
                audioMuteCheck.Checked = app.SettingsAudioMuted;
                int audioVolume = Math.Max(audioVolumeTrack.Minimum,
                    Math.Min(audioVolumeTrack.Maximum,
                        app.SettingsAudioVolumePercent));
                if (audioVolumeTrack.Value != audioVolume)
                    audioVolumeTrack.Value = audioVolume;
                audioVolumeValueLabel.Text = audioVolume + "%";
                bool canUseSlugpup = app.SettingsCanUseSlugpupAppearance();
                pupAppearanceCheck.Enabled = canUseSlugpup;
                pupAppearanceCheck.Text = canUseSlugpup
                    ? T("슬러그펍", "Slugpup")
                    : T("슬러그펍 (More Slugcats 확장팩 필요)",
                        "Slugpup (More Slugcats Expansion required)");
                pupAppearanceCheck.Checked = canUseSlugpup && app.SettingsIsSlugpupAppearance();
                for (int i = 0; i < languageSelector.Items.Count; i++)
                {
                    LanguageChoice language = languageSelector.Items[i] as LanguageChoice;
                    if (language != null && language.Id == UiLocalization.Current)
                    {
                        languageSelector.SelectedIndex = i;
                        break;
                    }
                }

                for (int i = 0; i < characterSelector.Items.Count; i++)
                {
                    CharacterChoice choice = characterSelector.Items[i] as CharacterChoice;
                    if (choice != null && choice.Id == app.SettingsSlugcatId)
                    {
                        characterSelector.SelectedIndex = i;
                        break;
                    }
                }
                for (int i = 0; i < sizeSelector.Items.Count; i++)
                {
                    SizeChoice choice = sizeSelector.Items[i] as SizeChoice;
                    if (choice != null && choice.Id == app.SettingsSlugcatSize)
                    {
                        sizeSelector.SelectedIndex = i;
                        break;
                    }
                }
                statusLabel.Text = T("실행 중인 슬러그캣: " + names.Length + "마리",
                    names.Length + " active Slugcat" +
                        (names.Length == 1 ? string.Empty : "s"));
            }
            finally { updating = false; }
        }

        private void RefreshCharacterChoices()
        {
            IList<SlugcatProfile> selectable =
                SlugcatProfiles.Selectable(app.SettingsInvUnlocked);
            bool rebuild = characterSelector.Items.Count != selectable.Count;
            if (!rebuild)
            {
                for (int i = 0; i < selectable.Count; i++)
                {
                    CharacterChoice choice = characterSelector.Items[i] as CharacterChoice;
                    if (choice == null || choice.Id != selectable[i].Id)
                    {
                        rebuild = true;
                        break;
                    }
                }
            }
            if (!rebuild) return;

            characterSelector.Items.Clear();
            for (int i = 0; i < selectable.Count; i++)
                characterSelector.Items.Add(new CharacterChoice(selectable[i]));
        }

        private void SlugcatSelectionChanged(object sender, EventArgs e)
        {
            if (updating || slugcatList.SelectedIndex < 0) return;
            app.SettingsSelectSlugcat(slugcatList.SelectedIndex);
            RefreshFromApp();
        }

        private void CharacterChanged(object sender, EventArgs e)
        {
            if (updating) return;
            CharacterChoice choice = characterSelector.SelectedItem as CharacterChoice;
            if (choice == null) return;
            app.SettingsSetSlugcat(choice.Id);
            app.SettingsSynchronizeSlugpupAppearance();
            RefreshFromApp();
        }

        private void SlugcatSizeChanged(object sender, EventArgs e)
        {
            if (updating) return;
            SizeChoice choice = sizeSelector.SelectedItem as SizeChoice;
            if (choice == null) return;
            app.SettingsSetSlugcatSize(choice.Id);
            app.SettingsSynchronizeSlugpupAppearance();
            RefreshFromApp();
        }

        private void SlugpupAppearanceChanged(object sender, EventArgs e)
        {
            if (updating) return;
            app.SettingsSetSlugpupAppearance(pupAppearanceCheck.Checked);
            RefreshFromApp();
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            if (updating) return;
            LanguageChoice choice = languageSelector.SelectedItem as LanguageChoice;
            if (choice == null || choice.Id == UiLocalization.Current) return;
            app.SettingsSetLanguage(choice.Id);
            statusLabel.Text = T(
                "언어 설정을 저장했습니다. 프로그램을 다시 시작하면 모든 UI에 적용됩니다.",
                "Language saved. Restart the application to apply it to the entire UI.");
        }

        private void RefreshWorkshop()
        {
            try
            {
                string status = app.SettingsRefreshWorkshop();
                RefreshFromApp();
                statusLabel.Text = status;
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                statusLabel.Text = T("Workshop 새로 고침 실패: ",
                    "Workshop refresh failed: ") + exception.Message;
            }
        }

        private static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button ActionButton(string text, Action click)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(105, 30) };
            button.Click += delegate { click(); };
            return button;
        }

        private static string T(string korean, string english)
        { return UiLocalization.Text(korean, english); }

        private sealed class CharacterChoice
        {
            public CharacterChoice(SlugcatProfile profile)
            {
                Id = profile.Id;
                Name = SlugcatProfiles.SelectionLabel(profile.Id);
            }
            public readonly SlugcatId Id;
            public readonly string Name;
            public override string ToString()
            { return Name; }
        }

        private sealed class LanguageChoice
        {
            public LanguageChoice(UiLanguage id, string name)
            { Id = id; Name = name; }
            public readonly UiLanguage Id;
            public readonly string Name;
            public override string ToString() { return Name; }
        }

        private sealed class SizeChoice
        {
            public SizeChoice(SlugcatSize id, string name)
            { Id = id; Name = name; }
            public readonly SlugcatSize Id;
            public readonly string Name;
            public override string ToString() { return Name; }
        }
    }

    // Keep the new appearance option settings-only without duplicating a tray
    // action. LayeredOverlayWindow intentionally exposes only user-facing
    // settings primitives, so this bridge resolves its currently selected
    // GameLoop inside the same assembly and synchronizes graphics-only pup
    // geometry when profiles are rebuilt.
    internal static class SlugpupSettingsBridge
    {
        private static readonly FieldInfo SelectedLoopField =
            typeof(LayeredOverlayWindow).GetField("gameLoop",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly List<WeakReference> trackedLoops =
            new List<WeakReference>();
        private static int lastIdleSyncTick;

        static SlugpupSettingsBridge()
        {
            Application.Idle += SynchronizeTrackedLoops;
        }

        internal static bool SettingsIsSlugpupAppearance(
            this LayeredOverlayWindow app)
        {
            GameLoop loop = SelectedLoop(app);
            if (loop == null) return false;
            Track(loop);
            return EnsureSlugpupAvailability(loop) && loop.Slugcat.PupAppearance;
        }

        internal static void SynchronizeRestoredAppearance(GameLoop loop)
        {
            Track(loop);
            EnsureSlugpupAvailability(loop);
            SynchronizeGraphics(loop);
        }

        internal static bool SettingsCanUseSlugpupAppearance(
            this LayeredOverlayWindow app)
        {
            GameLoop loop = SelectedLoop(app);
            if (loop == null) return false;
            Track(loop);
            return EnsureSlugpupAvailability(loop);
        }

        internal static void SettingsSetSlugpupAppearance(
            this LayeredOverlayWindow app, bool enabled)
        {
            GameLoop loop = SelectedLoop(app);
            if (loop == null) return;
            Track(loop);
            loop.Slugcat.SetPupAppearance(EnsureSlugpupAvailability(loop) && enabled);
            SynchronizeGraphics(loop);

            // Re-enter the existing size boundary once so hit testing and the
            // published mouse snapshot immediately see the new collision radii.
            app.SettingsSetSlugcatSize(app.SettingsSlugcatSize);
            SynchronizeGraphics(loop);
        }

        internal static void SettingsSynchronizeSlugpupAppearance(
            this LayeredOverlayWindow app)
        {
            GameLoop loop = SelectedLoop(app);
            if (loop == null) return;
            Track(loop);
            EnsureSlugpupAvailability(loop);
            SynchronizeGraphics(loop);
        }

        private static GameLoop SelectedLoop(LayeredOverlayWindow app)
        {
            if (app == null || SelectedLoopField == null) return null;
            return SelectedLoopField.GetValue(app) as GameLoop;
        }

        private static void Track(GameLoop loop)
        {
            if (loop == null) return;
            for (int i = trackedLoops.Count - 1; i >= 0; i--)
            {
                GameLoop existing = trackedLoops[i].Target as GameLoop;
                if (existing == null)
                {
                    trackedLoops.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(existing, loop)) return;
            }
            trackedLoops.Add(new WeakReference(loop));
        }

        private static void SynchronizeTrackedLoops(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            if (unchecked(now - lastIdleSyncTick) >= 0 &&
                unchecked(now - lastIdleSyncTick) < 250) return;
            lastIdleSyncTick = now;

            for (int i = trackedLoops.Count - 1; i >= 0; i--)
            {
                GameLoop loop = trackedLoops[i].Target as GameLoop;
                if (loop == null)
                {
                    trackedLoops.RemoveAt(i);
                    continue;
                }
                EnsureSlugpupAvailability(loop);
                if (!loop.Slugcat.PupAppearance) continue;
                try { SynchronizeGraphics(loop); }
                catch (ObjectDisposedException) { trackedLoops.RemoveAt(i); }
            }
        }

        private static bool EnsureSlugpupAvailability(GameLoop loop)
        {
            bool available = loop != null && loop.Installation != null &&
                loop.Installation.HasMoreSlugcatsExpansion;
            if (!available && loop != null && loop.Slugcat.PupAppearance)
            {
                // A configuration may survive after the installed game changes.
                // Restore the adult geometry before exposing the disabled control.
                loop.Slugcat.SetPupAppearance(false);
                SynchronizeGraphics(loop);
            }
            return available;
        }

        private static void SynchronizeGraphics(GameLoop loop)
        {
            if (loop == null) return;
            const double scale = 1.0;
            if (loop.Slugcat.Appearance != null &&
                Math.Abs(loop.Slugcat.Appearance.PupScale - scale) > 0.000001)
                loop.Slugcat.Appearance.SetPupScale(scale);

            if (loop.Graphics.Tail != null)
                loop.Graphics.Tail.SetPupGeometry(loop.Slugcat.PupAppearance,
                    loop.Slugcat.BodyChunks[1].Position);

            for (int i = 0; i < loop.Graphics.Arms.Length; i++)
                if (Math.Abs(loop.Graphics.Arms[i].GeometryScale - scale) > 0.000001)
                    loop.Graphics.Arms[i].SetGeometryScale(scale);
        }
    }
}
