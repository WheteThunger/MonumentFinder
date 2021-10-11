using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Monument Finder", "WhiteThunder", "3.0.0")]
    [Description("Find monuments with commands or API.")]
    internal class MonumentFinder : CovalencePlugin
    {
        #region Fields

        private static Configuration _pluginConfig;

        private const string PermissionFind = "monumentfinder.find";

        private const float DrawDuration = 30;

        private Dictionary<MonumentInfo, NormalMonumentAdapter> _normalMonuments = new Dictionary<MonumentInfo, NormalMonumentAdapter>();
        private Dictionary<DungeonGridCell, TrainTunnelAdapter> _trainTunnels = new Dictionary<DungeonGridCell, TrainTunnelAdapter>();
        private Dictionary<DungeonBaseLink, UnderwaterLabLinkAdapter> _labModules = new Dictionary<DungeonBaseLink, UnderwaterLabLinkAdapter>();
        private Dictionary<MonoBehaviour, BaseMonumentAdapter> _allMonuments = new Dictionary<MonoBehaviour, BaseMonumentAdapter>();

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionFind, this);

            AddCovalenceCommand(_pluginConfig.Command, nameof(CommandFind));
        }

        private void Unload()
        {
            _pluginConfig = null;
        }

        private void OnServerInitialized()
        {
            foreach (var underwaterLab in TerrainMeta.Path.DungeonBaseEntrances)
            {
                foreach (var linkObj in underwaterLab.Links)
                {
                    var link = linkObj.GetComponent<DungeonBaseLink>();
                    if (link == null)
                        continue;

                    // End links represent the posts holding up the lab modules.
                    if (link.Type == DungeonBaseLinkType.End)
                        continue;

                    var labLink = new UnderwaterLabLinkAdapter(link);
                    _labModules[link] = labLink;
                    _allMonuments[link] = labLink;
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

            if (basePlayer.IsAdmin)
            {
                ShowMonumentName(basePlayer, monument);

                var withBoundingBox = monument as SingleBoundingBox;
                if (withBoundingBox != null)
                {
                    var boundingBox = withBoundingBox.BoundingBox;
                    if (boundingBox.extents != Vector3.zero)
                        Ddraw.Box(basePlayer, boundingBox, Color.magenta, DrawDuration);

                    return;
                }

                var withMultipleBoundingBoxes = monument as MultipleBoundingBoxes;
                if (withMultipleBoundingBoxes != null)
                {
                    foreach (var boundingBox in withMultipleBoundingBoxes.BoundingBoxes)
                        Ddraw.Box(basePlayer, boundingBox, Color.magenta, DrawDuration, showInfo: false);
                }
            }
        }

        #endregion

        #region Helper Methods

        private T GetClosestMonument<T>(IEnumerable<T> monumentList, Vector3 position) where T : BaseMonumentAdapter
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

        private Dictionary<string, object> GetClosestMonumentForAPI(IEnumerable<BaseMonumentAdapter> monumentList, Vector3 position)
        {
            var baseMonument = GetClosestMonument(monumentList, position);
            if (baseMonument == null)
                return null;

            return baseMonument.APIResult;
        }

        private List<T> FilterMonuments<T>(IEnumerable<T> monumentList, string filter = null, string shortName = null, string alias = null) where T : BaseMonumentAdapter
        {
            var results = new List<T>();

            foreach (var baseMonument in monumentList)
            {
                if (baseMonument.MatchesFilter(filter, shortName, alias))
                    results.Add(baseMonument);
            }

            return results;
        }

        private List<Dictionary<string, object>> FilterMonumentsForAPI(IEnumerable<BaseMonumentAdapter> monumentList, string filter = null, string shortName = null, string alias = null)
        {
            var results = new List<Dictionary<string, object>>();

            foreach (var baseMonument in monumentList)
            {
                if (baseMonument.MatchesFilter(filter, shortName, alias))
                    results.Add(baseMonument.APIResult);
            }

            return results;
        }

        private void PrintMonumentList(IPlayer player, IEnumerable<BaseMonumentAdapter> monuments)
        {
            var builder = new StringBuilder();
            builder.AppendLine(GetMessage(player, Lang.ListHeader));

            foreach (var monument in monuments)
                builder.AppendLine(monument.PrefabName);

            player.Reply(builder.ToString());
        }

        private void ShowMonumentName(BasePlayer player, BaseMonumentAdapter monument)
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

            public MonoBehaviour Object { get; protected set; }
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
            private static Bounds DefaultMonumentMarkerBounds = new Bounds(new Vector3(0, 15, 0), new Vector3(30, 30, 30));

            private static Dictionary<string, Bounds> MonumentBounds = new Dictionary<string, Bounds>
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
                ["fishing_village_a"] = new Bounds(new Vector3(-3, 5, -11), new Vector3(76, 24, 80)),
                ["fishing_village_b"] = new Bounds(new Vector3(-3, 4, -4), new Vector3(42, 24, 76)),
                ["fishing_village_c"] = new Bounds(new Vector3(-0.5f, 4, -4.5f), new Vector3(31, 22, 75)),
                ["harbor_1"] = new Bounds(new Vector3(-8, 23, 15), new Vector3(246, 60, 200)),
                ["harbor_2"] = new Bounds(new Vector3(6, 23, 18), new Vector3(224, 60, 250)),
                ["junkyard_1"] = new Bounds(new Vector3(0, 20, 0), new Vector3(180, 50, 180)),
                ["launch_site_1"] = new Bounds(new Vector3(10, 25, -26), new Vector3(544, 120, 276)),
                ["lighthouse"] = new Bounds(new Vector3(10f, 23, 5), new Vector3(74, 96, 68)),
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
                ["sphere_tank"] = new Bounds(new Vector3(0, 41, 0), new Vector3(100, 84, 100)),
                ["stables_a"] = new Bounds(new Vector3(0, 10, 4), new Vector3(50, 20, 60)),
                ["stables_b"] = new Bounds(new Vector3(2, 15, 6), new Vector3(78, 30, 66)),
                ["supermarket_1"] = new Bounds(new Vector3(1, 5, 1), new Vector3(40, 10, 44)),
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
                ["satellite_dish"] = new Bounds(new Vector3(0, 25, 3), new Vector3(155, 55, 125)),
                ["excavator_1"] = new Bounds(new Vector3(-70, 40, 65), new Vector3(240, 100, 230)),
                ["gas_station_1"] = new Bounds(new Vector3(0, 13, 15), new Vector3(70, 42, 60)),
                ["military_tunnel_1"] = new Bounds(new Vector3(0, 15, -25), new Vector3(265, 70, 250)),
            };

            private static Bounds GetBounds(MonumentInfo monumentInfo, string shortName)
            {
                var boundsInfo = _pluginConfig.GetOverrideBounds(shortName);
                if (boundsInfo != null)
                    return boundsInfo.ToBounds();

                Bounds bounds;
                return MonumentBounds.TryGetValue(shortName, out bounds)
                    ? bounds
                    : monumentInfo.Bounds;
            }

            public MonumentInfo MonumentInfo { get; private set; }
            public OBB BoundingBox { get; private set; }

            public NormalMonumentAdapter(MonumentInfo monumentInfo) : base(monumentInfo)
            {
                MonumentInfo = monumentInfo;

                if (monumentInfo.name.Contains("monument_marker.prefab"))
                {
                    PrefabName = monumentInfo.transform.root.name;
                    ShortName = PrefabName;
                    BoundingBox = new OBB(Position, Rotation, GetBounds(monumentInfo, ShortName));
                }
                else
                {
                    BoundingBox = new OBB(Position, Rotation, GetBounds(monumentInfo, ShortName));
                }
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
            public static readonly string[] IgnoredPrefabs = new string[]
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

            // Straight tunnels with a dividier in the tracks.
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

            public DungeonGridCell DungeonCell { get; private set; }
            public OBB BoundingBox { get; private set; }

            private BaseTunnelInfo _tunnelInfo;

            public TrainTunnelAdapter(DungeonGridCell dungeonCell) : base(dungeonCell)
            {
                DungeonCell = dungeonCell;

                _tunnelInfo = GetTunnelInfo(ShortName);

                Rotation = _tunnelInfo.Rotation;
                Alias = _tunnelInfo.Alias;

                var bounds = _pluginConfig.GetOverrideBounds(_tunnelInfo.Alias)?.ToBounds() ?? _tunnelInfo.Bounds;
                BoundingBox = new OBB(Position, Rotation, bounds);
            }

            public override bool IsInBounds(Vector3 position) =>
                BoundingBox.Contains(position);

            public override Vector3 ClosestPointOnBounds(Vector3 position) =>
                BoundingBox.ClosestPoint(position);
        }

        private class UnderwaterLabLinkAdapter : BaseMonumentAdapter, MultipleBoundingBoxes
        {
            public DungeonBaseLink DungeonLink { get; private set; }
            public OBB[] BoundingBoxes { get; private set; }

            public UnderwaterLabLinkAdapter(DungeonBaseLink dungeonLink) : base(dungeonLink)
            {
                DungeonLink = dungeonLink;

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
                Vector3 overallClosestPoint = Vector3.positiveInfinity;
                float closestSqrDistance = float.MaxValue;

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

                Ddraw.Sphere(player, forwardUpperLeft, sphereRadius, color, duration);
                Ddraw.Sphere(player, forwardUpperRight, sphereRadius, color, duration);
                Ddraw.Sphere(player, forwardLowerLeft, sphereRadius, color, duration);
                Ddraw.Sphere(player, forwardLowerRight, sphereRadius, color, duration);

                Ddraw.Sphere(player, backLowerRight, sphereRadius, color, duration);
                Ddraw.Sphere(player, backLowerLeft, sphereRadius, color, duration);
                Ddraw.Sphere(player, backUpperRight, sphereRadius, color, duration);
                Ddraw.Sphere(player, backUpperLeft, sphereRadius, color, duration);

                Ddraw.Segments(player, forwardUpperLeft, forwardUpperRight, color, duration);
                Ddraw.Segments(player, forwardLowerLeft, forwardLowerRight, color, duration);
                Ddraw.Segments(player, forwardUpperLeft, forwardLowerLeft, color, duration);
                Ddraw.Segments(player, forwardUpperRight, forwardLowerRight, color, duration);

                Ddraw.Segments(player, backUpperLeft, backUpperRight, color, duration);
                Ddraw.Segments(player, backLowerLeft, backLowerRight, color, duration);
                Ddraw.Segments(player, backUpperLeft, backLowerLeft, color, duration);
                Ddraw.Segments(player, backUpperRight, backLowerRight, color, duration);

                Ddraw.Segments(player, forwardUpperLeft, backUpperLeft, color, duration);
                Ddraw.Segments(player, forwardLowerLeft, backLowerLeft, color, duration);
                Ddraw.Segments(player, forwardUpperRight, backUpperRight, color, duration);
                Ddraw.Segments(player, forwardLowerRight, backLowerRight, color, duration);

                if (showInfo)
                {
                    Ddraw.Sphere(player, forwardLowerMiddle, sphereRadius, Color.yellow, duration);
                    Ddraw.Sphere(player, forwardUpperMiddle, sphereRadius, Color.yellow, duration);
                    Ddraw.Sphere(player, backLowerMiddle, sphereRadius, Color.yellow, duration);
                    Ddraw.Sphere(player, backUpperMiddle, sphereRadius, Color.yellow, duration);

                    Ddraw.Sphere(player, leftLowerMiddle, sphereRadius, Color.green, duration);
                    Ddraw.Sphere(player, leftUpperMiddle, sphereRadius, Color.green, duration);
                    Ddraw.Sphere(player, rightLowerMiddle, sphereRadius, Color.green, duration);
                    Ddraw.Sphere(player, rightUpperMiddle, sphereRadius, Color.green, duration);

                    Ddraw.Text(player, forwardUpperMiddle, "<size=20>+Z</size>", Color.yellow, duration);
                    Ddraw.Text(player, forwardLowerMiddle, "<size=20>+Z</size>", Color.yellow, duration);
                    Ddraw.Text(player, backUpperMiddle, "<size=20>-Z</size>", Color.yellow, duration);
                    Ddraw.Text(player, backLowerMiddle, "<size=20>-Z</size>", Color.yellow, duration);

                    Ddraw.Text(player, leftLowerMiddle, "<size=20>-X</size>", Color.green, duration);
                    Ddraw.Text(player, leftUpperMiddle, "<size=20>-X</size>", Color.green, duration);
                    Ddraw.Text(player, rightLowerMiddle, "<size=20>+X</size>", Color.green, duration);
                    Ddraw.Text(player, rightUpperMiddle, "<size=20>+X</size>", Color.green, duration);

                    Ddraw.Text(player, forwardUpperLeft, "<size=28>*</size>", color, duration);
                    Ddraw.Text(player, forwardUpperRight, "<size=28>*</size>", color, duration);
                    Ddraw.Text(player, forwardLowerLeft, "<size=28>*</size>", color, duration);
                    Ddraw.Text(player, forwardLowerRight, "<size=28>*</size>", color, duration);

                    Ddraw.Text(player, backLowerRight, "<size=28>*</size>", color, duration);
                    Ddraw.Text(player, backLowerLeft, "<size=28>*</size>", color, duration);
                    Ddraw.Text(player, backUpperRight, "<size=28>*</size>", color, duration);
                    Ddraw.Text(player, backUpperLeft, "<size=28>*</size>", color, duration);
                }
            }

            public static void Box(BasePlayer player, OBB boundingBox, Color color, float duration, bool showInfo = true)
            {
                Ddraw.Box(player, boundingBox.position, boundingBox.rotation, boundingBox.extents, color, duration, showInfo);
            }
        }

        #endregion

        #region Configuration

        private class BoundsInfo
        {
            [JsonProperty("Center")]
            public Vector3 Center;

            [JsonProperty("Size")]
            public Vector3 Size;

            public Bounds ToBounds() => new Bounds(Center, Size);
        }

        private class Configuration : SerializableConfiguration
        {
            [JsonProperty(PropertyName = "Command")]
            public string Command = "mf";

            [JsonProperty("OverrideMonumentBounds")]
            public Dictionary<string, BoundsInfo> OverrideMonumentBounds = new Dictionary<string, BoundsInfo>
            {
                ["example_monument"] = new BoundsInfo
                {
                    Center = new Vector3(0, 10, 0),
                    Size = new Vector3(30, 20, 30),
                },
            };

            public BoundsInfo GetOverrideBounds(string alias)
            {
                BoundsInfo boundsInfo;
                return OverrideMonumentBounds.TryGetValue(alias, out boundsInfo)
                    ? boundsInfo
                    : null;
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
            bool changed = false;

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

        private string GetMessage(BasePlayer player, string messageName, params object[] args) =>
            GetMessage(player.UserIDString, messageName, args);

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(string.Format(GetMessage(player, messageName), args));

        private void ChatMessage(BasePlayer player, string messageName, params object[] args) =>
            player.ChatMessage(string.Format(GetMessage(player, messageName), args));

        private class Lang
        {
            public const string ErrorNoPermission = "NoPermission";
            public const string NoMonumentsFound = "NoMonumentsFound";
            public const string ListHeader = "List.Header";
            public const string AtMonument = "AtMonument";
            public const string ClosestMonument = "ClosestMonument";
            public const string HelpHeader = "Help.Header";
            public const string HelpList = "Help.List";
            public const string HelpShow = "Help.Show";
            public const string HelpClosest = "Help.Closest";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ErrorNoPermission] = "You don't have permission to do that.",
                [Lang.NoMonumentsFound] = "No monuments found",
                [Lang.AtMonument] = "At monument: {0}\nRelative position: {1}",
                [Lang.ClosestMonument] = "Closest monument: {0}\nDistance: {1:f2}m",
                [Lang.ListHeader] = "Listing monuments:",
                [Lang.HelpHeader] = "Monument Finder commands:",
                [Lang.HelpList] = "<color=#9f6>{0} list <filter></color> - List monuments matching filter",
                [Lang.HelpShow] = "<color=#9f6>{0} show <filter></color> - Show monuments matching filter",
                [Lang.HelpClosest] = "<color=#9f6>{0} closest</color> - Show info about the closest monument",
            }, this, "en");
        }

        #endregion
    }
}
