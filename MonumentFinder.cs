using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Monument Finder", "WhiteThunder", "3.1.2")]
    [Description("Find monuments with commands or API.")]
    internal class MonumentFinder : CovalencePlugin
    {
        #region Fields

        private static MonumentFinder _pluginInstance;
        private static Configuration _pluginConfig;

        private const string PermissionFind = "monumentfinder.find";

        private const float DrawDuration = 30;

        private readonly FieldInfo DungeonBaseLinksFieldInfo = typeof(TerrainPath).GetField("DungeonBaseLinks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private Dictionary<MonumentInfo, NormalMonumentAdapter> _normalMonuments = new Dictionary<MonumentInfo, NormalMonumentAdapter>();
        private Dictionary<DungeonGridCell, TrainTunnelAdapter> _trainTunnels = new Dictionary<DungeonGridCell, TrainTunnelAdapter>();
        private Dictionary<DungeonBaseLink, UnderwaterLabLinkAdapter> _labModules = new Dictionary<DungeonBaseLink, UnderwaterLabLinkAdapter>();
        private Dictionary<MonoBehaviour, BaseMonumentAdapter> _allMonuments = new Dictionary<MonoBehaviour, BaseMonumentAdapter>();

        private Collider[] _colliderBuffer = new Collider[8];

        #endregion

        #region Hooks

        private void Init()
        {
            _pluginInstance = this;
            permission.RegisterPermission(PermissionFind, this);

            AddCovalenceCommand(_pluginConfig.Command, nameof(CommandFind));
        }

        private void Unload()
        {
            _pluginConfig = null;
            _pluginInstance = null;
        }

        private void OnServerInitialized()
        {
            if (DungeonBaseLinksFieldInfo != null)
            {
                var dungeonLinks = DungeonBaseLinksFieldInfo.GetValue(TerrainMeta.Path) as List<DungeonBaseLink>;
                if (dungeonLinks != null)
                {
                    foreach (var link in dungeonLinks)
                    {
                        // End links represent the posts holding up the lab modules.
                        if (link.Type == DungeonBaseLinkType.End)
                            continue;

                        var labLink = new UnderwaterLabLinkAdapter(link);
                        _labModules[link] = labLink;
                        _allMonuments[link] = labLink;
                    }
                }
            }

            foreach (var dungeonCell in TerrainMeta.Path.DungeonGridCells)
            {
                if (TrainTunnelAdapter.IgnoredPrefabs.Contains(dungeonCell.name))
                    continue;

                try
                {
                    var trainTunnel = new TrainTunnelAdapter(dungeonCell);
                    _trainTunnels[dungeonCell] = trainTunnel;
                    _allMonuments[dungeonCell] = trainTunnel;
                }
                catch (NotImplementedException exception)
                {
                    LogWarning(exception.Message);
                }
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                var normalMonument = new NormalMonumentAdapter(monument);
                _normalMonuments[monument] = normalMonument;
                _allMonuments[monument] = normalMonument;
            }
        }

        #endregion

        #region API

        private Dictionary<string, object> API_GetClosest(Vector3 position) =>
            GetClosestMonumentForAPI(_allMonuments.Values, position);

        private Dictionary<string, object> API_GetClosestMonument(Vector3 position) =>
            GetClosestMonumentForAPI(_normalMonuments.Values, position);

        private Dictionary<string, object> API_GetClosestTrainTunnel(Vector3 position) =>
            GetClosestMonumentForAPI(_trainTunnels.Values, position);

        private Dictionary<string, object> API_GetClosestUnderwaterLabModules(Vector3 position) =>
            GetClosestMonumentForAPI(_labModules.Values, position);

        private List<Dictionary<string, object>> API_FindMonuments(string filter) =>
            FilterMonumentsForAPI(_normalMonuments.Values, filter);

        private List<Dictionary<string, object>> API_FindTrainTunnels(string filter) =>
            FilterMonumentsForAPI(_trainTunnels.Values, filter);

        private List<Dictionary<string, object>> API_FindUnderwaterLabModules(string filter) =>
            FilterMonumentsForAPI(_labModules.Values, filter);

        private List<Dictionary<string, object>> API_Find(string filter) =>
            FilterMonumentsForAPI(_allMonuments.Values, filter);

        private List<Dictionary<string, object>> API_FindByShortName(string shortName) =>
            FilterMonumentsForAPI(_allMonuments.Values, shortName: shortName);

        private List<Dictionary<string, object>> API_FindByAlias(string alias) =>
            FilterMonumentsForAPI(_allMonuments.Values, alias: alias);

        // Kept for backwards compatibility with previous versions.
        private List<MonumentInfo> FindMonuments(string filter)
        {
            var monuments = new List<MonumentInfo>();

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (!monument.name.Contains("/monument/") || !string.IsNullOrEmpty(filter) &&
                    !monument.Type.ToString().Contains(filter, CompareOptions.IgnoreCase) &&
                    !monument.name.Contains(filter, CompareOptions.IgnoreCase))
                    continue;

                monuments.Add(monument);
            }

            return monuments;
        }

        #endregion

        #region Commands

        private void CommandFind(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermissionFind))
            {
                ReplyToPlayer(player, Lang.ErrorNoPermission);
                return;
            }

            if (args == null || args.Length == 0)
            {
                SubcommandHelp(player, command);
                return;
            }

            switch (args[0].ToLower())
            {
                case "find":
                case "f":
                case "list":
                case "l":
                {
                    SubcommandList(player, command, args.Skip(1).ToArray());
                    return;
                }

                case "show":
                case "s":
                case "view":
                case "v":
                {
                    SubcommandShow(player, command, args.Skip(1).ToArray());
                    break;
                }

                case "closest":
                case "nearest":
                {
                    SubcommandClosest(player, command, args.Skip(1).ToArray());
                    break;
                }

                default:
                {
                    SubcommandHelp(player, command);
                    break;
                }
            }
        }

        private void SubcommandHelp(IPlayer player, string command)
        {
            var builder = new StringBuilder();
            builder.AppendLine(GetMessage(player, Lang.HelpHeader));
            builder.AppendLine(GetMessage(player, Lang.HelpList, command));
            builder.AppendLine(GetMessage(player, Lang.HelpShow, command));
            builder.AppendLine(GetMessage(player, Lang.HelpClosest, command));
            builder.AppendLine(GetMessage(player, Lang.HelpClosestConfig, command));
            player.Reply(builder.ToString());
        }

        private void SubcommandList(IPlayer player, string cmd, string[] args)
        {
            var filterArg = args.Length >= 1 ? args[0] : string.Empty;
            var monuments = FilterMonuments(_allMonuments.Values, filterArg);

            if (monuments.Count == 0)
            {
                ReplyToPlayer(player, Lang.NoMonumentsFound);
                return;
            }

            PrintMonumentList(player, monuments);
        }

        private void SubcommandShow(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
                return;

            var filterArg = args.Length >= 1 ? args[0] : string.Empty;
            var monuments = FilterMonuments(_allMonuments.Values, filterArg);
            if (monuments.Count == 0)
            {
                ReplyToPlayer(player, Lang.NoMonumentsFound);
                return;
            }

            foreach (var monument in monuments)
                ShowMonumentName(basePlayer, monument);
        }

        private void SubcommandClosest(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
                return;

            var position = basePlayer.transform.position;
            var monument = GetClosestMonument(_allMonuments.Values, position);
            if (monument == null)
            {
                ReplyToPlayer(player, Lang.NoMonumentsFound);
                return;
            }

            var firstArg = args.FirstOrDefault() ?? string.Empty;
            if (firstArg.Equals("config", StringComparison.CurrentCultureIgnoreCase))
            {
                var aliasOrShortName = monument.Alias ?? monument.ShortName;
                if (_pluginConfig.AddMonument(aliasOrShortName, monument))
                {
                    Config.WriteObject(_pluginConfig, true);
                    ReplyToPlayer(player, Lang.ClosestConfigSuccess, aliasOrShortName);
                }
                else
                {
                    ReplyToPlayer(player, Lang.ClosestConfigAlreadyPresent, aliasOrShortName);
                }
            }
            else
            {
                if (monument.IsInBounds(position))
                {
                    var relativePosition = monument.InverseTransformPoint(position);
                    ReplyToPlayer(player, Lang.AtMonument, monument.PrefabName, relativePosition);
                }
                else
                {
                    var closestPoint = monument.ClosestPointOnBounds(position);
                    var distance = (position - closestPoint).magnitude;
                    ReplyToPlayer(player, Lang.ClosestMonument, monument.PrefabName, distance);
                }
            }

            if (basePlayer.IsAdmin)
            {
                ShowMonumentName(basePlayer, monument);

                var withBoundingBox = monument as SingleBoundingBox;
                if (withBoundingBox != null)
                {
                    var boundingBox = withBoundingBox.BoundingBox;
                    if (boundingBox.extents != Vector3.zero)
                    {
                        Ddraw.Box(basePlayer, boundingBox, Color.magenta, DrawDuration);
                    }

                    return;
                }

                var withMultipleBoundingBoxes = monument as MultipleBoundingBoxes;
                if (withMultipleBoundingBoxes != null)
                {
                    foreach (var boundingBox in withMultipleBoundingBoxes.BoundingBoxes)
                    {
                        Ddraw.Box(basePlayer, boundingBox, Color.magenta, DrawDuration, showInfo: false);
                    }
                }
            }
        }

        #endregion

        #region Helper Methods

        private static void LogError(string message) => Interface.Oxide.LogError($"[Monument Finder] {message}");

        private static void LogWarning(string message) => Interface.Oxide.LogWarning($"[Monument Finder] {message}");

        private static bool IsCustomMonument(MonumentInfo monumentInfo)
        {
            return monumentInfo.name.Contains("monument_marker.prefab");
        }

        private static Collider FindPreventBuildingVolume(Vector3 position)
        {
            var buffer = _pluginInstance._colliderBuffer;
            var count = Physics.OverlapSphereNonAlloc(position, 1, buffer, Rust.Layers.Mask.Prevent_Building, QueryTriggerInteraction.Ignore);

            if (count == 0)
                return null;

            for (var i = 0; i < count; i++)
            {
                var collider = buffer[i];
                if ((collider is BoxCollider || collider is SphereCollider)
                    // Only count prevent_building prefabs, not all prefabs that have prevent building colliders.
                    && collider.name.Contains("prevent_building", CompareOptions.IgnoreCase))
                    return collider;
            }

            return null;
        }

        private static T GetClosestMonument<T>(IEnumerable<T> monumentList, Vector3 position) where T : BaseMonumentAdapter
        {
            T closestMonument = null;
            var closestSqrDistance = float.MaxValue;

            foreach (var baseMonument in monumentList)
            {
                var currentSqrDistance = (position - baseMonument.ClosestPointOnBounds(position)).sqrMagnitude;
                if (currentSqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = currentSqrDistance;
                    closestMonument = baseMonument;
                }
            }

            return closestMonument;
        }

        private static Dictionary<string, object> GetClosestMonumentForAPI(IEnumerable<BaseMonumentAdapter> monumentList, Vector3 position)
        {
            return GetClosestMonument(monumentList, position)?.APIResult;
        }

        private static List<T> FilterMonuments<T>(IEnumerable<T> monumentList, string filter = null, string shortName = null, string alias = null) where T : BaseMonumentAdapter
        {
            var results = new List<T>();

            foreach (var baseMonument in monumentList)
            {
                if (baseMonument.MatchesFilter(filter, shortName, alias))
                    results.Add(baseMonument);
            }

            return results;
        }

        private static List<Dictionary<string, object>> FilterMonumentsForAPI(IEnumerable<BaseMonumentAdapter> monumentList, string filter = null, string shortName = null, string alias = null)
        {
            var results = new List<Dictionary<string, object>>();

            foreach (var baseMonument in monumentList)
            {
                if (baseMonument.MatchesFilter(filter, shortName, alias))
                {
                    results.Add(baseMonument.APIResult);
                }
            }

            return results;
        }

        private void PrintMonumentList(IPlayer player, IEnumerable<BaseMonumentAdapter> monuments)
        {
            var builder = new StringBuilder();
            builder.AppendLine(GetMessage(player, Lang.ListHeader));

            foreach (var monument in monuments)
            {
                builder.AppendLine(monument.PrefabName);
            }

            player.Reply(builder.ToString());
        }

        private static void ShowMonumentName(BasePlayer player, BaseMonumentAdapter monument)
        {
            Ddraw.Text(player, monument.Position, $"<size=20>{monument.ShortName}</size>", Color.magenta, 30);
        }

        #endregion

        #region Monument Adapter

        private interface SingleBoundingBox
        {
            OBB BoundingBox { get; }
        }

        private interface MultipleBoundingBoxes
        {
            OBB[] BoundingBoxes { get; }
        }

        private abstract class BaseMonumentAdapter
        {
            protected static string GetShortName(string prefabName)
            {
                var slashIndex = prefabName.LastIndexOf("/");
                var baseName = (slashIndex == -1) ? prefabName : prefabName.Substring(slashIndex + 1);
                return baseName.Replace(".prefab", "");
            }

            public MonoBehaviour Object { get; }
            public string PrefabName { get; protected set; }
            public string ShortName { get; protected set; }

            // Subclasses should overwrite this is if multiple monuments need to share an alias.
            // For instance, each train station prefab should share the same alias since they only differ in rotation.
            public string Alias { get; protected set; }

            public Vector3 Position { get; protected set; }
            public Quaternion Rotation { get; protected set; }

            public BaseMonumentAdapter(MonoBehaviour behavior)
            {
                Object = behavior;
                PrefabName = behavior.name;
                ShortName = GetShortName(behavior.name);
                Position = behavior.transform.position;
                Rotation = behavior.transform.rotation;
            }

            public Vector3 TransformPoint(Vector3 localPosition)
            {
                return Position + Rotation * localPosition;
            }

            public Vector3 InverseTransformPoint(Vector3 worldPosition)
            {
                return Quaternion.Inverse(Rotation) * (worldPosition - Position);
            }

            public abstract bool IsInBounds(Vector3 position);
            public abstract Vector3 ClosestPointOnBounds(Vector3 position);

            public virtual bool MatchesFilter(string filter, string shortName, string alias)
            {
                if (alias != null)
                    return Alias?.Equals(alias, StringComparison.InvariantCultureIgnoreCase) ?? false;

                if (shortName != null)
                    return ShortName.Equals(shortName, StringComparison.InvariantCultureIgnoreCase);

                if (string.IsNullOrEmpty(filter))
                    return true;

                return PrefabName.Contains(filter, CompareOptions.IgnoreCase);
            }

            private Dictionary<string, object> _cachedAPIResult;
            public Dictionary<string, object> APIResult
            {
                get
                {
                    if (_cachedAPIResult == null)
                    {
                        _cachedAPIResult = new Dictionary<string, object>
                        {
                            ["Object"] = Object,
                            ["PrefabName"] = PrefabName,
                            ["ShortName"] = ShortName,
                            ["Alias"] = Alias,
                            ["Position"] = Position,
                            ["Rotation"] = Rotation,
                            ["TransformPoint"] = new Func<Vector3, Vector3>(TransformPoint),
                            ["InverseTransformPoint"] = new Func<Vector3, Vector3>(InverseTransformPoint),
                            ["ClosestPointOnBounds"] = new Func<Vector3, Vector3>(ClosestPointOnBounds),
                            ["IsInBounds"] = new Func<Vector3, bool>(IsInBounds),
                        };
                    }

                    return _cachedAPIResult;
                }
            }
        }

        private class NormalMonumentAdapter : BaseMonumentAdapter, SingleBoundingBox
        {
            public static Dictionary<string, Bounds> MonumentBounds = new Dictionary<string, Bounds>
            {
                // These bounds are more accurate than what is provided in vanilla.
                ["airfield_1"] = new Bounds(new Vector3(0, 15, -25), new Vector3(355, 70, 210)),
                ["bandit_town"] = new Bounds(new Vector3(0, 12, -5), new Vector3(150, 40, 140)),
                ["cave_large_sewers_hard"] = new Bounds(new Vector3(18, -5, -9), new Vector3(52, 80, 56)),
                ["cave_medium_medium"] = new Bounds(new Vector3(-5, 10, -3), new Vector3(100, 20, 50)),
                ["cave_small_easy"] = new Bounds(new Vector3(5, 10, 0), new Vector3(55, 24, 55)),
                ["cave_small_hard"] = new Bounds(new Vector3(0, 10, -5), new Vector3(40, 20, 35)),
                ["cave_small_medium"] = new Bounds(new Vector3(10, 10, 0), new Vector3(45, 26, 40)),
                ["compound"] = new Bounds(new Vector3(0, 12, 0), new Vector3(200, 50, 200)),
                ["entrance_bunker_a"] = new Bounds(new Vector3(-3.5f, 1, -0.5f), new Vector3(20, 30, 18)),
                ["entrance_bunker_b"] = new Bounds(new Vector3(-8, 1, 0), new Vector3(30, 30, 18)),
                ["entrance_bunker_c"] = new Bounds(new Vector3(-3.5f, 1, -5f), new Vector3(24, 30, 27)),
                ["entrance_bunker_d"] = new Bounds(new Vector3(-3.5f, 1, -0.5f), new Vector3(20, 30, 17)),
                ["excavator_1"] = new Bounds(new Vector3(0, 40, 0), new Vector3(240, 100, 230)),
                ["fishing_village_a"] = new Bounds(new Vector3(-3, 5, -11), new Vector3(76, 24, 80)),
                ["fishing_village_b"] = new Bounds(new Vector3(-3, 4, -4), new Vector3(42, 24, 76)),
                ["fishing_village_c"] = new Bounds(new Vector3(-0.5f, 4, -4.5f), new Vector3(31, 22, 75)),
                ["gas_station_1"] = new Bounds(new Vector3(0, 13, 15), new Vector3(70, 42, 60)),
                ["harbor_1"] = new Bounds(new Vector3(-8, 23, 15), new Vector3(246, 60, 200)),
                ["harbor_2"] = new Bounds(new Vector3(6, 23, 18), new Vector3(224, 60, 250)),
                ["junkyard_1"] = new Bounds(new Vector3(0, 20, 0), new Vector3(180, 50, 180)),
                ["launch_site_1"] = new Bounds(new Vector3(10, 25, -26), new Vector3(544, 120, 276)),
                ["lighthouse"] = new Bounds(new Vector3(10f, 23, 5), new Vector3(74, 96, 68)),
                ["military_tunnel_1"] = new Bounds(new Vector3(0, 15, -25), new Vector3(265, 70, 250)),
                ["mining_quarry_a"] = new Bounds(new Vector3(2, 10, 2), new Vector3(52, 20, 72)),
                ["mining_quarry_b"] = new Bounds(new Vector3(-5, 10, -8), new Vector3(60, 20, 40)),
                ["mining_quarry_c"] = new Bounds(new Vector3(-6, 10, 8), new Vector3(42, 20, 60)),
                ["OilrigAI"] = new Bounds(new Vector3(18, 20, -2), new Vector3(68, 60, 76)),
                ["OilrigAI2"] = new Bounds(new Vector3(3, 43, 12), new Vector3(80, 96, 120)),
                ["power_sub_big_1"] = new Bounds(new Vector3(0, 5, 0.5f), new Vector3(20, 10, 22f)),
                ["power_sub_big_2"] = new Bounds(new Vector3(-1, 5, 1), new Vector3(23, 10, 22)),
                ["power_sub_small_1"] = new Bounds(new Vector3(0, 4, 0), new Vector3(14, 8, 14)),
                ["power_sub_small_2"] = new Bounds(new Vector3(0, 4, 0), new Vector3(14, 8, 14)),
                ["powerplant_1"] = new Bounds(new Vector3(-15, 25, -11), new Vector3(220, 64, 290)),
                ["radtown_small_3"] = new Bounds(new Vector3(-10, 15, -18), new Vector3(130, 50, 148)),
                ["satellite_dish"] = new Bounds(new Vector3(0, 25, 3), new Vector3(155, 55, 125)),
                ["sphere_tank"] = new Bounds(new Vector3(0, 41, 0), new Vector3(100, 84, 100)),
                ["stables_a"] = new Bounds(new Vector3(0, 10, 4), new Vector3(50, 20, 60)),
                ["stables_b"] = new Bounds(new Vector3(2, 15, 6), new Vector3(78, 30, 66)),
                ["supermarket_1"] = new Bounds(new Vector3(1, 4.5f, 1), new Vector3(40, 10, 44)),
                ["swamp_a"] = new Bounds(new Vector3(-10, 11, 0), new Vector3(140, 30, 140)),
                ["swamp_b"] = new Bounds(new Vector3(0, 14, 0), new Vector3(100, 36, 100)),
                ["swamp_c"] = new Bounds(new Vector3(0, 7, 0), new Vector3(100, 30, 100)),
                ["trainyard_1"] = new Bounds(new Vector3(10, 22, -30), new Vector3(235, 70, 220)),
                ["underwater_lab_a"] = new Bounds(),
                ["underwater_lab_b"] = new Bounds(),
                ["underwater_lab_c"] = new Bounds(),
                ["underwater_lab_d"] = new Bounds(),
                ["warehouse"] = new Bounds(new Vector3(0, 5, -8), new Vector3(44, 10, 24)),
                ["water_treatment_plant_1"] = new Bounds(new Vector3(20, 30, -45), new Vector3(250, 84, 290)),
                ["water_well_a"] = new Bounds(new Vector3(0, 10, 0), new Vector3(24, 20, 24)),
                ["water_well_b"] = new Bounds(new Vector3(0, 10, 0), new Vector3(24, 20, 24)),
                ["water_well_c"] = new Bounds(new Vector3(0, 10, 0), new Vector3(24, 20, 24)),
                ["water_well_d"] = new Bounds(new Vector3(0, 10, 0), new Vector3(30, 20, 30)),
                ["water_well_e"] = new Bounds(new Vector3(0, 10, 0), new Vector3(24, 20, 24)),
            };

            public MonumentInfo MonumentInfo { get; }
            public OBB BoundingBox { get; }

            public NormalMonumentAdapter(MonumentInfo monumentInfo) : base(monumentInfo)
            {
                MonumentInfo = monumentInfo;
                var bounds = monumentInfo.Bounds;

                if (IsCustomMonument(monumentInfo))
                {
                    PrefabName = monumentInfo.transform.root.name;
                    ShortName = PrefabName;

                    var monumentSettings = _pluginConfig.GetMonumentSettings(ShortName)
                        ?? _pluginConfig.DefaultCustomMonumentSettings;

                    var volumeCollider = monumentSettings.Position.UsePreventBuildingVolume
                        || monumentSettings.Rotation.UsePreventBuildingVolume
                        || monumentSettings.Bounds.UsePreventBuildingVolume
                            ? FindPreventBuildingVolume(Position)
                            : null;

                    if (!monumentSettings.Position.UseMonumentMarker
                        && monumentSettings.Position.UsePreventBuildingVolume)
                    {
                        if (volumeCollider != null)
                        {
                            Position = volumeCollider.transform.position;
                        }
                        else
                        {
                            LogWarning($"Unable to find a PreventBuilding volume for monument {ShortName}. Determining position from monument marker instead.");
                        }
                    }

                    if (!monumentSettings.Rotation.UseMonumentMarker
                        && monumentSettings.Rotation.UsePreventBuildingVolume)
                    {
                        if (volumeCollider != null)
                        {
                            Rotation = volumeCollider.transform.rotation;
                        }
                        else
                        {
                            LogWarning($"Unable to find a PreventBuilding volume for monument {ShortName}. Determining rotation from monument marker instead.");
                        }
                    }

                    if (monumentSettings.Bounds.UseCustomBounds)
                    {
                        bounds = monumentSettings.Bounds.CustomBounds.ToBounds();
                    }
                    else if (monumentSettings.Bounds.UseMonumentMarker)
                    {
                        bounds = new Bounds(Position - monumentInfo.transform.position, monumentInfo.transform.localScale);
                    }
                    else if (monumentSettings.Bounds.UsePreventBuildingVolume)
                    {
                        if (volumeCollider != null)
                        {
                            bounds = volumeCollider.bounds;
                            bounds.center = Quaternion.Inverse(Rotation) * (bounds.center - Position);
                        }
                        else
                        {
                            LogError($"Unable to find a PreventBuilding volume for monument {ShortName}. Unable to determine bounds.");
                        }
                    }
                }
                else
                {
                    var monumentSettings = _pluginConfig.GetMonumentSettings(ShortName);
                    if (monumentSettings != null && monumentSettings.Bounds.UseCustomBounds)
                    {
                        bounds = monumentSettings.Bounds.CustomBounds.ToBounds();
                    }
                    else
                    {
                        Bounds hardCodedBounds;
                        if (MonumentBounds.TryGetValue(ShortName, out hardCodedBounds))
                        {
                            bounds = hardCodedBounds;
                        }
                    }
                }

                BoundingBox = new OBB(Position, Rotation, bounds);
            }

            public override bool IsInBounds(Vector3 position) =>
                BoundingBox.Contains(position);

            public override Vector3 ClosestPointOnBounds(Vector3 position) =>
                BoundingBox.ClosestPoint(position);

            public override bool MatchesFilter(string filter, string shortName, string alias)
            {
                return base.MatchesFilter(filter, shortName, alias)
                    || !string.IsNullOrEmpty(filter) && MonumentInfo.Type.ToString().Contains(filter, CompareOptions.IgnoreCase);
            }
        }

        private class TrainTunnelAdapter : BaseMonumentAdapter, SingleBoundingBox
        {
            public static readonly string[] IgnoredPrefabs =
            {
                // These prefabs are simply used for decorating.
                "assets/bundled/prefabs/autospawn/tunnel-transition/transition-sn-0.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-transition/transition-sn-1.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-transition/transition-we-0.prefab",
                "assets/bundled/prefabs/autospawn/tunnel-transition/transition-we-1.prefab",
            };

            private abstract class BaseTunnelInfo
            {
                public Quaternion Rotation;
                public virtual Bounds Bounds { get; }
                public virtual string Alias { get; }
            }

            // Train stations.
            private class TrainStation : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 8.75f, 0), new Vector3(108, 18, 216));
                public override string Alias => "TrainStation";
            }

            // Straight tunnels that contain barricades, loot and tunnel dwellers.
            private class BarricadeTunnel : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 4.25f, 0), new Vector3(45f, 9, 216));
                public override string Alias => "BarricadeTunnel";
            }

            // Straight tunnels contain loot and tunnel dwellers.
            private class LootTunnel : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 4.25f, 0), new Vector3(16.5f, 9, 216));
                public override string Alias => "LootTunnel";
            }

            // Straight tunnels with a divider in the tracks.
            private class SplitTunnel : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 4.25f, 0), new Vector3(16.5f, 9, 216));
                public override string Alias => "SplitTunnel";
            }

            // 3-way intersections.
            private class Intersection : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 4.25f, 49.875f), new Vector3(216, 9, 116.25f));
                public override string Alias => "Intersection";
            }

            // 4-way intersections.
            private class LargeIntersection : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 4.25f, 0), new Vector3(216, 9, 216));
                public override string Alias => "LargeIntersection";
            }

            // 3-way intersections that connect to above ground.
            private class VerticalIntersection : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(0, 4.25f, 49.875f), new Vector3(216, 9, 116.25f));
                public override string Alias => "VerticalIntersection";
            }

            // Corner tunnels (45-degree angle).
            private class CornerTunnel : BaseTunnelInfo
            {
                public override Bounds Bounds => new Bounds(new Vector3(-49.875f, 4.25f, 49.875f), new Vector3(116.25f, 9, 116.25f));
                public override string Alias => "CornerTunnel";
            }

            private static readonly Dictionary<string, BaseTunnelInfo> PrefabToTunnelInfo = new Dictionary<string, BaseTunnelInfo>
            {
                ["station-sn-0"] = new TrainStation { Rotation = Quaternion.Euler(0, 180, 0) },
                ["station-sn-1"] = new TrainStation { Rotation = Quaternion.Euler(0, 0, 0) },
                ["station-sn-2"] = new TrainStation { Rotation = Quaternion.Euler(0, 180, 0) },
                ["station-sn-3"] = new TrainStation { Rotation = Quaternion.Euler(0, 0, 0) },
                ["station-we-0"] = new TrainStation { Rotation = Quaternion.Euler(0, 90, 0) },
                ["station-we-1"] = new TrainStation { Rotation = Quaternion.Euler(0, 270, 0) },
                ["station-we-2"] = new TrainStation { Rotation = Quaternion.Euler(0, 90, 0) },
                ["station-we-3"] = new TrainStation { Rotation = Quaternion.Euler(0, 270, 0) },

                ["straight-sn-4"] = new BarricadeTunnel { Rotation = Quaternion.Euler(0, 180, 0) },
                ["straight-sn-5"] = new BarricadeTunnel { Rotation = Quaternion.Euler(0, 0, 0) },
                ["straight-we-4"] = new BarricadeTunnel { Rotation = Quaternion.Euler(0, 90, 0) },
                ["straight-we-5"] = new BarricadeTunnel { Rotation = Quaternion.Euler(0, 270, 0) },

                ["straight-sn-0"] = new LootTunnel { Rotation = Quaternion.Euler(0, 180, 0) },
                ["straight-sn-1"] = new LootTunnel { Rotation = Quaternion.Euler(0, 0, 0) },
                ["straight-we-0"] = new LootTunnel { Rotation = Quaternion.Euler(0, 90, 0) },
                ["straight-we-1"] = new LootTunnel { Rotation = Quaternion.Euler(0, 270, 0) },

                ["straight-we-2"] = new SplitTunnel { Rotation = Quaternion.Euler(0, 90, 0) },
                ["straight-we-3"] = new SplitTunnel { Rotation = Quaternion.Euler(0, 270, 0) },
                ["straight-sn-2"] = new SplitTunnel { Rotation = Quaternion.Euler(0, 180, 0) },
                ["straight-sn-3"] = new SplitTunnel { Rotation = Quaternion.Euler(0, 0, 0) },

                ["intersection-n"] = new Intersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-e"] = new Intersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-s"] = new Intersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-w"] = new Intersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection-b1-n"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-b1-e"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-b1-s"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-b1-w"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection-b2-n"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-b2-e"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-b2-s"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-b2-w"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection-b3-n"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-b3-e"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-b3-s"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-b3-w"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection-b4-n"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-b4-e"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-b4-s"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-b4-w"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection-b5-n"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-b5-e"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-b5-s"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-b5-w"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection-b6-n"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 0, 0) },
                ["intersection-b6-e"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 90, 0) },
                ["intersection-b6-s"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 180, 0) },
                ["intersection-b6-w"] = new VerticalIntersection { Rotation = Quaternion.Euler(0, 270, 0) },

                ["intersection"] = new LargeIntersection { Rotation = Quaternion.Euler(0, 0, 0) },

                ["curve-ne-0"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 90, 0) },
                ["curve-ne-1"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 90, 0) },
                ["curve-nw-0"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 0, 0) },
                ["curve-nw-1"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 0, 0) },
                ["curve-se-0"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 180, 0) },
                ["curve-se-1"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 180, 0) },
                ["curve-sw-0"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 270, 0) },
                ["curve-sw-1"] = new CornerTunnel { Rotation = Quaternion.Euler(0, 270, 0) },
            };

            private static BaseTunnelInfo GetTunnelInfo(string shortName)
            {
                BaseTunnelInfo tunnelInfo;
                if (PrefabToTunnelInfo.TryGetValue(shortName, out tunnelInfo))
                    return tunnelInfo;

                throw new NotImplementedException($"Tunnel type not implemented: {shortName}");
            }

            public OBB BoundingBox { get; }

            private BaseTunnelInfo _tunnelInfo;

            public TrainTunnelAdapter(DungeonGridCell dungeonCell) : base(dungeonCell)
            {
                _tunnelInfo = GetTunnelInfo(ShortName);

                Rotation = _tunnelInfo.Rotation;
                Alias = _tunnelInfo.Alias;

                var bounds = _tunnelInfo.Bounds;

                var monumentSettings = _pluginConfig.GetMonumentSettings(_tunnelInfo.Alias);
                if (monumentSettings != null && monumentSettings.Bounds.UseCustomBounds)
                {
                    bounds = monumentSettings.Bounds.CustomBounds.ToBounds();
                }

                BoundingBox = new OBB(Position, Rotation, bounds);
            }

            public override bool IsInBounds(Vector3 position) =>
                BoundingBox.Contains(position);

            public override Vector3 ClosestPointOnBounds(Vector3 position) =>
                BoundingBox.ClosestPoint(position);
        }

        private class UnderwaterLabLinkAdapter : BaseMonumentAdapter, MultipleBoundingBoxes
        {
            public OBB[] BoundingBoxes { get; }

            public UnderwaterLabLinkAdapter(DungeonBaseLink dungeonLink) : base(dungeonLink)
            {
                var volumeList = dungeonLink.GetComponentsInChildren<DungeonVolume>();
                BoundingBoxes = new OBB[volumeList.Length];

                for (var i = 0; i < volumeList.Length; i++)
                {
                    var volume = volumeList[i];
                    BoundingBoxes[i] = new OBB(volume.transform.position, volume.transform.rotation, volume.bounds);
                }
            }

            public override bool IsInBounds(Vector3 position)
            {
                foreach (var box in BoundingBoxes)
                {
                    if (box.Contains(position))
                        return true;
                }

                return false;
            }

            public override Vector3 ClosestPointOnBounds(Vector3 position)
            {
                var overallClosestPoint = Vector3.positiveInfinity;
                var closestSqrDistance = float.MaxValue;

                foreach (var box in BoundingBoxes)
                {
                    var closestPoint = box.ClosestPoint(position);
                    var currentSqrDistance = (position - closestPoint).sqrMagnitude;

                    if (currentSqrDistance < closestSqrDistance)
                    {
                        overallClosestPoint = closestPoint;
                        closestSqrDistance = currentSqrDistance;
                    }
                }

                return overallClosestPoint;
            }
        }

        #endregion

        #region Ddraw

        private static class Ddraw
        {
            public static void Sphere(BasePlayer player, Vector3 origin, float radius, Color color, float duration) =>
                player.SendConsoleCommand("ddraw.sphere", duration, color, origin, radius);

            public static void Line(BasePlayer player, Vector3 origin, Vector3 target, Color color, float duration) =>
                player.SendConsoleCommand("ddraw.line", duration, color, origin, target);

            public static void Text(BasePlayer player, Vector3 origin, string text, Color color, float duration) =>
                player.SendConsoleCommand("ddraw.text", duration, color, origin, text);

            public static void Segments(BasePlayer player, Vector3 origin, Vector3 target, Color color, float duration)
            {
                var delta = target - origin;
                var distance = delta.magnitude;
                var direction = delta.normalized;

                var segmentLength = 10f;
                var numSegments = Mathf.CeilToInt(distance / segmentLength);

                for (var i = 0; i < numSegments; i++)
                {
                    var length = segmentLength;
                    if (i == numSegments - 1 && distance % segmentLength != 0)
                        length = distance % segmentLength;

                    var start = origin + i * segmentLength * direction;
                    var end = start + length * direction;
                    Line(player, start, end, color, duration);
                }
            }

            public static void Box(BasePlayer player, Vector3 center, Quaternion rotation, Vector3 halfExtents, Color color, float duration, bool showInfo = true)
            {
                var boxArea = halfExtents.x * halfExtents.z;

                var sphereRadius = boxArea > 200
                    ? 1f
                    : boxArea > 10
                    ? 0.5f
                    : 0.1f;

                var forwardUpperLeft = center + rotation * halfExtents.WithX(-halfExtents.x);
                var forwardUpperRight = center + rotation * halfExtents;
                var forwardLowerLeft = center + rotation * halfExtents.WithX(-halfExtents.x).WithY(-halfExtents.y);
                var forwardLowerRight = center + rotation * halfExtents.WithY(-halfExtents.y);

                var backLowerRight = center + rotation * -halfExtents.WithX(-halfExtents.x);
                var backLowerLeft = center + rotation * -halfExtents;
                var backUpperRight = center + rotation * -halfExtents.WithX(-halfExtents.x).WithY(-halfExtents.y);
                var backUpperLeft = center + rotation * -halfExtents.WithY(-halfExtents.y);

                var forwardLowerMiddle = Vector3.Lerp(forwardLowerLeft, forwardLowerRight, 0.5f);
                var forwardUpperMiddle = Vector3.Lerp(forwardUpperLeft, forwardUpperRight, 0.5f);

                var backLowerMiddle = Vector3.Lerp(backLowerLeft, backLowerRight, 0.5f);
                var backUpperMiddle = Vector3.Lerp(backUpperLeft, backUpperRight, 0.5f);

                var leftLowerMiddle = Vector3.Lerp(forwardLowerLeft, backLowerLeft, 0.5f);
                var leftUpperMiddle = Vector3.Lerp(forwardUpperLeft, backUpperLeft, 0.5f);

                var rightLowerMiddle = Vector3.Lerp(forwardLowerRight, backLowerRight, 0.5f);
                var rightUpperMiddle = Vector3.Lerp(forwardUpperRight, backUpperRight, 0.5f);

                Sphere(player, forwardUpperLeft, sphereRadius, color, duration);
                Sphere(player, forwardUpperRight, sphereRadius, color, duration);
                Sphere(player, forwardLowerLeft, sphereRadius, color, duration);
                Sphere(player, forwardLowerRight, sphereRadius, color, duration);

                Sphere(player, backLowerRight, sphereRadius, color, duration);
                Sphere(player, backLowerLeft, sphereRadius, color, duration);
                Sphere(player, backUpperRight, sphereRadius, color, duration);
                Sphere(player, backUpperLeft, sphereRadius, color, duration);

                Segments(player, forwardUpperLeft, forwardUpperRight, color, duration);
                Segments(player, forwardLowerLeft, forwardLowerRight, color, duration);
                Segments(player, forwardUpperLeft, forwardLowerLeft, color, duration);
                Segments(player, forwardUpperRight, forwardLowerRight, color, duration);

                Segments(player, backUpperLeft, backUpperRight, color, duration);
                Segments(player, backLowerLeft, backLowerRight, color, duration);
                Segments(player, backUpperLeft, backLowerLeft, color, duration);
                Segments(player, backUpperRight, backLowerRight, color, duration);

                Segments(player, forwardUpperLeft, backUpperLeft, color, duration);
                Segments(player, forwardLowerLeft, backLowerLeft, color, duration);
                Segments(player, forwardUpperRight, backUpperRight, color, duration);
                Segments(player, forwardLowerRight, backLowerRight, color, duration);

                if (showInfo)
                {
                    Sphere(player, forwardLowerMiddle, sphereRadius, Color.yellow, duration);
                    Sphere(player, forwardUpperMiddle, sphereRadius, Color.yellow, duration);
                    Sphere(player, backLowerMiddle, sphereRadius, Color.yellow, duration);
                    Sphere(player, backUpperMiddle, sphereRadius, Color.yellow, duration);

                    Sphere(player, leftLowerMiddle, sphereRadius, Color.green, duration);
                    Sphere(player, leftUpperMiddle, sphereRadius, Color.green, duration);
                    Sphere(player, rightLowerMiddle, sphereRadius, Color.green, duration);
                    Sphere(player, rightUpperMiddle, sphereRadius, Color.green, duration);

                    Text(player, forwardUpperMiddle, "<size=20>+Z</size>", Color.yellow, duration);
                    Text(player, forwardLowerMiddle, "<size=20>+Z</size>", Color.yellow, duration);
                    Text(player, backUpperMiddle, "<size=20>-Z</size>", Color.yellow, duration);
                    Text(player, backLowerMiddle, "<size=20>-Z</size>", Color.yellow, duration);

                    Text(player, leftLowerMiddle, "<size=20>-X</size>", Color.green, duration);
                    Text(player, leftUpperMiddle, "<size=20>-X</size>", Color.green, duration);
                    Text(player, rightLowerMiddle, "<size=20>+X</size>", Color.green, duration);
                    Text(player, rightUpperMiddle, "<size=20>+X</size>", Color.green, duration);

                    Text(player, forwardUpperLeft, "<size=28>*</size>", color, duration);
                    Text(player, forwardUpperRight, "<size=28>*</size>", color, duration);
                    Text(player, forwardLowerLeft, "<size=28>*</size>", color, duration);
                    Text(player, forwardLowerRight, "<size=28>*</size>", color, duration);

                    Text(player, backLowerRight, "<size=28>*</size>", color, duration);
                    Text(player, backLowerLeft, "<size=28>*</size>", color, duration);
                    Text(player, backUpperRight, "<size=28>*</size>", color, duration);
                    Text(player, backUpperLeft, "<size=28>*</size>", color, duration);
                }
            }

            public static void Box(BasePlayer player, OBB boundingBox, Color color, float duration, bool showInfo = true)
            {
                Box(player, boundingBox.position, boundingBox.rotation, boundingBox.extents, color, duration, showInfo);
            }
        }

        #endregion

        #region Configuration

        private class CustomBounds
        {
            [JsonProperty("Size")]
            public Vector3 Size;

            [JsonProperty("Center adjustment")]
            public Vector3 CenterOffset;

            [JsonProperty("Center")]
            private Vector3 DeprecatedCenter { set { CenterOffset = value ; } }

            public Bounds ToBounds() => new Bounds(CenterOffset, Size);

            public CustomBounds Copy()
            {
                return new CustomBounds
                {
                    Size = Size,
                    CenterOffset = CenterOffset,
                };
            }
        }

        private class BaseDetectionSettings
        {
            [JsonProperty("Auto determine from monument marker", Order = -3)]
            public bool UseMonumentMarker;

            [JsonProperty("Auto determine from prevent building volume", Order = -2)]
            public bool UsePreventBuildingVolume;

            public BaseDetectionSettings Copy()
            {
                return new BaseDetectionSettings
                {
                    UseMonumentMarker = UseMonumentMarker,
                    UsePreventBuildingVolume = UsePreventBuildingVolume,
                };
            }
        }

        private class BoundSettings : BaseDetectionSettings
        {
            [JsonProperty("Use custom bounds")]
            public bool UseCustomBounds;

            [JsonProperty("Custom bounds")]
            public CustomBounds CustomBounds = new CustomBounds();

            public new BoundSettings Copy()
            {
                return new BoundSettings
                {
                    UseMonumentMarker = UseMonumentMarker,
                    UsePreventBuildingVolume = UsePreventBuildingVolume,
                    UseCustomBounds = UseCustomBounds,
                    CustomBounds = CustomBounds.Copy(),
                };
            }
        }

        private class MonumentSettings
        {
            [JsonProperty("Position")]
            public BaseDetectionSettings Position = new BaseDetectionSettings();

            [JsonProperty("Rotation")]
            public BaseDetectionSettings Rotation = new BaseDetectionSettings();

            [JsonProperty("Bounds")]
            public BoundSettings Bounds = new BoundSettings();

            public MonumentSettings Copy()
            {
                return new MonumentSettings
                {
                    Position = Position.Copy(),
                    Rotation = Rotation.Copy(),
                    Bounds = Bounds.Copy(),
                };
            }
        }

        private class Configuration : SerializableConfiguration
        {
            [JsonProperty(PropertyName = "Command")]
            public string Command = "mf";

            [JsonProperty("Default custom monument settings")]
            public MonumentSettings DefaultCustomMonumentSettings = new MonumentSettings
            {
                Position = new BaseDetectionSettings
                {
                    UseMonumentMarker = true,
                    UsePreventBuildingVolume = false,
                },
                Rotation = new BaseDetectionSettings
                {
                    UseMonumentMarker = true,
                    UsePreventBuildingVolume = false,
                },
                Bounds = new BoundSettings
                {
                    UseMonumentMarker = false,
                    UseCustomBounds = true,
                    CustomBounds = new CustomBounds
                    {
                        CenterOffset = new Vector3(0, 10, 0),
                        Size = new Vector3(30, 30, 30),
                    },
                },
            };

            [JsonProperty("Monuments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            private Dictionary<string, MonumentSettings> MonumentSettingsMap = new Dictionary<string, MonumentSettings>
            {
                ["example_monument"] = new MonumentSettings
                {
                    Position = new BaseDetectionSettings
                    {
                        UseMonumentMarker = true,
                        UsePreventBuildingVolume = false,
                    },
                    Rotation = new BaseDetectionSettings
                    {
                        UseMonumentMarker = true,
                        UsePreventBuildingVolume = false,
                    },
                    Bounds = new BoundSettings
                    {
                        UseMonumentMarker = false,
                        UseCustomBounds = true,
                        CustomBounds = new CustomBounds
                        {
                            CenterOffset = new Vector3(0, 10, 0),
                            Size = new Vector3(30, 30, 30),
                        },
                    },
                },
            };

            [JsonProperty("OverrideMonumentBounds")]
            private Dictionary<string, CustomBounds> DeprecatedOverrideMonumentBounds
            {
                set
                {
                    foreach (var entry in value)
                    {
                        MonumentSettingsMap[entry.Key] = new MonumentSettings
                        {
                            Position = new BaseDetectionSettings { UseMonumentMarker = true },
                            Rotation = new BaseDetectionSettings { UseMonumentMarker = true },
                            Bounds = new BoundSettings
                            {
                                UseCustomBounds = true,
                                CustomBounds = entry.Value,
                            },
                        };
                    }
                }
            }

            public MonumentSettings GetMonumentSettings(string monumentName)
            {
                MonumentSettings monumentSettings;
                return MonumentSettingsMap.TryGetValue(monumentName, out monumentSettings)
                    ? monumentSettings
                    : null;
            }

            public bool AddMonument(string aliasOrShortName, BaseMonumentAdapter monument)
            {
                if (MonumentSettingsMap.ContainsKey(aliasOrShortName))
                    return false;

                var monumentInfo = monument.Object as MonumentInfo;
                var isCustomMonument = monumentInfo != null && IsCustomMonument(monumentInfo);

                MonumentSettings monumentSettings;

                if (isCustomMonument)
                {
                    monumentSettings = DefaultCustomMonumentSettings.Copy();
                }
                else
                {
                    Bounds bounds;
                    if (!NormalMonumentAdapter.MonumentBounds.TryGetValue(aliasOrShortName, out bounds))
                    {
                        bounds = default(Bounds);
                    }

                    monumentSettings = new MonumentSettings
                    {
                        Bounds = new BoundSettings
                        {
                            UseCustomBounds = true,
                            CustomBounds = new CustomBounds
                            {
                                Size = bounds.size,
                                CenterOffset = bounds.center,
                            },
                        }
                    };
                }

                MonumentSettingsMap[aliasOrShortName] = monumentSettings;
                return true;
            }
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #endregion

        #region Configuration Boilerplate

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _pluginConfig = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_pluginConfig))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_pluginConfig, true);
        }

        #endregion

        #region Localization

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string GetMessage(IPlayer player, string messageName, params object[] args) =>
            GetMessage(player.Id, messageName, args);

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(string.Format(GetMessage(player, messageName), args));

        private static class Lang
        {
            public const string ErrorNoPermission = "NoPermission";
            public const string NoMonumentsFound = "NoMonumentsFound";
            public const string ListHeader = "List.Header";
            public const string AtMonument = "AtMonument";
            public const string ClosestMonument = "ClosestMonument";
            public const string ClosestConfigSuccess = "Closest.Config.Success";
            public const string ClosestConfigAlreadyPresent = "Closest.Config.AlreadyPresent";
            public const string HelpHeader = "Help.Header";
            public const string HelpList = "Help.List";
            public const string HelpShow = "Help.Show";
            public const string HelpClosest = "Help.Closest";
            public const string HelpClosestConfig = "Help.Closest.Config";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ErrorNoPermission] = "You don't have permission to do that.",
                [Lang.NoMonumentsFound] = "No monuments found",
                [Lang.AtMonument] = "At monument: {0}\nRelative position: {1}",
                [Lang.ClosestMonument] = "Closest monument: {0}\nDistance: {1:f2}m",
                [Lang.ClosestConfigSuccess] = "Added monument <color=#9f6>{0}</color> to the plugin config.",
                [Lang.ClosestConfigAlreadyPresent] = "Monument <color=#9f6>{0}</color> is already in the plugin config.",
                [Lang.ListHeader] = "Listing monuments:",
                [Lang.HelpHeader] = "Monument Finder commands:",
                [Lang.HelpList] = "<color=#9f6>{0} list <filter></color> - List monuments matching filter",
                [Lang.HelpShow] = "<color=#9f6>{0} show <filter></color> - Show monuments matching filter",
                [Lang.HelpClosest] = "<color=#9f6>{0} closest</color> - Show info about the closest monument",
                [Lang.HelpClosestConfig] = "<color=#9f6>{0} closest config</color> - Adds the closest monument to the config",
            }, this, "en");
        }

        #endregion
    }
}
