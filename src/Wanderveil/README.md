# Wanderveil source map

`Wanderveil.csproj` is built by root `Wanderveil.slnx`. `Plugin.cs` owns host composition/lifecycle and `Configuration.cs` stores the reduced-motion preference. The shell and settings exist; other areas remain reserved.

| Directory | Responsibility |
| --- | --- |
| `Application/` | Use cases and view state |
| `Domain/` | Host-independent passport, stamp, discovery models/interfaces |
| `Features/Discover/` | Browsing/details/collection presentation |
| `Features/Passport/` | Identity, pages, stamp collection |
| `Features/Echoes/` | Future adventure presentation |
| `Features/Studio/` | Creator templates/editor/preview |
| `Features/Social/` | Future consent/request UI |
| `Features/Settings/` | Implemented appearance window; future privacy/account UI |
| `UI/` | Implemented native-tab shell, initial color/scale tokens, and procedural compass; fuller toolkit later |
| `Infrastructure/` | Dalamud, local persistence, future API/media/rendering adapters |

Keep one plugin project and only the classes needed by the current slice. Domain/application behavior must not depend on drawing APIs, private backend projects, or optional graphics. This shell does not yet implement discovery collection, passports, creator tools, or online services.
