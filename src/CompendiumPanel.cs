using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Utangard
{
    /// <summary>
    /// The Utangard page of the compendium, drawn as a panel (LHM-26): a strip of every biome,
    /// the selected biome's name, who it is waiting on, the character's Fighting and Discovery
    /// bars there, and what a locked biome does to you.
    ///
    /// Robbin picked this from three mockups on 2026-09-24, "C - Custom panel", and the mockup is
    /// the spec. Every colour, size and gap below is the mockup's and sits in the mockup's order,
    /// so the two can be read side by side. The two text-only mockups lost because a bar written
    /// out as a number is still a number, and the point of a foothold is seeing how far it has to go.
    ///
    /// <b>It draws inside the compendium's own page rather than beside it.</b> The Utangard entry
    /// in the list is still the text page it always was (GateReport.Page, put there by
    /// UtangardPatches.CompendiumPage). When that entry is the one showing, this covers the text
    /// area and blanks the text. So the list, the selection, gamepad up and down, the close button
    /// and Escape all stay vanilla's. A tab of our own on the compendium bar, the way Rist and Saga
    /// do it, would have been a second door to the same page and a window nothing else closes.
    ///
    /// <b>The text page is the fallback, on purpose.</b> If ShowText cannot be patched, the panel
    /// fails to build, or a refresh throws, the old text page is left showing. It says less but it
    /// says nothing false, and a broken panel must never cost a player the one screen that tells
    /// them why their food vanished.
    ///
    /// <b>Sizes are canvas units, and at 1080p they are the mockup's pixels.</b> GuiScaler sets
    /// the canvas scale to min(width/1920, height/1080) times the GUI scale setting, so at
    /// 1920x1080 and 100% one unit is one screen pixel and the smallest text here, the strip's 12,
    /// is 12 px. A bigger GUI scale grows this with the rest of the game's UI. The one thing that
    /// could still shrink it is the compendium itself sitting under a scaled parent, which lives
    /// in the UI asset and no decompile shows. So Fit measures that scale on the running game,
    /// logs it, and when it is below 1 draws the panel back up to full size. Robbin reads small
    /// text badly, and 12 px on his screen is the floor, not 12 units of somebody's transform.
    ///
    /// <b>Cheap.</b> Built once per compendium, which means once per world. Redrawn when the page
    /// opens, on a click, and once a second while it is showing. Every write is skipped when the
    /// value has not changed, so a panel whose numbers are still costs no layout at all.
    ///
    /// <b>Player tabs (LHM-61).</b> Robbin picked mockup B from two on 2026-10-03, a vertical list of
    /// every active player down the left with two mini bars each, and the biome page on the right
    /// in two rows of four buttons. Your own tab is first and is what the page opens on; picking
    /// another shows their Fighting and Discovery bars in the selected biome and what each has earned
    /// back, read-only. Picking is a click that changes which numbers Draw reads. It never builds
    /// anything: the rows are made once with the panel, a fixed pool of PlayerBars.MostTabs, and
    /// Draw switches them on and off. A panel rebuilt on a click would be the squeeze of 2026-09-29
    /// again, since a fresh layout is the moment every label can be measured at no height.
    ///
    /// His one change to the mockup was a thicker border, which he did not place. Read as: the
    /// panel's own border is 2 px where the mockup draws 1, and the selected player and the
    /// selected biome are 3 where it draws 2. The box and row borders stay 1.
    ///
    /// Your own numbers are read on this client: the gate from the world's keys, the bars from the
    /// kill tally Utangard keeps in the local character and from its minimap. Everyone else's are
    /// read from the world's global keys, which PlayerBars publishes, and which this panel never
    /// writes.
    /// </summary>
    internal static class CompendiumPanel
    {
        // ------------------------------------------------------------ mockup C, as data ---

        private static readonly Color PanelFace = Rgb(0x1f1a14);
        private static readonly Color PanelEdge = Rgb(0x5a4a36);
        private static readonly Color BodyText = Rgb(0xe6dac2);
        private static readonly Color Muted = Rgb(0xa3968a);
        private static readonly Color Orange = Rgb(0xffa500);
        private static readonly Color OpenText = Rgb(0x86c27e);
        private static readonly Color OpenEdge = Rgb(0x5b7a4f);
        private static readonly Color LockedText = Rgb(0xd27e6e);
        private static readonly Color LockedEdge = Rgb(0x6e4a40);
        private static readonly Color BoxEdge = Rgb(0x4a3d2e);
        private static readonly Color Track = Rgb(0x3a3129);
        private static readonly Color FightingFill = Rgb(0xbf854a);
        private static readonly Color DiscoveryFill = Rgb(0x7d9fb8);
        private static readonly Color RowSelected = Rgb(0x2b2318);
        private static readonly Color OfflineDot = Rgb(0x5f554a);

        private const float BodySize = 14f;
        private const float TitleSize = 19f;
        private const float SmallSize = 13f;
        private const float StripSize = 13f;
        private const float CaptionSize = 12f;

        /// <summary>
        /// Border widths in mockup pixels. The mockup has the panel at 1 and the selected player
        /// and biome at 2; Robbin asked for a thicker border on 2026-10-03, so the panel is 2 and
        /// the selected state 3. See the class comment.
        /// </summary>
        private const float PanelEdgeWidth = 2f;
        private const float SelectedEdgeWidth = 3f;

        /// <summary>A player tab's height in the mockup, and the gap between two.</summary>
        private const float RowHeight = 64f;
        private const float RowGap = 6f;

        /// <summary>
        /// Past this many tabs the rows close up so all of them fit in the page's 641 units: no
        /// minimum height, a 4 gap and tighter padding. Seven at full size is 490 of the 550 the
        /// column has under its heading and above its caption, an eighth is not.
        /// </summary>
        private const int RoomyTabs = 7;
        private const float TightGap = 4f;

        /// <summary>
        /// The mockup's line-height. TextMeshPro sets its lines at the font's own spacing, which
        /// for this font is tighter, so the difference is made up twice: half above and half below
        /// every text (Lead), and a line-height tag for the gap between wrapped lines (Wrapped).
        /// That gives each block of text the same height the mockup gives it.
        /// </summary>
        private const float LineHeight = 1.55f;

        /// <summary>How often the numbers are read again while the page is open.</summary>
        private const float RefreshSeconds = 1f;

        // ------------------------------------------------------------------- state -------

        /// <summary>
        /// The Utangard entry in the list the compendium was last filled with, so ShowText can tell
        /// it apart from Logs and the rest. Set by UtangardPatches.CompendiumPage every time the
        /// list is built, and cleared when the page is switched off.
        /// </summary>
        internal static TextsDialog.TextInfo Page;

        private static TextsDialog _dialog;
        private static GameObject _root;

        /// <summary>The rect the compendium's text is shown in, which the panel covers. See Host.</summary>
        private static RectTransform _host;

        /// <summary>The panel's minimum height, which Fit keeps at the host's so the page fills it.</summary>
        private static LayoutElement _floor;

        /// <summary>What Fit last fitted to, so an unchanged compendium costs no layout.</summary>
        private static float _fitScale = -1f;
        private static Vector2 _fitSize;
        private static float _fitPixels = -1f;

        /// <summary>How many screen pixels one of the panel's units covers, measured by Fit. See Edge.</summary>
        private static float _pixelsPerUnit = 1f;

        /// <summary>Each box's face, which sits inside a 1 px edge that Fit redraws. See Edge.</summary>
        private static readonly List<RectTransform> Rims = new List<RectTransform>();

        /// <summary>The panel's own face, inside an edge PanelEdgeWidth wide. Redrawn by Fit with the rims.</summary>
        private static RectTransform _panelFace;

        /// <summary>A compendium the panel already failed in. It keeps the text page from then on.</summary>
        private static TextsDialog _failedFor;

        private static Heightmap.Biome _selected = Heightmap.Biome.None;

        /// <summary>
        /// Whose bars the page shows: a character id, 0 for you. It is resolved against the tabs
        /// every Draw, so a player who has dropped off the list takes the page back to yours.
        /// </summary>
        private static long _viewing;

        private static float _nextRefresh;

        /// <summary>A stripped copy of the compendium's own text, which every label is cloned from.</summary>
        private static TMP_Text _proto;

        /// <summary>One line of this font at size 1, measured once per build. See LineHeight.</summary>
        private static float _naturalPerEm;

        private static readonly List<Cell> Cells = new List<Cell>();
        private static readonly List<PlayerRow> PlayerRows = new List<PlayerRow>();
        private static GameObject _left;
        private static TMP_Text _playersHeading;
        private static TMP_Text _caption;
        private static VerticalLayoutGroup _list;
        private static bool _tight;
        private static GameObject _head;
        private static TMP_Text _viewName;
        private static TMP_Text _viewNote;
        private static Sprite _dot;
        private static TMP_Text _title;
        private static TMP_Text _waiting;
        private static TMP_Text _rules;
        private static GameObject _barsGap;
        private static GameObject _bars;
        private static GameObject _rulesGap;
        private static Box _fighting;
        private static Box _discovery;

        /// <summary>A button drawn as an edge colour with a face inset by the edge width.</summary>
        private class Frame
        {
            public GameObject Go;
            public Image Edge;
            public RectTransform Face;
            public float EdgeWidth = -1f;
        }

        private sealed class Cell : Frame
        {
            public Heightmap.Biome Biome;
            public TMP_Text Label;
        }

        /// <summary>One player tab. Made once, and filled in from whoever PlayerBars lists.</summary>
        private sealed class PlayerRow : Frame
        {
            public long Id;
            public Image FaceImage;
            public Image Dot;
            public TMP_Text Name;
            public TMP_Text Status;
            public GameObject BarsGap;
            public GameObject Bars;
            public RectTransform Fighting;
            public RectTransform Discovery;
            public VerticalLayoutGroup Stack;
            public LayoutElement Size;
        }

        private sealed class Box
        {
            public TMP_Text Percent;
            public RectTransform Fill;
            public TMP_Text State;
        }

        // ---------------------------------------------------------------- the seam -------

        /// <summary>
        /// TextsDialog.ShowText(TextInfo): every way a page gets shown ends here - the compendium
        /// opening on its first entry, a click in the list, gamepad up and down - so one postfix
        /// sees each switch to and away from the Utangard page.
        ///
        /// Private, and overloaded with ShowText(int), so it is found by shape: the ShowText whose
        /// one parameter is a TextInfo. If that ever stops matching, Seams reports the seam broken
        /// and the text page shows instead.
        /// </summary>
        [HarmonyPatch]
        internal static class Show
        {
            [HarmonyTargetMethod]
            private static MethodBase Target()
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(TextsDialog)))
                {
                    if (method.Name != "ShowText") continue;

                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length == 1 && args[0].ParameterType == typeof(TextsDialog.TextInfo))
                        return method;
                }

                UtangardPlugin.Log.LogError(
                    "TextsDialog has no ShowText(TextInfo) any more - the Utangard page in the "
                    + "compendium stays the plain text version.");

                return null;
            }

            /// <param name="__0">The page being shown. Positional, so a rename cannot unseat it.</param>
            [HarmonyPostfix]
            private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo __0)
            {
                Shown(__instance, __0);
            }
        }

        private static void Shown(TextsDialog dialog, TextsDialog.TextInfo text)
        {
            bool ours = Page != null && ReferenceEquals(text, Page)
                && UtangardConfig.ShowCompendiumPanel.Value;
            if (!ours)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            if (dialog == null || ReferenceEquals(dialog, _failedFor)) return;

            try
            {
                if (_root == null || !ReferenceEquals(_dialog, dialog)) Build(dialog);

                // Opens on the frontier every time, because "where is my group stuck" is what
                // somebody opens this page to ask. A biome clicked last time is not remembered.
                _selected = Default();
                _viewing = 0L;

                _root.transform.SetAsLastSibling();
                _root.SetActive(true);
                Fit();
                Draw();

                // Only once the panel has drawn. Anything above that throws leaves the text page
                // showing, which is the fallback.
                dialog.m_textArea.text = "";
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
            }
            catch (Exception e)
            {
                Abandon(dialog, e);
            }
        }

        /// <summary>
        /// Called every frame by the ticker on the panel, so only while the page is on screen.
        /// </summary>
        internal static void Tick()
        {
            if (_root == null || !_root.activeInHierarchy) return;

            try
            {
                // Vanilla's own compendium input is up and down for the list and the right stick
                // for scrolling, so left and right are free to walk the strip.
                int step = 0;
                if (ZInput.IsExclusiveGamepadActive())
                {
                    if (ZInput.GetButtonDown("JoyDPadLeft")) step = -1;
                    else if (ZInput.GetButtonDown("JoyDPadRight")) step = 1;
                }

                if (step != 0)
                {
                    Step(step);
                }
                else
                {
                    if (Time.unscaledTime < _nextRefresh) return;
                }

                _nextRefresh = Time.unscaledTime + RefreshSeconds;

                // Once a second as well, for a window resized or a GUI scale changed while the
                // page is open. It writes nothing unless one of them did change.
                Fit();
                Draw();
            }
            catch (Exception e)
            {
                Abandon(_dialog, e);
            }
        }

        private static void Select(Heightmap.Biome biome)
        {
            if (_root == null) return;

            try
            {
                _selected = biome;
                Draw();
            }
            catch (Exception e)
            {
                Abandon(_dialog, e);
            }
        }

        private static void SelectPlayer(PlayerRow row)
        {
            if (_root == null) return;

            try
            {
                _viewing = row.Id;
                Draw();
            }
            catch (Exception e)
            {
                Abandon(_dialog, e);
            }
        }

        private static void Step(int step)
        {
            int at = -1;
            for (int i = 0; i < Cells.Count; i++)
                if (Cells[i].Biome == _selected) at = i;

            for (int n = 0; n < Cells.Count; n++)
            {
                at += step;
                if (at < 0 || at >= Cells.Count) return;
                if (Cells[at].Go.activeSelf)
                {
                    _selected = Cells[at].Biome;
                    return;
                }
            }
        }

        /// <summary>
        /// The frontier, which is the first biome the group has not earned. With nothing locked,
        /// the biome the character is standing in, so the page still opens on something that is
        /// about them.
        /// </summary>
        private static Heightmap.Biome Default()
        {
            Heightmap.Biome frontier = Foothold.Frontier();
            if (frontier != Heightmap.Biome.None) return frontier;

            Player player = Player.m_localPlayer;
            if (player != null)
            {
                Heightmap.Biome here = player.GetCurrentBiome();
                if (Listed(here)) return here;
            }

            return UtangardConfig.GateableBiomes[0];
        }

        /// <summary>
        /// Whether a biome gets a cell in the strip. Every gateable biome does except the Ocean,
        /// which is on nobody's road and only appears once a server has actually given it a key.
        /// </summary>
        private static bool Listed(Heightmap.Biome biome)
        {
            if (Array.IndexOf(UtangardConfig.GateableBiomes, biome) < 0) return false;
            return biome != Heightmap.Biome.Ocean || UtangardConfig.RequiredKeyFor(biome) != null;
        }

        /// <summary>
        /// Give up on the panel for this compendium, put the text page back, and say why once. A
        /// panel that threw once would throw again every second, and the text page is true.
        /// </summary>
        private static void Abandon(TextsDialog dialog, Exception e)
        {
            _failedFor = dialog;

            UtangardPlugin.Log.LogError(
                "The Utangard compendium panel failed, so the page is back to plain text for this "
                + "world. Nothing else is affected. " + e);

            try
            {
                if (_root != null) Object.Destroy(_root);
                _root = null;

                if (dialog != null && dialog.m_textArea != null && Page != null)
                    dialog.m_textArea.text = Localization.instance != null
                        ? Localization.instance.Localize(Page.m_text)
                        : Page.m_text;
            }
            catch (Exception again)
            {
                UtangardPlugin.Log.LogError("Could not put the Utangard text page back either: " + again);
            }
        }

        // ----------------------------------------------------------------- drawing -------

        /// <summary>
        /// Everything on the panel, read fresh. GateReport says which biomes are open and who a
        /// locked one waits on, the same rows the text page and the spawn log read, so the three
        /// cannot disagree. Foothold says how far this character's bars have got, and PlayerBars
        /// says how far everybody else's have.
        /// </summary>
        private static void Draw()
        {
            List<GateReport.Row> rows = GateReport.Rows();

            // The tabs only exist while footholds do: with them off nothing is published, and a
            // list of players with no bars would be a list of "no data".
            List<PlayerBars.Member> members = UtangardConfig.FootholdEnabled.Value
                ? PlayerBars.Members()
                : new List<PlayerBars.Member>();

            bool tabs = members.Count > 0;
            PlayerBars.Member viewed = tabs ? Viewed(members) : null;
            bool other = viewed != null && !viewed.You;

            SetActive(_left, tabs);
            SetActive(_head, tabs);
            if (tabs) DrawPlayers(members, viewed, rows);

            foreach (Cell cell in Cells)
            {
                bool listed = Listed(cell.Biome);
                SetActive(cell.Go, listed);
                if (!listed) continue;

                GateReport.Row row = RowOf(rows, cell.Biome);
                bool open = row.Key == null || row.Open;
                bool selected = cell.Biome == _selected;

                SetText(cell.Label, GateReport.BiomeName(cell.Biome));
                SetColor(cell.Label, selected ? Orange : open ? OpenText : LockedText);
                SetColor(cell.Edge, selected ? Orange : open ? OpenEdge : LockedEdge);
                SetEdgeWidth(cell, selected ? SelectedEdgeWidth : 1f);
            }

            GateReport.Row chosen = RowOf(rows, _selected);
            bool locked = chosen.Key != null && !chosen.Open;

            if (tabs)
            {
                SetText(_viewName, viewed.Name);
                SetText(_viewNote, other ? "Viewing another player" : "Your own progress");
            }

            SetText(_title, "The " + GateReport.BiomeName(_selected));
            SetText(_waiting, Wrapped(rows.Count == 0
                ? "No world loaded yet."
                : other ? WaitingFor(chosen, viewed) : GateReport.WaitingLine(chosen)));

            // The bars only mean something where the lock is, and only while footholds are on.
            // With footholds off they could never unlock anything, so they are not shown at all.
            bool bars = locked && UtangardConfig.FootholdEnabled.Value;
            SetActive(_barsGap, bars);
            SetActive(_bars, bars);
            if (bars)
            {
                if (other) DrawOtherBars(viewed.Bars, _selected);
                else DrawBars(Foothold.Read(_selected));
            }

            // The last line. On your own page it is what the lock does to you, as this server has
            // it set. On somebody else's it is how old their numbers are, as the mockup has it,
            // since the rules are the same for everybody and the numbers are not. A server that
            // has switched every rule off gets no rules line at all, rather than an empty one
            // holding its gap open under the boxes.
            bool warning = false;
            string tail = other
                ? (bars ? Updated(viewed.Bars) : null)
                : locked ? Rules(bars, out warning) : null;
            bool showTail = !string.IsNullOrEmpty(tail);

            SetActive(_rulesGap, showTail);
            SetActive(_rules.gameObject, showTail);
            if (showTail)
            {
                SetText(_rules, Wrapped(tail));
                SetColor(_rules, warning ? LockedText : Muted);
            }
        }

        /// <summary>
        /// The tab being viewed: the one with the chosen id, else yours. A tab that has gone, a
        /// player who left the list or a character that was never there, takes the page back to
        /// you instead of leaving it on somebody nobody can see.
        /// </summary>
        private static PlayerBars.Member Viewed(List<PlayerBars.Member> members)
        {
            foreach (PlayerBars.Member member in members)
                if (member.Id == _viewing) return member;

            PlayerBars.Member self = members.Find(m => m.You) ?? members[0];
            _viewing = self.Id;
            return self;
        }

        private static void DrawPlayers(List<PlayerBars.Member> members, PlayerBars.Member viewed,
            List<GateReport.Row> rows)
        {
            int shown = Math.Min(members.Count, Math.Min(PlayerBars.MostTabs, PlayerRows.Count));
            SetTight(shown > RoomyTabs);

            SetText(_playersHeading, "Players, last " + Math.Max(1, (int)UtangardConfig.RosterDays.Value) + " days");

            for (int i = 0; i < PlayerRows.Count; i++)
            {
                PlayerRow row = PlayerRows[i];
                SetActive(row.Go, i < shown);
                if (i >= shown) continue;

                PlayerBars.Member member = members[i];
                bool selected = ReferenceEquals(member, viewed);
                row.Id = member.Id;

                SetColor(row.Edge, selected ? Orange : BoxEdge);
                SetEdgeWidth(row, selected ? SelectedEdgeWidth : 1f);
                SetColor(row.FaceImage, selected ? RowSelected : PanelFace);

                SetColor(row.Dot, member.You ? Orange : member.Online ? OpenText : OfflineDot);
                SetText(row.Name, member.Name);
                SetColor(row.Name, selected ? Orange : BodyText);
                SetText(row.Status, member.Bars == null ? "Older build, no data" : PlayerBars.Status(member));

                int fighting = 0;
                int discovery = 0;
                bool any = member.Bars != null && Averages(member.Bars, rows, out fighting, out discovery);
                SetActive(row.BarsGap, any);
                SetActive(row.Bars, any);
                if (!any) continue;

                SetFill(row.Fighting, fighting / 100f);
                SetFill(row.Discovery, discovery / 100f);
            }

            int hidden = members.Count - shown;
            SetText(_caption, Wrapped("Bars: Fighting, Discovery, averaged over locked biomes"
                + (hidden > 0 ? ". " + hidden + " more not shown." : "")));
        }

        /// <summary>
        /// A tab's two mini bars: each bar's average over the biomes this page calls locked, which
        /// are the ones whose bars mean anything. False when none of them has a number, which draws
        /// the tab with no bars rather than two empty ones that read as "has done nothing".
        /// </summary>
        private static bool Averages(PlayerBars.Bars bars, List<GateReport.Row> rows, out int fighting, out int discovery)
        {
            int sumF = 0, countF = 0, sumD = 0, countD = 0;

            for (int i = 0; i < UtangardConfig.GateableBiomes.Length; i++)
            {
                GateReport.Row row = RowOf(rows, UtangardConfig.GateableBiomes[i]);
                if (row.Key == null || row.Open) continue;

                if (bars.Fighting[i] >= 0) { sumF += bars.Fighting[i]; countF++; }
                if (bars.Discovery[i] >= 0) { sumD += bars.Discovery[i]; countD++; }
            }

            fighting = countF > 0 ? sumF / countF : 0;
            discovery = countD > 0 ? sumD / countD : 0;
            return countF > 0 || countD > 0;
        }

        /// <summary>
        /// The line under the title when somebody else is being viewed: whether they have the boss
        /// that opens this biome, which is the question their tab can answer and yours already
        /// does. An open biome, and a server that gates on the world's own keys, have no
        /// per-player answer, so they say what the group page says.
        /// </summary>
        private static string WaitingFor(GateReport.Row row, PlayerBars.Member viewed)
        {
            bool group = row.Key != null && !row.Open && UtangardConfig.GateOnGroup.Value;
            if (!group) return GateReport.WaitingLine(row);

            string boss = GateReport.BossName(row.Key);
            return Progression.HasDone(viewed.Id, row.Key)
                ? boss + " is down for " + viewed.Name + "."
                : boss + " is not down for " + viewed.Name + " yet.";
        }

        /// <summary>How old somebody else's numbers are, which is also the last time one moved.</summary>
        private static string Updated(PlayerBars.Bars bars)
        {
            if (bars == null) return "Percentages only.";

            long seconds = Math.Max(0L, Progression.Now() - bars.Minutes * 60L);
            return "Updated " + GateReport.Span(seconds) + " ago. Percentages only.";
        }

        /// <summary>
        /// Somebody else's two boxes, from the numbers they published. The same words as your own
        /// boxes, from the same rules: eating is back at EatAtFightingPercent, healing when both are
        /// full. A bar they never published, or could not read, is a question mark and not a zero.
        /// </summary>
        private static void DrawOtherBars(PlayerBars.Bars bars, Heightmap.Biome biome)
        {
            int slot = PlayerBars.SlotOf(biome);
            int fighting = bars != null && slot >= 0 ? bars.Fighting[slot] : PlayerBars.None;
            int discovery = bars != null && slot >= 0 ? bars.Discovery[slot] : PlayerBars.None;
            string nothing = bars == null ? "No data from this player" : "No data yet";

            if (fighting >= 0)
            {
                SetText(_fighting.Percent, fighting + "%");
                SetFill(_fighting.Fill, fighting / 100f);

                if (!UtangardConfig.BlockEating.Value || fighting >= Foothold.EatAtPercent())
                    SetState(_fighting.State, "Eating allowed", true);
                else if (Foothold.EatAtPercent() > 100)
                    SetState(_fighting.State, "No eating here", false);
                else
                    SetState(_fighting.State, "Eating at " + Foothold.EatAtPercent() + "%", false);
            }
            else
            {
                SetText(_fighting.Percent, "?");
                SetFill(_fighting.Fill, 0f);
                SetState(_fighting.State, nothing, false);
            }

            if (discovery >= 0)
            {
                SetText(_discovery.Percent, discovery + "%");
                SetFill(_discovery.Fill, discovery / 100f);

                if (UtangardConfig.HealthRegenMultiplier.Value >= 1f || (fighting >= 100 && discovery >= 100))
                    SetState(_discovery.State, "Healing allowed", true);
                else
                    SetState(_discovery.State, "Healing needs both full", false);
            }
            else
            {
                SetText(_discovery.Percent, "?");
                SetFill(_discovery.Fill, 0f);
                SetState(_discovery.State, nothing, false);
            }
        }

        /// <summary>
        /// Closes the rows up for a long list, and opens them again for a short one. Only on a
        /// change, since it rewrites every row's padding and so dirties each one's layout.
        /// </summary>
        private static void SetTight(bool tight)
        {
            if (_tight == tight) return;
            _tight = tight;

            _list.spacing = tight ? TightGap : RowGap;
            foreach (PlayerRow row in PlayerRows)
            {
                row.Size.minHeight = tight ? 0f : RowHeight;
                row.Stack.padding = RowPadding(tight);
            }
        }

        private static RectOffset RowPadding(bool tight)
        {
            return tight ? new RectOffset(12, 12, 3, 2) : new RectOffset(12, 12, 7, 6);
        }

        private static void DrawBars(Foothold.Standing s)
        {
            // Fighting. In percent of a full bar, rounded down the way the console and the refused
            // meal round it, since the bar stopped being 100 points: a bar at 74 of 150 says 49%,
            // and "Eating at 50%" beside it is then exactly true. The fill is drawn from the same
            // rounded number, so the bar and the figure over it cannot disagree.
            int fighting = s.FightingAvailable ? s.FightingPercent : 0;
            SetText(_fighting.Percent, s.FightingAvailable ? fighting + "%" : "?");
            SetFill(_fighting.Fill, fighting / 100f);

            if (!UtangardConfig.BlockEating.Value || s.CanEat)
                SetState(_fighting.State, "Eating allowed", true);
            else if (!s.FightingAvailable)
                SetState(_fighting.State, "Kills are not being counted", false);
            else if (s.EatAtPercent > 100)
                SetState(_fighting.State, "No eating here", false);
            else
                SetState(_fighting.State, "Eating at " + s.EatAtPercent + "%", false);

            // Discovery. Rounded down, so the bar never reads 100% a pixel short of full.
            int percent = s.DiscoveryAvailable ? Mathf.FloorToInt(s.DiscoveryPercent) : 0;
            SetText(_discovery.Percent, s.DiscoveryAvailable ? percent + "%" : "?");
            SetFill(_discovery.Fill, percent / 100f);

            // A HealthRegenMultiplier of 1 or more means the lock never slowed healing, so there
            // is nothing to earn back and saying "needs both full" would be a lie.
            if (UtangardConfig.HealthRegenMultiplier.Value >= 1f || s.CanHeal)
                SetState(_discovery.State, "Healing allowed", true);
            else if (!s.DiscoveryAvailable)
                SetState(_discovery.State, "The map cannot be read", false);
            else if (s.DiscoveryCounting)
                SetState(_discovery.State, "Still reading your map", false);
            else
                SetState(_discovery.State, "Healing needs both full", false);
        }

        /// <summary>The last line: what the lock does to you, as this server has it set.</summary>
        /// <param name="warning">The line is the not-enforcing warning, which is drawn in red.</param>
        private static string Rules(bool bars, out bool warning)
        {
            // Said in red in place of the rules when the mod has stopped enforcing them, because
            // the rules line would otherwise describe a punishment nobody is receiving. The text
            // page leads with the same warning, for the same reason.
            warning = !Seams.PenaltyIsEscapable();
            if (warning)
                return "Nothing is enforced right now. A game update moved the code that records "
                    + "boss kills, so the mod has stopped until it is updated.";

            // Eating is in the Fighting box when the bars are showing. Without them it goes back
            // in the rules, or the panel would not mention the harshest rule at all.
            return GateReport.RulesLine(!bars && UtangardConfig.BlockEating.Value);
        }

        private static GateReport.Row RowOf(List<GateReport.Row> rows, Heightmap.Biome biome)
        {
            foreach (GateReport.Row row in rows)
                if (row.Biome == biome) return row;

            return new GateReport.Row { Biome = biome, Open = true, SecondsLeft = -1L };
        }

        private static void SetState(TMP_Text label, string text, bool good)
        {
            SetText(label, text);
            SetColor(label, good ? OpenText : Muted);
        }

        /// <summary>
        /// A line that may wrap, with the mockup's line pitch and its text taken literally. The
        /// waiting line carries player names, and a name is not markup.
        /// </summary>
        private static string Wrapped(string text)
        {
            return "<line-height=" + LineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "em><noparse>" + text + "</noparse>";
        }

        // Each of these writes only on a change. A TextMeshPro text or a RectTransform written
        // with its own value still dirties the layout, and this runs every second.

        private static void SetText(TMP_Text label, string text)
        {
            if (label.text != text) label.text = text;
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic.color != color) graphic.color = color;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }

        private static void SetFill(RectTransform fill, float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (!Mathf.Approximately(fill.anchorMax.x, amount)) fill.anchorMax = new Vector2(amount, 1f);
        }

        /// <summary>
        /// The selected cell's edge is SelectedEdgeWidth and the rest 1. Kept by what it came
        /// to in units, so the Draw after a Fit that changed the pixel size redraws it.
        /// </summary>
        private static void SetEdgeWidth(Frame frame, float width)
        {
            float edge = Edge(width);
            if (Mathf.Approximately(frame.EdgeWidth, edge)) return;
            frame.EdgeWidth = edge;
            Inset(frame.Face, edge);
        }

        /// <summary>
        /// An edge the mockup draws this many pixels wide, in panel units: a whole number of screen
        /// pixels, never less than one.
        ///
        /// It used to be the width in units, and a unit is a pixel only at a canvas scale of 1.
        /// Robbin's screenshot of 2026-09-29 has 0.934 screen pixels to a unit, and the GPU fills a
        /// pixel only when the pixel's centre is inside the shape, so a 1-unit line that lands
        /// between two centres is not drawn at all. That is what took the right edge off Meadows,
        /// Deep North and the Discovery box while every other edge showed: the centre rule at
        /// 0.934 a unit, from the panel's position in that screenshot, misses exactly those three
        /// and no others. Nothing was clipped; the panel's own right edge is in the shot. A width of
        /// N whole pixels covers exactly N centres wherever it lands, so every edge draws, and all
        /// at one width, where a 1-unit line at 1.33 a unit (1440p) would come out 1 or 2 px.
        /// </summary>
        private static float Edge(float width)
        {
            float pixels = Mathf.Max(1f, Mathf.Round(width * _pixelsPerUnit));
            return pixels / _pixelsPerUnit;
        }

        /// <summary>A face inside a 1 px edge, which Fit redraws whenever the pixel size changes.</summary>
        private static void Rim(RectTransform face)
        {
            Rims.Add(face);
            Inset(face, Edge(1f));
        }

        // ---------------------------------------------------------------- building -------

        /// <summary>
        /// Builds the panel over the text area of this compendium.
        ///
        /// Everything is made under an inactive root, so nothing cloned runs its Awake until it has
        /// been stripped. The labels are clones of the compendium's own text rather than new
        /// TextMeshPro objects: that is how they wear the game's font and material without this
        /// mod naming either, and it keeps working if an update changes the font.
        /// </summary>
        private static void Build(TextsDialog dialog)
        {
            Forget();

            TMP_Text donor = dialog.m_textArea;
            if (donor == null) throw new InvalidOperationException("TextsDialog.m_textArea is not set.");

            string passed;
            RectTransform host = Host(dialog, donor, out passed);

            var root = new GameObject("Utangard_Panel", typeof(RectTransform));
            root.SetActive(false);

            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(host, false);

            // Laid out by nobody but itself, whatever the host turns out to carry. Its minimum
            // height is the page's floor, which Fit keeps at the host's: see Fit. ignoreLayout
            // only keeps it out of a layout group above; the root's own fitter still reads it.
            _floor = root.AddComponent<LayoutElement>();
            _floor.ignoreLayout = true;

            _root = root;
            _dialog = dialog;
            _host = host;

            // The panel: an edge in #5a4a36 around a #1f1a14 face, PanelEdgeWidth wide. An Image
            // with no sprite is a flat rectangle, so the edge is the root's colour showing round a
            // face inset by it. Raycast on, so a click on the panel does not fall through to
            // whatever is under it.
            Paint(rootRect, PanelEdge, true);
            RectTransform face = Child("Face", rootRect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _panelFace = face;
            Inset(face, Edge(PanelEdgeWidth));
            Paint(face, PanelFace, false);

            // As tall as what it holds, and never squeezed: see Fit. The body below is the one
            // child this column lays out, the edge in from the edge on every side.
            VerticalLayoutGroup frame = root.AddComponent<VerticalLayoutGroup>();
            int edge = Mathf.CeilToInt(PanelEdgeWidth);
            frame.padding = new RectOffset(edge, edge, edge, edge);
            Stack(frame, false);
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The label everything is cloned from, stripped and kept switched off.
            GameObject proto = Object.Instantiate(donor.gameObject, rootRect);
            proto.name = "Utangard_Label";
            Strip(proto);
            proto.SetActive(false);

            _proto = proto.GetComponent<TMP_Text>();
            if (_proto == null) throw new InvalidOperationException("The compendium's text carried no TextMeshPro component.");
            Prime(_proto);

            // Measured with the root switched on, since an inactive label has not loaded its font.
            root.SetActive(true);
            _naturalPerEm = Measure(rootRect);

            root.AddComponent<CompendiumPanelTicker>();

            RectTransform body = Child("Body", rootRect);

            // Exactly as tall as what it holds, with the floor's spare height left under it. The
            // strip and the pair of boxes each report a flexible height of 1, because each row
            // stretches its cells to one height, and a column hands its spare height to whatever
            // is flexible. Without this zero the frame would pass that height into the body and
            // the body would share it out between those two rows: biome buttons and boxes a
            // hundred or more units taller than their text on a page with little to say.
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 0f;
            VerticalLayoutGroup column = body.gameObject.AddComponent<VerticalLayoutGroup>();

            // The mockup's panel pads 12 above and 14 either side and below. It is inset 14 from
            // the page there and fills the compendium's text area here, so the sides take that 14
            // as well, and the 1 px edge it has inside the padding is the panel's own, above.
            column.padding = new RectOffset(28, 28, 12, 14);
            Stack(column, false);

            _fitScale = -1f;
            Fit();

            // Two columns: the players, 200 wide, and the biome page beside them, 14 apart.
            RectTransform columns = Child("Columns", body);
            HorizontalLayoutGroup split = columns.gameObject.AddComponent<HorizontalLayoutGroup>();
            split.spacing = 14f;
            Row(split, false);

            // ---- the players, down the left

            RectTransform left = Child("Players", columns);
            LayoutElement leftSize = left.gameObject.AddComponent<LayoutElement>();
            leftSize.minWidth = 200f;
            leftSize.preferredWidth = 200f;
            leftSize.flexibleWidth = 0f;
            leftSize.flexibleHeight = 0f;
            Stack(left.gameObject.AddComponent<VerticalLayoutGroup>(), false);
            _left = left.gameObject;

            _playersHeading = Label(left, SmallSize, Muted, TextAlignmentOptions.TopLeft);
            Gap(left, 6f);

            RectTransform list = Child("List", left);
            _list = list.gameObject.AddComponent<VerticalLayoutGroup>();
            Stack(_list, false);
            _list.spacing = RowGap;

            int sounded = 0;
            for (int i = 0; i < PlayerBars.MostTabs; i++)
            {
                PlayerRow tab = MakeRow(list);
                if (Sound(tab.Go, dialog)) sounded++;
                PlayerRows.Add(tab);
            }

            Gap(left, RowGap);
            _caption = Label(left, CaptionSize, Muted, TextAlignmentOptions.TopLeft);

            // ---- the biome page, on the right

            // No flexible height of its own, for the reason the body has none: the strip and the
            // pair of boxes inside it each report one, and a column stretched to the players'
            // height would share the spare out between them. The players are the taller side.
            RectTransform right = Child("Biome", columns);
            LayoutElement rightSize = right.gameObject.AddComponent<LayoutElement>();
            rightSize.minWidth = 0f;
            rightSize.preferredWidth = 0f;
            rightSize.flexibleWidth = 1f;
            rightSize.flexibleHeight = 0f;
            Stack(right.gameObject.AddComponent<VerticalLayoutGroup>(), false);

            // 1. Whose page it is, and who is the page's name. The name takes the line and the note
            // sits at its right, on the same baseline.
            RectTransform head = Child("Head", right);
            HorizontalLayoutGroup who = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            who.spacing = 8f;
            Row(who, false);
            who.childAlignment = TextAnchor.LowerLeft;
            _head = head.gameObject;

            _viewName = Label(head, TitleSize, Orange, TextAlignmentOptions.TopLeft);
            Bold(_viewName);
            _viewName.textWrappingMode = TextWrappingModes.NoWrap;
            _viewName.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement nameGrow = _viewName.gameObject.AddComponent<LayoutElement>();
            nameGrow.minWidth = 0f;
            nameGrow.flexibleWidth = 1f;

            _viewNote = Label(head, SmallSize, Muted, TextAlignmentOptions.TopRight);
            _viewNote.textWrappingMode = TextWrappingModes.NoWrap;

            Gap(right, 8f);

            // 2. The biome strip: four across in two rows, cells 5 apart both ways. The Ocean, which
            // only has a button once a server has given it a key, joins the second row as a fifth.
            RectTransform strip = Child("Biomes", right);
            VerticalLayoutGroup lines = strip.gameObject.AddComponent<VerticalLayoutGroup>();
            Stack(lines, false);
            lines.spacing = 5f;

            RectTransform[] across = new RectTransform[2];
            for (int i = 0; i < across.Length; i++)
            {
                across[i] = Child("Row" + (i + 1), strip);
                HorizontalLayoutGroup row = across[i].gameObject.AddComponent<HorizontalLayoutGroup>();
                row.spacing = 5f;
                Row(row, true);
            }

            for (int i = 0; i < UtangardConfig.GateableBiomes.Length; i++)
            {
                Cell cell = MakeCell(across[Math.Min(1, i / 4)], UtangardConfig.GateableBiomes[i]);
                if (Sound(cell.Go, dialog)) sounded++;
                Cells.Add(cell);
            }

            Gap(right, 12f);

            // 3. The biome's title, 2 below it.
            _title = Label(right, TitleSize, Orange, TextAlignmentOptions.TopLeft);
            Bold(_title);
            Gap(right, 2f);

            // 4. Who it waits on, or that it is open.
            _waiting = Label(right, SmallSize, Muted, TextAlignmentOptions.TopLeft);

            // 5. The two boxes, 8 below the line above and 12 apart.
            _barsGap = Gap(right, 8f);
            RectTransform bars = Child("Bars", right);
            HorizontalLayoutGroup pair = bars.gameObject.AddComponent<HorizontalLayoutGroup>();
            pair.spacing = 12f;
            Row(pair, true);
            _bars = bars.gameObject;

            _fighting = MakeBox(bars, "Fighting", FightingFill);
            _discovery = MakeBox(bars, "Discovery", DiscoveryFill);

            // 6. The last line, 14 below: the rules on your own page, how old the numbers are on
            // somebody else's.
            _rulesGap = Gap(right, 14f);
            _rules = Label(right, SmallSize, Muted, TextAlignmentOptions.TopLeft);

            // Every number here is one nothing but the running game can answer, and each is the
            // first thing to read if the panel looks wrong: the area it covers and the rects it
            // climbed past to find one the text does not size (see Host), the scale that area
            // sits at (below 1 the panel is being drawn back up), how many screen pixels a unit
            // covers (which the edges are rounded to, see Edge), the size the compendium's own
            // text uses, and whether the biome buttons found vanilla's sounds.
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Rect area = host.rect;
            UtangardPlugin.Log.LogInfo("Compendium panel built over '" + host.name + "'"
                + (passed.Length > 0 ? " (past " + passed + ", sized by what is in it)" : "") + ", "
                + Mathf.RoundToInt(area.width) + " x " + Mathf.RoundToInt(area.height)
                + " at scale " + _fitScale.ToString("0.###", inv)
                + ", " + _pixelsPerUnit.ToString("0.###", inv) + " screen pixels to a unit"
                + ", the compendium's own text at size " + donor.fontSize.ToString("0.#", inv)
                + ", one line of text at " + _naturalPerEm.ToString("0.00", inv) + " em, "
                + sounded + " of " + (Cells.Count + PlayerRows.Count)
                + " biome and player buttons with vanilla's click sound.");
        }

        /// <summary>
        /// Keeps the panel over the area the compendium's text would fill: as wide as it, from
        /// its top, and at least as tall as it.
        ///
        /// <b>The height is the panel's own.</b> A ContentSizeFitter on the root makes it as tall
        /// as what it holds, and this only sets the floor under that, on the root's own
        /// LayoutElement, so the dark page still fills the text area when there is less to say.
        /// The floor is on the root and not the body so that the body is never taller than its
        /// content: a body stretched to the floor shares the spare height out to the strip and
        /// the boxes (see Build), which is the same fault the other way up. It used to be
        /// the other way round, the panel stretched to its host and the columns inside fitted to
        /// that, and a column squeezed shorter than its content hands each child its MINIMUM
        /// height, which for a TextMeshPro label is 0 (TMP_Text never sets m_minHeight). So when
        /// the host shrank (see Host), every text collapsed to no height at all and was drawn
        /// over its neighbour: each biome button came out as its own 4-unit edge, which reads as
        /// a line struck through the name, the title and the waiting line landed on one line, and
        /// each box's heading sat under its bar, the bar's 12-unit LayoutElement being the one
        /// minimum that held. Robbin's screenshot of 2026-09-29. Grown from its content, no
        /// label can get less than its text needs, whatever it is drawn over.
        ///
        /// At a scale of 1 or more it is drawn as it is. Below 1 the compendium sits under a
        /// parent that shrinks it, and the panel is drawn at 1/scale instead, with its width and
        /// floor shrunk by the same factor so it still covers the same area on screen. Its text
        /// then comes out at the mockup's pixel sizes at 1080p, which is the floor this page
        /// promises. A compendium scaled up is left alone: its text is only bigger, and bigger is
        /// readable.
        ///
        /// It also measures how many screen pixels one of the panel's units covers, and redraws
        /// the edges to whole pixels of it: see Edge. A window resized changes that without
        /// changing the host's size in units, so it is checked here too.
        /// </summary>
        private static void Fit()
        {
            if (_root == null || _host == null || _floor == null) return;

            float scale = HostScale(_host);
            Vector2 size = _host.rect.size;
            float pixels = PixelsPerUnit(_host);
            if (Mathf.Approximately(scale, _fitScale) && size == _fitSize
                && Mathf.Approximately(pixels, _fitPixels)) return;

            _fitScale = scale;
            _fitSize = size;
            _fitPixels = pixels;

            float drawn = scale >= 0.99f ? 1f : scale;
            float grow = 1f / drawn;

            var rect = (RectTransform)_root.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = new Vector3(grow, grow, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x * drawn);

            _floor.minHeight = Mathf.Max(0f, size.y * drawn);

            // The panel sits straight under the host at the scale set above. The cells follow in
            // the Draw that always comes after a Fit.
            _pixelsPerUnit = pixels * grow;
            foreach (RectTransform rim in Rims) Inset(rim, Edge(1f));
            if (_panelFace != null) Inset(_panelFace, Edge(PanelEdgeWidth));
        }

        /// <summary>
        /// How many screen pixels one of this rect's units covers: the root canvas's scale
        /// factor, which GuiScaler sets and which is pixels per canvas unit, times whatever the
        /// rect's parents add to it. A reading that cannot be right counts as 1.
        /// </summary>
        private static float PixelsPerUnit(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return 1f;

            Canvas top = canvas.rootCanvas;
            float canvasScale = top.transform.lossyScale.x;
            if (canvasScale <= 0f || top.scaleFactor <= 0f) return 1f;

            float perUnit = top.scaleFactor * rect.lossyScale.x / canvasScale;
            return perUnit > 0.1f && perUnit < 20f ? perUnit : 1f;
        }

        /// <summary>
        /// How big one of the host's units is against one unit of the canvas it is drawn on. The
        /// canvas's own scale is GuiScaler's, which is what already makes a unit a pixel at 1080p,
        /// so dividing it out leaves only what the compendium's own parents add. A reading that
        /// cannot be right counts as 1, which is the panel as it was designed.
        /// </summary>
        private static float HostScale(RectTransform host)
        {
            Canvas canvas = host.GetComponentInParent<Canvas>();
            if (canvas == null) return 1f;

            float canvasScale = canvas.rootCanvas.transform.lossyScale.x;
            if (canvasScale <= 0f) return 1f;

            float scale = host.lossyScale.x / canvasScale;
            return scale > 0.2f && scale < 5f ? scale : 1f;
        }

        /// <summary>
        /// The click and hover sounds of the compendium's own list, on a biome button. Copied off
        /// the list's row prefab rather than named, so they are whatever vanilla plays there.
        ///
        /// A method of its own inside a try, because ButtonSfx lives in assembly_guiutils: if an
        /// update renamed it, the failure has to cost the sound and never the panel.
        /// </summary>
        private static bool Sound(GameObject button, TextsDialog dialog)
        {
            try
            {
                return CopySound(button, dialog);
            }
            catch (Exception e)
            {
                UtangardPlugin.Log.LogWarning("Compendium panel: the biome buttons stay silent. " + e.Message);
                return false;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CopySound(GameObject button, TextsDialog dialog)
        {
            if (dialog.m_elementPrefab == null) return false;

            ButtonSfx donor = dialog.m_elementPrefab.GetComponentInChildren<ButtonSfx>(true);
            if (donor == null) return false;

            // Added after the Button, because ButtonSfx looks for it once, in Awake, and the panel
            // is switched on by now so Awake runs inside this AddComponent.
            ButtonSfx sfx = button.AddComponent<ButtonSfx>();
            sfx.m_sfxPrefab = donor.m_sfxPrefab;
            sfx.m_sfxPrefabVibrationOnly = donor.m_sfxPrefabVibrationOnly;
            sfx.m_selectSfxPrefab = donor.m_selectSfxPrefab;
            sfx.m_selectSfxPrefabVibrationOnly = donor.m_selectSfxPrefabVibrationOnly;
            sfx.m_enterSfxPrefab = donor.m_enterSfxPrefab;
            sfx.m_enterSfxPrefabVibrationOnly = donor.m_enterSfxPrefabVibrationOnly;
            return true;
        }

        /// <summary>
        /// One biome in the strip. A cell is its edge colour with a face inset by the edge width,
        /// and a centred label; the padding leaves room for the selected cell's 3 px edge, so
        /// selecting one does not shift its neighbours. 30 high and one line, as the mockup draws
        /// it: the height is the cell's own and the label inside is given what is left, which is
        /// more than a line of text needs, so nothing in it can be squeezed.
        /// </summary>
        private static Cell MakeCell(RectTransform strip, Heightmap.Biome biome)
        {
            RectTransform rect = Child("Biome_" + biome, strip);
            Image edge = Paint(rect, LockedEdge, true);

            // Equal widths: nothing preferred, everything flexible, so the row shares out evenly
            // whatever the names are. A name too long for its cell wraps inside it.
            LayoutElement share = rect.gameObject.AddComponent<LayoutElement>();
            share.minWidth = 0f;
            share.preferredWidth = 0f;
            share.flexibleWidth = 1f;
            share.minHeight = 30f;
            share.preferredHeight = 30f;

            int pad = Mathf.CeilToInt(SelectedEdgeWidth);
            VerticalLayoutGroup inner = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            inner.padding = new RectOffset(pad, pad, pad, pad);
            Stack(inner, true);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Paint(face, PanelFace, false);

            TMP_Text label = Label(rect, StripSize, OpenText, TextAlignmentOptions.Center);
            float lead = Lead(StripSize);
            label.margin = new Vector4(2f, lead, 2f, lead);
            Bold(label);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = edge;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            Heightmap.Biome chosen = biome;
            button.onClick.AddListener(() => Select(chosen));

            var cell = new Cell { Biome = biome, Go = rect.gameObject, Edge = edge, Face = face, Label = label };
            SetEdgeWidth(cell, 1f);
            return cell;
        }

        /// <summary>
        /// A Fighting or Discovery box: 1 px #4a3d2e edge, 10 px padding (8 below), a bold heading
        /// with the percent at the right, a 10 px bar with 8 px either side, and the unlock line.
        /// </summary>
        private static Box MakeBox(RectTransform parent, string name, Color fill)
        {
            RectTransform rect = Child(name, parent);
            Paint(rect, BoxEdge, false);

            // Two columns of equal width, as the mockup's grid of 1fr 1fr.
            LayoutElement share = rect.gameObject.AddComponent<LayoutElement>();
            share.minWidth = 0f;
            share.preferredWidth = 0f;
            share.flexibleWidth = 1f;

            VerticalLayoutGroup stack = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(11, 11, 11, 9);   // 10 and 8 of padding inside a 1 px edge
            Stack(stack, false);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rim(face);
            Paint(face, PanelFace, false);

            RectTransform head = Child("Head", rect);
            HorizontalLayoutGroup line = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 8f;
            Row(line, false);

            TMP_Text heading = Label(head, SmallSize, BodyText, TextAlignmentOptions.TopLeft);
            heading.text = name;
            Bold(heading);
            LayoutElement grow = heading.gameObject.AddComponent<LayoutElement>();
            grow.minWidth = 0f;
            grow.flexibleWidth = 1f;

            TMP_Text percent = Label(head, SmallSize, BodyText, TextAlignmentOptions.TopRight);
            Bold(percent);
            percent.textWrappingMode = TextWrappingModes.NoWrap;

            Gap(rect, 8f);

            RectTransform track = Child("Track", rect);
            Paint(track, Track, false);
            LayoutElement height = track.gameObject.AddComponent<LayoutElement>();
            height.minHeight = 10f;
            height.preferredHeight = 10f;

            // The fill is the track's width times the percent, by its right anchor.
            RectTransform bar = Child("Fill", track);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            Paint(bar, fill, false);

            Gap(rect, 8f);

            TMP_Text state = Label(rect, SmallSize, Muted, TextAlignmentOptions.TopLeft);

            return new Box { Percent = percent, Fill = bar, State = state };
        }

        /// <summary>
        /// One player tab: the mockup's 64 high card with a coloured dot and the name in bold, who
        /// they are to you in 13, and two 5 high bars side by side. Made once, with the panel, and
        /// only ever switched on and off and rewritten, never rebuilt, so choosing a player cannot
        /// reach a fresh layout (see the class comment).
        ///
        /// The card is its own vertical column with a minimum height, not a fixed one. A column
        /// squeezed below what it holds hands each label its minimum height, which is 0, and that
        /// is the fault the whole panel was rebuilt around. At 64 the content, 51 units of text
        /// and bar, fits with room; in the tight list there is no minimum and the card is as
        /// tall as what it holds.
        /// </summary>
        private static PlayerRow MakeRow(RectTransform list)
        {
            RectTransform rect = Child("Player", list);
            Image edge = Paint(rect, BoxEdge, true);

            var row = new PlayerRow { Go = rect.gameObject, Edge = edge };

            row.Size = rect.gameObject.AddComponent<LayoutElement>();
            row.Size.minHeight = RowHeight;

            row.Stack = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            row.Stack.padding = RowPadding(false);
            Stack(row.Stack, false);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            row.Face = face;
            row.FaceImage = Paint(face, PanelFace, false);

            RectTransform line = Child("Who", rect);
            HorizontalLayoutGroup who = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            who.spacing = 7f;
            Row(who, false);
            who.childAlignment = TextAnchor.MiddleLeft;

            RectTransform dot = Child("Dot", line);
            row.Dot = Paint(dot, OfflineDot, false);
            row.Dot.sprite = DotSprite();
            LayoutElement dotSize = dot.gameObject.AddComponent<LayoutElement>();
            dotSize.minWidth = 10f;
            dotSize.preferredWidth = 10f;
            dotSize.minHeight = 10f;
            dotSize.preferredHeight = 10f;

            row.Name = Label(line, BodySize, BodyText, TextAlignmentOptions.TopLeft);
            Bold(row.Name);
            row.Name.textWrappingMode = TextWrappingModes.NoWrap;
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement nameGrow = row.Name.gameObject.AddComponent<LayoutElement>();
            nameGrow.minWidth = 0f;
            nameGrow.flexibleWidth = 1f;

            row.Status = Label(rect, SmallSize, Muted, TextAlignmentOptions.TopLeft);
            row.Status.textWrappingMode = TextWrappingModes.NoWrap;
            row.Status.overflowMode = TextOverflowModes.Ellipsis;

            row.BarsGap = Gap(rect, 4f);

            RectTransform bars = Child("Bars", rect);
            HorizontalLayoutGroup pair = bars.gameObject.AddComponent<HorizontalLayoutGroup>();
            pair.spacing = 4f;
            Row(pair, true);
            row.Bars = bars.gameObject;

            row.Fighting = MiniBar(bars, FightingFill);
            row.Discovery = MiniBar(bars, DiscoveryFill);

            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = edge;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => SelectPlayer(row));

            SetEdgeWidth(row, 1f);
            return row;
        }

        /// <summary>A 5 high track in a tab, half the card wide, and the fill that goes in it.</summary>
        private static RectTransform MiniBar(RectTransform parent, Color fill)
        {
            RectTransform track = Child("Track", parent);
            Paint(track, Track, false);

            LayoutElement size = track.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 0f;
            size.preferredWidth = 0f;
            size.flexibleWidth = 1f;
            size.minHeight = 5f;
            size.preferredHeight = 5f;

            RectTransform bar = Child("Fill", track);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            Paint(bar, fill, false);
            return bar;
        }

        private static void Bold(TMP_Text label)
        {
            label.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// The tab's dot as a sprite: a white disc with a one pixel soft edge, tinted by the Image.
        /// The mockup's dots are round and an Image with no sprite is a square. Drawn here rather
        /// than borrowed from the game, so it is ours and survives the game unloading its own
        /// assets at logout, and kept for the process, so it is made once.
        /// </summary>
        private static Sprite DotSprite()
        {
            if (_dot != null) return _dot;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;

            float radius = size / 2f;
            var middle = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), middle);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
                }
            }

            texture.Apply(false, false);

            _dot = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            _dot.hideFlags = HideFlags.HideAndDontSave;
            return _dot;
        }

        /// <summary>
        /// Where the panel goes: the nearest rect above the compendium's text whose height is its
        /// own rather than the text's, so the panel covers the area the text is shown in, is
        /// clipped where the text would be, and does not scroll with it.
        ///
        /// <b>The text's own parent is not it.</b> The first version looked for a ScrollRect's
        /// viewport and came out on that parent, which in 1.0 is 'Content': 840 x 565 on the log
        /// line when the panel was built. Content is sized by the text inside it, so the moment
        /// the panel blanked the text it shrank, to about 55 units measured off Robbin's
        /// screenshot, and took the panel with it. That is what squeezed every label flat (see
        /// Fit).
        ///
        /// So this climbs past every rect whose height follows what is in it: one a
        /// ContentSizeFitter sizes, one a scroll view moves, one a layout group above it sizes.
        /// It stops at the first rect that clips what is drawn in it, since that is the area the
        /// text is seen in, and it never climbs to the dialog itself, nor to a rect that would
        /// bring the page's topic, the text's scrollbar or the list under the panel. The hierarchy is asset data no
        /// decompile shows, so the rects it passed are named on the build log line. Where it
        /// cannot climb far enough the panel is still never squeezed, since it grows to its
        /// content; it can then come out shorter than the text area, and that is all.
        /// </summary>
        private static RectTransform Host(TextsDialog dialog, TMP_Text donor, out string passed)
        {
            var host = donor.transform.parent as RectTransform;
            if (host == null) throw new InvalidOperationException("The compendium's text has no parent to draw over.");

            passed = "";
            while (!Clips(host) && FollowsContent(host))
            {
                var above = host.parent as RectTransform;
                if (above == null || above == dialog.transform || !above.IsChildOf(dialog.transform)) break;
                if (Brings(host, above, dialog.m_textAreaTopic) || Brings(host, above, dialog.m_rightScrollbar)
                    || Brings(host, above, dialog.m_listRoot)) break;

                passed += (passed.Length > 0 ? ", '" : "'") + host.name + "'";
                host = above;
            }

            return host;
        }

        /// <summary>Whether something sizes or moves this rect by what is inside it.</summary>
        private static bool FollowsContent(RectTransform rect)
        {
            foreach (ContentSizeFitter fitter in rect.GetComponents<ContentSizeFitter>())
                if (fitter.enabled && fitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained) return true;

            foreach (ScrollRect scroll in rect.GetComponentsInParent<ScrollRect>(true))
                if (scroll.content == rect) return true;

            Transform parent = rect.parent;
            if (parent == null) return false;

            foreach (HorizontalOrVerticalLayoutGroup group in parent.GetComponents<HorizontalOrVerticalLayoutGroup>())
                if (group.enabled && group.childControlHeight) return true;

            return false;
        }

        /// <summary>Whether this rect cuts off what is drawn in it, the way a scroll view's viewport does.</summary>
        private static bool Clips(RectTransform rect)
        {
            RectMask2D rectMask = rect.GetComponent<RectMask2D>();
            if (rectMask != null && rectMask.enabled) return true;

            Mask mask = rect.GetComponent<Mask>();
            return mask != null && mask.enabled;
        }

        /// <summary>Whether climbing from one rect to the one above it puts this part under the panel.</summary>
        private static bool Brings(Transform from, Transform to, Component part)
        {
            return part != null && part.transform.IsChildOf(to) && !part.transform.IsChildOf(from);
        }

        /// <summary>
        /// Everything off the clone except the text itself. A copy of the compendium's text would
        /// otherwise bring whatever sizes and localises the original, and those would go on doing
        /// it to our labels.
        ///
        /// DestroyImmediate, since the clone is used in the same frame. Twice over, because a
        /// component another one requires cannot go first, and one pass in the wrong order would
        /// leave it behind.
        /// </summary>
        private static void Strip(GameObject go)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Component component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    if (component is RectTransform || component is CanvasRenderer || component is TMP_Text) continue;

                    Object.DestroyImmediate(component);
                }
            }

            for (int i = go.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        }

        /// <summary>The settings every label shares, whatever the compendium's text used.</summary>
        private static void Prime(TMP_Text label)
        {
            label.text = "";
            label.enableAutoSizing = false;
            label.richText = true;
            label.raycastTarget = false;
            label.overrideColorTags = false;
            label.enableVertexGradient = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.margin = Vector4.zero;
            label.lineSpacing = 0f;
            label.paragraphSpacing = 0f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = BodyText;
        }

        /// <summary>
        /// One line of the compendium's font at size 1, as TextMeshPro lays it out. Taken from a
        /// throwaway label, because asking for preferred values rewrites a label's working text.
        /// A result that cannot be a line height falls back to 1.2, which is a typical one.
        /// </summary>
        private static float Measure(RectTransform parent)
        {
            GameObject probe = Object.Instantiate(_proto.gameObject, parent);
            try
            {
                probe.SetActive(true);
                TMP_Text text = probe.GetComponent<TMP_Text>();
                text.fontSize = 100f;
                text.textWrappingMode = TextWrappingModes.NoWrap;

                float perEm = text.GetPreferredValues("Hg").y / 100f;
                if (perEm > 0.6f && perEm < 2.5f) return perEm;

                UtangardPlugin.Log.LogWarning("Compendium panel: measured a line of " + perEm
                    + " em, which cannot be right; spacing the text as if it were 1.2.");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }

            return 1.2f;
        }

        /// <summary>Half the difference between the mockup's line and the font's own, at this size.</summary>
        private static float Lead(float size)
        {
            return Mathf.Max(0f, (LineHeight - _naturalPerEm) * size * 0.5f);
        }

        private static TMP_Text Label(Transform parent, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = Object.Instantiate(_proto.gameObject, parent);
            go.name = "Text";
            go.SetActive(true);

            TMP_Text label = go.GetComponent<TMP_Text>();
            label.fontSize = size;
            label.color = color;
            label.alignment = align;

            float lead = Lead(size);
            label.margin = new Vector4(0f, lead, 0f, lead);
            return label;
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image Paint(RectTransform rect, Color color, bool raycast)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>Fill the parent, less this much on every side.</summary>
        private static void Inset(RectTransform rect, float by)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(by, by);
            rect.offsetMax = new Vector2(-by, -by);
        }

        private static GameObject Gap(Transform parent, float height)
        {
            RectTransform rect = Child("Gap", parent);
            LayoutElement space = rect.gameObject.AddComponent<LayoutElement>();
            space.minHeight = height;
            space.preferredHeight = height;
            return rect.gameObject;
        }

        /// <summary>
        /// A column that gives its children the full width and their text's height, top down.
        /// Filling the height is for a strip cell only, whose label must take the whole cell so
        /// a one-line name sits in the middle of a row another name has wrapped to two lines.
        /// </summary>
        private static void Stack(VerticalLayoutGroup group, bool fillHeight)
        {
            group.spacing = 0f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = fillHeight;
        }

        /// <summary>
        /// A row. Even, its children share its width and all take the tallest one's height, which
        /// is the strip and the pair of boxes. Not even, each child is as wide as its text and the
        /// one marked flexible takes what is left, which is a box's heading and its percent.
        /// </summary>
        private static void Row(HorizontalLayoutGroup group, bool even)
        {
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = even;
            group.childForceExpandHeight = even;
        }

        /// <summary>Drop every reference into a previous compendium, which a new world has destroyed.</summary>
        private static void Forget()
        {
            if (_root != null) Object.Destroy(_root);

            _root = null;
            _dialog = null;
            _host = null;
            _floor = null;
            _fitScale = -1f;
            _fitPixels = -1f;
            Rims.Clear();
            _panelFace = null;
            _proto = null;
            _left = null;
            _playersHeading = null;
            _caption = null;
            _list = null;
            _tight = false;
            _head = null;
            _viewName = null;
            _viewNote = null;
            PlayerRows.Clear();
            _title = null;
            _waiting = null;
            _rules = null;
            _bars = null;
            _barsGap = null;
            _rulesGap = null;
            _fighting = null;
            _discovery = null;
            Cells.Clear();
        }

        private static Color Rgb(int hex)
        {
            return new Color(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, 1f);
        }
    }

    /// <summary>
    /// Keeps the panel's numbers fresh while it is on screen. A component on the panel itself,
    /// so it runs exactly while the page is showing: Unity stops calling Update the moment the
    /// panel, or the compendium around it, is switched off.
    /// </summary>
    internal sealed class CompendiumPanelTicker : MonoBehaviour
    {
        private void Update()
        {
            CompendiumPanel.Tick();
        }
    }
}
