## Features

- Allows privileged players to list and view monuments
- Provides API for plugins to find monuments
- Supports vanilla monuments, custom monuments, train tunnels, and underwater labs
- Allows customizing monument bounds which is useful for custom monuments

## Permissions

- `monumentfinder.find` -- Allows players to find monuments.

## Commands

Note: The command `mf` can be changed in the plugin configuration.

- `mf` -- Prints help info about available commands.
- `mf list <filter>` -- List all monuments, train tunnels, and underwater lab modules. The filter is optional.
- `mf show <filter>` -- (Requires admin) Shows text above all monuments, train tunnels, and underwater lab modules. The filter is optional.
- `mf closest` -- Prints information about the closest monument, train tunnel, or underwater lab module. If admin, this also displays the monument bounds.

## Configuration

Default configuration:

```json
{
  "Command": "mf",
  "OverrideMonumentBounds": {
    "example_monument": {
      "Center": {
        "x": 0.0,
        "y": 10.0,
        "z": 0.0
      },
      "Size": {
        "x": 30.0,
        "y": 20.0,
        "z": 30.0
      }
    }
  }
}
```

- `Command` -- Determines the base command of the plugin.
- `OverrideMonumentBounds` -- This section allows you to override the bounds for any vanilla or custom monument. Does not work for train tunnels or underwater labs.
  - These bounds have no effect on vanilla. Instead, they allow plugins to accurately determine whether a given position is at a monument.
  - Most vanilla monuments already have bounds hard-coded into the plugin which should be fairly accurate, so you probably don't need to override bounds except for custom monuments.
  - Monuments should be listed using their short prefab name, with the following options.
    - `Center` -- Determines the center of the bounding box. If the center of the monument and the origin of the monument are equal, then you can probably just use all zeros for this value. If you want to offset the bounding box relative to the monument's origin, then update this value to offset the bounding box. The most common reason to offset a bounding box is to raise it above ground, which can be achieved by simply increasing the `y` component of this value.
    - `Size` -- Determines the size of the bounding box relative to the monument's center. For example, if the monument is shaped like a cube, set `x`, `y` and `z` to the same value.

## Localization

```json
{
  "NoPermission": "You don't have permission to do that.",
  "NoMonumentsFound": "No monuments found",
  "AtMonument": "At monument: {0}\nRelative position: {1}",
  "ClosestMonument": "Closest monument: {0}\nDistance: {1:f2}m",
  "List.Header": "Listing monuments:",
  "Help.Header": "Monument Finder commands:",
  "Help.List": "<color=#9f6>{0} list <filter></color> - List monuments matching filter",
  "Help.Show": "<color=#9f6>{0} show <filter></color> - Show monuments matching filter",
  "Help.Closest": "<color=#9f6>{0} closest</color> - Show info about the closest monument"
}
```

## Developer API

Note: If you want to 100% accurately determine if a given position is at a monument (especially if it's in a cave), you should use monument topology checks, rather than completely relying on this plugin's API. After the topology check, if you want to determine specifically which monument the position is within or closest to, then this API will help you since the bounds provided by this plugin are more accurate than vanilla.

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
  - Type: `new Func<Vector3, Vector3>`
- `"ClosestPointOnBounds"` -- Delegate that returns the closest position that is within the monument's bounds, like [UnityEngine.Collider.ClosestPointOnBounds](https://docs.unity3d.com/ScriptReference/Collider.ClosestPointOnBounds.html).
  - Type: `new Func<Vector3, Vector3>`
- `"IsInBounds"` -- Delegate that returns true if the position is within the monument's bounds, like Rust's `MonumentInfo.IsInBounds(Vector3)`.
  - Type: `new Func<Vector3, bool>`

### Example use in plugins

```cs
// Place at top of plugin file.
using Oxide.Core.Plugins;

// The rest goes within a plugin class.
[PluginReference]
Plugin MonumentFinder;

class MonumentAdapter
{
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
