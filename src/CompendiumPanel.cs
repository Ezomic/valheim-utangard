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
    /// Everything it shows is read on this client: the gate from the world's keys, the bars from
    /// the local character's kill tally and minimap. Nothing here is sent anywhere.
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

        private const float BodySize = 14f;
        private const float TitleSize = 20f;
        private const float SmallSize = 13f;
        private const float StripSize = 12f;

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

        /// <summary>The compendium's text viewport, which the panel covers exactly.</summary>
        private static RectTransform _host;

        /// <summary>What Fit last fitted to, so an unchanged compendium costs no layout.</summary>
        private static float _fitScale = -1f;
        private static Vector2 _fitSize;

        /// <summary>A compendium the panel already failed in. It keeps the text page from then on.</summary>
        private static TextsDialog _failedFor;

        private static Heightmap.Biome _selected = Heightmap.Biome.None;
        private static float _nextRefresh;

        /// <summary>A stripped copy of the compendium's own text, which every label is cloned from.</summary>
        private static TMP_Text _proto;

        /// <summary>One line of this font at size 1, measured once per build. See LineHeight.</summary>
        private static float _naturalPerEm;

        private static readonly List<Cell> Cells = new List<Cell>();
        private static TMP_Text _title;
        private static TMP_Text _waiting;
        private static TMP_Text _rules;
        private static GameObject _barsGap;
        private static GameObject _bars;
        private static GameObject _rulesGap;
        private static Box _fighting;
        private static Box _discovery;

        private sealed class Cell
        {
            public Heightmap.Biome Biome;
            public GameObject Go;
            public Image Edge;
            public RectTransform Face;
            public TMP_Text Label;
            public float EdgeWidth = -1f;
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
        /// cannot disagree. Foothold says how far this character's bars have got.
        /// </summary>
        private static void Draw()
        {
            List<GateReport.Row> rows = GateReport.Rows();

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
                SetEdgeWidth(cell, selected ? 2f : 1f);
            }

            GateReport.Row chosen = RowOf(rows, _selected);
            bool locked = chosen.Key != null && !chosen.Open;

            SetText(_title, "The " + GateReport.BiomeName(_selected));
            SetText(_waiting, Wrapped(rows.Count == 0 ? "No world loaded yet." : GateReport.WaitingLine(chosen)));

            // The bars only mean something where the lock is, and only while footholds are on.
            // With footholds off they could never unlock anything, so they are not shown at all.
            bool bars = locked && UtangardConfig.FootholdEnabled.Value;
            SetActive(_barsGap, bars);
            SetActive(_bars, bars);
            if (bars) DrawBars(Foothold.Read(_selected));

            // A server that has switched every rule off gets no rules line at all, rather than an
            // empty one holding its 12 px gap open under the boxes.
            bool warning = false;
            string rules = locked ? Rules(bars, out warning) : null;
            bool showRules = !string.IsNullOrEmpty(rules);

            SetActive(_rulesGap, showRules);
            SetActive(_rules.gameObject, showRules);
            if (showRules)
            {
                SetText(_rules, Wrapped(rules));
                SetColor(_rules, warning ? LockedText : Muted);
            }
        }

        private static void DrawBars(Foothold.Standing s)
        {
            // Fighting. Points out of 100, so the points are the percent.
            SetText(_fighting.Percent, s.FightingAvailable ? s.Fighting + "%" : "?");
            SetFill(_fighting.Fill, s.FightingAvailable ? s.Fighting / (float)Foothold.FullBar : 0f);

            if (!UtangardConfig.BlockEating.Value || s.CanEat)
                SetState(_fighting.State, "Eating allowed", true);
            else if (!s.FightingAvailable)
                SetState(_fighting.State, "Kills cannot be read", false);
            else if (s.EatAt > Foothold.FullBar)
                SetState(_fighting.State, "No eating here", false);
            else
                SetState(_fighting.State, "Eating at " + s.EatAt + "%", false);

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

        /// <summary>The selected cell's edge is 2 px and the rest 1, as in the mockup.</summary>
        private static void SetEdgeWidth(Cell cell, float width)
        {
            if (Mathf.Approximately(cell.EdgeWidth, width)) return;
            cell.EdgeWidth = width;
            Inset(cell.Face, width);
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

            RectTransform host = Host(dialog, donor);

            var root = new GameObject("Utangard_Panel", typeof(RectTransform));
            root.SetActive(false);

            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(host, false);

            // Laid out by nobody but itself, whatever the host turns out to carry.
            root.AddComponent<LayoutElement>().ignoreLayout = true;

            _root = root;
            _dialog = dialog;
            _host = host;
            _fitScale = -1f;
            Fit();

            // The panel: a 1 px edge in #5a4a36 around a #1f1a14 face. An Image with no sprite is
            // a flat rectangle, so the edge is the root's colour showing round a face inset by 1.
            // Raycast on, so a click on the panel does not fall through to whatever is under it.
            Paint(rootRect, PanelEdge, true);
            RectTransform face = Child("Face", rootRect);
            Inset(face, 1f);
            Paint(face, PanelFace, false);

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
            Inset(body, 1f);
            VerticalLayoutGroup column = body.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(22, 22, 18, 18);
            Stack(column, false);

            // 1. The biome strip. Equal cells 6 apart, 12 below it.
            RectTransform strip = Child("Biomes", body);
            HorizontalLayoutGroup row = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 6f;
            Row(row, true);

            int sounded = 0;
            foreach (Heightmap.Biome biome in UtangardConfig.GateableBiomes)
            {
                Cell cell = MakeCell(strip, biome);
                if (Sound(cell.Go, dialog)) sounded++;
                Cells.Add(cell);
            }

            Gap(body, 12f);

            // 2. The title, 4 below it.
            _title = Label(body, TitleSize, Orange, TextAlignmentOptions.TopLeft);
            Gap(body, 4f);

            // 3. Who it waits on, or that it is open.
            _waiting = Label(body, BodySize, Muted, TextAlignmentOptions.TopLeft);

            // 4. The two boxes, 12 below the line above and 14 apart.
            _barsGap = Gap(body, 12f);
            RectTransform bars = Child("Bars", body);
            HorizontalLayoutGroup pair = bars.gameObject.AddComponent<HorizontalLayoutGroup>();
            pair.spacing = 14f;
            Row(pair, true);
            _bars = bars.gameObject;

            _fighting = MakeBox(bars, "Fighting", FightingFill);
            _discovery = MakeBox(bars, "Discovery", DiscoveryFill);

            // 5. The rules, 12 below.
            _rulesGap = Gap(body, 12f);
            _rules = Label(body, SmallSize, Muted, TextAlignmentOptions.TopLeft);

            // Every number here is one nothing but the running game can answer, and each is the
            // first thing to read if the panel looks wrong: the size of the area it covers, the
            // scale that area sits at (below 1 the panel is being drawn back up), the size the
            // compendium's own text uses, and whether the biome buttons found vanilla's sounds.
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Rect area = host.rect;
            UtangardPlugin.Log.LogInfo("Compendium panel built over '" + host.name + "', "
                + Mathf.RoundToInt(area.width) + " x " + Mathf.RoundToInt(area.height)
                + " at scale " + _fitScale.ToString("0.###", inv)
                + ", the compendium's own text at size " + donor.fontSize.ToString("0.#", inv)
                + ", one line of text at " + _naturalPerEm.ToString("0.00", inv) + " em, "
                + sounded + " of " + Cells.Count + " biome buttons with vanilla's click sound.");
        }

        /// <summary>
        /// Keeps the panel over exactly the area the compendium's text would fill.
        ///
        /// At a scale of 1 or more that is simply "fill the parent". Below 1 the compendium sits
        /// under a parent that shrinks it, and the panel is drawn at 1/scale instead, with its rect
        /// shrunk by the same factor so it still covers the same area on screen. Its text then
        /// comes out at the mockup's pixel sizes at 1080p, which is the floor this page promises.
        /// A compendium scaled up is left alone: its text is only bigger, and bigger is readable.
        /// </summary>
        private static void Fit()
        {
            if (_root == null || _host == null) return;

            float scale = HostScale(_host);
            Vector2 size = _host.rect.size;
            if (Mathf.Approximately(scale, _fitScale) && size == _fitSize) return;

            _fitScale = scale;
            _fitSize = size;

            var rect = (RectTransform)_root.transform;
            if (scale >= 0.99f)
            {
                rect.localScale = Vector3.one;
                Inset(rect, 0f);
                return;
            }

            float grow = 1f / scale;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = new Vector3(grow, grow, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size * scale;
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
        /// and a centred label; the padding leaves room for the selected cell's 2 px edge, so
        /// selecting one does not shift its neighbours.
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

            VerticalLayoutGroup inner = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            inner.padding = new RectOffset(2, 2, 2, 2);
            Stack(inner, true);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Paint(face, PanelFace, false);

            // The mockup's 6 px above and below the text, plus the line-height's half leading.
            TMP_Text label = Label(rect, StripSize, OpenText, TextAlignmentOptions.Center);
            float lead = Lead(StripSize);
            label.margin = new Vector4(2f, 6f + lead, 2f, 6f + lead);

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
        /// A Fighting or Discovery box: 1 px #4a3d2e edge, 10 px padding, a heading with the
        /// percent at the right, a 12 px bar with 6 px either side, and the unlock line in 13 px.
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
            stack.padding = new RectOffset(11, 11, 11, 11);   // 10 of padding inside a 1 px edge
            Stack(stack, false);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Inset(face, 1f);
            Paint(face, PanelFace, false);

            RectTransform head = Child("Head", rect);
            HorizontalLayoutGroup line = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 8f;
            Row(line, false);

            TMP_Text heading = Label(head, BodySize, BodyText, TextAlignmentOptions.TopLeft);
            heading.text = name;
            LayoutElement grow = heading.gameObject.AddComponent<LayoutElement>();
            grow.minWidth = 0f;
            grow.flexibleWidth = 1f;

            TMP_Text percent = Label(head, BodySize, BodyText, TextAlignmentOptions.TopRight);
            percent.textWrappingMode = TextWrappingModes.NoWrap;

            Gap(rect, 6f);

            RectTransform track = Child("Track", rect);
            Paint(track, Track, false);
            LayoutElement height = track.gameObject.AddComponent<LayoutElement>();
            height.minHeight = 12f;
            height.preferredHeight = 12f;

            // The fill is the track's width times the percent, by its right anchor.
            RectTransform bar = Child("Fill", track);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            Paint(bar, fill, false);

            Gap(rect, 6f);

            TMP_Text state = Label(rect, SmallSize, Muted, TextAlignmentOptions.TopLeft);

            return new Box { Percent = percent, Fill = bar, State = state };
        }

        /// <summary>
        /// Where the panel goes: over the scroll view that holds the compendium's text, in its
        /// viewport, so it is clipped where the text would be and does not scroll with it.
        /// The text area's size lives in the UI asset rather than in any code, so it is taken
        /// from the running game and logged once on build.
        /// </summary>
        private static RectTransform Host(TextsDialog dialog, TMP_Text donor)
        {
            ScrollRect scroll = donor.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll != dialog.m_leftScrollRect)
            {
                if (scroll.viewport != null) return scroll.viewport;
                return (RectTransform)scroll.transform;
            }

            var parent = donor.transform.parent as RectTransform;
            if (parent == null) throw new InvalidOperationException("The compendium's text has no parent to draw over.");
            return parent;
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
            _fitScale = -1f;
            _proto = null;
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
