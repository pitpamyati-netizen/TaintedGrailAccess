using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    // Behavioral tests of sections 7-9 against the public API. These do not
    // establish that a game host physically suppresses Rewired/EventSystem input.
    internal static class Program
    {
        private static readonly List<(string Name, Action Test)> Cases = new List<(string, Action)>
        {
            ("combat_inspection_never_plays_and_actions_are_fresh", CombatTests.InspectionAndFreshAction),
            ("combat_card_detail_empty_tooltip_and_hidden_description", CombatTests.CardDetailVisibility),
            ("combat_class_deck_nested_sides_return_and_held_input", CombatTests.ClassDeckNestedInspection),
            ("combat_reward_equal_buttons_overlap_replace_and_disappear", CombatTests.RewardActionIdentity),
            ("combat_duplicate_instance_reorder_deletion_and_empty_hand", CombatTests.DuplicateIdentityAndRemoval),
            ("combat_hand_focus_survives_targeting_first_last_card_and_empty_hand", CombatTests.HandFocusAcrossSectionsAndTargeting),
            ("combat_info_exact_chords_modifier_matrix", CombatReadoutTests.ExactChordMatrix),
            ("combat_info_passive_fresh_hold_and_native_ownership", CombatReadoutTests.PassiveFreshAndHeld),
            ("combat_info_alt_unknown_raw_and_text_capture_boundaries", CombatReadoutTests.AltAndCaptureBoundaries),
            ("escape_native_menu_opener_and_closer_require_distinct_physical_presses", EscapeTests.NativeMenuOpenerDoesNotCloseOnHold),
            ("escape_search_clear_cannot_close_menu_on_same_hold", EscapeTests.SearchCancelNeedsAnotherPressForBack),
            ("escape_repeat_retains_text_raw_and_modified_key_boundaries", EscapeTests.RepeatRespectsCaptureAndModifiers),
            ("escape_exception_after_back_does_not_repeat_or_reopen", EscapeTests.ThrowAfterBackDoesNotReplay),
            ("combat_exact_optional_display_only_and_stale_selection", CombatTests.SelectionRules),
            ("combat_target_ownership_visibility_death_and_legality_matrix", CombatTests.TargetRules),
            ("combat_log_equal_events_append_once_and_reset", CombatTests.LogNewLinesOnce),
            ("combat_read_only_status_has_no_action", CombatTests.ReadOnlyControls),
            ("game_speech_visible_markup_is_plain", FormattedSpeechTests.VisibleFormatting),
            ("game_speech_icons_keep_semantic_labels", FormattedSpeechTests.IconsKeepMeaning),
            ("game_speech_private_content_remains_literal", FormattedSpeechTests.PrivateContentIsLiteral),
            ("game_speech_private_content_has_no_ordinary_fallback", FormattedSpeechTests.PrivateContentNeverFallsBack),
            ("text_native_actual_insert_delete_space", TextEditFeedbackTests.ActualEdits),
            ("text_native_paste_replace_and_duplicate_observation", TextEditFeedbackTests.PasteAndReplacement),
            ("text_native_selection_caret_and_explicit_read", TextEditFeedbackTests.SelectionAndCaret),
            ("text_native_blur_field_change_and_empty", TextEditFeedbackTests.ResetAndFieldChange),
            ("text_native_password_never_echoes", TextEditFeedbackTests.Passwords),
            ("text_native_unicode_and_invalid_selection", TextEditFeedbackTests.UnicodeAndInvalidSelection),
            ("physical_native_text_read_and_finish_own_only_reserved_chords", PhysicalInputTests.NativeTextCommands),
            ("physical_native_raw_and_window_return_do_not_replay", PhysicalInputTests.NativeCaptureCommandsStandDown),
            ("physical_scene_replacement_retains_consumed_press", PhysicalInputTests.SceneReplacementRetainsConsumedPress),
            ("loading_overlay_owns_ready_additive_scene", PhysicalInputTests.LoadingOverlayOwnsReadyScene),
            ("initial_screen_and_focus_are_queued_once", InitialQueuedOnce),
            ("direct_focus_interrupts_without_differ_duplicate", DirectLandingOnce),
            ("programmatic_landing_queues_once", SuggestedLandingQueues),
            ("initial_live_state_is_not_duplicated", InitialLiveBaseline),
            ("async_live_delta_queues_only_changed_part", AsyncLiveDelta),
            ("synchronous_toggle_interrupts_and_rebaselines", SyncToggleBaseline),
            ("slider_repeat_feedback_is_immediate_without_live_duplicate", SliderFeedback),
            ("unseated_overlay_bubbles_enter_tooltip_secondary", UnseatedBubbles),
            ("tab_seats_overlay_and_boundary_blurs", TabSeatingAndBlur),
            ("real_text_field_stands_down_and_clears_search", TextFieldStandDown),
            ("raw_capture_stands_down_and_clears_search", RawCaptureStandDown),
            ("typeahead_is_stop_scoped_strips_markup_and_excludes_nodes", TypeaheadScope),
            ("typeahead_results_are_recomputed_from_live_state", TypeaheadLiveResults),
            ("search_survives_reference_reorder_but_clears_on_deleted_focus", SearchAndIdentity),
            ("modal_restores_covered_cursor_and_queued_name", CoveredStateRestore),
            ("popped_screen_resets_state_unless_keep_state_is_enabled", PopResetAndKeep),
            ("deepest_child_of_top_root_wins_and_orphans_are_inactive", HierarchyPriority),
            ("build_exceptions_are_isolated_logged_once_and_focus_recovers", BuildIsolation),
            ("fault_deduplication_keys_screen_and_exception_type", FaultDeduplication),
            ("claimed_chords_only_are_suppressed_per_frame", PerChordArbitration),
            ("exclusive_modal_and_nested_own_action_scope", ExclusiveAndOwnAction),
            ("closing_modal_does_not_leak_consumed_chord_in_same_frame", ModalCloseNoLeak),
            ("turn_transition_preserves_claim_and_new_context", TurnTransitionNoLeak),
            ("opening_modal_gates_toolkit_without_duplicate_activation", ModalOpenToolkitGate),
            ("held_enter_does_not_repeat_spending_action", HeldEnterNoRepeat),
            ("screen_change_clears_typeahead", ScreenChangeClearsSearch),
            ("opening_raw_capture_retains_consumed_chord_for_current_frame", RawCaptureTransitionNoLeak),
            ("state_text_failure_after_action_does_not_leak_activation", StateTextFailureAfterAction),
            ("throwing_hover_sound_does_not_duplicate_spoken_focus", HoverFailureAnnouncesOnce),
            ("consumed_submit_stays_gated_after_unseated_or_raw_transition", ToolkitSubmitTransitionNoLeak),
            ("initial_focus_uses_model_selection_and_prioritizes_valid_suggestions", InitialFocusSelectionPriority),
            ("passive_text_reads_once_then_live_changes_without_input_claims", PassiveTextLive),
            ("passive_text_hides_unavailable_and_blank_sources", PassiveTextHidden),
            ("passive_text_announcement_rechecks_source_without_retaining_views", PassiveTextFresh),
            ("keyboard_press_hold_release", KeyboardSnapshotTests.PressHoldRelease),
            ("keyboard_startup_held_key_requires_release", KeyboardSnapshotTests.StartupHeldKey),
            ("keyboard_navigation_repeat_has_delay_and_no_burst", KeyboardSnapshotTests.NavigationRepeat),
            ("keyboard_modifier_change_is_not_a_new_press", KeyboardSnapshotTests.ModifierIsNotNewPress),
            ("keyboard_window_return_requires_release", KeyboardSnapshotTests.WindowReturnRequiresRelease),
            ("keyboard_raw_and_text_capture_do_not_replay_enter", KeyboardSnapshotTests.RawAndTextCapture),
            ("keyboard_multiple_gui_passes_do_not_repeat_action", KeyboardSnapshotTests.MultipleGuiPasses),
            ("keyboard_return_and_keypad_enter_have_distinct_lifetimes", KeyboardSnapshotTests.PhysicalIdentity),
            ("keyboard_invalid_snapshot_does_not_fabricate_release", KeyboardSnapshotTests.InvalidSnapshotDoesNotRelease),
            ("keyboard_reset_requires_a_new_neutral_snapshot", KeyboardSnapshotTests.ResetRequiresNeutral),
            ("keyboard_snapshots_drive_one_action_and_retain_owned_repeats", SnapshotActionOnce),
            ("physical_navigation_gap_and_release", PhysicalInputTests.NavigationGap),
            ("physical_action_once_and_toolkit_guard", PhysicalInputTests.ActionAndToolkit),
            ("physical_raw_transition_owns_opener_only", PhysicalInputTests.RawTransition),
            ("physical_unknown_and_multiple_mapped_contributors", PhysicalInputTests.UnknownAndMappedContributors),
            ("physical_blur_return_and_disable", PhysicalInputTests.BlurAndDisable),
            ("physical_modal_text_and_unfocused", PhysicalInputTests.ModalAndUnfocused),
            ("physical_exception_after_original_action", PhysicalInputTests.ExceptionAfterAction),
            ("physical_menu_is_not_model_main_view", PhysicalInputTests.NonMainView),
        };

        private static int Main(string[] args)
        {
            bool verbose = args.Contains("--verbose");
            string filter = null;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--filter") filter = args[i + 1];
            int passed = 0, failed = 0;
            foreach (var test in Cases)
            {
                if (filter != null && test.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    test.Test();
                    passed++;
                    if (verbose) Console.WriteLine("PASS " + test.Name);
                }
                catch (Exception error)
                {
                    failed++;
                    Console.WriteLine("FAIL " + test.Name + ": " + error.Message);
                }
            }
            Console.WriteLine("Runtime: пройдено " + passed + "; ошибок " + failed + ".");
            Console.WriteLine("Проверены правила интерфейса; интеграция с запущенной игрой этими тестами не проверяется.");
            return failed == 0 && passed > 0 ? 0 : 1;
        }

        private static ControlId Id(string key) { return ControlId.Structural(key); }
        private static NodeVtable Label(string text, Action activate = null)
        { return new NodeVtable(new NodeAnnouncement(() => text, "label")) { OnActivate = activate }; }
        private static void TwoItems(GraphBuilder builder)
        { builder.BeginStop("menu"); builder.Add(Id("first"), Label("First")); builder.Add(Id("second"), Label("Second")); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message)
        { if (!Equals(expected, actual)) throw new Exception(message + " Expected=" + expected + ", actual=" + actual); }
        private static string Current(Rig rig) { return rig.Navigator.Current == null ? null : rig.Navigator.Current.Id.StructuralKey.ToString(); }
        private static KeyChord Key(UiKey key, Modifiers modifiers = Modifiers.None, bool repeat = false)
        { return new KeyChord(key, modifiers, isRepeat: repeat); }
        private static KeyChord Text(string text) { return new KeyChord(UiKey.None, text: text); }

        private static void InitialQueuedOnce()
        {
            using (var rig = new Rig())
            {
                rig.Add("main", TwoItems);
                rig.Step();
                Equal(2, rig.Speech.Lines.Count, "One screen name and one initial landing.");
                Check(rig.Speech.Lines.All(line => !line.Interrupt), "Initial speech must queue.");
                Equal("screen:main", rig.Speech.Lines[0].Text, "Screen name precedes focus.");
                Check(rig.Speech.Lines[1].Text.StartsWith("First", StringComparison.Ordinal), "Initial landing must follow screen name.");
                rig.Step(); rig.Step();
                Equal(2, rig.Speech.Lines.Count, "Idle rerenders must be silent.");
            }
        }

        private static void DirectLandingOnce()
        {
            using (var rig = new Rig())
            {
                rig.Add("main", TwoItems); rig.Step(); rig.Speech.Clear();
                rig.Step(Key(UiKey.Down));
                Equal("second", Current(rig), "Arrow moves focus.");
                Equal(1, rig.Speech.Lines.Count, "Direct landing must be spoken once.");
                Check(rig.Speech.Lines[0].Interrupt, "Direct input must interrupt.");
                rig.Step();
                Equal(1, rig.Speech.Lines.Count, "Frame differ must not repeat direct landing.");
            }
        }

        private static void SuggestedLandingQueues()
        {
            using (var rig = new Rig())
            {
                rig.Add("main", TwoItems); rig.Step(); rig.Speech.Clear();
                rig.Screens.Current.State.NextSuggestedMove = Id("second");
                rig.Step();
                Equal("second", Current(rig), "Pending game selection must reconcile.");
                Equal(1, rig.Speech.Lines.Count, "Suggested landing speaks once.");
                Check(!rig.Speech.Lines[0].Interrupt, "Differ feedback queues.");
                Check(rig.Screens.Current.State.NextSuggestedMove == null, "Suggestion is consumed.");
                rig.Step(); Equal(1, rig.Speech.Lines.Count, "Suggestion must not replay.");
            }
        }

        private static void InitialLiveBaseline()
        {
            using (var rig = new Rig())
            {
                int hp = 20;
                rig.Add("combat", b => b.Add(Id("hero"), new NodeVtable(
                    new NodeAnnouncement(() => "Hero", "label"), new NodeAnnouncement(() => "HP " + hp, "health", true))));
                rig.Step();
                Equal(2, rig.Speech.Lines.Count, "Initial live value belongs in landing, not a third utterance.");
                Check(rig.Speech.Lines[1].Text.Contains("HP 20"), "Landing includes live state.");
                rig.Step(); Equal(2, rig.Speech.Lines.Count, "Initial baseline is silent on next frame.");
            }
        }

        private static void AsyncLiveDelta()
        {
            using (var rig = new Rig())
            {
                int hp = 20, energy = 3;
                rig.Add("combat", b => b.Add(Id("hero"), new NodeVtable(new NodeAnnouncement(() => "Hero", "label"),
                    new NodeAnnouncement(() => "HP " + hp, "health", true), new NodeAnnouncement(() => "Energy " + energy, "energy", true))));
                rig.Step(); rig.Speech.Clear(); hp = 14; rig.Step();
                Equal(1, rig.Speech.Lines.Count, "Only changed live part is spoken.");
                Equal("HP 14", rig.Speech.Lines[0].Text, "Do not reread label or unchanged energy.");
                Check(!rig.Speech.Lines[0].Interrupt, "Async update queues.");
                rig.Step(); Equal(1, rig.Speech.Lines.Count, "Stable delta is not repeated.");
            }
        }

        private static void SyncToggleBaseline()
        {
            using (var rig = new Rig())
            {
                bool value = false;
                rig.Add("settings", b => b.Add(Id("toggle"), new NodeVtable(new NodeAnnouncement(() => "Voice", "label"),
                    new NodeAnnouncement(() => value ? "On" : "Off", "state", true))
                { OnActivate = () => value = !value, StateText = () => value ? "On" : "Off" }));
                rig.Step(); rig.Speech.Clear(); rig.Step(Key(UiKey.Enter));
                Equal(1, rig.Speech.Lines.Count, "Synchronous state feedback appears once.");
                Equal("On", rig.Speech.Lines[0].Text, "State text is immediate.");
                Check(rig.Speech.Lines[0].Interrupt, "Toggle feedback interrupts.");
                rig.Step(); Equal(1, rig.Speech.Lines.Count, "Live watch is rebaselined.");
            }
        }

        private static void SliderFeedback()
        {
            using (var rig = new Rig())
            {
                int volume = 10;
                rig.Add("settings", b => b.Add(Id("slider"), new NodeVtable(new NodeAnnouncement(() => "Volume", "label"),
                    new NodeAnnouncement(() => volume.ToString(), "value", true))
                { OnAdjust = (sign, large) => volume += sign * (large ? 10 : 1), StateText = () => volume.ToString() }));
                rig.Step(); rig.Speech.Clear();
                rig.Step(Key(UiKey.Right)); rig.Step(Key(UiKey.Right, repeat: true)); rig.Step(Key(UiKey.Right, Modifiers.Shift));
                Equal(22, volume, "Slider adjustment and large step must be supported.");
                Equal("11|12|22", string.Join("|", rig.Speech.Lines.Select(line => line.Text)), "Exactly one state utterance per adjustment.");
                Check(rig.Speech.Lines.All(line => line.Interrupt), "Synchronous repeated adjustments interrupt.");
                rig.Step(); Equal(3, rig.Speech.Lines.Count, "No live duplicate after adjustments.");
            }
        }

        private static void UnseatedBubbles()
        {
            using (var rig = new Rig())
            {
                int calls = 0;
                var screen = rig.Add("world", b => b.Add(Id("point"), Label("Point", () => calls++)));
                screen.Unseated = true;
                rig.Step(); rig.Speech.Clear();
                foreach (var key in new[] { UiKey.Enter, UiKey.Space, UiKey.F1, UiKey.Backspace })
                {
                    var chord = Key(key); rig.Step(chord);
                    Check(!rig.Input.ShouldSuppressGameChord(chord), "An unseated overlay must bubble " + key);
                }
                Equal(0, calls, "Unseated Enter must not activate.");
                Equal(0, rig.Speech.Lines.Count, "No tooltip-unavailable speech while unseated.");
                Check(!rig.Navigator.HasFocus, "Overlay remains unfocused.");
            }
        }

        private static void TabSeatingAndBlur()
        {
            using (var rig = new Rig())
            {
                var screen = rig.Add("world", b => { b.BeginStop("nearby"); b.Add(Id("near"), Label("Nearby")); b.BeginStop("quests"); b.Add(Id("quest"), Label("Quest")); });
                screen.Unseated = true; rig.Step(); rig.Speech.Clear();
                rig.Step(Key(UiKey.Tab)); Equal("near", Current(rig), "First Tab seats first stop.");
                Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Tab)), "Seating Tab is consumed.");
                rig.Step(Key(UiKey.Tab)); Equal("quest", Current(rig), "Second Tab reaches second stop.");
                rig.Step(Key(UiKey.Tab)); Check(!rig.Navigator.HasFocus && !rig.Navigator.IsSeated, "Tab beyond final stop blurs.");
                Equal("unfocused:world", rig.Speech.Lines.Last().Text, "Blur is announced.");
                rig.Step(Key(UiKey.Tab, Modifiers.Shift)); Equal("quest", Current(rig), "Shift+Tab seats last stop.");
            }
        }

        private static void TextFieldStandDown()
        {
            using (var rig = new Rig())
            {
                int actions = 0, contextual = 0, buildCount;
                var screen = rig.Add("dialog", b => { b.Add(Id("first"), Label("Alpha", () => actions++)); b.Add(Id("second"), Label("Beta")); });
                screen.Modal = true;
                screen.Contextual = chord => { contextual++; return false; };
                rig.Step(); rig.Step(Text("alp"));
                Check(rig.Navigator.SearchQuery.Length > 0, "Search setup must succeed.");
                rig.Speech.Clear(); contextual = 0; buildCount = screen.Builds;
                rig.StepText(true, Text("typing"), Key(UiKey.Enter), Key(UiKey.Down));
                Equal("", rig.Navigator.SearchQuery, "Real text field clears typeahead.");
                Equal(0, actions, "Enter belongs to the real text field.");
                Equal(0, contextual, "Even contextual graph verbs stand down.");
                Check(!rig.Input.ShouldSuppressGameChord(Text("typing")), "Exclusive overlay must not block real text entry.");
                Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Real field receives Enter.");
                Check(!rig.Input.ShouldSuppressToolkitSubmit(rig.Navigator.HasFocus), "Real field keeps toolkit submit path.");
                Equal(0, rig.Speech.Lines.Count, "Graph must not speak over text-field handling.");
                Equal(buildCount, screen.Builds, "Stand-down must not rebuild or reconcile graph during text input.");
            }
        }

        private static void RawCaptureStandDown()
        {
            using (var rig = new Rig())
            {
                int actions = 0;
                var screen = rig.Add("keys", b => b.Add(Id("bind"), Label("Binding", () => actions++)));
                screen.Modal = true; rig.Step(); rig.Step(Text("bind")); rig.Speech.Clear();
                int builds = screen.Builds; screen.Raw = true;
                rig.Step(Key(UiKey.Enter), Key(UiKey.Tab), Text("x"));
                Equal("", rig.Navigator.SearchQuery, "Raw capture clears search.");
                Equal(0, actions, "Captured raw Enter is not graph activation.");
                Equal(builds, screen.Builds, "Raw capture does not rebuild graph.");
                Equal(0, rig.Speech.Lines.Count, "No graph speech while raw capture is active.");
                Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Raw key reaches application.");
                Check(!rig.Input.ShouldSuppressToolkitSubmit(true), "Raw capture releases toolkit gating.");
            }
        }

        private static void TypeaheadScope()
        {
            using (var rig = new Rig())
            {
                rig.Add("cards", b =>
                {
                    b.BeginStop("hand");
                    b.Add(Id("fire"), Label("Fire"));
                    b.Add(Id("ice"), Label("<color=#abcdef>Ice &amp; snow</color>"));
                    var ignored = Label("Ice hidden"); ignored.ExcludeFromSearch = true; b.Add(Id("excluded"), ignored);
                    var other = Label("Other label"); other.SearchText = () => "<b>Ice</b> lance"; b.Add(Id("override"), other);
                    b.BeginStop("inventory"); b.Add(Id("elsewhere"), Label("Ice potion"));
                });
                rig.Step(); rig.Speech.Clear(); rig.Step(Text("iCE"));
                Equal("ice", Current(rig), "Search strips markup and ignores case.");
                rig.Step(Key(UiKey.Down)); Equal("override", Current(rig), "SearchText override is included, excluded node is skipped.");
                rig.Step(Key(UiKey.Down, repeat: true)); Equal("override", Current(rig), "Search cannot cross stop or wrap beyond final result.");
                rig.Step(Key(UiKey.Home)); Equal("ice", Current(rig), "Home reaches first search result.");
                rig.Step(Key(UiKey.End)); Equal("override", Current(rig), "End reaches last search result.");
                rig.Step(Key(UiKey.Escape)); Equal("", rig.Navigator.SearchQuery, "Escape clears typeahead.");
                Check(rig.Speech.Lines.Where(line => line.Text.Contains("Ice") || line.Text.Contains("Other")).All(line => line.Interrupt), "Search landings interrupt.");
            }
        }

        private static void TypeaheadLiveResults()
        {
            using (var rig = new Rig())
            {
                bool secondMatches = false;
                rig.Add("cards", b =>
                {
                    b.BeginStop("hand"); b.Add(Id("one"), Label("Rune one"));
                    b.Add(Id("two"), Label(secondMatches ? "Rune two" : "Plain two"));
                    b.Add(Id("three"), Label("Rune three"));
                });
                rig.Step(); rig.Step(Text("rune"));
                secondMatches = true;
                rig.Step(Key(UiKey.Down)); Equal("two", Current(rig), "Search must include newly matching live node.");
                secondMatches = false;
                rig.Step(Key(UiKey.Down)); Equal("three", Current(rig), "Search must exclude a node whose live label stopped matching.");
            }
        }

        private static void SearchAndIdentity()
        {
            using (var rig = new Rig())
            {
                var first = new Item("first", "Rune first");
                var second = new Item("second", "Rune second");
                var third = new Item("third", "Stone third");
                var items = new List<Item> { first, second, third };
                int generation = 0;
                rig.Add("list", b =>
                {
                    b.BeginStop("items");
                    foreach (var item in items) b.Add(ControlId.Referenced(item, generation + ":" + item.Id), Label(item.Label));
                });
                rig.Step(); rig.Step(Text("rune")); rig.Step(Key(UiKey.Down));
                Check(ReferenceEquals(second, rig.Navigator.Current.Id.Reference), "Setup focuses second match.");
                rig.Speech.Clear(); generation++;
                items.Clear(); items.Add(second); items.Add(first); items.Add(third);
                rig.Step();
                Check(ReferenceEquals(second, rig.Navigator.Current.Id.Reference), "Stable object identity survives structural-key reorder.");
                Equal("rune", rig.Navigator.SearchQuery, "Search remains on same object after reorder.");
                Equal(0, rig.Speech.Lines.Count, "An object merely moving is not a new landing.");
                items.Remove(second); rig.Step();
                Equal("", rig.Navigator.SearchQuery, "Deleting focused result clears search after recovery to another node.");
                Equal(1, rig.Speech.Lines.Count, "Recovery landing must be announced once.");
                Check(!rig.Speech.Lines[0].Interrupt, "Deletion-driven landing is queued.");
            }
        }

        private static void CoveredStateRestore()
        {
            using (var rig = new Rig())
            {
                var parent = rig.Add("parent", TwoItems);
                var child = rig.Add("modal", b => b.Add(Id("ok"), Label("OK"))); child.Parent = "parent"; child.Enabled = false; child.Modal = true;
                rig.Step(); rig.Step(Key(UiKey.Down));
                var state = rig.Screens.Current.State;
                child.Enabled = true; rig.Step(); Equal("modal", rig.Screens.Current.Screen.Id, "Child covers parent.");
                Equal(0, parent.Pops, "Covering does not pop parent.");
                rig.Speech.Clear(); child.Enabled = false; rig.Step();
                Check(ReferenceEquals(state, rig.Screens.Current.State), "Covered screen retains exact GraphState.");
                Equal("second", Current(rig), "Covered cursor restores to previous control.");
                Equal(2, rig.Speech.Lines.Count, "Restoration announces name then landing.");
                Equal("screen:parent", rig.Speech.Lines[0].Text, "Restored screen name comes first.");
                Check(rig.Speech.Lines.All(line => !line.Interrupt), "Restoration speech queues.");
            }
        }

        private static void PopResetAndKeep()
        {
            foreach (bool keep in new[] { false, true })
            using (var rig = new Rig())
            {
                var screen = rig.Add("screen", TwoItems); screen.Keep = keep;
                rig.Step(); rig.Step(Key(UiKey.Down)); var state = rig.Screens.Current.State;
                screen.Enabled = false; rig.Step(); Equal(1, screen.Pops, "True close invokes OnPop.");
                screen.Enabled = true; rig.Step();
                Equal(keep, ReferenceEquals(state, rig.Screens.Current.State), "Only KeepStateOnPop preserves graph object.");
                Equal(keep ? "second" : "first", Current(rig), "Reopening follows pop policy.");
            }
        }

        private static void HierarchyPriority()
        {
            using (var rig = new Rig())
            {
                var lower = rig.Add("lower", TwoItems); lower.Priority = 1;
                var lowerChild = rig.Add("lower-child", TwoItems); lowerChild.Parent = "lower"; lowerChild.Priority = 100;
                var top = rig.Add("top", TwoItems); top.Priority = 2;
                var topChild = rig.Add("top-child", TwoItems); topChild.Parent = "top"; topChild.Priority = -5;
                rig.Step(); Equal("top-child", rig.Screens.Current.Screen.Id, "Deepest active child of top root wins, not globally largest Layer.");
                top.Enabled = false; rig.Step();
                Equal("lower-child", rig.Screens.Current.Screen.Id, "Inactive parent removes its child subtree.");
                Equal(1, topChild.Pops, "Orphaned child pops with parent.");
                lower.Enabled = false; rig.Step(); Check(rig.Screens.Current == null, "No orphan may stay current.");
            }
        }

        private static void BuildIsolation()
        {
            using (var rig = new Rig())
            {
                bool broken = false; int count = 0;
                rig.Add("fragile", b => { if (broken) throw new InvalidOperationException("changing object " + count++); TwoItems(b); });
                rig.Step(); rig.Step(Key(UiKey.Down)); rig.Speech.Clear();
                broken = true; for (int i = 0; i < 5; i++) rig.Step();
                Equal(1, rig.Log.Entries.Count(entry => entry.Category == "fault"), "Build failure logs once despite varying messages.");
                Equal("second", rig.Screens.Current.State.CurKey.StructuralKey.ToString(), "Null renders preserve cursor identity.");
                broken = false; rig.Step(); Equal("second", Current(rig), "Next valid build restores previous cursor.");
                Equal(0, rig.Speech.Lines.Count, "Transient empty render must not reannounce unchanged landing.");
            }
        }

        private static void FaultDeduplication()
        {
            using (var rig = new Rig())
            {
                int variant = 0;
                var first = rig.Add("first-screen", b => { if (variant == 0) throw new InvalidOperationException("frame " + firstCounter++); throw new ArgumentException("other type"); });
                var second = rig.Add("second-screen", b => { throw new InvalidOperationException("same type on other screen"); }); second.Enabled = false;
                rig.Step(); rig.Step(); variant = 1; rig.Step(); rig.Step();
                first.Enabled = false; second.Enabled = true; rig.Step(); rig.Step();
                Equal(3, rig.Log.Entries.Count(entry => entry.Category == "fault"), "Dedup key is screen AND exception type.");
            }
        }
        private static int firstCounter;

        private static void PerChordArbitration()
        {
            var input = new InputArbitration(); var enter = Key(UiKey.Enter); var down = Key(UiKey.Down);
            input.BeginFrame(1, false, false); input.Claim(enter);
            Check(input.ShouldSuppressGameChord(enter), "Consumed chord is suppressed.");
            Check(!input.ShouldSuppressGameChord(down), "Unclaimed contextual key is preserved.");
            Check(!input.ShouldSuppressGameChord(Key(UiKey.Enter, Modifiers.Control)), "Modifiers are part of chord identity.");
            Check(input.ShouldSuppressToolkitSubmit(true), "Toolkit submit needs its own focus gate.");
            input.BeginFrame(2, false, false);
            Check(!input.ShouldSuppressGameChord(enter), "Claims expire at next frame.");
            Check(!input.ShouldSuppressToolkitSubmit(false), "Unfocused toolkit path remains available.");
        }

        private static void ExclusiveAndOwnAction()
        {
            var input = new InputArbitration(); var arbitrary = Text("game-hotkey");
            input.BeginFrame(1, true, false);
            Check(input.ShouldSuppressGameChord(arbitrary), "Exclusive modal owns unclaimed application hotkeys too.");
            using (input.EnterOwnAction())
            {
                Check(!input.ShouldSuppressGameChord(arbitrary), "Original game handler called by accessibility action is allowed.");
                using (input.EnterOwnAction()) Check(!input.ShouldSuppressToolkitSubmit(true), "Nested own-action scope is allowed.");
                Check(input.OwnAction, "Disposing inner scope must retain outer ownership.");
            }
            Check(!input.OwnAction && input.ShouldSuppressToolkitSubmit(false), "Disposing scopes restores exclusive gate.");
            input.Release(); Check(!input.ShouldSuppressGameChord(arbitrary), "Release removes all ownership.");
        }

        private static void ModalCloseNoLeak()
        {
            using (var rig = new Rig())
            {
                rig.Add("world", TwoItems);
                FakeScreen modal = null;
                modal = rig.Add("modal", b => b.Add(Id("close"), Label("Close", () => modal.Enabled = false)));
                modal.Parent = "world"; modal.Modal = true; rig.Step();
                rig.Step(Key(UiKey.Enter));
                Equal("world", rig.Screens.Current.Screen.Id, "Original action closes modal in the same update.");
                Check(!rig.Input.Exclusive, "New world context releases modal exclusivity.");
                Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Closing Enter remains claimed through end of frame.");
                Check(!rig.Input.ShouldSuppressGameChord(Text("unrelated")), "Other world chords are still free.");
                rig.Step(); Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Claim expires on following frame.");
            }
        }

        private static void TurnTransitionNoLeak()
        {
            using (var rig = new Rig())
            {
                FakeScreen turn = null;
                turn = rig.Add("player-turn", b => b.Add(Id("end"), Label("End turn", () => turn.Enabled = false)));
                var resolution = rig.Add("resolution", b => b.Add(Id("log"), Label("Enemy resolution"))); resolution.Enabled = false;
                turn.AfterUpdate = () => { if (!turn.Enabled) resolution.Enabled = true; };
                // Actual game handler changes the predicates synchronously.
                turn.Recipe = b => b.Add(Id("end"), Label("End turn", () => { turn.Enabled = false; resolution.Enabled = true; }));
                rig.Step(); rig.Speech.Clear(); rig.Step(Key(UiKey.Enter));
                Equal("resolution", rig.Screens.Current.Screen.Id, "Turn action settles the new screen before frame ends.");
                Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "End-turn chord cannot fall through to another handler.");
                Equal(2, rig.Speech.Lines.Count, "New screen name and new landing are spoken once each.");
                Check(rig.Speech.Lines.All(line => !line.Interrupt), "Resulting screen/differ feedback queues.");
            }
        }

        private static void ModalOpenToolkitGate()
        {
            using (var rig = new Rig())
            {
                int count = 0; bool ownActionWasAllowed = false;
                var modal = new FakeScreen("confirmation", b => b.Add(Id("confirm"), Label("Confirm", () => count++))) { Parent = "parent", Enabled = false, Modal = true };
                rig.Add("parent", b => b.Add(Id("open"), Label("Open", () =>
                {
                    ownActionWasAllowed = !rig.Input.ShouldSuppressToolkitSubmit(true);
                    modal.Enabled = true;
                })));
                rig.Screens.Register(modal); rig.Step(); rig.Step(Key(UiKey.Enter));
                Check(ownActionWasAllowed, "Own original-handler invocation must bypass ownership gate.");
                Equal("confirmation", rig.Screens.Current.Screen.Id, "Modal receives focus in this frame.");
                Equal(0, count, "Opening Enter must not also activate new modal.");
                Check(rig.Input.ShouldSuppressToolkitSubmit(rig.Navigator.HasFocus), "Toolkit cannot independently process the same Submit.");
                Check(rig.Input.ShouldSuppressGameChord(Text("global-action")), "New exclusive modal blocks other game paths immediately.");
            }
        }

        private static void SnapshotActionOnce()
        {
            using (var rig = new Rig())
            {
                int actions = 0;
                rig.Add("menu", b => b.Add(Id("action"), Label("Action", () => actions++)));
                var tracker = new KeyboardSnapshotTracker();
                rig.Step(tracker.Sample(0, 0, true, false, Array.Empty<HeldKey>()).ToArray());
                rig.Step(tracker.Sample(1, 0.1, true, false,
                    new[] { new HeldKey(13, Key(UiKey.Enter)) }).ToArray());
                for (int i = 2; i < 5; i++)
                {
                    rig.Step(tracker.Sample(i, i * 0.1, true, false,
                        new[] { new HeldKey(13, Key(UiKey.Enter, Modifiers.Shift)) }).ToArray());
                    Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter, Modifiers.Shift)), "Owned held chord must remain claimed without reactivating.");
                }
                Equal(1, actions, "One physical press must invoke one action, including modifier changes while held.");
                rig.Step(tracker.Sample(5, 0.5, true, false, Array.Empty<HeldKey>()).ToArray());
                Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Released key is not claimed.");
                rig.Step(tracker.Sample(6, 0.6, true, false,
                    new[] { new HeldKey(13, Key(UiKey.Enter)) }).ToArray());
                Equal(2, actions, "A separate physical press invokes a separate action.");
            }
        }

        private static void HeldEnterNoRepeat()
        {
            using (var rig = new Rig())
            {
                int purchases = 0;
                rig.Add("shop", b => b.Add(Id("buy"), Label("Buy", () => purchases++)));
                rig.Step(); rig.Step(Key(UiKey.Enter)); rig.Step(Key(UiKey.Enter, repeat: true));
                Equal(1, purchases, "Held Enter must not make another purchase/card play.");
                Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Repeat is consumed so application cannot perform repeated purchase.");
            }
        }

        private static void ScreenChangeClearsSearch()
        {
            using (var rig = new Rig())
            {
                rig.Add("base", TwoItems);
                var modal = rig.Add("modal", TwoItems); modal.Parent = "base"; modal.Enabled = false;
                rig.Step(); rig.Step(Text("first")); Equal("first", rig.Navigator.SearchQuery, "Search is active.");
                modal.Enabled = true; rig.Step(); Equal("", rig.Navigator.SearchQuery, "New screen clears old search.");
                modal.Enabled = false; rig.Step(); Equal("", rig.Navigator.SearchQuery, "Search session does not leak back through covered screen.");
            }
        }

        private static void RawCaptureTransitionNoLeak()
        {
            using (var rig = new Rig())
            {
                var capture = new FakeScreen("capture", b => b.Add(Id("raw"), Label("Press a binding")))
                { Parent = "settings", Enabled = false, Raw = true, Modal = true };
                rig.Add("settings", b => b.Add(Id("start"), Label("Change binding", () => capture.Enabled = true)));
                rig.Screens.Register(capture); rig.Step(); rig.Step(Key(UiKey.Enter));
                Equal("capture", rig.Screens.Current.Screen.Id, "Binding action opens raw capture synchronously.");
                Check(rig.Input.RawCapture, "New raw ownership must be settled immediately.");
                Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Enter which opened capture cannot become the new binding in the same frame.");
                Check(!rig.Input.ShouldSuppressGameChord(Text("new-combo")), "Other raw keys remain free in this frame.");
                rig.Step(Key(UiKey.Enter));
                Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Next-frame Enter reaches raw capture normally.");
            }
        }

        private static void StateTextFailureAfterAction()
        {
            using (var rig = new Rig())
            {
                int invocations = 0;
                rig.Add("shop", b =>
                {
                    var node = Label("Buy", () => invocations++);
                    node.StateText = () => invocations == 0 ? "Available" : throw new InvalidOperationException("UI destroyed after purchase");
                    b.Add(Id("buy"), node);
                });
                rig.Step(); rig.Step(Key(UiKey.Enter));
                Equal(1, invocations, "Original action executes exactly once.");
                Check(rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "Failure reading resulting state cannot let the application repeat activation.");
                Check(rig.Log.Entries.Any(entry => entry.Category == "fault"), "Feedback failure is observable in diagnostics.");
            }
        }

        private static void HoverFailureAnnouncesOnce()
        {
            var sound = new ThrowingSound();
            using (var rig = new Rig(sound))
            {
                rig.Add("list", b =>
                {
                    b.Add(Id("first"), Label("First"));
                    var second = Label("Second"); second.HoverSound = "failure"; b.Add(Id("second"), second);
                });
                rig.Step(); rig.Speech.Clear(); rig.Step(Key(UiKey.Down)); rig.Step();
                Equal("second", Current(rig), "Sound backend failure cannot lose the new focus.");
                Equal(1, rig.Speech.Lines.Count(line => line.Text == "Second"), "Spoken focus is remembered before optional hover effects can fail.");
                Check(rig.Speech.Lines.Single().Interrupt, "The one landing utterance remains direct feedback.");
                Check(sound.Calls >= 1, "Test must exercise the failing sound backend.");
            }
        }

        private static void ToolkitSubmitTransitionNoLeak()
        {
            foreach (bool opensRawCapture in new[] { false, true })
            using (var rig = new Rig())
            {
                var world = rig.Add("world", TwoItems); world.Unseated = true;
                var capture = new FakeScreen("capture", b => b.Add(Id("raw"), Label("Press a binding")))
                { Parent = "modal", Enabled = false, Raw = true, Modal = true };
                FakeScreen modal = null;
                modal = rig.Add("modal", b => b.Add(Id("proceed"), Label("Proceed", () =>
                {
                    if (opensRawCapture) capture.Enabled = true;
                    else modal.Enabled = false;
                })));
                modal.Parent = "world"; modal.Modal = true; rig.Screens.Register(capture);
                rig.Step(); Check(rig.Navigator.HasFocus && rig.Input.Exclusive, "Setup starts with an owned modal.");
                var enter = Key(UiKey.Enter); rig.Step(enter);
                Equal(opensRawCapture ? "capture" : "world", rig.Screens.Current.Screen.Id, "Action settles its new screen this frame.");
                Check(!rig.Navigator.HasFocus && !rig.Input.Exclusive, "A new focus/exclusivity gate cannot mask the old chord's ownership.");
                Equal(opensRawCapture, rig.Input.RawCapture, "The destination's raw-capture state is settled.");
                Check(rig.Input.ShouldSuppressGameChord(enter), "Consumed Enter remains claimed after the screen transition.");
                Check(rig.Input.ShouldSuppressToolkitSubmit(false), "Toolkit Submit must retain the consumed Enter even when focus is now absent or raw capture opened.");
                Check(!rig.Input.ShouldSuppressGameChord(Text("unrelated")), "Unrelated destination input remains available.");
                using (rig.Input.EnterOwnAction())
                    Check(!rig.Input.ShouldSuppressToolkitSubmit(false), "A deliberate original-handler call still bypasses retained ownership.");
                rig.Step(enter);
                Check(!rig.Input.ShouldSuppressGameChord(enter), "The next frame passes Enter to the destination.");
                Check(!rig.Input.ShouldSuppressToolkitSubmit(false), "The next frame also releases the toolkit Submit claim.");
            }
        }

        private static void InitialFocusSelectionPriority()
        {
            Action<GraphBuilder> recipe = b =>
            {
                b.BeginStop("overview"); b.Add(Id("requested"), Label("Overview"));
                b.BeginStop("choices"); b.Add(Id("first-choice"), Label("First choice"));
                b.Add(Id("chosen"), new NodeVtable(new NodeAnnouncement(() => "Chosen choice", "label"),
                    new NodeAnnouncement(() => "MODEL_SELECTED", "selected")));
            };
            Func<AnnouncementSettings> quietSelection = () => new AnnouncementSettings
            { SpeakPositions = false, IncludePart = (type, kind) => kind != "selected" };

            using (var rig = new Rig(settings: quietSelection()))
            {
                var screen = rig.Add("settings", recipe); screen.InitialStop = "choices";
                rig.Step(); Equal("chosen", Current(rig), "Initial stop selects the game's chosen model even when its selected word is hidden.");
                Check(rig.Speech.Lines.All(line => !line.Text.Contains("MODEL_SELECTED")), "The test's verbosity filter must actually suppress selected speech.");
            }

            foreach (bool startUnfocused in new[] { false, true })
            using (var rig = new Rig(settings: quietSelection()))
            {
                var screen = rig.Add("settings", recipe); screen.InitialStop = "choices"; screen.Unseated = startUnfocused;
                rig.Screens.Settle(); rig.Screens.Current.State.NextSuggestedMove = Id("requested");
                rig.Step(); Equal("requested", Current(rig), "A valid pending request has priority over InitialFocusStop for seated and unseated screens.");
                Check(rig.Screens.Current.State.NextSuggestedMove == null, "Valid suggestion is consumed once.");
                rig.Step(); Equal("requested", Current(rig), "A later initial-stop application must not undo the request.");
            }

            using (var rig = new Rig(settings: quietSelection()))
            {
                var screen = rig.Add("world", recipe); screen.InitialStop = "choices"; screen.Unseated = true;
                rig.Screens.Settle(); rig.Screens.Current.State.NextSuggestedMove = Id("absent");
                rig.Step(); Check(!rig.Navigator.HasFocus && !rig.Navigator.IsSeated, "An invalid suggestion must not seat an exploration overlay.");
                Check(rig.Screens.Current.State.NextSuggestedMove == null, "An absent suggestion is consumed instead of being retried indefinitely.");
                rig.Step(Key(UiKey.Tab)); Equal("chosen", Current(rig), "A later deliberate Tab can still apply the initial-stop policy.");
            }
        }

        private static void PassiveTextLive()
        {
            using (var rig = new Rig())
            {
                var identity = new object();
                string value = "Нажмите любую кнопку";
                rig.Screens.Register(new PassiveTextScreen("prompt", "Стартовый экран",
                    () => new VisibleText(identity, value)));
                rig.Step();
                Equal(2, rig.Speech.Lines.Count, "Title and visible prompt are announced once.");
                Equal(value, rig.Speech.Lines[1].Text, "Game text passes through unchanged.");
                Check(rig.Speech.Lines.All(line => !line.Interrupt), "Passive text queues.");
                rig.Step(); rig.Step();
                Equal(2, rig.Speech.Lines.Count, "Repeated builds are silent.");
                Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Enter)), "The empty stream leaves Enter to the game.");
                Check(!rig.Input.ShouldSuppressGameChord(Key(UiKey.Space)), "The empty stream leaves Space to the game.");
                value = "Нажмите кнопку для продолжения";
                rig.Step();
                Equal(3, rig.Speech.Lines.Count, "Changed visible text is read once.");
                Equal(value, rig.Speech.Lines[2].Text, "Live text reads current state.");
                Check(rig.Navigator.Current.Vtable.OnActivate == null, "A passive screen has no original action to invoke.");
            }
        }

        private static void PassiveTextHidden()
        {
            using (var rig = new Rig())
            {
                VisibleText source = null;
                rig.Screens.Register(new PassiveTextScreen("prompt", "Стартовый экран", () => source));
                rig.Step(); Equal(0, rig.Speech.Lines.Count, "An absent prompt is silent.");
                source = new VisibleText(new object(), "  ");
                rig.Step(); Equal(0, rig.Speech.Lines.Count, "Blank text is not an accessible prompt.");
                source = new VisibleText(new object(), "Видимая подсказка");
                rig.Step(); Equal(2, rig.Speech.Lines.Count, "A newly visible prompt is read.");
                source = null; rig.Step();
                Check(rig.Navigator.Current == null, "Hidden text is removed instead of served from a cache.");
                Equal(2, rig.Speech.Lines.Count, "Disappearance does not announce a false empty list.");
            }
        }

        private static void PassiveTextFresh()
        {
            VisibleText source = new VisibleText(new object(), "Старый текст");
            var screen = new PassiveTextScreen("prompt", "Стартовый экран", () => source);
            var builder = new GraphBuilder(new GraphState());
            screen.Build(builder);
            var node = builder.Build().Nodes[Id("prompt.text")];
            source = new VisibleText(new object(), "Новый текст");
            Equal("Новый текст", node.Vtable.Announcements[0].Text(), "Even an existing node rechecks the provider.");
            source = null;
            Check(node.Vtable.Announcements[0].Text() == null, "An unavailable provider cannot read an old view.");
            builder = new GraphBuilder(new GraphState());
            screen.Build(builder);
            Check(builder.Build() == null, "Build independently checks current availability.");
        }

        private sealed class Rig : IDisposable
        {
            public readonly RecordingSpeech Speech = new RecordingSpeech();
            public readonly RecordingLog Log = new RecordingLog();
            public readonly ScreenManager Screens;
            public readonly Navigator Navigator;
            public readonly InputArbitration Input = new InputArbitration();
            private readonly AccessibilityRuntime _runtime;
            private long _frame;
            public Rig(ISoundOutput sound = null, AnnouncementSettings settings = null)
            {
                var faults = new FaultBoundary(Log);
                Screens = new ScreenManager(faults);
                Navigator = new Navigator(Speech, new Localizer(new Dictionary<string, string>
                {
                    ["position"] = "{0} of {1}", ["tree.expanded"] = "Expanded", ["tree.collapsed"] = "Collapsed",
                    ["tree.empty"] = "Empty group", ["tree.leaf"] = "Leaf", ["search.none"] = "No results: {0}",
                    ["search.matches"] = "Matches: {0}", ["search.cleared"] = "Search cleared",
                    ["action.unavailable"] = "Unavailable", ["action.noSecondary"] = "No secondary action",
                    ["action.noTooltip"] = "No tooltip"
                }), faults, Log, sound, settings: settings ?? new AnnouncementSettings { SpeakPositions = false });
                _runtime = new AccessibilityRuntime(Screens, Navigator, Input);
            }
            public FakeScreen Add(string id, Action<GraphBuilder> recipe)
            { var screen = new FakeScreen(id, recipe); Screens.Register(screen); return screen; }
            public void Step(params KeyChord[] chords) { _runtime.Update(++_frame, chords); }
            public void StepText(bool active, params KeyChord[] chords) { _runtime.Update(++_frame, chords, active); }
            public void Dispose() { _runtime.Dispose(); }
        }

        private sealed class FakeScreen : AccessibleScreen
        {
            private readonly string _id;
            public bool Enabled = true, Unseated, Modal, Raw, Keep;
            public string Parent;
            public object InitialStop;
            public int Priority, Builds, Pushes, Focuses, Unfocuses, Pops;
            public Action<GraphBuilder> Recipe;
            public Func<KeyChord, bool> Contextual;
            public Func<bool> Back = null;
            public Action AfterUpdate;
            public FakeScreen(string id, Action<GraphBuilder> recipe) { _id = id; Recipe = recipe; }
            public override string Id { get { return _id; } }
            public override string ParentId { get { return Parent; } }
            public override int Layer { get { return Priority; } }
            public override string ScreenName { get { return "screen:" + _id; } }
            public override bool StartUnfocused { get { return Unseated; } }
            public override bool Exclusive { get { return Modal; } }
            public override bool CapturesRawInput { get { return Raw; } }
            public override bool KeepStateOnPop { get { return Keep; } }
            public override object InitialFocusStop { get { return InitialStop; } }
            public override string UnfocusedAnnouncement { get { return "unfocused:" + _id; } }
            public override bool IsActive() { return Enabled; }
            public override void Build(GraphBuilder builder) { Builds++; Recipe(builder); }
            public override bool OnContextualInput(KeyChord chord) { return Contextual != null && Contextual(chord); }
            public override bool OnBack() { return Back != null && Back(); }
            public override void OnPush() { Pushes++; }
            public override void OnFocus() { Focuses++; }
            public override void OnUnfocus() { Unfocuses++; }
            public override void OnPop() { Pops++; }
            public override void OnUpdate() { if (AfterUpdate != null) AfterUpdate(); }
        }

        private sealed class Item
        {
            public readonly string Id, Label;
            public Item(string id, string label) { Id = id; Label = label; }
        }
        private sealed class SpeechLine
        {
            public readonly string Text;
            public readonly bool Interrupt;
            public SpeechLine(string text, bool interrupt) { Text = text; Interrupt = interrupt; }
        }
        private sealed class RecordingSpeech : ISpeechOutput
        {
            public readonly List<SpeechLine> Lines = new List<SpeechLine>();
            public void Speak(string text, bool interrupt) { Lines.Add(new SpeechLine(text, interrupt)); }
            public void Stop() { }
            public void Clear() { Lines.Clear(); }
        }
        private sealed class LogEntry
        {
            public readonly string Category, Message;
            public LogEntry(string category, string message) { Category = category; Message = message; }
        }
        private sealed class RecordingLog : IRuntimeLog
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();
            public void Write(string category, string message) { Entries.Add(new LogEntry(category, message)); }
        }
        private sealed class ThrowingSound : ISoundOutput
        {
            public int Calls;
            public void Play(object sound) { Calls++; throw new InvalidOperationException("Audio output unavailable"); }
        }
    }
}
