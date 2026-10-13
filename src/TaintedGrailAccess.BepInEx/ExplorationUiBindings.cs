using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>Exact GOG 1.61 exploration. Retains metadata, scalar travel state and weak model labels, never views.</summary>
    internal sealed partial class TitleUiBindings
    {
        private readonly Dictionary<string, MethodInfo> _explorationMethods = new Dictionary<string, MethodInfo>();
        private readonly ExplorationTravelWatch _travelWatch = new ExplorationTravelWatch();
        private readonly ExplorationPlaceNames _placeNames = new ExplorationPlaceNames();
        private ConstructorInfo _targetChangeConstructor;
        private object _emptyUiPosition;
        private MethodInfo _getPathfinder;
        private string _travelTargetId;
        private string _lastWyrdState;
        private string _helpSource;
        private static readonly string[] ExplorationScreens = { "exploration", "locationBrief" };

        private void InitializeExploration(Assembly assembly)
        {
            CombatPath(assembly, "exploration", "Awaken.TG.Main.Heroes.Hero", "Awaken.TG.Main.Heroes.VHeroController");
            CombatPath(assembly, "location", "Awaken.TG.Main.Locations.Location", "Awaken.TG.Main.Locations.VLocation");
            CombatPath(assembly, "mapMarker", "Awaken.TG.Main.Map.Minimap.MapMarkerUI", "Awaken.TG.Main.Map.Minimap.VMapMarkerUI");
            CombatPath(assembly, "candle", "Awaken.TG.Main.Wyrding.WyrdcandlesUI", "Awaken.TG.Main.Wyrding.VWyrdcandlesUI");
            CombatPath(assembly, "locationBrief", "Awaken.TG.Main.Locations.LocationBrief", "Awaken.TG.Main.Locations.VLocationBrief");
            CombatPath(assembly, "mapCamera", "Awaken.TG.Main.Cameras.GameCamera", "Awaken.TG.Main.Cameras.VGameCamera");
            foreach (var pair in new[] {
                new[] { "movement", "Awaken.TG.Main.Heroes.Orders.HeroMovement" },
                new[] { "move", "Awaken.TG.Main.Heroes.Orders.Move" },
                new[] { "mapCoords", "Awaken.TG.Main.General.MapCoords" },
                new[] { "markerReference", "Awaken.TG.Main.Map.MapMarker" },
                new[] { "locationInteractability", "Awaken.TG.Main.Locations.LocationInteractability" },
                new[] { "mapStateStack", "Awaken.TG.MVC.UI.Handlers.States.UIStateStack" },
                new[] { "mapState", "Awaken.TG.MVC.UI.Handlers.States.UIState" },
                new[] { "mapPathfinder", "Awaken.TG.Main.Paths.Pathfinding.IPathfinder" },
                new[] { "mapMoveBlock", "Awaken.TG.Main.Heroes.Orders.HeroMoveBlock" },
                new[] { "mapBlocker", "Awaken.TG.MVC.UI.Universal.VModalBlocker" } }) AddType(assembly, pair[0], pair[1]);
            var any = assembly.GetType("Awaken.TG.MVC.World", true).GetMethods().Single(m => m.Name == "Any" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            _find.Add("mapStateStack", any.MakeGenericMethod(_types["mapStateStack"]));
            Prop("mapStateStack", "State", _types["mapState"]);
            Prop("mapState", "IsMapInteractive", typeof(bool));
            Prop("exploration", "Coords", _types["mapCoords"]); Prop("exploration", "Movement", _types["movement"]);
            Prop("movement", "CurrentMove");
            foreach (var name in new[] { "FullyVisible", "VisibleToPlayer" }) Prop("location", name, typeof(bool));
            Prop("locationView", "ShouldBeVisible", typeof(bool));
            Prop("location", "DisplayName", typeof(string)); Prop("location", "HeroTarget", _types["mapCoords"]);
            Prop("location", "Interactability", _types["locationInteractability"]);
            Field("locationInteractability", "interactable", typeof(bool));
            Field("mapCoords", "x", typeof(float)); Field("mapCoords", "z", typeof(float));
            Field("mapMarker", "reference", _types["markerReference"]);
            Prop("markerReference", "ParentModel"); Prop("markerReference", "MapCoords", _types["mapCoords"]);
            Prop("markerReference", "TooltipText", typeof(string));
            foreach (var name in new[] { "fullyIcon", "partlyIcon" }) Field("mapMarkerView", name, typeof(Image));
            Prop("mapCameraView", "Camera", typeof(Transform));
            Prop("move", "TargetPosition", _types["mapCoords"]); Prop("move", "CurrentPosition", _types["mapCoords"]);
            foreach (var name in new[] { "AnyWaypointsRemaining", "CanExecute", "Cancellable" }) Prop("move", name, typeof(bool));
            Field("candleView", "wyrdcandlesCounter", typeof(TextMeshProUGUI));
            Field("candleView", "wyrdcandleBurnRange", typeof(Image)); Field("candleView", "wyrdTouchEffect", typeof(GameObject));
            Field("candleView", "arButton", _types["button"]);
            Field("locationBriefView", "nameText", typeof(TextMeshProUGUI));
            Field("locationBriefView", "garrison", typeof(GameObject));
            Field("locationBriefView", "garrisonFirstRow", typeof(Transform)); Field("locationBriefView", "garrisonSecondRow", typeof(Transform));
            Prop("locationBrief", "Place");
            Prop("locationBrief", "IsOnHoverInfo", typeof(bool));
            var targetChange = assembly.GetType("Awaken.TG.MVC.UI.Handlers.Targets.TargetChange", true);
            var position = assembly.GetType("Awaken.TG.MVC.UI.UIPosition", true);
            _emptyUiPosition = Activator.CreateInstance(position);
            _targetChangeConstructor = targetChange.GetConstructor(new[] {
                assembly.GetType("Awaken.TG.MVC.UI.Handlers.Targets.IOrderable", true),
                assembly.GetType("Awaken.TG.MVC.UI.Handlers.Targets.ITargetable", true), position, typeof(EventModifiers), typeof(bool) })
                ?? throw new MissingMethodException(targetChange.FullName, ".ctor");
            _explorationMethods.Add("accept", RequireMethod(_types["exploration"], "AcceptTarget", targetChange));
            _explorationMethods.Add("go", RequireMethod(_types["movement"], "GoTo", _types["mapCoords"], typeof(bool), typeof(float)));
            _explorationMethods.Add("cancel", RequireMethod(_types["movement"], "CancelMove"));
            _explorationMethods.Add("brief.close", RequireMethod(_types["mapBlocker"], "Handle", assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true)));
            _explorationMethods.Add("blocked", _types["model"].GetMethods().Single(m => m.Name == "HasElement" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(_types["mapMoveBlock"]));
            _explorationMethods.Add("path", RequireMethod(_types["mapPathfinder"], "FindWaypoints", _types["mapCoords"], _types["mapCoords"], typeof(bool), Assembly.Load("AstarPathfindingProject").GetType("Pathfinding.OnPathDelegate", true)));
            _getPathfinder = assembly.GetType("Awaken.TG.MVC.Services", true).GetMethods().Single(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(_types["mapPathfinder"]);
        }

        private bool MapInteractive()
        {
            var stack = Find("mapStateStack");
            var state = ModelLive(stack) ? ReadProp("mapStateStack", "State", stack) : null;
            return state != null && (bool)ReadProp("mapState", "IsMapInteractive", state);
        }
        private bool ExplorationActive(string kind)
        {
            if (kind == "locationBrief") return ClickableLocationBrief() != null;
            // Stay registered under dialogs and fights so the scanner's GraphState survives covering.
            return ModelLive(Find("exploration")) && Find("mapStateStack") != null;
        }
        private bool ExplorationCanAct(string kind)
        {
            if (_actionScreen != kind || HelpOpen || Capturing() || !ExplorationActive(kind)) return false;
            if (View("settings") != null || View("popup") != null || View("combatMenu") != null || StoryView() != null || CombatActive("combat")) return false;
            var top = TopLayerModel();
            return kind == "locationBrief" ? top == null || ReferenceEquals(top, ClickableLocationBrief()) :
                MapInteractive() && top == null && !ExplorationActive("locationBrief");
        }
        private object ClickableLocationBrief() => All("locationBrief").FirstOrDefault(model => ModelLive(model) &&
            ExplorationPresentation.BlocksExploration(ViewOf(model, _types["locationBriefView"]) != null,
                (bool)ReadProp("locationBrief", "IsOnHoverInfo", model)));

        private IEnumerable<object> ShownLocations()
        {
            var shown = new HashSet<object>();
            foreach (var model in All("location"))
            {
                if (!ModelLive(model) || !(bool)ReadProp("location", "FullyVisible", model)) continue;
                var view = ViewOf(model, _types["locationView"]);
                if (WorldLocationShown(view) && shown.Add(model)) yield return model;
            }
            foreach (var model in All("mapMarker"))
            {
                if (!ModelLive(model)) continue;
                var view = ViewOf(model, _types["mapMarkerView"]);
                if (view == null) continue;
                var reference = ReadField("mapMarker", "reference", model);
                if (!ModelLive(reference)) continue;
                var location = ReadProp("markerReference", "ParentModel", reference);
                if (!_types["location"].IsInstanceOfType(location) || !ModelLive(location) || !(bool)ReadProp("location", "VisibleToPlayer", location)) continue;
                var full = ReadField("mapMarkerView", "fullyIcon", view) as Image;
                var partial = ReadField("mapMarkerView", "partlyIcon", view) as Image;
                if (((full != null && Visible(full) && full.enabled) || (partial != null && Visible(partial) && partial.enabled)) && shown.Add(location)) yield return location;
            }
        }
        private bool LocationShown(object location) => ShownLocations().Any(item => ReferenceEquals(item, location));
        private bool WorldLocationShown(Component view) => view != null && view.transform.lossyScale.sqrMagnitude > 0.000001f &&
            (bool)ReadProp("locationView", "ShouldBeVisible", view);
        private bool LocationDisclosed(object location)
        {
            if (!ModelLive(location) || !(bool)ReadProp("location", "FullyVisible", location)) return false;
            var worldView = ViewOf(location, _types["locationView"]);
            if (WorldLocationShown(worldView)) return true;
            foreach (var marker in All("mapMarker"))
            {
                if (!ModelLive(marker)) continue;
                var reference = ReadField("mapMarker", "reference", marker);
                if (!ModelLive(reference) || !ReferenceEquals(ReadProp("markerReference", "ParentModel", reference), location)) continue;
                var view = ViewOf(marker, _types["mapMarkerView"]);
                var icon = view == null ? null : ReadField("mapMarkerView", "fullyIcon", view) as Image;
                if (icon != null && Visible(icon) && icon.enabled) return true;
            }
            return false;
        }
        private object LocationCoords(object location) => ReadProp("location", "HeroTarget", location);
        private bool LocationInteractable(object location) => (bool)ReadField("locationInteractability", "interactable", ReadProp("location", "Interactability", location));
        private string LocationLabel(object location) => !LocationShown(location) ? null :
            ExplorationPresentation.PlaceLabel(LocationDisclosed(location), () => ShownLocationNames(location),
                () => MarkerLocationLabel(location, true), () => AnonymousLocationLabel(location));
        private string AnonymousLocationLabel(object location) => MarkerLocationLabel(location, false);
        private string MarkerLocationLabel(object location, bool disclosed)
        {
            var number = _placeNames.Number(location);
            var kind = ShownLocationMarkerKind(location);
            return kind == null ? _text.Get(disclosed ? "exploration.numberedPlace" : "exploration.numberedUnknownPlace", number) :
                _text.Get(disclosed ? "exploration.numberedKind" : "exploration.numberedPartialKind", _text.Get("exploration.kind." + kind), number);
        }
        private string ShownLocationMarkerKind(object location)
        {
            foreach (var marker in All("mapMarker"))
            {
                if (!ModelLive(marker)) continue;
                var reference = ReadField("mapMarker", "reference", marker);
                if (!ModelLive(reference) || !ReferenceEquals(ReadProp("markerReference", "ParentModel", reference), location)) continue;
                var view = ViewOf(marker, _types["mapMarkerView"]);
                if (view == null) continue;
                foreach (var field in new[] { "fullyIcon", "partlyIcon" })
                {
                    var image = ReadField("mapMarkerView", field, view) as Image;
                    if (image == null) continue;
                    var kind = ExplorationPresentation.MarkerKind(Visible(image) && image.color.a > 0f, image.enabled,
                        () => image.sprite == null ? null : image.sprite.name);
                    if (kind != null) return kind;
                }
            }
            return null;
        }
        private string BriefLabel(object brief)
        {
            if (!ModelLive(brief)) return null;
            var place = ReadProp("locationBrief", "Place", brief);
            if (_types["location"].IsInstanceOfType(place)) return LocationLabel(place);
            var view = ViewOf(brief, _types["locationBriefView"]);
            return view == null ? null : ExplorationPresentation.PlaceLabel(true,
                () => new[] { PlainGameText(ShownText("locationBriefView", "nameText", view)) },
                () => _text.Get("exploration.unnamedPlace"), () => _text.Get("exploration.unknownPlace"));
        }
        private IEnumerable<string> ShownLocationNames(object location)
        {
            foreach (var brief in All("locationBrief"))
            {
                if (!ModelLive(brief) || !ReferenceEquals(ReadProp("locationBrief", "Place", brief), location)) continue;
                var view = ViewOf(brief, _types["locationBriefView"]);
                if (view != null && view.transform.lossyScale.sqrMagnitude > 0.000001f)
                    yield return PlainGameText(ShownText("locationBriefView", "nameText", view));
            }
            // MapMarker.TooltipText is the name actually shown on the full icon, created by Place.
            // It can differ from a later DisplayName override. Never read it for a partial icon.
            foreach (var marker in All("mapMarker"))
            {
                if (!ModelLive(marker)) continue;
                var reference = ReadField("mapMarker", "reference", marker);
                if (!ModelLive(reference) || !ReferenceEquals(ReadProp("markerReference", "ParentModel", reference), location)) continue;
                var view = ViewOf(marker, _types["mapMarkerView"]);
                var icon = view == null ? null : ReadField("mapMarkerView", "fullyIcon", view) as Image;
                if (icon != null && Visible(icon) && icon.enabled)
                    yield return PlainGameText((string)ReadProp("markerReference", "TooltipText", reference));
            }
            yield return PlainGameText((string)ReadProp("location", "DisplayName", location));
        }
        private MapPoint Point(object coords) => new MapPoint((float)ReadField("mapCoords", "x", coords), (float)ReadField("mapCoords", "z", coords));
        private object HeroCoords() => ReadProp("exploration", "Coords", Find("exploration"));
        private string LocationGeometry(object location)
        {
            if (!LocationShown(location)) return null;
            var origin = Point(HeroCoords()); var target = Point(LocationCoords(location));
            var camera = View("mapCamera");
            var cameraTransform = camera == null ? null : ReadProp("mapCameraView", "Camera", camera) as Transform;
            var forward = cameraTransform == null ? Vector3.zero : cameraTransform.forward;
            return _text.Get("exploration.geometry", _text.Get("exploration.direction." + ExplorationPresentation.Direction(origin, target, forward.x, forward.z)), origin.DistanceTo(target).ToString("F1"));
        }
        private string LocationReason(object location)
        {
            if (!LocationDisclosed(location)) return _text.Get("exploration.partial");
            return JoinText(new[] { LocationInteractable(location) ? null : _text.Get("exploration.inactive"), LocationHazard(location) });
        }
        private string LocationHazard(object location)
        {
            // Count only the actual displayed enemy icons, never CombatAction.EnemyTemplates.
            foreach (var brief in All("locationBrief"))
            {
                if (!ModelLive(brief) || !ReferenceEquals(ReadProp("locationBrief", "Place", brief), location)) continue;
                var view = ViewOf(brief, _types["locationBriefView"]);
                if (view == null || view.transform.lossyScale.sqrMagnitude <= 0.000001f || !((GameObject)ReadField("locationBriefView", "garrison", view)).activeInHierarchy) continue;
                int count = 0;
                foreach (var field in new[] { "garrisonFirstRow", "garrisonSecondRow" })
                {
                    var row = (Transform)ReadField("locationBriefView", field, view);
                    foreach (Transform icon in row)
                        if (icon.gameObject.activeInHierarchy && icon.GetComponentsInChildren<Image>(false).Any(Visible)) count++;
                }
                if (count > 0) return _text.Get("exploration.enemies", count);
            }
            return null;
        }
        private RouteInspection InspectRoute(object location)
        {
            var origin = HeroCoords(); var target = LocationCoords(location);
            var service = _getPathfinder.Invoke(_properties["world.Services"].GetValue(World), null);
            // A query without the hero's Seeker cannot replace its current movement request.
            var path = _explorationMethods["path"].Invoke(service, new[] { origin, target, (object)false, null }) as IEnumerable;
            var route = RouteInspection.Inspect(path == null ? null : path.Cast<object>().Select(Point), Point(origin), Point(target), 3);
            _log.Write("exploration.route", "status=" + route.Availability + "; points=" + route.PointCount +
                "; start-gap=" + route.StartGap + "; end-gap=" + route.EndGap + "; length=" + route.Length);
            return route;
        }
        private string RouteText(RouteInspection route) => route.Availability == RouteAvailability.Complete ?
            _text.Get("exploration.route.complete", route.Length.ToString("F1")) : route.Availability == RouteAvailability.Partial ?
            _text.Get("exploration.route.partial", route.StartGap.ToString("F1"), route.EndGap.ToString("F1"), route.Length.ToString("F1")) :
            _text.Get("exploration.route." + route.Availability.ToString().ToLowerInvariant());

        private void MoveToLocation(object location)
        {
            if (!ExplorationCanAct("exploration") || !LocationShown(location)) return;
            var hero = Find("exploration"); var movement = ReadProp("exploration", "Movement", hero);
            if (!ModelLive(movement) || (bool)_explorationMethods["blocked"].Invoke(movement, null))
            { _speech?.Speak(_text.Get("exploration.blocked"), true); return; }
            if (!Point(HeroCoords()).IsFinite || !Point(LocationCoords(location)).IsFinite)
            { _speech?.Speak(_text.Get("exploration.invalidTarget"), true); return; }
            var distance = Point(HeroCoords()).DistanceTo(Point(LocationCoords(location)));
            _log.Write("exploration.action", "target=" + ReadProp("model", "ID", location) + "; distance=" + distance +
                "; disclosed=" + LocationDisclosed(location) + "; interactable=" + (LocationDisclosed(location) && LocationInteractable(location)));
            // FindWaypoints is a passive estimate with different constraints/modifiers from
            // HeroMovement.PlanMove's FindWaypointsFor(Hero.Seeker). It cannot veto an original
            // action, including the game's close-click interaction. Only the game plans movement.
            bool accepted = true;
            var previousMove = ReadProp("movement", "CurrentMove", movement);
            if (LocationDisclosed(location))
            {
                var change = _targetChangeConstructor.Invoke(new[] { hero, location, _emptyUiPosition, (object)EventModifiers.None, false });
                Invoke("exploration.move", () => accepted = (bool)_explorationMethods["accept"].Invoke(hero, new[] { change }));
            }
            else
                Invoke("exploration.approachMarker", () => _explorationMethods["go"].Invoke(movement, new[] { LocationCoords(location), (object)true, 1f }));
            var move = ReadProp("movement", "CurrentMove", movement);
            // A close-click can open interaction while AcceptTarget returns false (no Move created).
            if (!MapInteractive() || ExplorationActive("locationBrief") || StoryView() != null)
            { _travelWatch.Reset(); _travelTargetId = null; return; }
            if (!accepted || !_types["move"].IsInstanceOfType(move) || !ModelLive(move))
            {
                var message = LocationDisclosed(location) && distance < 4 ?
                    LocationInteractable(location) ? "exploration.atNoAction" : "exploration.atInactive" : "exploration.noMove";
                _speech?.Speak(_text.Get(message), true); return;
            }
            if (ReferenceEquals(previousMove, move)) { _speech?.Speak(_text.Get("exploration.noMove"), true); return; }
            _travelTargetId = (string)ReadProp("model", "ID", location);
            _travelWatch.Begin((string)ReadProp("model", "ID", move), Point(HeroCoords()), Time.unscaledTime, Point(HeroCoords()).DistanceTo(Point(LocationCoords(location))));
            _speech?.Speak(JoinText(new[] { _text.Get("exploration.started"), LocationDisclosed(location) && !LocationInteractable(location) ? _text.Get("exploration.inactive") : null }), true);
        }

        private void BuildExploration(string kind, GraphBuilder builder)
        {
            if (kind == "locationBrief")
            {
                var brief = ClickableLocationBrief();
                var view = ViewOf(brief, _types["locationBriefView"]);
                builder.Add(ControlId.Structural("exploration.brief.name"), new NodeVtable(new NodeAnnouncement(() => BriefLabel(brief))));
                foreach (Component button in view.GetComponentsInChildren(_types["button"], false))
                    AddButton(builder, kind, button, ControlId.ForObject(button), "exploration.brief.action");
                builder.Add(ControlId.Structural("exploration.brief.close"), new NodeVtable(new NodeAnnouncement(() => _text.Get("exploration.close"))) { OnActivate = () => ExplorationBack(kind) });
                return;
            }
            if (!MapInteractive()) return;
            builder.BeginStop("exploration.objects"); builder.PushContext(_text.Get("exploration.objects"));
            var locations = ShownLocations().ToArray();
            // Assign once before distance sorting; walking, temporary hiding and disclosure keep the same number.
            foreach (var location in locations) _placeNames.Number(location);
            locations = locations.OrderBy(location => Point(HeroCoords()).DistanceTo(Point(LocationCoords(location)))).ToArray();
            foreach (var location in locations)
            {
                var current = location;
                ExplorationPresentation.Add(builder, current, InstanceKey("exploration.location", current),
                    () => LocationDisclosed(current), () => LocationShown(current),
                    () => LocationLabel(current), () => AnonymousLocationLabel(current),
                    () => LocationGeometry(current), () => LocationReason(current), () => MoveToLocation(current),
                    ReadDetail(() => ExplorationCanAct(kind) && LocationShown(current) ? JoinText(new[] { LocationGeometry(current), LocationReason(current), RouteText(InspectRoute(current)) }) : null));
            }
            if (locations.Length == 0) builder.Add(ControlId.Structural("exploration.empty"), new NodeVtable(new NodeAnnouncement(() => _text.Get("exploration.empty"))));
            builder.PopContext();
            builder.BeginStop("exploration.travel"); builder.PushContext(_text.Get("exploration.travel"));
            builder.Add(ControlId.Structural("exploration.travel.state"), new NodeVtable(new NodeAnnouncement(() => TravelState(), "label")) { OnTooltip = ReadDetail(TravelState) });
            builder.Add(ControlId.Structural("exploration.cancel"), new NodeVtable(new NodeAnnouncement(() => _text.Get("exploration.cancel"))) { OnActivate = () => CancelExplorationMove() });
            builder.PopContext();
            var candle = View("candle");
            if (candle == null) return;
            builder.BeginStop("exploration.light"); builder.PushContext(_text.Get("exploration.light"));
            builder.Add(ControlId.Structural("exploration.light.state"), new NodeVtable(new NodeAnnouncement(() => WyrdState(), "label")) { OnTooltip = ReadDetail(() => TooltipText(View("candle"))) });
            AddButton(builder, kind, (Component)ReadField("candleView", "arButton", candle), ControlId.Structural("exploration.burn"), "exploration.burn");
            builder.PopContext();
        }
        private string TravelState()
        {
            var hero = Find("exploration");
            if (!ModelLive(hero)) return null;
            var movement = ReadProp("exploration", "Movement", hero);
            var move = ModelLive(movement) ? ReadProp("movement", "CurrentMove", movement) : null;
            if (!_types["move"].IsInstanceOfType(move) || !ModelLive(move)) return _text.Get("exploration.idle");
            return _text.Get("exploration.remaining", Point(HeroCoords()).DistanceTo(Point(ReadProp("move", "TargetPosition", move))).ToString("F1"));
        }
        private string WyrdState()
        {
            var view = View("candle");
            if (view == null) return null;
            var counter = ShownText("candleView", "wyrdcandlesCounter", view);
            var range = ReadField("candleView", "wyrdcandleBurnRange", view) as Image;
            var effect = ReadField("candleView", "wyrdTouchEffect", view) as GameObject;
            return JoinText(new[] { counter == null ? null : _text.Get("exploration.candles", counter),
                Visible(range) ? _text.Get("exploration.lightLevel", Math.Round(range.fillAmount * 100)) : null,
                effect != null && effect.activeInHierarchy ? _text.Get("exploration.wyrdTouch") : null });
        }
        private bool CancelExplorationMove()
        {
            if (!ExplorationCanAct("exploration")) return false;
            var movement = ReadProp("exploration", "Movement", Find("exploration"));
            var move = ReadProp("movement", "CurrentMove", movement);
            if (move == null) return false;
            // Only cancellable ordinary Move; do not cancel combat/time actions or unfamiliar orders.
            if (!_types["move"].IsInstanceOfType(move) || !ModelLive(move) || !(bool)ReadProp("move", "Cancellable", move)) return false;
            Invoke("exploration.cancel", () => _explorationMethods["cancel"].Invoke(movement, null));
            _travelWatch.Reset(); _travelTargetId = null;
            _speech?.Speak(_text.Get("exploration.cancelled"), true);
            return true;
        }
        private bool ExplorationBack(string kind)
        {
            if (kind == "exploration") return CancelExplorationMove();
            if (kind != "locationBrief" || !ExplorationCanAct(kind)) return false;
            var blocker = ViewOf(ClickableLocationBrief(), _types["mapBlocker"]);
            if (blocker == null) return false;
            Invoke("exploration.brief.close", () => _explorationMethods["brief.close"].Invoke(blocker, new[] { _submit }));
            return true;
        }
        public void ObserveExploration()
        {
            var hero = Find("exploration");
            if (!ModelLive(hero)) { ClearExploration(); return; }
            var movement = ReadProp("exploration", "Movement", hero);
            var move = ModelLive(movement) ? ReadProp("movement", "CurrentMove", movement) : null;
            bool ordinaryMove = _types["move"].IsInstanceOfType(move) && ModelLive(move);
            var position = Point(HeroCoords());
            var target = _travelTargetId == null ? null : All("location").FirstOrDefault(location => ModelLive(location) && (string)ReadProp("model", "ID", location) == _travelTargetId);
            bool targetShown = target != null && LocationShown(target);
            if (_travelWatch.IsTracking && ordinaryMove && !targetShown && MapInteractive())
            {
                // A discarded/hidden destination must not cause an unseen interaction later.
                // Stop only the order we started; a replacement/native order belongs to the game.
                if (_travelWatch.Owns((string)ReadProp("model", "ID", move)))
                {
                    Invoke("exploration.targetLost", () => _explorationMethods["cancel"].Invoke(movement, null));
                    _speech?.Speak(_text.Get("exploration.targetLost"), false);
                    _travelWatch.Reset(); _travelTargetId = null; return;
                }
            }
            bool arrived = targetShown && position.DistanceTo(Point(LocationCoords(target))) <= 4;
            var state = _travelWatch.Observe(ordinaryMove ? (string)ReadProp("model", "ID", move) : null,
                position, Time.unscaledTime, ordinaryMove ? position.DistanceTo(Point(ReadProp("move", "TargetPosition", move))) : 0,
                MapInteractive() && ordinaryMove && View("combatMenu") == null, Application.isFocused && !HelpOpen, arrived);
            if (state != null)
            {
                _log.Write("exploration.travel", state);
                _speech?.Speak(state == "progress" ? TravelState() : state == "arrived" && targetShown &&
                    LocationDisclosed(target) && !LocationInteractable(target) ? _text.Get("exploration.atInactive") : _text.Get("exploration." + state), false);
                if (!_travelWatch.IsTracking) _travelTargetId = null;
            }
            if (_actionScreen != "exploration" || !MapInteractive() || HelpOpen || Capturing()) return;
            var candle = View("candle");
            if (candle == null) { _lastWyrdState = null; return; }
            var range = ReadField("candleView", "wyrdcandleBurnRange", candle) as Image;
            var effect = ReadField("candleView", "wyrdTouchEffect", candle) as GameObject;
            var stamp = ShownText("candleView", "wyrdcandlesCounter", candle) + ":" +
                (Visible(range) ? (int)Math.Floor(range.fillAmount * 4) : -1) + ":" + (effect != null && effect.activeInHierarchy);
            // Compare quarter-bar crossings, count and the shown hazard flash; don't narrate every animation frame.
            if (_lastWyrdState != null && stamp != _lastWyrdState) _speech?.Speak(WyrdState(), false);
            _lastWyrdState = stamp;
        }
        public void ClearExploration() { _travelWatch.Reset(); _placeNames.Reset(); _travelTargetId = null; _lastWyrdState = null; _helpSource = null; }
        public void PauseExploration() { _travelWatch.Pause(Time.unscaledTime); }
    }
}
