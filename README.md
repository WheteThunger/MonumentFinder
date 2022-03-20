## Features

- Allows privileged players to list and view monuments
- Provides API for plugins to find monuments
- Supports vanilla monuments, custom monuments, train tunnels, and underwater labs
- Allows customizing monument bounds which is useful for custom monuments

## Permissions

- `monumentfinder.find` -- Allows players to find monuments. Intended for administrators.

## Commands

Note: The command `mf` can be changed in the plugin configuration.

- `mf` -- Prints help info about available commands.
- `mf list <filter>` -- List all monuments, train tunnels, and underwater lab modules. The filter is optional.
- `mf show <filter>` -- (Requires admin) Shows text above all monuments, train tunnels, and underwater lab modules. The filter is optional.
- `mf closest` -- Prints information about the closest monument, train tunnel, or underwater lab module. If admin, this also displays the monument bounds.
- `mf closest config` -- Adds an entry into the config for the closest monument.

## Configuration

Default configuration:

```json
{
  "Command": "mf",
  "Default custom monument settings": {
    "Position": {
      "Auto determine from monument marker": true,
      "Auto determine from prevent building volume": false
    },
    "Rotation": {
      "Auto determine from monument marker": true,
      "Auto determine from prevent building volume": false
    },
    "Bounds": {
      "Auto determine from monument marker": false,
      "Auto determine from prevent building volume": false,
      "Use custom bounds": true,
      "Custom bounds": {
        "Size": {
          "x": 30.0,
          "y": 30.0,
          "z": 30.0
        },
        "Center adjustment": {
          "x": 0.0,
          "y": 10.0,
          "z": 0.0
        }
      }
    }
  },
  "Monuments": {
    "example_monument": {
      "Position": {
        "Auto determine from monument marker": true,
        "Auto determine from prevent building volume": false
      },
      "Rotation": {
        "Auto determine from monument marker": true,
        "Auto determine from prevent building volume": false
      },
      "Bounds": {
        "Auto determine from monument marker": false,
        "Auto determine from prevent building volume": false,
        "Use custom bounds": true,
        "Custom bounds": {
          "Size": {
            "x": 30.0,
            "y": 30.0,
            "z": 30.0
          },
          "Center adjustment": {
            "x": 0.0,
            "y": 10.0,
            "z": 0.0
          }
        }
      }
    }
  }
}
```

- `Command` -- Determines the base command of the plugin.
- `Default custom monument settings` -- Defines how the plugin will determine the position, rotation and bounds of custom monuments. The behavior for individual custom monuments can be overriden in the `Monuments` section on the config.
  - `Position` -- Defines how the plugin will determine the monument's "origin". Plugins such as Monument Addons or Custom Vending Setup will capture coordinates relative to the origin. **Caution: Changing how the origin is determined for a monument may cause issues with plugins that have already generated coordinates relative to that monument, meaning you may have to redo setup for those plugins at that monument.**
    - `Auto determine from monument marker` (`true` or `false`) -- While `true`, plugins will see the monument's origin as the monument marker's position.
      - Note: Since RustEdit does not currently allow custom prefabs to include a monument marker, map developers might not position/rotate monument markers consistently for every instance of a monument, so relying on the marker position/rotation may cause issues with plugins that use monument-relative coordinates.
    - `Auto determine from prevent building volume` (`true` or `false`) -- While `true`, plugins will see the monument's origin as the center of the Prevent Building Sphere/Cube, if there is one overlapping the monument marker.
  - `Rotation` -- Defines how the plugin will determine the monument's rotation. **Caution: Changing how the rotation is determined for a monument may cause issues with plugins that have already generated coordinates relative to that monument, meaning you may have to redo setup for those plugins at that monument.**
    - `Auto determine from monument marker` (`true` or `false`) -- While `true`, plugins will see the monument's rotation as the monument marker's rotation.
      - Note: Since RustEdit does not currently allow custom prefabs to include a monument marker, map developers might not position/rotate monument markers consistently for every instance of a monument, so relying on the marker position/rotation may cause issues with plugins that use monument-relative coordinates.
    - `Auto determine from prevent building volume` (`true` or `false`) -- While `true`, plugins will see the monument's rotation as the rotation of the Prevent Building Sphere/Cube, if there is one overlapping the monument marker.
  - `Bounds` -- Defines how the plugin will determine the monument's bounding box. Bounds are used by other plugins to accurately determine whether a given position is in a monument, and to determine specifically which monument.
    - `Auto determine from monument marker` (`true` or `false`) -- While `true`, plugins will see the monument's bounding box as monument marker's bounding box. This is an appropriate choice only if the map developer scaled the monument marker to cover the monument, which you should encourage map developers to do since it makes it very easy for this plugin to determine monument bounds.
    - `Auto determine from prevent building volume` (`true` or `false`) -- While `true`, plugins will see the monument's bounding box as the bounding box of the Prevent Building Sphere/Cube, if there is one overlapping the monument marker.
    - `Use custom bounds` (`true` or `false`) -- While `true`, plugins will see the monument's bounding box as you configure in `Custom bounds`.
    - `Custom bounds`
      - `Size` -- Determines the size of the bounding box relative tot he monument's origin. For example, if the monument is shaped like a cube, set `x`, `y` and `z` to the same value.
      - `Center adjustment` -- Adjusts the center of the bounding box, **relative** to the monument's origin. For example, if you want the center of the bounding box to be 10 meters above the monument's origin, set this to `"x": 0, "y": 10, "z": 0`.
