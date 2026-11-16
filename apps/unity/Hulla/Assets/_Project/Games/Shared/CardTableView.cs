using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Voice;
using Hulla.Presentation.Audio;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using Hulla.Presentation.Voice;
using Hulla.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Games.Shared
{
    /// <summary>
    /// What every card table shows around the game itself, on the shared table layout: the seats, the
    /// quick-chat strip and the chat sheet with phrases, emotes and typed lines, the player sheet with
    /// mute, block and report, the menu, the Watching sheet, the fair-deal sheet, the voice controls and
    /// the question before leaving mid-round. A game's table derives from it and draws its own felt.
    /// </summary>
    public abstract class CardTableView
    {
        protected readonly TableClient _client;
        protected readonly Func<long> _nowMs;
        protected readonly OpponentRing _ring;
        protected readonly PlayerBadge _me = new PlayerBadge();
        protected readonly VisualElement _root;
        protected readonly Action _leave;
        protected readonly Action<string?>? _openRules;
        protected readonly Action? _openSettings;
        protected readonly Label _watchCount;
        protected readonly VisualElement _watchIcon;
        protected readonly VisualElement _dim;
        protected readonly Dictionary<string, Button> _emoteTiles = new Dictionary<string, Button>();
        protected Func<string, bool>? _ownsPack;
        protected readonly IChatCatalog _chat;
        protected readonly VisualElement _chatPanel;
        protected readonly VisualElement _strip;
        protected readonly Label _mySay;
        protected float _mySayLeft;
        protected string _context = QuickStrip.Any;
        protected int _stripSeed;
        protected Func<string?, bool>? _isMuted;
        protected Action<string, bool>? _setMuted;
        protected Action<string>? _block;
        protected Action<string, string, Action<bool>>? _report;
        protected VisualElement _sheet = null!;
        protected Label _sheetTitle = null!;
        protected VisualElement _sheetActions = null!;
        protected Label _sheetStatus = null!;
        protected readonly Func<string, Awaitable<Domain.Result<bool>>>? _sendText;
        protected VisualElement _chatLog = null!;
        protected TextField _chatInput = null!;
        protected Label _chatStatus = null!;
        protected readonly Button _mic;
        protected readonly Label _voicePill;
        protected readonly Button _speaker;
        protected Func<string, int>? _peerVolume;
        protected Action<string, int>? _setPeerVolume;
        protected VoiceController? _voice;
        protected MicControl? _micControl;
        protected Func<string>? _tutorialHeader;
        protected readonly SoundPlayer? _sounds;
        protected bool _haptics;
        protected string _chatTab = "chat";
        protected Action<string>? _addFriend;
        /// <summary>Lets the results offer "Add friend" for co-players who aren't friends or pending yet.</summary>
        protected Func<string, bool>? _canAddFriend;
        protected readonly Label _info;
        /// <summary>How long a phrase, an emote or a play hint stays on screen.</summary>
        protected const float SaySeconds = 3f;
        /// <summary>The chat log keeps the latest lines only.</summary>
        protected const int ChatLogLines = 30;

        /// <summary>The quick-chat context the strip is showing (`gave_thulla`, `any`…); read by tests.</summary>
        public string StripContext => _context;

        /// <summary>Hides muted players' text, quick chat and emotes, and offers Mute on a tapped badge (GDD 11.6).</summary>
        public void UseMutes(Func<string?, bool> isMuted, Action<string, bool> setMuted)
        {
            _isMuted = isMuted;
            _setMuted = setMuted;
        }

        /// <summary>Block and report from the player sheet (GDD 10.11); both run against the server.</summary>
        public void UseModeration(Action<string> block, Action<string, string, Action<bool>> report)
        {
            _block = block;
            _report = report;
        }

        /// <summary>The player sheet for a tapped badge: Mute or Unmute, Block, Report.</summary>
        protected void OnBadgeTapped(long seat)
        {
            var model = _client.Model;
            if (_setMuted == null || seat >= model.Seats.Count)
            {
                return;
            }

            var id = model.Seats[(int)seat].PlayerId;
            if (id == null || model.Seats[(int)seat].Bot)
            {
                return;
            }

            ShowPlayer(id, NameOf(seat), model.Seats[(int)seat].Avatar, seat);
        }

        /// <summary>The player sheet for anyone at the table, seated or watching (seat null for a watcher).</summary>
        protected void ShowPlayer(string id, string name, string? avatar, long? seat)
        {
            var model = _client.Model;
            if (_setMuted == null)
            {
                return;
            }

            var muted = _isMuted?.Invoke(id) == true;
            _sheetTitle.style.display = DisplayStyle.None;
            _sheetStatus.text = "";
            _sheetActions.Clear();

            // The player sheet (concept: Player sheet at the table): face, name, handle and title, one stats
            // line, cards picked up this round, Add friend, their voice level, then Mute · Block · Report.
            var head = new VisualElement();
            head.AddToClassList("psheet__head");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("seat__avatar");
            face.AddToClassList("psheet__face");
            var initial = new Label { pickingMode = PickingMode.Ignore };
            initial.AddToClassList("seat__initial");
            face.Add(initial);
            AvatarArt.Apply(face, initial, avatar, name);
            head.Add(face);
            var lines = new VisualElement();
            lines.AddToClassList("psheet__lines");
            lines.Add(Line(name, "psheet__name"));
            var who = Line("", "psheet__line");
            var stats = Line("", "psheet__line");
            lines.Add(who);
            lines.Add(stats);
            head.Add(lines);
            _sheetActions.Add(head);
            if (LookUpProfile != null)
            {
                FillProfile(id, who, stats);
            }

            if (model.InRound && seat is long seated)
            {
                AddPlayerFacts(_sheetActions, seated);
            }

            if (_addFriend != null && _canAddFriend?.Invoke(id) == true)
            {
                Button add = null!;
                add = new Button(() =>
                {
                    _addFriend(id);
                    add.text = UiText.RequestSent;
                    add.SetEnabled(false);
                }) { text = UiText.AddFriend };
                add.AddToClassList("psheet__add");
                _sheetActions.Add(add);
            }

            if (!muted && _peerVolume != null && _setPeerVolume != null && _voice != null && _voice.State != VoiceState.Hidden)
            {
                // Their voice level on this phone only (GDD 11.4).
                var level = new SliderInt(UiText.Voice, 0, 100) { value = _peerVolume(id), tooltip = UiText.VoiceVolumeLabel };
                level.AddToClassList("sheet__level");
                level.AddToClassList("psheet__voice");
                var setLevel = _setPeerVolume;
                level.RegisterValueChangedCallback(evt => setLevel(id, evt.newValue));
                _sheetActions.Add(level);
            }

            var links = new VisualElement();
            links.AddToClassList("psheet__links");
            _sheetActions.Add(links);
            void Link(string text, Action click)
            {
                if (links.childCount > 0)
                {
                    links.Add(Line(UiText.Dot, "psheet__dot"));
                }

                var link = new Button(click) { text = text };
                link.AddToClassList("psheet__link");
                links.Add(link);
            }

            Link(muted ? UiText.Unmute : UiText.Mute, () =>
            {
                _setMuted(id, !muted);
                _sheetStatus.text = muted ? "" : Strings.Format(UiText.MutedNote, name);
                CloseSheetSoon();
            });
            if (_block != null)
            {
                Link(UiText.Block, () =>
                {
                    _block(id);
                    _sheetStatus.text = Strings.Format(UiText.Blocked, name);
                    CloseSheetSoon();
                });
            }

            if (_report != null)
            {
                Link(UiText.Report, () => ShowReasons(id, name));
            }

            // The host takes a watcher off the table here, not on the Watching sheet's row (one action per row).
            if (seat == null && _client.Model.IsHost && RemoveSpectator != null && IsWatching(id))
            {
                Link(UiText.RemoveFromTable, () =>
                {
                    RemoveSpectator?.Invoke(id);
                    _sheetStatus.text = Strings.Format(UiText.RemovedWatcher, name);
                    CloseSheetSoon();
                });
            }

            OpenPlayerSheet();
        }

        /// <summary>Looks up a player's public profile for the sheet's handle, title and stats; null hides them.</summary>
        public Func<string, Awaitable<Profile?>>? LookUpProfile { get; set; }

        protected async void FillProfile(string id, Label who, Label stats)
        {
            var profile = LookUpProfile == null ? null : await LookUpProfile(id);
            if (profile == null)
            {
                return;
            }

            who.text = string.IsNullOrEmpty(profile.Title)
                ? Strings.Format(UiText.PlayerLine, profile.Handle)
                : Strings.Format(UiText.PlayerLineTitle, profile.Handle, UiText.TitleName(profile.Title!));
            if (profile.Stats != null)
            {
                stats.text = Strings.Format(UiText.PlayerStats, Strings.Get("ui.rounds_count", profile.Stats.Rounds), Strings.Get("ui.times_bhabhi", profile.Stats.Bhabhis));
            }

            // The level leads the line (design: Player sheet), when the server sent it.
            if (profile.Level is long level)
            {
                stats.text = stats.text.Length == 0
                    ? Strings.Format(UiText.LevelN, level)
                    : Strings.Format(UiText.PlayerStats, Strings.Format(UiText.LevelN, level), stats.text);
            }
        }

        protected static Label Line(string text, string cls)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }

        protected void OpenPlayerSheet()
        {
            _sheet.style.display = DisplayStyle.Flex;
            _dim.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Report (design: Report and block): six reasons as a single-choice list, "Also block" on by
        /// default, then Send report; the thanks line replaces the sheet.
        /// </summary>
        protected void ShowReasons(string id, string name)
        {
            _sheetTitle.style.display = DisplayStyle.Flex;
            _sheetTitle.text = Strings.Format(UiText.ReportWhy, name);
            _sheetActions.Clear();
            _sheetStatus.text = "";
            string? chosen = null;
            var rows = new List<Button>();
            var list = new VisualElement();
            list.AddToClassList("report__list");
            foreach (var (reason, label) in UiText.ReportReasons)
            {
                var value = reason;
                var row = new Button { text = label };
                row.AddToClassList("report__reason");
                row.clicked += () =>
                {
                    chosen = value;
                    foreach (var other in rows)
                    {
                        other.EnableInClassList("report__reason--on", other == row);
                    }
                };
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("report__radio");
                row.Add(dot);
                rows.Add(row);
                list.Add(row);
            }

            _sheetActions.Add(list);
            var block = new Toggle(Strings.Format(UiText.AlsoBlock, name)) { value = _block != null };
            block.AddToClassList("report__block");
            block.style.display = _block != null ? DisplayStyle.Flex : DisplayStyle.None;
            _sheetActions.Add(block);
            var send = new Button(() =>
            {
                if (chosen == null)
                {
                    return;
                }

                if (block.value)
                {
                    _block?.Invoke(id);
                }

                _report?.Invoke(id, chosen, ok =>
                {
                    _sheetActions.Clear();
                    _sheetStatus.text = ok ? UiText.ReportThanks : UiText.Error("social.unavailable");
                    CloseSheetSoon();
                });
            }) { text = UiText.SendReport };
            send.AddToClassList("report__send");
            _sheetActions.Add(send);
        }

        protected void SheetButton(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            _sheetActions.Add(button);
        }

        protected void CloseSheetSoon() => _sheet.schedule.Execute(() =>
        {
            _sheet.style.display = DisplayStyle.None;
            _dim.style.display = _chatPanel.style.display == DisplayStyle.Flex ? DisplayStyle.Flex : DisplayStyle.None;
        }).StartingIn(2500);

        protected bool Muted(long seat)
        {
            var model = _client.Model;
            return _isMuted != null && seat < model.Seats.Count && _isMuted(model.Seats[(int)seat].PlayerId);
        }

        /// <summary>
        /// Voice at this table (GDD 11.4): the mic beside chat, hold-to-talk on push-to-talk tables, the
        /// voice pill and the speaking rings. Hidden when the build, the server or the table has no voice.
        /// </summary>
        public void UseVoice(VoiceController voice)
        {
            _voice = voice;
            // One control for the table and the lobby: tap on open mic, hold on push-to-talk tables.
            _micControl = new MicControl(_mic, voice, ShowVoice, "table__mic-tip");
            voice.Changed += ShowVoice;
            ShowVoice();
        }

        /// <summary>Each player's voice level for the player sheet, 0 to 100.</summary>
        public void UseVoiceLevels(Func<string, int> get, Action<string, int> set)
        {
            _peerVolume = get;
            _setPeerVolume = set;
        }

        /// <summary>The speaker toggle's text; empty when it is hidden. Read by tests.</summary>
        public string SpeakerNote => _speaker.style.display == DisplayStyle.Flex ? _speaker.text : "";

        /// <summary>The voice pill's text; empty when it is hidden. Read by tests.</summary>
        public string VoiceNote => _voicePill.style.display == DisplayStyle.Flex ? _voicePill.text : "";

        public bool ShowsMic => _mic.style.display == DisplayStyle.Flex;

        /// <summary>True while the mic works by holding (push-to-talk tables). Read by tests.</summary>
        public bool ShowsTalk => _micControl != null && _voice != null && _micControl.HoldMode && _voice.CanSpeak && _voice.State != VoiceState.Hidden;

        protected void ShowVoice()
        {
            var voice = _voice;
            if (voice == null || _micControl == null)
            {
                return;
            }

            var state = voice.State;
            var hidden = state == VoiceState.Hidden;
            // A watcher sees the mic too, off and inert (design: Watching a table): only seats speak.
            _mic.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            _mic.SetEnabled(voice.CanSpeak);
            var retry = state == VoiceState.Failed && voice.MicOn;
            var live = voice.MicOn && !retry && (!_micControl.HoldMode || voice.Talking);
            _mic.text = _micControl.Label();
            _mic.EnableInClassList("table__mic--off", !voice.CanSpeak || !live);
            _mic.EnableInClassList("table__mic--on", live);
            _mic.EnableInClassList("table__mic--retry", retry);
            _mic.EnableInClassList("table__mic--held", voice.Talking);
            var note = MicControl.Line(voice, _micControl.Render());
            _voicePill.text = note;
            _voicePill.style.display = note.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            // The speaker: hear the table or not, whenever voice exists here.
            var speakerShown = !hidden && state != VoiceState.Suspended;
            _speaker.style.display = speakerShown ? DisplayStyle.Flex : DisplayStyle.None;
            _speaker.text = voice.SpeakerOn ? UiText.VoicesOn : UiText.VoicesOff;
            _speaker.EnableInClassList("voice-speaker--off", !voice.SpeakerOn);
            var model = _client.Model;
            _me.SetSpeaking(!hidden && voice.IsSpeaking(model.UserId));
            foreach (var seat in model.Seats)
            {
                _ring.BadgeFor(seat.Seat)?.SetSpeaking(!hidden && voice.IsSpeaking(seat.PlayerId));
            }
        }

        protected void Buzz(Haptic kind)
        {
            if (_haptics)
            {
                Haptics.Play(kind);
            }
        }

        protected static void Countdown(ref float left, float deltaSeconds, VisualElement element)
        {
            if (left <= 0f)
            {
                return;
            }

            left -= deltaSeconds;
            if (left <= 0f)
            {
                element.style.display = DisplayStyle.None;
            }
        }

        protected void BuildChatPanel(VisualElement phrases, VisualElement emotes)
        {
            foreach (var phrase in _chat.Phrases)
            {
                var id = phrase.Id;
                var button = new Button(() => Say(() => _client.SendQuickChat(id))) { text = phrase.Text, tooltip = phrase.Translation };
                button.AddToClassList("chip");
                phrases.Add(button);
            }

            foreach (var emote in _chat.Emotes)
            {
                var id = emote;
                var tile = EmoteArt.Tile(id, UiText.EmoteLabel(id), !CanSend(id), () => TapEmote(id));
                _emoteTiles[id] = tile;
                emotes.Add(tile);
            }
        }

        /// <summary>Which emote packs the player owns (GDD 11.3); until set, only the free pack.</summary>
        public void UseEmotePacks(Func<string, bool> owns)
        {
            _ownsPack = owns;
            foreach (var pair in _emoteTiles)
            {
                EmoteArt.SetLocked(pair.Value, !CanSend(pair.Key));
            }
        }

        protected bool CanSend(string emoteId)
        {
            var pack = _chat.EmotePack(emoteId) ?? EmoteArt.FreePack;
            return _ownsPack?.Invoke(pack) ?? pack == EmoteArt.FreePack;
        }

        /// <summary>An owned emote is sent; a locked one says which shop pack it comes from.</summary>
        protected void TapEmote(string emoteId)
        {
            if (CanSend(emoteId))
            {
                Say(() => _client.SendEmote(emoteId));
                return;
            }

            var grid = _root.Q<VisualElement>("chat-emotes");
            if (grid != null)
            {
                EmoteArt.ShowLockNote(grid, Strings.Format(UiText.EmotePackLocked, ItemText.Name(_chat.EmotePack(emoteId) ?? "")));
            }
        }

        /// <summary>The strip follows the last beat at the table (design 7.6); each change rotates the picks.</summary>
        protected void SetContext(string context)
        {
            _context = context;
            _stripSeed++;
            RefreshStrip();
        }

        protected void RefreshStrip()
        {
            _strip.Clear();
            var picks = QuickStrip.Pick(_chat.Phrases, _context, _stripSeed);
            if (picks.Count == 0)
            {
                _strip.style.display = DisplayStyle.None;
                return;
            }

            // The strip steps aside in the tutorial and while Ask for hand fills the bar.
            _strip.style.display = _tutorialHeader != null || _root.ClassListContains("table--take-hand") ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var phrase in picks)
            {
                var id = phrase.Id;
                var chip = new Button(() => _client.SendQuickChat(id)) { text = phrase.Text, tooltip = phrase.Translation };
                chip.AddToClassList("chip");
                chip.AddToClassList("quick-strip__chip");
                _strip.Add(chip);
            }

            var more = new Button(() => SetChatOpen(true, "phrases")) { text = UiText.More };
            more.AddToClassList("chip");
            more.AddToClassList("quick-strip__more");
            _strip.Add(more);
        }

        protected void Say(Action send)
        {
            send();
            SetChatOpen(false);
        }

        protected void SetChatOpen(bool open) => SetChatOpen(open, "chat");

        /// <summary>The chat sheet (design: Chat and quick-chat panel), on the tab asked for; "…" opens Phrases.</summary>
        protected void SetChatOpen(bool open, string tab)
        {
            _chatPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _dim.style.display = open || _sheet.style.display == DisplayStyle.Flex ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                ShowChatTab(tab);
            }
        }

        protected void BuildChatTabs(VisualElement tabs)
        {
            foreach (var (name, label) in new[] { ("chat", UiText.Chat), ("phrases", UiText.Phrases), ("emotes", UiText.Emotes) })
            {
                var tab = name;
                var item = new Button(() => ShowChatTab(tab)) { name = "chat-tab-" + tab, text = label };
                item.AddToClassList("segment__item");
                tabs.Add(item);
            }
        }

        protected void ShowChatTab(string tab)
        {
            _chatTab = tab;
            foreach (var item in _root.Q<VisualElement>("chat-tabs").Children())
            {
                item.EnableInClassList("segment__item--on", item.name == "chat-tab-" + tab);
            }

            var chat = tab == "chat";
            _root.Q<VisualElement>("chat-log-scroll").style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<VisualElement>(className: "chat-panel__compose").style.display = chat && _sendText != null ? DisplayStyle.Flex : DisplayStyle.None;
            _chatStatus.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<VisualElement>("chat-picks").style.display = chat ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<VisualElement>("chat-phrases").style.display = tab == "phrases" ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<VisualElement>("chat-emotes").style.display = tab == "emotes" ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Closes whichever sheet is open on the table (a tap on the dim).</summary>
        protected void CloseSheets()
        {
            if (SheetsLocked)
            {
                return;
            }

            _chatPanel.style.display = DisplayStyle.None;
            _sheet.style.display = DisplayStyle.None;
            _dim.style.display = DisplayStyle.None;
        }

        /// <summary>Leaving mid-round costs the seat: one question first (design: Dialogs), straight out otherwise.</summary>
        protected void AskLeave()
        {
            var model = _client.Model;
            var playing = model.InRound && !model.Spectating && model.MySeat is long mine && StillPlaying(mine);
            if (!playing || _tutorialHeader != null)
            {
                _leave();
                return;
            }

            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, UiText.LeaveTableTitle);
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(UiText.LeaveTableBody));
            var answers = Hulla.Presentation.Navigation.Sheet.Answers(panel);
            answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Stay, () => Hulla.Presentation.Navigation.Sheet.Close(panel)));
            answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Leave, () =>
            {
                Hulla.Presentation.Navigation.Sheet.Close(panel);
                _leave();
            }, "danger"));
        }

        /// <summary>The ⋯ menu (design: Table): Rules, Score and Settings.</summary>
        protected void OpenMenu()
        {
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, null);
            panel.AddToClassList("menu-sheet");
            void Item(string text, Action action)
            {
                var button = Hulla.Presentation.Navigation.Sheet.Button(text, () =>
                {
                    Hulla.Presentation.Navigation.Sheet.Close(panel);
                    action();
                });
                button.AddToClassList("menu-sheet__item");
                panel.Add(button);
            }

            if (_openRules != null)
            {
                Item(Strings.Text("rulebook.title"), () => _openRules(null));
            }

            AddMenuItems(Item);
            if (_openSettings != null)
            {
                Item(UiText.Settings, _openSettings);
            }
        }

        /// <summary>The tick on the code plate (design: Fair deal): the commitment now, the seed once the round ends.</summary>
        protected void OpenFairDeal()
        {
            if (ExplainFinishedDeal())
            {
                return;
            }

            // During the round (concept: Fair deal): the code the server committed to, a copy of it,
            // how it works, and the reminder that every round is dealt this way.
            var commitment = _client.Model.Commitment;
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, UiText.FairDeal);
            panel.AddToClassList("fair-sheet");
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(commitment.Length > 0 ? Strings.Format(UiText.FairRoundBody, ShortCode(commitment)) : UiText.SeedLater, "fair-sheet__body"));
            var answers = Hulla.Presentation.Navigation.Sheet.Answers(panel);
            if (commitment.Length > 0)
            {
                answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.CopyCommitment, () => GUIUtility.systemCopyBuffer = commitment));
            }

            answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.FairHow, () =>
            {
                var how = Hulla.Presentation.Navigation.Sheet.Open(_root, UiText.FairHowTitle);
                how.Add(Hulla.Presentation.Navigation.Sheet.Line(UiText.FairHowBody));
                Hulla.Presentation.Navigation.Sheet.Answers(how).Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Close, () => Hulla.Presentation.Navigation.Sheet.Close(how), "yes"));
            }));
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(UiText.FairEvery, "fair-sheet__foot"));
        }

        protected static string ShortCode(string value) => value.Length > 12 ? value.Substring(0, 4) + "…" + value.Substring(value.Length - 4) : value;

        /// <summary>The Watching sheet (design: Watching a table): who watches and whose friend, the free seats, Seat and Remove for the host.</summary>
        public void OpenWatchers()
        {
            var model = _client.Model;
            var free = 0;
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied)
                {
                    free++;
                }
            }

            foreach (var watcher in model.Spectators)
            {
                if (watcher.SeatNext)
                {
                    free--;
                }
            }

            free = Math.Max(0, free);
            // The Watching sheet (concept: Watching sheet): the count and the free seats on one line, a plate
            // per watcher with the face, Seated next round or the host's Seat, and a row tap for the player sheet.
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, null);
            panel.AddToClassList("watch-sheet");
            var head = new VisualElement();
            head.AddToClassList("watch-sheet__head");
            head.Add(Line(Strings.Format(UiText.WatchingCount, model.Spectators.Count), "watch-sheet__title"));
            head.Add(Line(free > 0 ? Strings.Get("ui.seats_free_next", free) : UiText.WatchingNoSeats, "watch-sheet__free"));
            panel.Add(head);
            foreach (var watcher in model.Spectators)
            {
                var id = watcher.UserId;
                var watcherName = watcher.Name;
                var row = new Button(() =>
                {
                    Hulla.Presentation.Navigation.Sheet.Close(panel);
                    ShowPlayer(id, watcherName, AvatarArt.ForAccount(id), null);
                }) { text = "" };
                row.AddToClassList("watch-row");
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("seat__avatar");
                face.AddToClassList("watch-row__face");
                var initial = new Label { pickingMode = PickingMode.Ignore };
                initial.AddToClassList("seat__initial");
                face.Add(initial);
                AvatarArt.Apply(face, initial, AvatarArt.ForAccount(id), watcherName);
                row.Add(face);
                var who = new VisualElement { pickingMode = PickingMode.Ignore };
                who.AddToClassList("watch-row__who");
                who.Add(Line(watcherName, "watch-row__name"));
                // Whose friend they are (concept: Watching sheet), from the seat the server names.
                if (watcher.FriendOfSeat is long friendSeat && friendSeat < model.Seats.Count && model.Seats[(int)friendSeat].Occupied)
                {
                    who.Add(Line(Strings.Format(UiText.FriendOf, NameOf(friendSeat)), "watch-row__friend"));
                }

                row.Add(who);
                if (watcher.SeatNext)
                {
                    row.Add(Line(UiText.SeatedNext, "watch-row__tag"));
                }
                else if (model.IsHost && SeatSpectator != null)
                {
                    var seatButton = new Button(() =>
                    {
                        SeatSpectator?.Invoke(id);
                        Hulla.Presentation.Navigation.Sheet.Close(panel);
                    }) { text = UiText.SeatThem };
                    seatButton.AddToClassList("watch-row__seat");
                    seatButton.SetEnabled(free > 0);
                    row.Add(seatButton);
                }


                panel.Add(row);
            }

            panel.Add(Line(UiText.WatchingTapRow, "watch-sheet__foot"));
        }

        /// <summary>The host seats a watcher from the Watching sheet; null hides Seat.</summary>
        public Action<string>? SeatSpectator { get; set; }

        /// <summary>The host removes a watcher from their player sheet; null hides Remove from table.</summary>
        public Action<string>? RemoveSpectator { get; set; }

        protected bool IsWatching(string userId)
        {
            foreach (var watcher in _client.Model.Spectators)
            {
                if (watcher.UserId == userId)
                {
                    return true;
                }
            }

            return false;
        }

        protected void OnChat(ChatLine line)
        {
            if (Muted(line.Seat))
            {
                return;
            }

            if (line.PhraseId == null && !string.IsNullOrEmpty(line.EmoteId))
            {
                // The sticker shows where it lands: by the seat it was thrown at, else by the sender (GDD 11.3).
                var at = line.TargetSeat ?? line.Seat;
                if (at == _client.Model.MySeat)
                {
                    SeatBadge.ShowEmote(_mySay, line.EmoteId);
                    _mySay.style.display = DisplayStyle.Flex;
                    _mySayLeft = SaySeconds;
                }
                else
                {
                    _ring.BadgeFor(at)?.SayEmote(line.EmoteId!, SaySeconds);
                }

                return;
            }

            var text = _chat.TextFor(line.PhraseId ?? "") ?? line.PhraseId ?? "";
            if (line.Seat == _client.Model.MySeat)
            {
                SeatBadge.ShowEmote(_mySay, null);
                _mySay.text = text;
                _mySay.style.display = DisplayStyle.Flex;
                _mySayLeft = SaySeconds;
                return;
            }

            _ring.BadgeFor(line.Seat)?.Say(text, SaySeconds);
        }

        /// <summary>A free-text line from the table channel: into the log and onto the sender's seat.</summary>
        public void ShowText(TextLine line)
        {
            if (_isMuted?.Invoke(line.SenderId) == true)
            {
                return;
            }

            var model = _client.Model;
            long? seat = null;
            foreach (var info in model.Seats)
            {
                if (info.PlayerId == line.SenderId)
                {
                    seat = info.Seat;
                }
            }

            var name = seat != null ? NameOf(seat.Value) : line.SenderName;
            var entry = new Label(name + ": " + line.Text);
            entry.AddToClassList("chat-log__line");
            _chatLog.Insert(0, entry);
            while (_chatLog.childCount > ChatLogLines)
            {
                _chatLog.RemoveAt(_chatLog.childCount - 1);
            }

            if (seat == model.MySeat)
            {
                _mySay.text = line.Text;
                _mySay.style.display = DisplayStyle.Flex;
                _mySayLeft = SaySeconds;
            }
            else if (seat != null)
            {
                _ring.BadgeFor(seat.Value)?.Say(line.Text, SaySeconds);
            }
        }

        protected async void SendTyped()
        {
            var text = _chatInput.value;
            if (_sendText == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _chatStatus.text = "";
            var sent = await _sendText(text);
            if (sent.IsOk)
            {
                _chatInput.value = "";
                return;
            }

            _chatStatus.text = UiText.ChatProblem(sent.Error.Code);
        }

        protected string NameOf(long seat)
        {
            var model = _client.Model;
            var name = seat < model.Seats.Count ? model.Seats[(int)seat].DisplayName : null;
            return name ?? (seat == model.MySeat ? UiText.You : "?");
        }

        protected string? AvatarOf(long seat)
        {
            var model = _client.Model;
            return seat < model.Seats.Count ? model.Seats[(int)seat].Avatar : null;
        }

        protected CardTableView(
            VisualElement root,
            TableClient client,
            IChatCatalog chat,
            Func<long> nowMs,
            Action leave,
            Action? openSettings,
            Action<string?>? openRules,
            Func<string, Awaitable<Domain.Result<bool>>>? sendText,
            SoundPlayer? sounds)
        {
            _root = root;
            _client = client;
            _chat = chat;
            _nowMs = nowMs;
            _leave = leave;
            _openSettings = openSettings;
            _openRules = openRules;
            _sendText = sendText;
            _sounds = sounds;
            _ring = new OpponentRing(root.Q<VisualElement>("opponents"));
            root.Q<VisualElement>("me")?.Add(_me);
            _ring.Tapped += OnBadgeTapped;
            _info = root.Q<Label>("info");
            var settingsButton = root.Q<Button>("settings");
            settingsButton.text = UiText.Settings;
            settingsButton.clicked += () => openSettings?.Invoke();
            settingsButton.style.display = DisplayStyle.None;
            var rulesButton = root.Q<Button>("rules");
            rulesButton.text = Strings.Text("rulebook.title");
            rulesButton.clicked += () => openRules?.Invoke(null);
            rulesButton.style.display = DisplayStyle.None;
            var leaveButton = root.Q<Button>("leave");
            leaveButton.text = UiText.Leave;
            leaveButton.clicked += AskLeave;
            var moreButton = root.Q<Button>("more");
            moreButton.text = "";
            moreButton.tooltip = UiText.More;
            moreButton.clicked += OpenMenu;
            var plate = root.Q<VisualElement>("info-plate");
            plate.RegisterCallback<ClickEvent>(_ => OpenFairDeal());
            _watchCount = root.Q<Label>("watch-count");
            _watchIcon = root.Q<VisualElement>("watch-icon");
            foreach (var eye in new VisualElement[] { _watchCount, _watchIcon })
            {
                eye.pickingMode = PickingMode.Position;
                eye.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    OpenWatchers();
                });
            }

            _dim = root.Q<VisualElement>("sheet-dim");
            _dim.RegisterCallback<PointerDownEvent>(_ => CloseSheets());
            _mySay = root.Q<Label>("my-say");
            _chatPanel = root.Q<VisualElement>("chat-panel");
            _strip = root.Q<VisualElement>("quick-strip");
            var chatButton = root.Q<Button>("chat");
            chatButton.text = "";
            chatButton.tooltip = UiText.Chat;
            chatButton.clicked += () => SetChatOpen(_chatPanel.style.display != DisplayStyle.Flex);
            BuildChatTabs(root.Q<VisualElement>("chat-tabs"));
            var closeChat = root.Q<Button>("chat-close");
            closeChat.text = UiText.Close;
            closeChat.clicked += () => SetChatOpen(false);
            BuildChatPanel(root.Q<VisualElement>("chat-phrases"), root.Q<VisualElement>("chat-emotes"));
            RefreshStrip();
            _chatLog = root.Q<VisualElement>("chat-log");
            _chatStatus = root.Q<Label>("chat-status");
            _chatInput = root.Q<TextField>("chat-input");
            _chatInput.textEdition.placeholder = UiText.ChatPlaceholder;
            _chatInput.maxLength = ITableChat.MaxLength;
            _chatInput.label = "";
            var send = root.Q<Button>("chat-send");
            send.text = "";
            send.tooltip = UiText.Send;
            send.AddToClassList("chat-sheet__send");
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("chat-sheet__send-icon");
            send.Add(arrow);
            send.clicked += SendTyped;
            _chatInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    SendTyped();
                }
            });
            var compose = root.Q<VisualElement>(className: "chat-panel__compose");
            compose.style.display = sendText == null ? DisplayStyle.None : DisplayStyle.Flex;
            _mic = root.Q<Button>("mic");
            _mic.style.display = DisplayStyle.None;
            _voicePill = root.Q<Label>("voice-pill");
            _speaker = root.Q<Button>("voice-speaker");
            _speaker.clicked += () => _voice?.ToggleSpeaker();
            _sheet = root.Q<VisualElement>("player-sheet");
            _sheetTitle = root.Q<Label>("sheet-title");
            _sheetActions = root.Q<VisualElement>("sheet-actions");
            _sheetStatus = root.Q<Label>("sheet-status");
            _client.Model.Chat += OnChat;
        }

        /// <summary>A game adds what it knows about a seated player to their sheet (Thulla: the cards picked up).</summary>
        protected virtual void AddPlayerFacts(VisualElement sheet, long seat)
        {
        }

        /// <summary>True while a game's own sheet must not be dismissed by a tap outside it.</summary>
        protected virtual bool SheetsLocked => false;

        /// <summary>Whether leaving now would cost this seat its place in the round.</summary>
        protected virtual bool StillPlaying(long seat) => true;

        /// <summary>A game's own entries in the menu, between Rules and Settings.</summary>
        protected virtual void AddMenuItems(Action<string, Action> item)
        {
        }

        /// <summary>After a deal, a game explains the revealed seed itself; true when it did.</summary>
        protected virtual bool ExplainFinishedDeal() => false;

        /// <summary>Add friend on the player sheet for players who are not friends or pending yet.</summary>
        public void UseFriendActions(Func<string, bool> canAdd, Action<string> addFriend)
        {
            _canAddFriend = canAdd;
            _addFriend = addFriend;
        }

        /// <summary>Stops listening to the table and the voice room; a game's own Dispose calls it.</summary>
        protected void DisposeShared()
        {
            _client.Model.Chat -= OnChat;
            if (_voice != null)
            {
                _voice.Changed -= ShowVoice;
                _micControl?.Dispose();
                _voice.SetTalking(false);
            }
        }

        /// <summary>The mic control repaints when its own state went stale, and my own bubble fades.</summary>
        protected void TickShared(float deltaSeconds)
        {
            foreach (var badge in _ring.Badges)
            {
                badge.Tick(deltaSeconds);
            }

            if (_micControl is { Stale: true })
            {
                ShowVoice();
            }

            Countdown(ref _mySayLeft, deltaSeconds, _mySay);
        }
    }
}