- `Monuments` -- This section allows you to override the configuration for individual monuments, including vanilla monuments and custom monuments. Customizing train tunnels and underwater labs is not supported. The options are the same as for `Default custom monument settings`, but options such as `Auto determine from monument marker` and `Auto determine from prevent building volume` have no effect on vanilla monuments.

## How to set up custom monuments

You must have `ownerid` or `moderatorid` to proceed.

1. Run the `mf list <filter>` command to verify that your monument can be found. If you don't see it in the list, then the monument is not using the `monument_marker` prefab, so it needs to be updated in a map editor before proceeding.
2. Once you determine the name of the monument, run the command `mf show <name>`. If the name has spaces, wrap the name in quotes. This command will show the monument name floating above the monument in-game.
3. Go to the monument and run the command `mf closest`. This should display the monument name floating above it, with a bounding box around the monument. If this box contains the monument accurately, then you are done.
4. If the bounding box is not accurate, run the command `mf closest config` to automatically add an entry into the plugin config.
5. Configure the bounds in the plugin configuration, reload the plugin, and run `mf closest` to visualize the bounds. Repeat this process until you are satisifed with the bounds. Note: If the map developer made the monument marker cover the monument, or if they added a Prevent Building Sphere/Cube to the monument, then you can try the config options to auto detect the bounds from those. If neither of those are present, or if the bounds aren't suitable, then you can configure custom bounds.

### Example of accurate bounds

Eventually it should look something like below. The +X, -X, +Z, and -Z guidelines indicate the directions of X and Z axes so that you make changes to the correct values if configuring custom bounds.

![](https://raw.githubusercontent.com/WheteThunger/MonumentFinder/master/Bounds.png)

## Example monument names

These were found by using Prefab Sniffer with the command `prefab find assets/bundled/prefabs/autospawn/monument`.

- airfield_1
- bandit_town
- cave_large_hard
- cave_large_medium
- cave_large_sewers_hard
- cave_medium_easy
- cave_medium_hard
- cave_medium_medium
- cave_small_easy
- cave_small_hard
- cave_small_medium
- compound
- desert_military_base_a
- desert_military_base_b
- desert_military_base_c
- desert_military_base_d
- excavator_1
- fishing_village_a
- fishing_village_b
- fishing_village_c
- gas_station_1
- harbor_1
- harbor_2
- ice_lake_1
- ice_lake_2
- ice_lake_3
- ice_lake_4
- junkyard_1
- launch_site_1
- lighthouse
- military_tunnel_1
- mining_quarry_a
- mining_quarry_b
- mining_quarry_c
- oilrig_1
- oilrig_2
- powerplant_1
- radtown_small_3
- satellite_dish
- sphere_tank
- stables_a
- stables_b
- supermarket_1
- swamp_a
- swamp_b
- swamp_c
- trainyard_1
- underwater_lab_a
- underwater_lab_b
- underwater_lab_c
- underwater_lab_d
- warehouse
- water_treatment_plant_1
- water_well_a
- water_well_b
- water_well_c
- water_well_d
- water_well_e

## Localization

```json
{
  "NoPermission": "You don't have permission to do that.",
  "NoMonumentsFound": "No monuments found",
  "AtMonument": "At monument: {0}\nRelative position: {1}",
  "ClosestMonument": "Closest monument: {0}\nDistance: {1:f2}m",
  "Closest.Config.Success": "Added monument <color=#9f6>{0}</color> to the plugin config.",
  "Closest.Config.AlreadyPresent": "Monument <color=#9f6>{0}</color> is already in the plugin config.",
  "List.Header": "Listing monuments:",
  "Help.Header": "Monument Finder commands:",
  "Help.List": "<color=#9f6>{0} list <filter></color> - List monuments matching filter",
  "Help.Show": "<color=#9f6>{0} show <filter></color> - Show monuments matching filter",
  "Help.Closest": "<color=#9f6>{0} closest</color> - Show info about the closest monument",
  "Help.Closest.Config": "<color=#9f6>{0} closest config</color> - Adds the closest monument to the config"
}
```

## Developer API

### When to use this API

If you want to find a predetermined aboveground vanilla monument such as Outpost, you can simply loop `TerrainMeta.Path.Monuments` to find it. Doing so will allow you to avoid the dependency on Monument Finder.

The main reasons to use this API are as follows:
- To accurately determine which monument a position is within. Without this plugin, you would have to resort to distance checks, which could return inaccurate results when small monuments are next to large monuments.
- To determine if a given position is within a train tunnel.
- To determine if a given position is within an underwater lab module.

Not supported:
- Caves. You can do topology checks for this use case.

### Find vanilla or custom monuments

```cs
List<Dictionary<string, object>> API_FindMonuments(string filter)
```

```cs
Dictionary<string, object> API_GetClosestMonument(Vector3 position)
```

Legacy API method:
```cs
List<MonumentInfo> FindMonuments(string filter)
```

### Find train tunnels

```cs
List<Dictionary<string, object>> API_FindTrainTunnels(string filter)
```

```cs
Dictionary<string, object> API_GetClosestTrainTunnel(Vector3 position)
```

### Find underwater lab modules

```cs
List<Dictionary<string, object>> API_FindUnderwaterLabModules(string filter)
```

```cs
Dictionary<string, object> API_GetClosestUnderwaterLabModule(Vector3 position)
```

### Find any type of monument

The following methods may return vanilla monuments, custom monuments, train tunnels and underwater lab modules.

```cs
List<Dictionary<string, object>> API_Find(string filter)
```

```cs
List<Dictionary<string, object>> API_FindByShortName(string shortName)
```

```cs
List<Dictionary<string, object>> API_FindByAlias(string alias)
```

```cs
Dictionary<string, object> API_GetClosest(Vector3 position)
```

### Monument wrapper object

Each `Dictionary<string, object>` object consists of the following keys.

- `"Object"` -- The underlying object representing the monument, train tunnel or underwater lab room.
  - Type: `MonumentInfo` | `DungeonGridCell` | `DungeonBaseLink`
- `"PrefabName"` -- The full prefab name of the monument.
  - Type: `string`
- `"ShortName"` -- The short prefab name of the monument.
  - Type: `string`
- `"Alias"` -- The alias of the monument if it has one, or else null. For example, all train stations will use the alias `TrainStation`.
  - Type: `string`
  - Possible aliases: `TrainStation` | `BarricadeTunnel` | `LootTunnel` | `SplitTunnel` | `Intersection` | `LargeIntersection` | `CornerTunnel`.
- `"Position"` -- The position of the monument.
  - Type: `Vector3`
- `"Rotation"` -- The rotation of the monument.
  - Type: `Quaternion`
- `"TransformPoint"` -- Delegate that works like [UnityEngine.Transform.TransformPoint](https://docs.unity3d.com/ScriptReference/Transform.TransformPoint.html).
  - Type: `Func<Vector3, Vector3>`
- `"InverseTransformPoint"` -- Delegate that works like [UnityEngine.Transform.InverseTransformPoint](https://docs.unity3d.com/ScriptReference/Transform.InverseTransformPoint.html).
  - Type: `Func<Vector3, Vector3>`
- `"ClosestPointOnBounds"` -- Delegate that returns the closest position that is within the monument's bounds, like [UnityEngine.Collider.ClosestPointOnBounds](https://docs.unity3d.com/ScriptReference/Collider.ClosestPointOnBounds.html).
  - Type: `Func<Vector3, Vector3>`
- `"IsInBounds"` -- Delegate that returns true if the position is within the monument's bounds, like Rust's `MonumentInfo.IsInBounds(Vector3)`.
  - Type: `Func<Vector3, bool>`

### Example use in plugins

```cs
// Place at top of plugin file.
using Oxide.Core.Plugins;

// The rest goes within a plugin class.
[PluginReference]
Plugin MonumentFinder;

class MonumentAdapter
{
    public MonoBehaviour Object => (MonoBehaviour)_monumentInfo["Object"];
    public string PrefabName => (string)_monumentInfo["PrefabName"];
    public string ShortName => (string)_monumentInfo["ShortName"];
    public string Alias => (string)_monumentInfo["Alias"];
    public Vector3 Position => (Vector3)_monumentInfo["Position"];
    public Quaternion Rotation => (Quaternion)_monumentInfo["Rotation"];

    private Dictionary<string, object> _monumentInfo;

    public MonumentAdapter(Dictionary<string, object> monumentInfo)
    {
        _monumentInfo = monumentInfo;
    }

    public Vector3 TransformPoint(Vector3 localPosition) =>
        ((Func<Vector3, Vector3>)_monumentInfo["TransformPoint"]).Invoke(localPosition);

    public Vector3 InverseTransformPoint(Vector3 worldPosition) =>
        ((Func<Vector3, Vector3>)_monumentInfo["InverseTransformPoint"]).Invoke(worldPosition);

    public Vector3 ClosestPointOnBounds(Vector3 position) =>
        ((Func<Vector3, Vector3>)_monumentInfo["ClosestPointOnBounds"]).Invoke(position);

    public bool IsInBounds(Vector3 position) =>
        ((Func<Vector3, bool>)_monumentInfo["IsInBounds"]).Invoke(position);
}

// Call this method within your plugin to get the closest monument, train tunnel, or underwater lab.
MonumentAdapter GetClosestMonument(Vector3 position)
{
    var dictResult = MonumentFinder?.Call("API_GetClosest", position) as Dictionary<string, object>;
    return dictResult != null ? new MonumentAdapter(dictResult) : null;
}
```

## Credits

- **misticos**, the original author of this plugin (v1 and v2)